// SocketViewerWinForms.cs — WinForms viewer for XplatUISocket, with toolbar.
// Single file, script-style. Deps: SkiaSharp + the Skia System.Drawing/WinForms stack.
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Drawing;
using System.Windows.Forms;

string sockDir = Environment.GetEnvironmentVariable("XPLAT_UI_SOCKET_DIR") ?? "windows";
string listPath = Path.Combine(sockDir, "window-list");
string shotDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
Directory.CreateDirectory(shotDir);

Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

// ──────────────────────────────── UI ────────────────────────────────
var form = new Form
{
    Text = "SocketViewerWinForms",
    ClientSize = new Size(1150, 810),
    StartPosition = FormStartPosition.CenterScreen,
    KeyPreview = true
};

// --- toolbar ---
var toolbar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = SystemColors.Control };
var btnRefresh = new Button { Text = "⟳ Full", Width = 70, Dock = DockStyle.Left };
var btnSave = new Button { Text = "💾 Save", Width = 70, Dock = DockStyle.Left };
var sep1 = new Label { Text = "", Width = 8, Dock = DockStyle.Left };
var btnFmt = new Button { Text = "JPEG", Width = 60, Dock = DockStyle.Left };
var btnGray = new Button { Text = "Gray: OFF", Width = 80, Dock = DockStyle.Left };
var sep2 = new Label { Text = "", Width = 8, Dock = DockStyle.Left };
var btnQMinus = new Button { Text = "Q−", Width = 40, Dock = DockStyle.Left };
var lblQ = new Label { Text = "75", Width = 34, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleCenter };
var btnQPlus = new Button { Text = "Q+", Width = 40, Dock = DockStyle.Left };
var sep3 = new Label { Text = "", Width = 8, Dock = DockStyle.Left };
var btnZoom = new Button { Text = "Fit", Width = 50, Dock = DockStyle.Left };
var chkLive = new CheckBox { Text = "Live", Checked = true, Dock = DockStyle.Left, Width = 55, TextAlign = ContentAlignment.MiddleCenter };
toolbar.Controls.AddRange(new Control[]
{
    // reverse order: Dock.Left stacks from the right of the previous
    chkLive, btnZoom, sep3, btnQPlus, lblQ, btnQMinus, sep2, btnGray, btnFmt, sep1, btnSave, btnRefresh
});

var pb = new PictureBox
{
    Dock = DockStyle.Fill,
    BackColor = Color.FromArgb(30, 30, 34),
    SizeMode = PictureBoxSizeMode.Zoom,
    TabStop = false
};

var debug = new TextBox
{
    Dock = DockStyle.Fill,
    Multiline = true,
    ReadOnly = true,
    ScrollBars = ScrollBars.Vertical,
    Font = new Font(FontFamily.GenericMonospace, 9),
    BackColor = Color.FromArgb(18, 18, 22),
    ForeColor = Color.LightGray
};

var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 600, FixedPanel = FixedPanel.Panel2 };
rightSplit.Panel1.Controls.Add(pb);
rightSplit.Panel2.Controls.Add(debug);

var listBox = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
var lblHint = new Label
{
    Dock = DockStyle.Bottom,
    Height = 34,
    Text = "F5=full refresh · click/type into image",
    ForeColor = Color.Gray
};
var leftPanel = new Panel { Dock = DockStyle.Fill };
leftPanel.Controls.Add(listBox);
leftPanel.Controls.Add(lblHint);

var mainSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 300, FixedPanel = FixedPanel.Panel1 };
mainSplit.Panel1.Controls.Add(leftPanel);
mainSplit.Panel2.Controls.Add(rightSplit);

var status = new StatusStrip();
var stConn = new ToolStripStatusLabel("list: disconnected");
var stWin = new ToolStripStatusLabel("  window: -");
var stFrame = new ToolStripStatusLabel("  frames: 0");
status.Items.AddRange(new ToolStripItem[] { stConn, stWin, stFrame });

form.Controls.Add(mainSplit);
form.Controls.Add(toolbar);
form.Controls.Add(status);
status.Dock = DockStyle.Bottom;
toolbar.Dock = DockStyle.Top;

// ──────────────────────────────── state ────────────────────────────────
var entries = new List<(string handle, string title, string socket, int w, int h)>();
(string handle, string title, string socket, int w, int h) cur = ("", "", "", 0, 0);

