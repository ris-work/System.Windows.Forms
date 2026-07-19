// SocketViewer.cs — single-file XplatUISocket browser/viewer. No classes.
// Run from the same working directory as the app (or set XPLAT_UI_SOCKET_DIR).
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SkiaSharp;

string sockDir = Environment.GetEnvironmentVariable("XPLAT_UI_SOCKET_DIR") ?? "windows";
string listPath = Path.Combine(sockDir, "window-list");

Console.OutputEncoding = Encoding.UTF8;

// ── shared state ──
List<(string handle, string title, string socket, int w, int h)> wins = new();
object winsLock = new();
int sel = 0;
bool quit = false;

// current window connection
Socket? curSock = null;
NetworkStream? curNs = null;
StreamReader? curRd = null;
Task? curReader = null;
object writeLock = new();
object frameLock = new();
SKBitmap? curFrame = null;
bool frameDirty = false;
bool dead = false;
string curTitle = "";
double lastScaleX = 1, lastScaleY = 1;
int lastCols = 1, lastCellRows = 1;
string format = "jpeg";
int quality = 75;

// ── helpers ──
void Send(string json)
{
    try
    {
        var b = Encoding.UTF8.GetBytes(json + "\n");
        lock (writeLock) { curNs?.Write(b, 0, b.Length); curNs?.Flush(); }
    }
    catch { dead = true; }
}

bool WaitChar(out char c)
{
    for (int i = 0; i < 60; i++)
    {
        if (Console.KeyAvailable) { c = Console.ReadKey(true).KeyChar; return true; }
        Thread.Sleep(2);
    }
    c = '\0'; return false;
}

Socket ConnectUnix(string path)
{
    var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
    s.Connect(new UnixDomainSocketEndPoint(path));
    return s;
}

void ParseWindowList(string line)
{
    try
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
            lock (winsLock) wins = nw;
        }
        else if (t == "window-added")
        {
            var w = r.GetProperty("window");
            lock (winsLock)
                wins.Add((w.GetProperty("handle").GetString() ?? "",
                          w.GetProperty("title").GetString() ?? "",
                          w.GetProperty("socket").GetString() ?? "",
                          w.GetProperty("width").GetInt32(),
                          w.GetProperty("height").GetInt32()));
        }
        else if (t == "window-removed")
        {
            var h = r.GetProperty("window").GetProperty("handle").GetString();
            lock (winsLock) wins.RemoveAll(x => x.handle == h);
        }
        else if (t == "window-updated")
        {
            var w = r.GetProperty("window");
            var h = w.GetProperty("handle").GetString() ?? "";
            lock (winsLock)
            {
                int i = wins.FindIndex(x => x.handle == h);
                if (i >= 0)
                    wins[i] = (h,
                               w.GetProperty("title").GetString() ?? "",
                               w.GetProperty("socket").GetString() ?? "",
                               w.GetProperty("width").GetInt32(),
                               w.GetProperty("height").GetInt32());
            }
        }
    }
    catch { }
}

void CloseCurrent()
{
    try { curSock?.Close(); } catch { }
    curSock = null; curNs = null; curRd = null;
    lock (frameLock) { curFrame?.Dispose(); curFrame = null; }
    dead = false;
}

void ConnectSelected()
{
    CloseCurrent();
    (string handle, string title, string socket, int w, int h) w;
    lock (winsLock)
    {
        if (wins.Count == 0) return;
        sel = ((sel % wins.Count) + wins.Count) % wins.Count;
        w = wins[sel];
    }
    try
    {
        curSock = ConnectUnix(w.socket);
        curNs = new NetworkStream(curSock, true);
        curRd = new StreamReader(curNs, Encoding.UTF8);
        curTitle = w.title;
        dead = false;
        curReader = Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = curRd.ReadLine()) != null)
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    var t = r.GetProperty("type").GetString();
                    if (t == "frame")
                    {
                        var data = r.GetProperty("data").GetString();
                        if (!string.IsNullOrEmpty(data))
                        {
                            var bmp = SKBitmap.Decode(Convert.FromBase64String(data));
                            if (bmp != null)
                                lock (frameLock) { curFrame?.Dispose(); curFrame = bmp; frameDirty = true; }
                        }
                    }
                    else if (t == "hello" || t == "info")
                    {
                        if (r.TryGetProperty("window", out var we) && we.TryGetProperty("title", out var tt))
                            curTitle = tt.GetString() ?? "";
                    }
                    else if (t == "bye") { dead = true; break; }
                }
            }
            catch { }
            dead = true;
        });
        Send("{\"type\":\"subscribe\",\"format\":\"" + format + "\",\"quality\":" + quality + "}");
        Send("{\"type\":\"refresh\",\"format\":\"" + format + "\",\"quality\":" + quality + "}");
    }
    catch { dead = true; }
}