bool running = true;
bool dead = true;
bool rebuilding = false;
int frameCount = 0;
int connSeq = 0;
DateTime lastMoveSend = DateTime.MinValue;
DateTime lastMoveLog = DateTime.MinValue;
DateTime lastFrameLog = DateTime.MinValue;
DateTime lastReconnectTry = DateTime.MinValue;

string format = "jpeg";
int quality = 75;
bool gray = false;

Socket? curSock = null;
NetworkStream? curNs = null;
StreamReader? curRd = null;
object writeLock = new object();

// ──────────────────────────────── helpers ────────────────────────────────
void UI(Action a) { try { if (!form.IsDisposed) form.BeginInvoke(a); } catch { } }

void Log(string s)
{
    UI(() =>
    {
        try
        {
            if (debug.TextLength > 30000)
                debug.Text = debug.Text.Substring(debug.TextLength - 15000);
            debug.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {s}{Environment.NewLine}");
        }
        catch { }
    });
}

string JEsc(string s)
{
    var sb = new StringBuilder(s.Length + 4);
    foreach (var c in s)
        switch (c)
        {
            case '"': sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\n': sb.Append("\\n"); break;
            case '\r': sb.Append("\\r"); break;
            case '\t': sb.Append("\\t"); break;
            default: sb.Append(c); break;
        }
    return sb.ToString();
}

void SendRaw(string json, bool logIt)
{
    if (curNs == null || dead) return;
    try
    {
        var b = Encoding.UTF8.GetBytes(json + "\n");
        lock (writeLock) { curNs.Write(b, 0, b.Length); curNs.Flush(); }
        if (logIt) Log(">> " + json);
    }
    catch (Exception ex) { Log("[send] failed: " + ex.Message); dead = true; }
}

string ParamsJson() =>
    $"\"format\":\"{format}\",\"quality\":{quality},\"gray\":{(gray ? "true" : "false")}";

void ResubscribeAndRefresh()
{
    if (chkLive.Checked)
        SendRaw("{\"type\":\"subscribe\"," + ParamsJson() + "}", true);
    SendRaw("{\"type\":\"refresh\"," + ParamsJson() + "}", true);
}

void UpdateToolbar()
{
    btnFmt.Text = format.ToUpper();
    btnGray.Text = gray ? "Gray: ON" : "Gray: OFF";
    lblQ.Text = quality.ToString();
}

void SendMouse(string type, Point p, string button)
{
    SendRaw($"{{\"type\":\"{type}\",\"x\":{p.X},\"y\":{p.Y},\"button\":\"{button}\"}}",
        logIt: type != "mousemove" || (DateTime.Now - lastMoveLog).TotalMilliseconds > 500);
    if (type == "mousemove") lastMoveLog = DateTime.Now;
}

string ItemText((string handle, string title, string socket, int w, int h) e)
    => $"{e.title}  [{e.handle}]  {e.w}x{e.h}";

string? SelectedHandle()
{
    int i = listBox.SelectedIndex;
    return (i >= 0 && i < entries.Count) ? entries[i].handle : null;
}

void Reselect(string? preferHandle)
{
    rebuilding = true;
    try
    {
        int idx = preferHandle != null ? entries.FindIndex(x => x.handle == preferHandle) : -1;
        if (idx < 0 && entries.Count > 0)
            idx = Math.Min(Math.Max(listBox.SelectedIndex, 0), entries.Count - 1);
        listBox.SelectedIndex = idx;
    }
    finally { rebuilding = false; }
    if (entries.Count == 0)
    {
        CloseCurrentWindow();
        stWin.Text = "  window: -";
    }
}

void RebuildItems(List<(string handle, string title, string socket, int w, int h)> nw)
{
    UI(() =>
    {
        string? keep = SelectedHandle() ?? cur.handle;
        entries = nw;
        rebuilding = true;
        try
        {
            listBox.BeginUpdate();
            listBox.Items.Clear();
            foreach (var e in entries) listBox.Items.Add(ItemText(e));
            listBox.EndUpdate();
        }
        finally { rebuilding = false; }
        Reselect(keep);
    });
}

// ──────────────────────────────── window socket ────────────────────────────────
void CloseCurrentWindow()
{
    connSeq++;
    dead = true;
    try { curSock?.Close(); } catch { }
    curSock = null; curNs = null; curRd = null;
}

void ConnectTo((string handle, string title, string socket, int w, int h) e)
{
    CloseCurrentWindow();
    cur = e;
    dead = false;
    stWin.Text = $"  window: {e.title} {e.w}x{e.h}";
    form.Text = "SocketViewerWinForms — " + e.title;
    Log($"[win] connecting {e.socket} ({e.title})");
    try
    {
        var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
        sock.Connect(new UnixDomainSocketEndPoint(e.socket));
        var ns = new NetworkStream(sock, true);
        var rd = new StreamReader(ns, Encoding.UTF8);
        curSock = sock; curNs = ns; curRd = rd;
        int myConn = ++connSeq;
        Task.Run(() => WindowReader(myConn, sock, rd));
        if (chkLive.Checked)
            SendRaw("{\"type\":\"subscribe\"," + ParamsJson() + "}", true);
        SendRaw("{\"type\":\"refresh\"," + ParamsJson() + ",\"full\":true}", true);
    }
    catch (Exception ex)
    {
        Log("[win] connect failed: " + ex.Message);
        dead = true;
    }
}

void WindowReader(int myConn, Socket sock, StreamReader rd)
{
    try
    {
        string? line;
        while (running && myConn == connSeq && (line = rd.ReadLine()) != null)
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            var t = r.GetProperty("type").GetString();
            switch (t)
            {
                case "frame":
                    {
                        var data = r.GetProperty("data").GetString();
                        if (string.IsNullOrEmpty(data)) break;
                        try
                        {
                            using var ms = new MemoryStream(Convert.FromBase64String(data));
                            var img = Image.FromStream(ms);
                            int fc = ++frameCount;
                            UI(() =>
                            {
                                var old = pb.Image;
                                pb.Image = img;
                                old?.Dispose();
                                stFrame.Text = $"  frames: {fc} ({img.Width}x{img.Height})";
                            });
                            if ((DateTime.Now - lastFrameLog).TotalMilliseconds > 1000)
                            {
                                lastFrameLog = DateTime.Now;
                                Log($"<< frame {img.Width}x{img.Height} {data.Length / 1024.0:F1}KB");
                            }
                        }
                        catch (Exception ex) { Log("[win] bad frame: " + ex.Message); }
                        break;
                    }
                case "hello":
                case "info":
                    if (r.TryGetProperty("window", out var we) && we.TryGetProperty("title", out var tt))
                        UI(() => form.Text = "SocketViewerWinForms — " + tt.GetString());
                    Log("<< " + (line.Length > 300 ? line.Substring(0, 300) + "..." : line));
                    break;
                case "pong":
                    Log("<< pong");
                    break;
                case "bye":
                    Log("<< bye (window closed)");
                    if (myConn == connSeq) dead = true;
                    break;
            }
        }
    }
    catch (Exception ex) { if (running && myConn == connSeq) Log("[win] read error: " + ex.Message); }
    if (myConn == connSeq) dead = true;
}