void Render(SKBitmap frame)
{
    int cols = Math.Min(Math.Max(Console.WindowWidth, 20), 160);
    int maxCellRows = Math.Max(5, Console.WindowHeight - 2);
    double scale = (double)cols / frame.Width;
    int cellRows = (int)(frame.Height * scale / 2);
    if (cellRows > maxCellRows)
    {
        scale = (double)(maxCellRows * 2) / frame.Height;
        cols = Math.Max(1, (int)(frame.Width * scale));
        cellRows = maxCellRows;
    }
    if (cellRows < 1) cellRows = 1;
    int w = Math.Max(1, cols), h = Math.Max(1, cellRows * 2);

    using var small = new SKBitmap(w, h);
    using (var canvas = new SKCanvas(small))
    {
        canvas.Clear(SKColors.Black);
        using var img = SKImage.FromBitmap(frame);
        canvas.DrawImage(img, new SKRect(0, 0, frame.Width, frame.Height), new SKRect(0, 0, w, h),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), null);
    }

    var sb = new StringBuilder(w * cellRows * 24);
    sb.Append("\e[H");
    for (int cy = 0; cy < cellRows; cy++)
    {
        for (int cx = 0; cx < w; cx++)
        {
            var tp = small.GetPixel(cx, cy * 2);
            var bp = small.GetPixel(cx, Math.Min(cy * 2 + 1, h - 1));
            sb.Append("\e[38;2;").Append(tp.Red).Append(';').Append(tp.Green).Append(';').Append(tp.Blue).Append('m');
            sb.Append("\e[48;2;").Append(bp.Red).Append(';').Append(bp.Green).Append(';').Append(bp.Blue).Append('m');
            sb.Append('▀');
        }
        sb.Append("\e[0m");
        if (cy < cellRows - 1) sb.Append('\n');
    }
    Console.Out.Write(sb.ToString());

    lastScaleX = frame.Width / (double)w;
    lastScaleY = frame.Height / (double)h;
    lastCols = w; lastCellRows = cellRows;
}

void DrawStatus()
{
    int n; string t;
    lock (winsLock) { n = wins.Count; t = sel < n ? wins[sel].title : ""; }
    Console.Out.Write("\e[" + Console.WindowHeight + ";1H\e[0m\e[K");
    Console.Out.Write($"[{sel + 1}/{n}] {t}  ({format} q{quality})  [ ]=switch q=quit r=refresh p=png/jpeg +/-=quality");
    Console.Out.Flush();
}

string MapKeyName(ConsoleKey k) => k switch
{
    ConsoleKey.LeftArrow => "Left",
    ConsoleKey.RightArrow => "Right",
    ConsoleKey.UpArrow => "Up",
    ConsoleKey.DownArrow => "Down",
    ConsoleKey.Enter => "Return",
    ConsoleKey.Backspace => "Back",
    ConsoleKey.Spacebar => "Space",
    ConsoleKey.PageUp => "Prior",
    ConsoleKey.PageDown => "Next",
    ConsoleKey.Escape => "Escape",
    _ => k.ToString()
};

void SendKey(ConsoleKeyInfo k)
{
    string text = k.KeyChar >= 32 ? k.KeyChar.ToString() : "";
    Send("{\"type\":\"key\",\"key\":\"" + MapKeyName(k.Key) + "\",\"text\":\"" + text + "\"}");
}

void SendMouse(string type, int px, int py, string button)
{
    Send("{\"type\":\"" + type + "\",\"x\":" + px + ",\"y\":" + py + ",\"button\":\"" + button + "\"}");
}