// ──────────────────────────────── window-list worker ────────────────────────────────
void HandleListLine(string line)
{
    using var doc = JsonDocument.Parse(line);
    var r = doc.RootElement;
    var t = r.GetProperty("type").GetString();
    if (t == "list")
    {
        var nw = new List<(string, string, string, int, int)>();
        foreach (var w in r.GetProperty("windows").EnumerateArray())
            nw.Add((w.GetProperty("handle").GetString() ?? "",
                    w.GetProperty("title").GetString() ?? "",
                    w.GetProperty("socket").GetString() ?? "",
                    w.GetProperty("width").GetInt32(),
                    w.GetProperty("height").GetInt32()));
        Log($"[list] snapshot: {nw.Count} window(s)");
        RebuildItems(nw);
    }
    else if (t == "window-added")
    {
        var w = r.GetProperty("window");
        var e = (w.GetProperty("handle").GetString() ?? "",
                 w.GetProperty("title").GetString() ?? "",
                 w.GetProperty("socket").GetString() ?? "",
                 w.GetProperty("width").GetInt32(),
                 w.GetProperty("height").GetInt32());
        Log($"[list] + {e.Item2} [{e.Item1}]");
        UI(() =>
        {
            entries.RemoveAll(x => x.handle == e.Item1);
            entries.Add(e);
            rebuilding = true;
            try { listBox.Items.Add(ItemText(e)); } finally { rebuilding = false; }
            if (listBox.SelectedIndex < 0) Reselect(e.Item1);
        });
    }
    else if (t == "window-removed")
    {
        string h = r.GetProperty("window").GetProperty("handle").GetString() ?? "";
        Log($"[list] - [{h}]");
        UI(() =>
        {
            int i = entries.FindIndex(x => x.handle == h);
            if (i >= 0)
            {
                entries.RemoveAt(i);
                rebuilding = true;
                try { listBox.Items.RemoveAt(i); } finally { rebuilding = false; }
            }
            if (cur.handle == h) dead = true;
            if (listBox.SelectedIndex < 0 && entries.Count > 0) Reselect(null);
        });
    }
    else if (t == "window-updated")
    {
        var w = r.GetProperty("window");
        string h = w.GetProperty("handle").GetString() ?? "";
        UI(() =>
        {
            int i = entries.FindIndex(x => x.handle == h);
            if (i >= 0)
            {
                entries[i] = (h,
                              w.GetProperty("title").GetString() ?? "",
                              w.GetProperty("socket").GetString() ?? "",
                              w.GetProperty("width").GetInt32(),
                              w.GetProperty("height").GetInt32());
                rebuilding = true;
                try { listBox.Items[i] = ItemText(entries[i]); } finally { rebuilding = false; }
                if (cur.handle == h)
                {
                    stWin.Text = $"  window: {entries[i].title} {entries[i].w}x{entries[i].h}";
                    form.Text = "SocketViewerWinForms — " + entries[i].title;
                }
            }
        });
    }
}

void ListWorker()
{
    while (running)
    {
        Socket? s = null;
        try
        {
            s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            s.Connect(new UnixDomainSocketEndPoint(listPath));
            UI(() => stConn.Text = "list: connected");
            Log("[list] connected " + listPath);
            using (var ns = new NetworkStream(s, true))
            using (var rd = new StreamReader(ns, Encoding.UTF8))
            {
                string? line;
                while (running && (line = rd.ReadLine()) != null)
                    HandleListLine(line);
            }
        }
        catch (Exception ex)
        {
            if (running) Log("[list] " + ex.GetType().Name + ": " + ex.Message);
        }
        UI(() => stConn.Text = "list: disconnected (retrying)");
        try { s?.Close(); } catch { }
        for (int i = 0; i < 15 && running; i++) Thread.Sleep(100);
    }
}

// ──────────────────────────────── toolbar actions ────────────────────────────────
btnRefresh.Click += (_, __) =>
    SendRaw("{\"type\":\"refresh\"," + ParamsJson() + ",\"full\":true}", true);