void HandleMouse(int b, int cx, int cy, bool press)
{
    int px = (int)((cx - 1) * lastScaleX);
    int py = (int)((cy - 1) * lastScaleY);
    if ((b & 64) != 0)
    {
        Send("{\"type\":\"wheel\",\"x\":" + px + ",\"y\":" + py + ",\"delta\":" + (((b & 1) == 0) ? 120 : -120) + "}");
        return;
    }
    string btn = (b & 3) switch { 1 => "middle", 2 => "right", _ => "left" };
    if ((b & 32) != 0) { SendMouse("mousemove", px, py, btn); return; }
    SendMouse(press ? "mousedown" : "mouseup", px, py, btn);
}

void HandleEscape()
{
    if (!WaitChar(out var c1) || c1 != '[') { Send("{\"type\":\"key\",\"key\":\"Escape\"}"); return; }
    if (!WaitChar(out var c2)) return;
    switch (c2)
    {
        case 'A': Send("{\"type\":\"key\",\"key\":\"Up\"}"); return;
        case 'B': Send("{\"type\":\"key\",\"key\":\"Down\"}"); return;
        case 'C': Send("{\"type\":\"key\",\"key\":\"Right\"}"); return;
        case 'D': Send("{\"type\":\"key\",\"key\":\"Left\"}"); return;
        case '<': break;
        default: return;
    }
    var sb = new StringBuilder();
    char f;
    for (; ; )
    {
        if (!WaitChar(out var ch)) return;
        if (ch == 'M' || ch == 'm') { f = ch; break; }
        sb.Append(ch);
        if (sb.Length > 32) return;
    }
    var parts = sb.ToString().Split(';');
    if (parts.Length != 3) return;
    if (int.TryParse(parts[0], out int b) && int.TryParse(parts[1], out int mx) && int.TryParse(parts[2], out int my))
        HandleMouse(b, mx, my, f == 'M');
}

// ── connect to window-list ──
Socket? listSock = null;
for (int attempt = 0; attempt < 50 && listSock == null; attempt++)
{
    try { listSock = ConnectUnix(listPath); }
    catch
    {
        if (attempt == 0) Console.WriteLine($"Waiting for {listPath} ...");
        Thread.Sleep(200);
    }
}
if (listSock == null) { Console.WriteLine("No XplatUISocket server found."); return; }

var listNs = new NetworkStream(listSock, true);
var listRd = new StreamReader(listNs, Encoding.UTF8);
string? first = listRd.ReadLine();
if (first != null) ParseWindowList(first);
var listReader = Task.Run(() =>
{
    try { string? l; while ((l = listRd.ReadLine()) != null) ParseWindowList(l); } catch { }
});

// ── terminal setup ──
Console.Out.Write("\e[?1049h\e[?25l\e[?1002h\e[?1006h");
Console.Out.Flush();
try
{
    ConnectSelected();
    while (!quit)
    {
        if (dead)
        {
            lock (winsLock) { if (wins.Count == 0) { DrawStatus(); Thread.Sleep(200); continue; } }
            ConnectSelected();
        }

        SKBitmap? snap = null;
        lock (frameLock) { if (frameDirty) { snap = curFrame; frameDirty = false; } }
        if (snap != null) Render(snap);
        DrawStatus();

        if (Console.KeyAvailable)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape) HandleEscape();
            else if (k.KeyChar == 'q') quit = true;
            else if (k.KeyChar == ']') { sel++; ConnectSelected(); }
            else if (k.KeyChar == '[') { sel--; ConnectSelected(); }
            else if (k.KeyChar == 'r') Send("{\"type\":\"refresh\",\"format\":\"" + format + "\",\"quality\":" + quality + "}");
            else if (k.KeyChar == 'p') { format = format == "png" ? "jpeg" : "png"; Send("{\"type\":\"subscribe\",\"format\":\"" + format + "\",\"quality\":" + quality + "}"); }
            else if (k.KeyChar == '+') { quality = Math.Min(100, quality + 5); Send("{\"type\":\"subscribe\",\"format\":\"" + format + "\",\"quality\":" + quality + "}"); }
            else if (k.KeyChar == '-') { quality = Math.Max(1, quality - 5); Send("{\"type\":\"subscribe\",\"format\":\"" + format + "\",\"quality\":" + quality + "}"); }
            else SendKey(k);
        }
        else Thread.Sleep(10);
    }
}
finally
{
    Console.Out.Write("\e[?1002l\e[?1006l\e[?25h\e[?1049h\e[0m");
    Console.Out.Flush();
    CloseCurrent();
    try { listSock.Close(); } catch { }
}