btnSave.Click += (_, __) =>
{
    var img = pb.Image;
    if (img == null) { Log("[save] no frame yet"); return; }
    try
    {
        string safe = string.Concat((cur.title ?? "window").Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        string path = Path.Combine(shotDir, $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        img.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Log("[save] " + path);
    }
    catch (Exception ex) { Log("[save] failed: " + ex.Message); }
};

btnFmt.Click += (_, __) =>
{
    format = format == "jpeg" ? "png" : "jpeg";
    UpdateToolbar();
    ResubscribeAndRefresh();
};

btnGray.Click += (_, __) =>
{
    gray = !gray;
    UpdateToolbar();
    ResubscribeAndRefresh();
};

btnQMinus.Click += (_, __) => { quality = Math.Max(1, quality - 5); UpdateToolbar(); ResubscribeAndRefresh(); };
btnQPlus.Click += (_, __) => { quality = Math.Min(100, quality + 5); UpdateToolbar(); ResubscribeAndRefresh(); };

btnZoom.Click += (_, __) =>
{
    pb.SizeMode = pb.SizeMode == PictureBoxSizeMode.Zoom ? PictureBoxSizeMode.Normal : PictureBoxSizeMode.Zoom;
    btnZoom.Text = pb.SizeMode == PictureBoxSizeMode.Zoom ? "Fit" : "1:1";
    Log("[view] SizeMode = " + pb.SizeMode);
};

chkLive.CheckedChanged += (_, __) =>
{
    if (curNs == null || dead) return;
    if (chkLive.Checked) SendRaw("{\"type\":\"subscribe\"," + ParamsJson() + "}", true);
    else SendRaw("{\"type\":\"unsubscribe\"}", true);
};

// ──────────────────────────────── input plumbing ────────────────────────────────
Point? MapToImage(Point p)
{
    if (pb.Image == null) return null;
    if (pb.SizeMode == PictureBoxSizeMode.Normal)
        return new Point(p.X, p.Y);
    float scale = Math.Min((float)pb.ClientSize.Width / pb.Image.Width,
                           (float)pb.ClientSize.Height / pb.Image.Height);
    int dw = Math.Max(1, (int)(pb.Image.Width * scale));
    int dh = Math.Max(1, (int)(pb.Image.Height * scale));
    int ox = (pb.ClientSize.Width - dw) / 2;
    int oy = (pb.ClientSize.Height - dh) / 2;
    int x = (int)((p.X - ox) / scale);
    int y = (int)((p.Y - oy) / scale);
    if (x < 0 || y < 0 || x >= pb.Image.Width || y >= pb.Image.Height) return null;
    return new Point(x, y);
}

string Btn(MouseButtons b) =>
    b == MouseButtons.Right ? "right" : b == MouseButtons.Middle ? "middle" : "left";

pb.MouseDown += (_, e) => { var p = MapToImage(e.Location); if (p.HasValue) SendMouse("mousedown", p.Value, Btn(e.Button)); };
pb.MouseUp += (_, e) => { var p = MapToImage(e.Location); if (p.HasValue) SendMouse("mouseup", p.Value, Btn(e.Button)); };
pb.MouseMove += (_, e) =>
{
    var p = MapToImage(e.Location);
    if (p.HasValue && (DateTime.Now - lastMoveSend).TotalMilliseconds > 15)
    {
        lastMoveSend = DateTime.Now;
        SendMouse("mousemove", p.Value, "left");
    }
};

form.MouseWheel += (_, e) =>
{
    var p = MapToImage(pb.PointToClient(Control.MousePosition));
    if (p.HasValue)
        SendRaw($"{{\"type\":\"wheel\",\"x\":{p.Value.X},\"y\":{p.Value.Y},\"delta\":{e.Delta}}}", true);
};

form.KeyDown += (_, e) =>
{
    if (e.KeyCode == Keys.F5)
    {
        SendRaw("{\"type\":\"refresh\"," + ParamsJson() + ",\"full\":true}", true);
        e.Handled = true;
        return;
    }
    SendRaw($"{{\"type\":\"keydown\",\"key\":\"{e.KeyCode}\"}}", true);
};
form.KeyUp += (_, e) => SendRaw($"{{\"type\":\"keyup\",\"key\":\"{e.KeyCode}\"}}", true);
form.KeyPress += (_, e) =>
{
    if (!char.IsControl(e.KeyChar))
        SendRaw($"{{\"type\":\"text\",\"text\":\"{JEsc(e.KeyChar.ToString())}\"}}", true);
};

listBox.SelectedIndexChanged += (_, __) =>
{
    if (rebuilding) return;
    int i = listBox.SelectedIndex;
    if (i >= 0 && i < entries.Count && entries[i].handle != cur.handle)
        ConnectTo(entries[i]);
};

// Watchdog: reconnect dead window (with backoff), or fall over to another one.
var watchdog = new System.Windows.Forms.Timer { Interval = 1000 };
watchdog.Tick += (_, __) =>
{
    if (!dead) return;
    if ((DateTime.Now - lastReconnectTry).TotalSeconds < 3) return;
    lastReconnectTry = DateTime.Now;

    int i = entries.FindIndex(x => x.handle == cur.handle);
    if (i >= 0)
    {
        Log("[win] watchdog: reconnecting...");
        ConnectTo(entries[i]);
    }
    else if (entries.Count > 0)
    {
        Log("[win] watchdog: window gone, switching to another");
        Reselect(entries[0].handle);
    }
    else
    {
        stWin.Text = "  window: - (none)";
    }
};
watchdog.Start();

Application.ThreadException += (_, e) => Log("[UI-EX] " + e.Exception.Message);
form.FormClosed += (_, __) =>
{
    running = false;
    watchdog.Stop();
    CloseCurrentWindow();
};

// ──────────────────────────────── go ────────────────────────────────
UpdateToolbar();
Log("SocketViewerWinForms starting, dir = " + Path.GetFullPath(sockDir));
var listThread = new Thread(ListWorker) { IsBackground = true, Name = "window-list" };
listThread.Start();

try { Application.Run(form); }
catch (Exception ex) { Console.Error.WriteLine("Fatal: " + ex); }