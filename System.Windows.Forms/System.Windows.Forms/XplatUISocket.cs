// XplatUISocket.cs — XplatUI driver that presents each top-level window as a
// UNIX domain socket. Rendering is 100% virtual (Skia back buffers); frames are
// served as base64 PNG/JPEG inside newline-delimited JSON. No X11/Win32 FFI.

using SkiaSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace System.Windows.Forms
{
    internal class XplatUISocket : XplatUIDriver
    {
        // ── Singleton ────────────────────────────────────────────────────
        static volatile XplatUISocket Instance;
        static int RefCount;
        static readonly object instancelock = new object();

        static WinInfo desktopWi;

        public static XplatUISocket GetInstance()
        {
            lock (instancelock)
            {
                if (Instance == null) Instance = new XplatUISocket();
                RefCount++;
            }
            return Instance;
        }

        // ── Global state ─────────────────────────────────────────────────
        static readonly object Sync = new object();
        static string SockDir;
        static bool serverStarted;
        static Socket listListener;
        static readonly List<ClientConn> listWatchers = new List<ClientConn>();

        static readonly Dictionary<IntPtr, WinInfo> windows = new Dictionary<IntPtr, WinInfo>();      // every hwnd
        static readonly Dictionary<IntPtr, WinInfo> topWindows = new Dictionary<IntPtr, WinInfo>();     // socketed toplevels
        static readonly Dictionary<IntPtr, Thread> handleThread = new Dictionary<IntPtr, Thread>();
        static readonly Dictionary<Thread, SocketQueue> queues = new Dictionary<Thread, SocketQueue>();
        static SocketQueue mainQueue;
        static readonly List<Timer> timers = new List<Timer>();

        static IntPtr FosterParent;
        static IntPtr ActiveWindow;
        static IntPtr FocusWindow;
        static IntPtr GrabHwnd;
        static bool GrabConfined;
        static Rectangle GrabArea;
        static MouseButtons mouse_state;
        static Point mouse_position;
        static Keys mods;
        static IntPtr lastMouseHwnd;
        static bool themes_enabled;
        static long nextNative = 0x10000;
        static bool in_doevents;

        static int screenW = 1920, screenH = 1080;

        // double-click tracking
        static bool clickPending;
        static IntPtr clickHwnd;
        static Msg clickMsg;
        static IntPtr clickW, clickL;
        static int clickTime;
        const int DoubleClickInterval = 500;

        // ── Small helper types ───────────────────────────────────────────
        class SocketQueue
        {
            public Thread Thread;
            public readonly Queue<MSG> Msgs = new Queue<MSG>();
            public readonly object Lock = new object();
            public readonly ManualResetEventSlim Evt = new ManualResetEventSlim(false);
            public bool Quit;
            public int ExitCode;
            public void Enqueue(MSG m) { lock (Lock) Msgs.Enqueue(m); Evt.Set(); }
            public bool TryDequeue(out MSG m) { lock (Lock) { if (Msgs.Count > 0) { m = Msgs.Dequeue(); return true; } } m = default; return false; }
        }

        class WinInfo
        {
            public IntPtr Handle;
            public Hwnd Hwnd;
            public string Title = "";
            public bool IsTop;
            public Socket Listener;
            public string SockPath;
            public readonly List<ClientConn> Clients = new List<ClientConn>();
            public Bitmap Buffer;
            public readonly object BufLock = new object();
            public FormWindowState State = FormWindowState.Normal;
            public Rectangle SavedBounds;
            public bool IsDesktop;        // ← was "= true"; only desktopWi sets this
        }

        class ClientConn
        {
            public Socket Sock;
            public NetworkStream Ns;
            public StreamReader Rd;
            public WinInfo Win;
            public bool Subscribed;
            public string Format = "jpeg";
            public int Quality = 80;
            public bool Gray;
            public volatile bool Dead;

            public readonly object WLock = new object();
            public readonly object QueueLock = new object();
            public readonly Queue<string> OutQueue = new Queue<string>();   // control msgs
            public bool FrameWanted;                                        // coalesce slot
            public readonly ManualResetEventSlim HasWork = new ManualResetEventSlim(false);
            public Thread Writer;
            const int MaxCtrl = 64;

            public void StartWriter()
            {
                Writer = new Thread(WriterLoop) { IsBackground = true, Name = "conn-writer" };
                Writer.Start();
            }

            // Control messages (hello/info/pong/bye) — queued, capped.
            public void Send(string s)
            {
                if (Dead) return;
                lock (QueueLock)
                {
                    if (OutQueue.Count >= MaxCtrl) { Dead = true; HasWork.Set(); return; } // hopeless client: cut it, don't OOM
                    OutQueue.Enqueue(s);
                }
                HasWork.Set();
            }

            // Frames — coalesced to a single "wanted" flag; encoded at send time
            // on the writer thread, so it's ALWAYS the freshest state.
            public void RequestFrame()
            {
                if (Dead) return;
                lock (QueueLock) FrameWanted = true;
                HasWork.Set();
            }

            void WriterLoop()
            {
                try
                {
                    while (!Dead)
                    {
                        HasWork.Wait(500);
                        HasWork.Reset();
                        for (; ; )
                        {
                            string msg = null;
                            bool wantFrame = false;
                            lock (QueueLock)
                            {
                                if (OutQueue.Count > 0) msg = OutQueue.Dequeue();
                                else if (FrameWanted) { FrameWanted = false; wantFrame = true; }
                            }
                            if (msg != null) WriteRaw(msg);
                            else if (wantFrame) SendFrame(this);   // build+encode+write HERE, off the UI thread
                            else break;
                        }
                    }
                }
                catch { }
                Dead = true;
            }

            public void WriteRaw(string s)
            {
                var b = Encoding.UTF8.GetBytes(s + "\n");
                lock (WLock) { Ns.Write(b, 0, b.Length); Ns.Flush(); }
            }
        }

        sealed class SocketPaintEventArgs : PaintEventArgs
        {
            public SocketPaintEventArgs(Graphics g, Rectangle clip, WinInfo wi) : base(g, clip) { Win = wi; }
            public WinInfo Win;
        }

        // ── Ctor / init ──────────────────────────────────────────────────
        XplatUISocket()
        {
            var scr = Environment.GetEnvironmentVariable("XPLAT_SOCKET_SCREEN");
            if (scr != null)
            {
                var p = scr.Split('x', 'X', ',');
                if (p.Length == 2) { int.TryParse(p[0], out screenW); int.TryParse(p[1], out screenH); }
            }
        }

        internal override IntPtr InitializeDriver()
        {
            EnsureServer();
            return IntPtr.Zero;
        }

        internal override void ShutdownDriver(IntPtr token) { CleanupSockets(); }

        static void EnsureServer()
        {
            if (serverStarted) return;
            serverStarted = true;
            SockDir = Environment.GetEnvironmentVariable("XPLAT_UI_SOCKET_DIR") ?? "windows";
            Directory.CreateDirectory(SockDir);
            foreach (var f in Directory.GetFiles(SockDir, "*.sock")) { try { File.Delete(f); } catch { } }
            try { File.Delete(Path.Combine(SockDir, "window-list")); } catch { }

            listListener = ListenUnix(Path.Combine(SockDir, "window-list"));
            var t = new Thread(ListAcceptLoop) { IsBackground = true, Name = "XplatUISocket.List" };
            t.Start();

            desktopWi = new WinInfo { IsDesktop = true, SockPath = Path.Combine(SockDir, "compositor") };
            desktopWi.Listener = ListenUnix(desktopWi.SockPath);
            new Thread(() => WinAcceptLoop(desktopWi)) { IsBackground = true }.Start();

            static string DesktopJson() =>
    "{\"handle\":\"desktop\",\"title\":\"Desktop (compositor)\",\"x\":0,\"y\":0," +
    "\"width\":" + screenW + ",\"height\":" + screenH +
    ",\"visible\":true,\"state\":\"Normal\",\"compositor\":true,\"socket\":\"" +
    JEsc(desktopWi.SockPath) + "\"}";



            AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupSockets();
            Console.Error.WriteLine($"[XplatUISocket] serving windows in '{Path.GetFullPath(SockDir)}' (screen {screenW}x{screenH})");
        }

        static void CleanupSockets()
        {
            try
            {
                lock (Sync)
                {
                    foreach (var wi in topWindows.Values)
                    {
                        try { wi.Listener?.Close(); } catch { }
                        try { if (wi.SockPath != null && File.Exists(wi.SockPath)) File.Delete(wi.SockPath); } catch { }
                    }
                }
                try { listListener?.Close(); } catch { }
                try { File.Delete(Path.Combine(SockDir, "window-list")); } catch { }
                try { desktopWi?.Listener?.Close(); } catch { }
                try { if (desktopWi != null && File.Exists(desktopWi.SockPath)) File.Delete(desktopWi.SockPath); } catch { }
            }
            catch { }
        }

        static Socket ListenUnix(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            s.Bind(new UnixDomainSocketEndPoint(path));
            s.Listen(16);
            return s;
        }

        // ── Queues / threads ─────────────────────────────────────────────
        static SocketQueue ThreadQueue(Thread t)
        {
            lock (Sync)
            {
                if (!queues.TryGetValue(t, out var q))
                {
                    q = new SocketQueue { Thread = t };
                    queues[t] = q;
                    if (mainQueue == null) mainQueue = q;
                }
                return q;
            }
        }

        static SocketQueue QueueForHandle(IntPtr handle)
        {
            lock (Sync)
            {
                if (handleThread.TryGetValue(handle, out var t) && queues.TryGetValue(t, out var q))
                    return q;
            }
            return mainQueue ?? ThreadQueue(Thread.CurrentThread);
        }

        static void EnqueueMsg(IntPtr target, Msg m, IntPtr w, IntPtr l)
        {
            QueueForHandle(target).Enqueue(new MSG { hwnd = target, message = m, wParam = w, lParam = l });
        }

        // ── JSON helpers ─────────────────────────────────────────────────
        static string JEsc(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
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

        static string WindowJson(WinInfo wi)
        {
            if (wi == null || wi.IsDesktop || wi.Hwnd == null) return DesktopJson();
            var h = wi.Hwnd;
            return "{\"handle\":\"0x" + wi.Handle.ToInt64().ToString("X") +
                   "\",\"title\":\"" + JEsc(wi.Title) +
                   "\",\"x\":" + h.x + ",\"y\":" + h.y +
                   ",\"width\":" + h.width + ",\"height\":" + h.height +
                   ",\"visible\":" + (h.visible ? "true" : "false") +
                   ",\"state\":\"" + wi.State +
                   "\",\"socket\":\"" + JEsc(wi.SockPath) + "\"}";
        }

        static string DesktopJson() =>
            "{\"handle\":\"desktop\",\"title\":\"Desktop (compositor)\",\"x\":0,\"y\":0," +
            "\"width\":" + screenW + ",\"height\":" + screenH +
            ",\"visible\":true,\"state\":\"Normal\",\"compositor\":true,\"socket\":\"" +
            JEsc(desktopWi.SockPath) + "\"}";

        static string ListJson()
        {
            lock (Sync)
            {
                var sb = new StringBuilder("{\"type\":\"list\",\"screen\":[" + screenW + "," + screenH + "],\"windows\":[");
                sb.Append(DesktopJson());
                foreach (var wi in topWindows.Values)
                {
                    sb.Append(',');
                    sb.Append(WindowJson(wi));
                }
                sb.Append("]}");
                return sb.ToString();
            }
        }

        

        static void BroadcastList(string kind, WinInfo wi)
        {
            string msg = "{\"type\":\"" + kind + "\",\"window\":" + WindowJson(wi) + "}";
            lock (Sync)
            {
                foreach (var w in listWatchers.ToArray())
                {
                    w.Send(msg);
                    if (w.Dead) listWatchers.Remove(w);
                }
            }
        }

        // ── Socket accept loops ──────────────────────────────────────────
        static void ListAcceptLoop()
        {
            while (true)
            {
                Socket s;
                try { s = listListener.Accept(); } catch { break; }
                var cc = new ClientConn { Sock = s, Ns = new NetworkStream(s, true) };
                cc.Rd = new StreamReader(cc.Ns, Encoding.UTF8);
                lock (Sync) listWatchers.Add(cc);
                cc.StartWriter();
                cc.Send(ListJson());
                var t = new Thread(() => ListClientLoop(cc)) { IsBackground = true };
                t.Start();
            }
        }

        static void ListClientLoop(ClientConn cc)
        {
            try
            {
                string line;
                while ((line = cc.Rd.ReadLine()) != null)
                {
                    if (line.Contains("\"list\"")) cc.Send(ListJson());
                }
            }
            catch { }
            cc.Dead = true;
            lock (Sync) listWatchers.Remove(cc);
            try { cc.Sock.Close(); } catch { }
            finally
            {
                cc.Dead = true;
                cc.HasWork.Set();
            }
        }

        static void WinAcceptLoop(WinInfo wi)
        {
            while (true)
            {
                Socket s;
                try { s = wi.Listener.Accept(); } catch { break; }
                var cc = new ClientConn { Sock = s, Ns = new NetworkStream(s, true), Win = wi };
                cc.Rd = new StreamReader(cc.Ns, Encoding.UTF8);
                lock (Sync) wi.Clients.Add(cc);
                cc.StartWriter();
                string hello = "{\"type\":\"hello\",\"protocol\":1,\"screen\":[" + screenW + "," + screenH +
                               "],\"window\":" + (wi.IsDesktop ? DesktopJson() : WindowJson(wi)) + "}";
                cc.Send(hello);
                var t = new Thread(() => WinClientLoop(cc)) { IsBackground = true };
                t.Start();
            }
        }

        static void WinClientLoop(ClientConn cc)
        {
            try
            {
                string line;
                while ((line = cc.Rd.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    using (var doc = JsonDocument.Parse(line))
                        HandleClientMessage(cc, doc.RootElement);
                }
            }
            catch { }
            cc.Dead = true;
            lock (Sync) cc.Win?.Clients.Remove(cc);
            try { cc.Sock.Close(); }
            catch { }
            finally
            {
                cc.Dead = true;
                cc.HasWork.Set();
            }
        }

        // ── Frame encoding / pushing ─────────────────────────────────────
        static byte[] EncodeFrame(Bitmap bmp, string fmt, int quality, bool gray)
        {
            var src = bmp._skBitmap;
            SKBitmap use = src;
            if (gray)
            {
                use = new SKBitmap(src.Width, src.Height, SKColorType.Gray8, SKAlphaType.Opaque);
                unsafe
                {
                    byte* s = (byte*)src.GetPixels(); byte *d = (byte*)use.GetPixels();
                    for (int y = 0; y < src.Height; y++)
                    {
                        byte* sr = s + y * src.RowBytes; byte *dr = d + y * use.RowBytes;
                        for (int x = 0; x < src.Width; x++)
                        {
                            int b = sr[x * 4], g = sr[x * 4 + 1], r = sr[x * 4 + 2];
                            dr[x] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
                        }
                    }
                }
            }
            byte[] result = null;
            using (var img = SKImage.FromBitmap(use))
            using (var data = img.Encode(fmt == "jpeg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, quality))
                result = data?.ToArray();
            if (use != src) use.Dispose();
            if (result == null && gray)   // encoder rejected Gray8 (older Skia JPEG)
                return EncodeFrame(bmp, fmt, quality, false);
            return result;
        }
        static byte[] EncodeWindowFrame(WinInfo wi, string fmt, int q, bool gray)
        {
            Bitmap frame = BuildTopFrame(wi);
            if (frame == null) return null;

            var pops = PopupsFor(wi);
            Bitmap src = frame, comp = null;
            if (pops.Count > 0)
            {
                comp = new Bitmap(frame.Width, frame.Height);
                using (var g = Graphics.FromImage(comp))
                {
                    g.DrawImage(frame, 0, 0);
                    foreach (var p in pops)
                        using (var pf = BuildTopFrame(p))
                            if (pf != null)
                                g.DrawImage(pf, p.Hwnd.x - wi.Hwnd.x, p.Hwnd.y - wi.Hwnd.y);
                }
                src = comp;
            }
            var bytes = EncodeFrame(src, fmt, q, gray);
            comp?.Dispose();
            frame.Dispose();
            return bytes;
        }

        static void SendFrame(ClientConn cc)
        {
            var wi = cc.Win;
            if (wi == null) return;

            // SVG requested but server-side SVG is off: negotiate down to PNG
            if (cc.Format == "svg" && !SvgSupport.Enabled)
                cc.Format = "png";

            if (wi.IsDesktop)
            {
                if (cc.Format == "svg")
                {
                    string dsvg = BuildDesktopSvg();
                    cc.Send("{\"type\":\"frame\",\"format\":\"svg\",\"width\":" + screenW +
                            ",\"height\":" + screenH + ",\"data\":\"" + JEsc(dsvg) + "\"}");
                    return;
                }
                var desk = BuildDesktopFrame();
                var dbytes = EncodeFrame(desk, cc.Format, cc.Quality, cc.Gray);
                desk.Dispose();
                cc.Send("{\"type\":\"frame\",\"format\":\"" + cc.Format + "\",\"width\":" + screenW +
                        ",\"height\":" + screenH + ",\"data\":\"" +
                        (dbytes != null ? Convert.ToBase64String(dbytes) : "") + "\"}");
                return;
            }

            if (cc.Format == "svg")
            {
                string svg = BuildTopFrameSvg(wi);
                cc.Send("{\"type\":\"frame\",\"format\":\"svg\",\"width\":" + wi.Hwnd.width +
                        ",\"height\":" + wi.Hwnd.height + ",\"data\":\"" + JEsc(svg) + "\"}");
                return;
            }

            int w = wi.Hwnd.width, h = wi.Hwnd.height;
            byte[] bytes = EncodeWindowFrame(wi, cc.Format, cc.Quality, cc.Gray);
            if (bytes == null)
            {
                cc.Send("{\"type\":\"frame\",\"format\":\"" + cc.Format + "\",\"width\":" + w +
                        ",\"height\":" + h + ",\"data\":\"\"}");
            }
            else
            {
                if (wi.Buffer != null) { w = wi.Buffer.Width; h = wi.Buffer.Height; }
                cc.Send("{\"type\":\"frame\",\"format\":\"" + cc.Format + "\",\"width\":" + w +
                        ",\"height\":" + h + ",\"data\":\"" + Convert.ToBase64String(bytes) + "\"}");
            }
        }

        static void PushFrames(WinInfo wi)
        {
            ClientConn[] subs;
            lock (Sync) subs = wi.Clients.FindAll(c => c.Subscribed && !c.Dead).ToArray();
            foreach (var c in subs) c.RequestFrame();
        }


        void FullRedraw(WinInfo topWi)
        {
            if (topWi.IsDesktop)
            {
                List<WinInfo> tops;
                lock (Sync) tops = topWindows.Values.ToList();
                foreach (var t in tops) FullRedraw(t);
                return;
            }
            var top = topWi.Hwnd;
            if (top == null) return;
            var subtree = new List<IntPtr>();
            lock (Sync)
                foreach (var kv in windows)
                {
                    kv.Value.Buffer?.ClearSvg();
                    if (Toplevel(kv.Value.Hwnd) == top && !kv.Value.Hwnd.zombie)
                        subtree.Add(kv.Key);
                }

            var c = Control.FromHandle(top.Handle);
            Action work = () =>
            {
                foreach (var h in subtree)
                {
                    var hw = Hwnd.ObjectFromHandle(h);
                    if (hw == null || hw.zombie) continue;
                    AddExpose(hw, true, 0, 0, hw.width, hw.height);
                    UpdateWindow(h);
                }
            };
            if (c != null) c.BeginInvoke(work); else work();
        }

        static bool Retarget(ref WinInfo wi, ref int fx, ref int fy)
        {
            int sx = wi.IsDesktop ? fx : wi.Hwnd.x + fx;
            int sy = wi.IsDesktop ? fy : wi.Hwnd.y + fy;
            WinInfo hit = null;
            lock (Sync)
                for (int i = zOrder.Count - 1; i >= 0; i--)
                    if (topWindows.TryGetValue(zOrder[i], out var w) &&
                        w.Hwnd != null && w.Hwnd.visible && !w.Hwnd.zombie &&
                        sx >= w.Hwnd.x && sx < w.Hwnd.x + w.Hwnd.width &&
                        sy >= w.Hwnd.y && sy < w.Hwnd.y + w.Hwnd.height)
                    { hit = w; break; }
            if (hit == null) return false;               // clicked bare desktop
            if (hit != wi) { wi = hit; fx = sx - hit.Hwnd.x; fy = sy - hit.Hwnd.y; }
            return true;
        }

        static readonly List<IntPtr> zOrder = new List<IntPtr>();   // back = topmost

        static bool LooksPopup(WinInfo w) =>
            w.Hwnd != null && ((int)w.Hwnd.initial_style & (int)WindowStyles.WS_POPUP) != 0;

        static bool IsPopupOf(WinInfo popup, WinInfo owner)
        {
            for (var o = popup.Hwnd?.owner; o != null; o = o.owner)
                if (o.Handle == owner.Handle) return true;
            return false;
        }

        static List<WinInfo> PopupsFor(WinInfo owner)
        {
            var list = new List<WinInfo>();
            lock (Sync)
            {
                foreach (var w in topWindows.Values)
                {
                    if (w == owner || w.Hwnd == null || !w.Hwnd.visible || w.Hwnd.zombie) continue;
                    bool yes = IsPopupOf(w, owner);
                    // fallback for ownerless MWF menus/dropdowns: WS_POPUP over the active window
                    if (!yes && LooksPopup(w) && ActiveWindow == owner.Handle)
                    {
                        var a = new Rectangle(w.Hwnd.x, w.Hwnd.y, w.Hwnd.width, w.Hwnd.height);
                        var b = new Rectangle(owner.Hwnd.x, owner.Hwnd.y, owner.Hwnd.width, owner.Hwnd.height);
                        yes = a.IntersectsWith(b);
                    }
                    if (yes) list.Add(w);
                }
                list.Sort((x, y) => zOrder.IndexOf(x.Handle).CompareTo(zOrder.IndexOf(y.Handle)));
            }
            return list;
        }

        // ── Message handling from clients ────────────────────────────────
        static void HandleClientMessage(ClientConn cc, JsonElement root)
        {
            var wi = cc.Win;
            string type = root.TryGetProperty("type", out var te) ? te.GetString() : "";
            switch (type)
            {
                case "ping":
                    cc.Send("{\"type\":\"pong\"}");
                    break;

                case "getinfo":
                    cc.Send("{\"type\":\"info\",\"window\":" + (wi.IsDesktop ? DesktopJson() : WindowJson(wi)) + "}");
                    break;

                case "refresh":
                case "subscribe":
                    if (type == "subscribe") cc.Subscribed = true;
                    if (root.TryGetProperty("format", out var f))
                    {
                        var fs = f.GetString();
                        cc.Format = (fs == "png" || fs == "jpeg" || fs == "svg") ? fs : "jpeg";
                    }
                    if (root.TryGetProperty("quality", out var q)) cc.Quality = Math.Max(1, Math.Min(100, q.GetInt32()));
                    if (root.TryGetProperty("gray", out var ge)) cc.Gray = ge.GetBoolean();
                    if (root.TryGetProperty("full", out var fe) && fe.GetBoolean())
                        Instance.FullRedraw(wi);
                    SendFrame(cc);
                    break;

                case "unsubscribe":
                    cc.Subscribed = false;
                    break;

                case "mousedown": RouteMouse(wi, root, down: true, up: false); break;
                case "mouseup": RouteMouse(wi, root, down: false, up: true); break;
                case "click": RouteMouse(wi, root, down: true, up: true); break;
                case "mousemove": RouteMouseMove(wi, root); break;
                case "wheel": RouteWheel(wi, root); break;
                case "keydown": RouteKey(root, down: true, up: false); break;
                case "keyup": RouteKey(root, down: false, up: true); break;
                case "key": RouteKey(root, down: true, up: true); break;

                case "char":
                case "text":
                    {
                        string txt = root.TryGetProperty("text", out var tt) ? tt.GetString() : "";
                        foreach (var ch in txt)
                            EnqueueMsg(KeyTarget(), Msg.WM_CHAR, (IntPtr)ch, IntPtr.Zero);
                        break;
                    }

                case "resize":
                    {
                        if (wi.IsDesktop || wi.Hwnd == null) break;
                        int rw = root.GetProperty("width").GetInt32();
                        int rh = root.GetProperty("height").GetInt32();
                        var c = Control.FromHandle(wi.Handle);
                        if (c != null)
                            c.BeginInvoke((Action)(() => { try { c.ClientSize = new Size(rw, rh); } catch { } }));
                        break;
                    }

                case "close":
                    {
                        if (wi.IsDesktop || wi.Hwnd == null) break;
                        var c = Control.FromHandle(wi.Handle);
                        if (c is Form frm)
                            frm.BeginInvoke((Action)(() => { try { frm.Close(); } catch { } }));
                        else
                            EnqueueMsg(wi.Handle, Msg.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        break;
                    }
            }
        }

        // ── Input routing ────────────────────────────────────────────────
        static IntPtr MouseWParam()
        {
            int r = 0;
            if ((mouse_state & MouseButtons.Left) != 0) r |= (int)MsgButtons.MK_LBUTTON;
            if ((mouse_state & MouseButtons.Right) != 0) r |= (int)MsgButtons.MK_RBUTTON;
            if ((mouse_state & MouseButtons.Middle) != 0) r |= (int)MsgButtons.MK_MBUTTON;
            if ((mods & Keys.Shift) != 0) r |= (int)MsgButtons.MK_SHIFT;
            if ((mods & Keys.Control) != 0) r |= (int)MsgButtons.MK_CONTROL;
            return (IntPtr)r;
        }

        static IntPtr PackLP(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

        static void ActivateInternal(IntPtr handle)
        {
            if (ActiveWindow == handle || handle == IntPtr.Zero) return;
            var prev = ActiveWindow;
            ActiveWindow = handle;
            lock (Sync) { zOrder.Remove(handle); zOrder.Add(handle); }
            if (prev != IntPtr.Zero) SendMessageStatic(prev, Msg.WM_ACTIVATE, (IntPtr)WindowActiveFlags.WA_INACTIVE, IntPtr.Zero);
            SendMessageStatic(handle, Msg.WM_ACTIVATE, (IntPtr)WindowActiveFlags.WA_ACTIVE, IntPtr.Zero);
        }

        static IntPtr SendMessageStatic(IntPtr hwnd, Msg m, IntPtr w, IntPtr l) => NativeWindow.WndProc(hwnd, m, w, l);

        static Control HitTestDeep(Control parent, Point p)
        {
            for (int i = 0; i < parent.Controls.Count; i++)   // was: Count-1 down to 0
            {
                var c = parent.Controls[i];
                if (!c.Visible || !c.IsHandleCreated) continue;
                if (c.Bounds.Contains(p))
                    return HitTestDeep(c, new Point(p.X - c.Left, p.Y - c.Top));
            }
            return parent;
        }

        static Point OffsetInToplevel(Hwnd h, bool client)
        {
            int ox = client ? h.ClientRect.X : 0;
            int oy = client ? h.ClientRect.Y : 0;
            var cur = h;
            while (cur.parent != null)
            {
                ox += cur.x; oy += cur.y;
                cur = cur.parent;
                ox += cur.ClientRect.X; oy += cur.ClientRect.Y;
            }
            return new Point(ox, oy);
        }

        static Hwnd Toplevel(Hwnd h) { while (h != null && h.parent != null) h = h.parent; return h; }

        static void RouteMouse(WinInfo wi, JsonElement root, bool down, bool up)
        {
            int x = root.TryGetProperty("x", out var xe) ? xe.GetInt32() : 0;
            int y = root.TryGetProperty("y", out var ye) ? ye.GetInt32() : 0;
            if (!Retarget(ref wi, ref x, ref y)) return;
            string btn = root.TryGetProperty("button", out var be) ? be.GetString() : "left";
            MouseButtons button = btn == "right" ? MouseButtons.Right : btn == "middle" ? MouseButtons.Middle : MouseButtons.Left;

            ActivateInternal(wi.Handle);
            mouse_position = new Point(wi.Hwnd.x + x, wi.Hwnd.y + y);

            if (down)
            {
                mouse_state |= button;
                DoButton(wi, x, y, button, true);
            }
            if (up)
            {
                DoButton(wi, x, y, button, false);
                mouse_state &= ~button;
            }
        }

        static void DoButton(WinInfo wi, int fx, int fy, MouseButtons button, bool isDown)
        {
            int sx = wi.IsDesktop ? fx : wi.Hwnd.x + fx;
            int sy = wi.IsDesktop ? fy : wi.Hwnd.y + fy;
            mouse_position = new Point(sx, sy);

            ButtonMsgs(button, isDown, out Msg msgClient, out Msg msgNC, out Msg msgDbl);

            // (1) mouse capture: ALL buttons go to the grab window, in ITS space
            if (GrabHwnd != IntPtr.Zero)
            {
                var gh = Hwnd.ObjectFromHandle(GrabHwnd);
                if (gh != null)
                {
                    var gtop = Toplevel(gh);
                    var goff = OffsetInToplevel(gh, true);          // grab client offset in its toplevel frame
                    int tx = sx - gtop.x - goff.X;
                    int ty = sy - gtop.y - goff.Y;
                    EnqueueMsg(GrabHwnd, msgClient, MouseWParam(), PackLP(tx, ty));
                    EnqueueMsg(GrabHwnd, Msg.WM_MOUSEMOVE, MouseWParam(), PackLP(tx, ty));
                    return;
                }
                GrabHwnd = IntPtr.Zero;
            }

            // (2) topmost window under the point (popups beat their owner)
            var hit = TopmostAt(sx, sy);
            if (hit == null) return;                                // bare desktop
            DeliverButton(hit, sx - hit.Hwnd.x, sy - hit.Hwnd.y, msgClient, msgNC, msgDbl, isDown);
        }

        static void DeliverButton(WinInfo wi, int fx, int fy, Msg msgClient, Msg msgNC, Msg msgDbl, bool isDown)
        {
            var topHwnd = wi.Hwnd;
            ActivateInternal(wi.Handle);

            int cx = fx - topHwnd.ClientRect.X;
            int cy = fy - topHwnd.ClientRect.Y;
            if (cx < 0 || cy < 0 || cx >= topHwnd.ClientRect.Width || cy >= topHwnd.ClientRect.Height)
            {
                EnqueueMsg(topHwnd.Handle, msgNC, (IntPtr)HitTest.HTCLIENT, PackLP(fx, fy));
                return;
            }

            var topCtrl = Control.FromHandle(topHwnd.Handle);
            Control hitCtrl = topCtrl != null ? HitTestDeep(topCtrl, new Point(cx, cy)) : null;
            IntPtr target = hitCtrl != null && hitCtrl.IsHandleCreated ? hitCtrl.Handle : topHwnd.Handle;
            var hh = Hwnd.ObjectFromHandle(target);
            var off = hh != null ? OffsetInToplevel(hh, true) : new Point(topHwnd.ClientRect.X, topHwnd.ClientRect.Y);
            int tx = fx - off.X, ty = fy - off.Y;

            Msg finalMsg = msgClient;
            if (isDown)
            {
                int now = Environment.TickCount;
                if (clickPending && clickHwnd == target && clickMsg == msgClient &&
                    Math.Abs((clickL.ToInt32() & 0xFFFF) - (tx & 0xFFFF)) <= 4 &&
                    Math.Abs(((clickL.ToInt32() >> 16) & 0xFFFF) - (ty & 0xFFFF)) <= 4 &&
                    unchecked((uint)(now - clickTime)) < DoubleClickInterval)
                {
                    finalMsg = msgDbl;
                    clickPending = false;
                }
                else
                {
                    clickPending = true;
                    clickHwnd = target; clickMsg = msgClient; clickL = PackLP(tx, ty); clickTime = now;
                }
            }

            EnqueueMsg(target, finalMsg, MouseWParam(), PackLP(tx, ty));
            EnqueueMsg(target, Msg.WM_MOUSEMOVE, MouseWParam(), PackLP(tx, ty));
        }


        static void RouteMouseMove(WinInfo wi, JsonElement root)
        {
            int fx = root.TryGetProperty("x", out var xe) ? xe.GetInt32() : 0;
            int fy = root.TryGetProperty("y", out var ye) ? ye.GetInt32() : 0;
            if (!Retarget(ref wi, ref fx, ref fy)) return;
            var topHwnd = wi.Hwnd;
            mouse_position = new Point(topHwnd.x + fx, topHwnd.y + fy);

            int cx = fx - topHwnd.ClientRect.X;
            int cy = fy - topHwnd.ClientRect.Y;
            if (cx < 0 || cy < 0 || cx >= topHwnd.ClientRect.Width || cy >= topHwnd.ClientRect.Height)
            {
                EnqueueMsg(topHwnd.Handle, Msg.WM_NCMOUSEMOVE, (IntPtr)HitTest.HTCLIENT, PackLP(fx, fy));
                return;
            }

            IntPtr target;
            int tx, ty;
            if (GrabHwnd != IntPtr.Zero)
            {
                target = GrabHwnd;
                var gh = Hwnd.ObjectFromHandle(target);
                var off = gh != null ? OffsetInToplevel(gh, true) : new Point(0, 0);
                tx = fx - off.X; ty = fy - off.Y;
            }
            else
            {
                var topCtrl = Control.FromHandle(topHwnd.Handle);
                Control hit = topCtrl != null ? HitTestDeep(topCtrl, new Point(cx, cy)) : null;
                target = hit != null && hit.IsHandleCreated ? hit.Handle : topHwnd.Handle;
                var hh = Hwnd.ObjectFromHandle(target);
                var off = hh != null ? OffsetInToplevel(hh, true) : new Point(topHwnd.ClientRect.X, topHwnd.ClientRect.Y);
                tx = fx - off.X; ty = fy - off.Y;
            }

            if (target != lastMouseHwnd)
            {
                if (lastMouseHwnd != IntPtr.Zero)
                    EnqueueMsg(lastMouseHwnd, Msg.WM_MOUSELEAVE, IntPtr.Zero, IntPtr.Zero);
                EnqueueMsg(target, Msg.WM_MOUSE_ENTER, IntPtr.Zero, IntPtr.Zero);
                lastMouseHwnd = target;
            }
            EnqueueMsg(target, Msg.WM_MOUSEMOVE, MouseWParam(), PackLP(tx, ty));
        }

        static void RouteWheel(WinInfo wi, JsonElement root)
        {
            int delta = root.TryGetProperty("delta", out var de) ? de.GetInt32() : 120;
            int fx = root.TryGetProperty("x", out var xe) ? xe.GetInt32() : 0;
            int fy = root.TryGetProperty("y", out var ye) ? ye.GetInt32() : 0;
            IntPtr target = FocusWindow != IntPtr.Zero ? FocusWindow : wi.Handle;
            int mk = 0;
            if ((mods & Keys.Control) != 0) mk |= (int)MsgButtons.MK_CONTROL;
            if ((mods & Keys.Shift) != 0) mk |= (int)MsgButtons.MK_SHIFT;
            EnqueueMsg(target, Msg.WM_MOUSEWHEEL, (IntPtr)((delta << 16) | mk), PackLP(fx, fy));
        }

        static IntPtr KeyTarget() => FocusWindow != IntPtr.Zero ? FocusWindow : (ActiveWindow != IntPtr.Zero ? ActiveWindow : IntPtr.Zero);

        static void UpdateMods(Keys k, bool isDown)
        {
            Keys flag = 0;
            if (k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey) flag = Keys.Shift;
            else if (k == Keys.ControlKey || k == Keys.LControlKey || k == Keys.RControlKey) flag = Keys.Control;
            else if (k == Keys.Menu || k == Keys.LMenu || k == Keys.RMenu) flag = Keys.Alt;
            if (flag != 0) { if (isDown) mods |= flag; else mods &= ~flag; }
        }

        static string CharForKey(Keys k)
        {
            switch (k)
            {
                case Keys.Return: return "\r";
                case Keys.Back: return "\b";
                case Keys.Tab: return "\t";
                case Keys.Escape: return "\u001B";
                default: return null;
            }
        }

        // inside RouteKey, replace the "if (down) { ... }" block with:
        static void RouteKey(JsonElement root, bool down, bool up)
        {
            string name = root.TryGetProperty("key", out var ke) ? ke.GetString() : "";
            string text = root.TryGetProperty("text", out var te) ? te.GetString() : null;
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(text)) return;

            Keys k = Keys.None;
            if (!string.IsNullOrEmpty(name)) Enum.TryParse(name, true, out k);
            var target = KeyTarget();
            if (target == IntPtr.Zero) return;

            if (down)
            {
                UpdateMods(k, true);
                EnqueueMsg(target, Msg.WM_KEYDOWN, (IntPtr)(int)k, IntPtr.Zero);
                var chars = !string.IsNullOrEmpty(text) ? text : CharForKey(k);
                if (chars != null)
                    foreach (var ch in chars)
                        EnqueueMsg(target, Msg.WM_CHAR, (IntPtr)ch, IntPtr.Zero);
            }
            if (up)
            {
                EnqueueMsg(target, Msg.WM_KEYUP, (IntPtr)(int)k, (IntPtr)unchecked((int)0xC0000000));
                UpdateMods(k, false);
            }
        }

        // ── Window bookkeeping ───────────────────────────────────────────
        static WinInfo RegisterWindow(Hwnd hwnd, CreateParams cp)
        {
            var wi = new WinInfo { Handle = hwnd.Handle, Hwnd = hwnd, Title = cp.Caption ?? "" };
            bool isTop = cp.Parent == IntPtr.Zero && (cp.Style & (int)WindowStyles.WS_CHILD) == 0;
            wi.IsTop = isTop;
            lock (Sync)
            {
                windows[wi.Handle] = wi;
                handleThread[wi.Handle] = Thread.CurrentThread;
                if (isTop)
                {
                    topWindows[wi.Handle] = wi;
                    zOrder.Remove(wi.Handle);
                    zOrder.Add(wi.Handle);           // back = topmost
                }
            }
            if (isTop)
            {
                EnsureServer();
                wi.SockPath = Path.Combine(SockDir, "win-" + wi.Handle.ToInt64().ToString("X") + ".sock");
                wi.Listener = ListenUnix(wi.SockPath);
                var t = new Thread(() => WinAcceptLoop(wi)) { IsBackground = true };
                t.Start();
                BroadcastList("window-added", wi);
            }
            return wi;
        }

        static Bitmap GetBuffer(WinInfo wi, int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            if (wi.Buffer == null || wi.Buffer.Width != w || wi.Buffer.Height != h)
            {
                wi.Buffer?.Dispose();
                wi.Buffer = new Bitmap(w, h);
                var c = Control.FromHandle(wi.Handle);
                var bg = c != null ? c.BackColor : SystemColors.Control;
                using (var g = Graphics.FromImage(wi.Buffer)) g.Clear(bg);
            }
            return wi.Buffer;
        }

        static Bitmap BuildDesktopFrame()
        {
            List<WinInfo> vis;
            lock (Sync)
                vis = zOrder
                    .Select(h => topWindows.TryGetValue(h, out var w) ? w : null)
                    .Where(w => w?.Hwnd != null && w.Hwnd.visible && !w.Hwnd.zombie)
                    .ToList();
            var bmp = new Bitmap(screenW, screenH);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(58, 58, 64));
                foreach (var w in vis)
                    using (var f = BuildTopFrame(w))
                        g.DrawImage(f, w.Hwnd.x, w.Hwnd.y);
            }
            return bmp;
        }


        // Desktop SVG: every visible top-level in z-order; popups ride inside their
        // owner's document instead of being emitted twice.
        static string BuildDesktopSvg()
        {
            List<WinInfo> vis;
            lock (Sync)
                vis = zOrder
                    .Select(h => topWindows.TryGetValue(h, out var w) ? w : null)
                    .Where(w => w?.Hwnd != null && w.Hwnd.visible && !w.Hwnd.zombie)
                    .ToList();

            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
                      $"width=\"{screenW}\" height=\"{screenH}\" viewBox=\"0 0 {screenW} {screenH}\">");
            sb.Append($"<rect x=\"0\" y=\"0\" width=\"{screenW}\" height=\"{screenH}\" fill=\"#3A3A40\"/>");
            foreach (var w in vis)
            {
                bool ridesAlong = vis.Any(o => o != w && IsPopupOf(w, o));
                if (ridesAlong) continue;
                sb.Append(Nest(BuildTopFrameSvg(w), w.Hwnd.x, w.Hwnd.y, w.Hwnd.width, w.Hwnd.height));
            }
            sb.Append("</svg>");
            return sb.ToString();
        }



        void AddExpose(Hwnd hwnd, bool client, int x, int y, int w, int h)
        {
            if (hwnd == null || hwnd.zombie) return;
            if (client)
            {
                hwnd.AddInvalidArea(x, y, w, h);
                if (!hwnd.expose_pending)
                {
                    hwnd.expose_pending = true;
                    EnqueueMsg(hwnd.Handle, Msg.WM_PAINT, IntPtr.Zero, IntPtr.Zero);
                }
            }
            else
            {
                hwnd.AddNcInvalidArea(x, y, w, h);
                if (!hwnd.nc_expose_pending)
                {
                    hwnd.nc_expose_pending = true;
                    EnqueueMsg(hwnd.Handle, Msg.WM_NCPAINT, (IntPtr)1, IntPtr.Zero);
                }
            }
        }

        void PerformNCCalc(Hwnd hwnd)
        {
            var ncp = new XplatUIWin32.NCCALCSIZE_PARAMS();
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(ncp));
            ncp.rgrc1.left = 0; ncp.rgrc1.top = 0;
            ncp.rgrc1.right = hwnd.Width; ncp.rgrc1.bottom = hwnd.Height;
            Marshal.StructureToPtr(ncp, ptr, true);
            NativeWindow.WndProc(hwnd.client_window, Msg.WM_NCCALCSIZE, (IntPtr)1, ptr);
            ncp = (XplatUIWin32.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(ptr, typeof(XplatUIWin32.NCCALCSIZE_PARAMS));
            Marshal.FreeHGlobal(ptr);
            hwnd.ClientRect = new Rectangle(ncp.rgrc1.left, ncp.rgrc1.top,
                ncp.rgrc1.right - ncp.rgrc1.left, ncp.rgrc1.bottom - ncp.rgrc1.top);
        }

        // ── Driver: window lifecycle ─────────────────────────────────────
        internal override IntPtr CreateWindow(CreateParams cp)
        {
            EnsureServer();
            var hwnd = new Hwnd();

            int X = cp.X, Y = cp.Y, W = Math.Max(1, cp.Width), H = Math.Max(1, cp.Height);
            if (cp.control is Form && cp.X == int.MinValue && cp.Y == int.MinValue)
            {
                var next = Hwnd.GetNextStackedFormLocation(cp);
                X = next.X; Y = next.Y;
            }

            hwnd.x = X; hwnd.y = Y; hwnd.width = W; hwnd.height = H;
            hwnd.parent = Hwnd.ObjectFromHandle(cp.Parent);
            hwnd.initial_style = cp.WindowStyle;
            hwnd.initial_ex_style = cp.WindowExStyle;
            hwnd.visible = false;
            hwnd.mapped = false;
            if ((cp.Style & (int)WindowStyles.WS_DISABLED) != 0) hwnd.enabled = false;

            hwnd.WholeWindow = (IntPtr)Interlocked.Increment(ref nextNative);
            hwnd.ClientWindow = (IntPtr)Interlocked.Increment(ref nextNative);
            hwnd.ClientRect = new Rectangle(0, 0, W, H);

           

            if (hwnd.parent == null && (cp.Style & (int)WindowStyles.WS_CHILD) != 0)
            {
                // park under foster parent until reparented
                if (FosterParent == IntPtr.Zero)
                {
                    var fp = new Hwnd();
                    fp.x = 0; fp.y = 0; fp.width = 1; fp.height = 1;
                    fp.WholeWindow = fp.ClientWindow = (IntPtr)Interlocked.Increment(ref nextNative);
                    fp.ClientRect = new Rectangle(0, 0, 1, 1);
                    FosterParent = fp.Handle;
                }
                hwnd.parent = Hwnd.ObjectFromHandle(FosterParent);
            }

            //SetHwndStyles(hwnd, cp);      // sets border_style/border_static/title_style/caption heights
            var wi = RegisterWindow(hwnd, cp);
            PerformNCCalc(hwnd);
            AddExpose(hwnd, false, 0, 0, hwnd.width, hwnd.height);   // initial NC/border paint

            // MWF draws its own decorations for tool windows
            if (cp.control is Form form && cp.IsSet(WindowExStyles.WS_EX_TOOLWINDOW) && form.window_manager == null)
                form.window_manager = new ToolWindowManager(form);

            SendMessageStatic(hwnd.Handle, Msg.WM_CREATE, (IntPtr)1, IntPtr.Zero);

            if ((cp.Style & (int)WindowStyles.WS_MINIMIZE) != 0) wi.State = FormWindowState.Minimized;
            else if ((cp.Style & (int)WindowStyles.WS_MAXIMIZE) != 0) wi.State = FormWindowState.Maximized;

            if ((cp.Style & (int)WindowStyles.WS_VISIBLE) != 0)
                SetVisible(hwnd.Handle, true, true);

            return hwnd.zombie ? IntPtr.Zero : hwnd.Handle;
        }

        internal override IntPtr CreateWindow(IntPtr Parent, int X, int Y, int Width, int Height)
        {
            var cp = new CreateParams
            {
                Caption = "",
                X = X,
                Y = Y,
                Width = Width,
                Height = Height,
                ClassName = XplatUI.GetDefaultClassName(GetType()),
                ClassStyle = 0,
                ExStyle = 0,
                Parent = IntPtr.Zero,
                Param = 0
            };
            return CreateWindow(cp);
        }

        internal override void DestroyWindow(IntPtr handle)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null || hwnd.zombie) return;

            WinInfo wi;
            lock (Sync) windows.TryGetValue(handle, out wi);

            CleanupCachedWindows(hwnd);

            var list = new ArrayList();
            AccumulateDestroyedHandles(Control.ControlNativeWindow.ControlFromHandle(handle), list);
            if (!list.Contains(hwnd)) list.Add(hwnd);

            foreach (Hwnd h in list)
                SendMessageStatic(h.Handle, Msg.WM_DESTROY, IntPtr.Zero, IntPtr.Zero);

            if (wi != null)
            {
                lock (Sync)
                {
                    windows.Remove(handle);
                    handleThread.Remove(handle);
                    if (wi.IsTop) { topWindows.Remove(handle); zOrder.Remove(handle); }
                }
                if (wi.IsTop)
                {
                    BroadcastList("window-removed", wi);
                    ClientConn[] cs;
                    lock (Sync) cs = wi.Clients.ToArray();
                    foreach (var c in cs) { c.Send("{\"type\":\"bye\"}"); try { c.Sock.Close(); } catch { } }
                    try { wi.Listener?.Close(); } catch { }
                    try { if (wi.SockPath != null && File.Exists(wi.SockPath)) File.Delete(wi.SockPath); } catch { }
                    lock (wi.BufLock) { wi.Buffer?.Dispose(); wi.Buffer = null; }
                }
            }

            foreach (Hwnd h in list)
            {
                WinInfo hw;
                lock (Sync)
                {
                    if (windows.TryGetValue(h.Handle, out hw))
                    {
                        windows.Remove(h.Handle);
                        handleThread.Remove(h.Handle);
                        if (hw.IsTop) topWindows.Remove(h.Handle);
                    }
                }
                if (hw != null)
                    lock (hw.BufLock) { hw.Buffer?.Dispose(); hw.Buffer = null; }

                h.zombie = true;
                h.expose_pending = h.nc_expose_pending = false;
                h.Dispose();
            }

            if (hwnd.parent != null)
            {
                AddExpose(hwnd.parent, true, 0, 0, hwnd.parent.width, hwnd.parent.height);
                var ptop = Toplevel(hwnd.parent);
                WinInfo pwi;
                lock (Sync) windows.TryGetValue(ptop.Handle, out pwi);
                if (pwi != null) PushRelated(pwi);
            }
        }

        void AccumulateDestroyedHandles(Control c, ArrayList list)
        {
            if (c == null) return;
            var controls = c.Controls.GetAllControls();
            if (c.IsHandleCreated && !c.IsDisposed)
            {
                var hwnd = Hwnd.ObjectFromHandle(c.Handle);
                if (hwnd != null && !list.Contains(hwnd)) { list.Add(hwnd); CleanupCachedWindows(hwnd); }
            }
            foreach (var ch in controls) AccumulateDestroyedHandles(ch, list);
        }

        void CleanupCachedWindows(Hwnd hwnd)
        {
            if (ActiveWindow == hwnd.Handle)
            {
                SendMessageStatic(hwnd.client_window, Msg.WM_ACTIVATE, (IntPtr)WindowActiveFlags.WA_INACTIVE, IntPtr.Zero);
                ActiveWindow = IntPtr.Zero;
            }
            if (FocusWindow == hwnd.Handle)
            {
                SendMessageStatic(hwnd.client_window, Msg.WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero);
                FocusWindow = IntPtr.Zero;
            }
            if (GrabHwnd == hwnd.Handle) { GrabHwnd = IntPtr.Zero; GrabConfined = false; }
            if (lastMouseHwnd == hwnd.Handle) lastMouseHwnd = IntPtr.Zero;
        }

        // ── Driver: painting (fully virtual) ─────────────────────────────
        internal override PaintEventArgs PaintEventStart(ref Message msg, IntPtr handle, bool client)
        {
            var hwnd = Hwnd.ObjectFromHandle(msg.HWnd);        // invalid-region owner
            var paint_hwnd = Hwnd.ObjectFromHandle(handle);    // drawable owner
            if (!paint_hwnd.visible)
                return new PaintEventArgs(Graphics.FromImage(new Bitmap(1, 1)), Rectangle.Empty);
            if (hwnd == null) hwnd = paint_hwnd;
            if (paint_hwnd == null)
                return new PaintEventArgs(Graphics.FromImage(new Bitmap(1, 1)), Rectangle.Empty);

            WinInfo wi;
            lock (Sync) windows.TryGetValue(paint_hwnd.Handle, out wi);
            if (wi == null)
                return new PaintEventArgs(Graphics.FromImage(new Bitmap(1, 1)), Rectangle.Empty);

            Monitor.Enter(wi.BufLock);
            try
            {
                // Buffer is the WHOLE window area (native semantics: client area
                // starts at ClientRect.X/Y inside it).
                var bmp = GetBuffer(wi, paint_hwnd.width, paint_hwnd.height);
                Graphics dc = Graphics.FromImage(bmp);
                if (client && (paint_hwnd.ClientRect.X != 0 || paint_hwnd.ClientRect.Y != 0))
                    dc.TranslateTransform(paint_hwnd.ClientRect.X, paint_hwnd.ClientRect.Y);

                // Update-region clip (perf + parity). Even if MWF later replaces the
                // clip, nothing can leak: compositing clips to our bounds.
                Rectangle bounds = client
                    ? new Rectangle(0, 0, paint_hwnd.ClientRect.Width, paint_hwnd.ClientRect.Height)
                    : new Rectangle(0, 0, paint_hwnd.width, paint_hwnd.height);
                Rectangle clip = bounds;
                // A repaint covering ~(almost) the whole control starts a new vector
                // generation: drop accumulated SVG ops (pixels are unaffected).
                if (client && wi.Buffer != null && !hwnd.Invalid.IsEmpty &&
                    hwnd.Invalid.Width >= bounds.Width * 0.9f &&
                    hwnd.Invalid.Height >= bounds.Height * 0.9f)
                    wi.Buffer.ClearSvg();
                if (client && !hwnd.Invalid.IsEmpty) clip = Rectangle.Intersect(bounds, hwnd.Invalid);
                else if (!client && !hwnd.nc_invalid.IsEmpty) clip = Rectangle.Intersect(bounds, hwnd.nc_invalid);
                if (!clip.IsEmpty) dc.SetClip(clip);

                // Plain-control borders — the X11 driver draws these itself.
                if (!client)
                {
                    if (hwnd.border_style == FormBorderStyle.Fixed3D)
                        ControlPaint.DrawBorder3D(dc, new Rectangle(0, 0, hwnd.Width, hwnd.Height),
                            hwnd.border_static ? Border3DStyle.SunkenOuter : Border3DStyle.Sunken);
                    else if (hwnd.border_style == FormBorderStyle.FixedSingle)
                        ControlPaint.DrawBorder(dc, new Rectangle(0, 0, hwnd.Width, hwnd.Height),
                            Color.Black, ButtonBorderStyle.Solid);
                }

                if (client) { hwnd.expose_pending = false; hwnd.ClearInvalidArea(); }
                else { hwnd.nc_expose_pending = false; hwnd.ClearNcInvalidArea(); }
                return new SocketPaintEventArgs(dc, clip, wi);
            }
            catch { Monitor.Exit(wi.BufLock); throw; }
        }


        internal override void PaintEventEnd(ref Message msg, IntPtr handle, bool client, PaintEventArgs pevent)
        {
            if (pevent.Graphics != null) { pevent.Graphics.Flush(); pevent.Graphics.Dispose(); }
            pevent.SetGraphics(null);
            if (pevent is SocketPaintEventArgs spea && spea.Win != null)
            {
                var wi = spea.Win;
                Monitor.Exit(wi.BufLock);
                var top = Toplevel(wi.Hwnd);
                WinInfo topWi;
                lock (Sync) windows.TryGetValue(top.Handle, out topWi);
                if (topWi != null) PushRelated(topWi);
            }
            pevent.Dispose();
        }

        // Blit wi's whole-window buffer into the top-level frame buffer, clipped to
        // wi's bounds and excluding wi's children when WS_CLIPCHILDREN is set.
        static void CompositeBlit(WinInfo wi)
        {
            var hwnd = wi.Hwnd;
            var top = Toplevel(hwnd);
            WinInfo topWi;
            lock (Sync) windows.TryGetValue(top.Handle, out topWi);
            if (topWi == null) return;

            lock (topWi.BufLock)
            {
                if (topWi.Buffer == null || wi.Buffer == null) return;
                var off = OffsetInToplevel(hwnd, false);
                var dest = new Rectangle(off.X, off.Y, hwnd.width, hwnd.height);
                dest = Rectangle.Intersect(dest, new Rectangle(0, 0, topWi.Buffer.Width, topWi.Buffer.Height));
                if (dest.IsEmpty) return;

                using (var g = Graphics.FromImage(topWi.Buffer))
                {
                    g.SetClip(dest);

                    // Compositor stencil: never blit over a visible child region.
                    // Do NOT gate this on WS_CLIPCHILDREN — the child's own buffer
                    // is authoritative for its pixels in every case.
                    var ctrl = Control.FromHandle(hwnd.Handle);
                    if (ctrl != null)
                    {
                        foreach (Control child in ctrl.Controls)
                        {
                            if (!child.Visible || !child.IsHandleCreated) continue;
                            var ch = Hwnd.ObjectFromHandle(child.Handle);
                            if (ch == null) continue;
                            var coff = OffsetInToplevel(ch, false);
                            g.ExcludeClip(new Rectangle(coff.X, coff.Y, ch.width, ch.height));
                        }
                    }

                    g.DrawImage(wi.Buffer, dest,
                        new Rectangle(dest.X - off.X, dest.Y - off.Y, dest.Width, dest.Height),
                        GraphicsUnit.Pixel);
                }
            }
        }

        // ── Frame rendering: full-tree painter's algorithm ──────────────
        // Every hwnd paints into its own buffer (unchanged). The served frame is
        // rebuilt from the control tree on each send: parents first, then children
        // bottom-of-z to top-of-z. No stencil/exclusion, no ordering assumptions.
        static Bitmap BuildTopFrame(WinInfo topWi)
        {
            var top = topWi.Hwnd;
            var frame = new Bitmap(Math.Max(1, top.width), Math.Max(1, top.height));
            using (var g = Graphics.FromImage(frame))
            {
                lock (topWi.BufLock)
                    if (topWi.Buffer != null)
                        g.DrawImage(topWi.Buffer, 0, 0);

                var c = Control.FromHandle(top.Handle);
                if (c != null) RenderChildren(c, g);
            }
            DrawCaretIfAny(top, frame);
            return frame;   // caller disposes
        }
        // A window's own SVG: its buffer (vectors or embedded PNG) + every visible
        // descendant's buffer as nested <svg>. No popup merging here.
        static string WindowOwnSvg(WinInfo wi)
        {
            var h = wi.Hwnd;
            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
                      $"width=\"{h.width}\" height=\"{h.height}\" viewBox=\"0 0 {h.width} {h.height}\">");
            lock (wi.BufLock)
                if (wi.Buffer != null)
                    sb.Append(Nest(wi.Buffer.AsSvg(), 0, 0, h.width, h.height));
            var c = Control.FromHandle(h.Handle);
            if (c != null) SvgChildren(c, sb);
            sb.Append("</svg>");
            return sb.ToString();
        }


        // Frame SVG for one window: own SVG + visible popups merged on top.
        static string BuildTopFrameSvg(WinInfo topWi)
        {
            var top = topWi.Hwnd;
            var own = WindowOwnSvg(topWi);
            var pops = PopupsFor(topWi);
            if (pops.Count == 0) return own;

            // inject popups before the closing tag of `own`
            int close = own.LastIndexOf("</svg>", StringComparison.Ordinal);
            var sb = new StringBuilder(close >= 0 ? own.Substring(0, close) : own);
            foreach (var p in pops)
                sb.Append(Nest(WindowOwnSvg(p),
                               p.Hwnd.x - top.x, p.Hwnd.y - top.y,
                               p.Hwnd.width, p.Hwnd.height));
            sb.Append("</svg>");
            return sb.ToString();
        }



        static string RenderTopFrameSvg(WinInfo wi) =>
            wi.Buffer != null ? wi.Buffer.AsSvg() : null;   // popup chrome; children ride along via NestedSvg below

        static void SvgChildren(Control parent, StringBuilder sb)
        {
            for (int i = parent.Controls.Count - 1; i >= 0; i--)
            {
                var c = parent.Controls[i];
                if (!c.Visible || !c.IsHandleCreated) continue;
                var h = Hwnd.ObjectFromHandle(c.Handle);
                if (h == null || h.zombie) continue;
                WinInfo wi;
                lock (Sync) windows.TryGetValue(c.Handle, out wi);
                if (wi != null)
                {
                    var off = OffsetInToplevel(h, false);
                    lock (wi.BufLock)
                        if (wi.Buffer != null)
                            sb.Append(Nest(wi.Buffer.AsSvg(), off.X, off.Y, h.width, h.height));
                }
                SvgChildren(c, sb);
            }
        }

        static string Nest(string inner, int x, int y, int w, int h) =>
            $"<svg x=\"{x}\" y=\"{y}\" width=\"{w}\" height=\"{h}\">{inner}</svg>";

        static void DrawCaretIfAny(Hwnd top, Bitmap frame)
        {
            IntPtr cw; int cx, cy, cw2, ch2;
            lock (caret)
            {
                if (!caret.Visible || !caret.On || caret.Hwnd == IntPtr.Zero) return;
                cw = caret.Hwnd; cx = caret.X; cy = caret.Y; cw2 = caret.W; ch2 = caret.H;
            }
            var h = Hwnd.ObjectFromHandle(cw);
            if (h == null || Toplevel(h) != top) return;

            var off = OffsetInToplevel(h, true);          // caret pos is client-relative
            int rx = off.X + cx, ry = off.Y + cy;
            int rw = Math.Max(1, cw2), rh = Math.Max(1, ch2);
            if (rx < 0) { rw += rx; rx = 0; }
            if (ry < 0) { rh += ry; ry = 0; }
            rw = Math.Min(rw, frame.Width - rx);
            rh = Math.Min(rh, frame.Height - ry);
            if (rw <= 0 || rh <= 0) return;

            var skb = frame._skBitmap;
            unsafe
            {
                byte* basePtr = (byte*)skb.GetPixels();
                int stride = skb.RowBytes;
                for (int y = 0; y < rh; y++)
                {
                    byte* row = basePtr + (ry + y) * stride + rx * 4;
                    for (int x = 0; x < rw; x++)
                    {
                        row[0] = (byte)(255 - row[0]);
                        row[1] = (byte)(255 - row[1]);
                        row[2] = (byte)(255 - row[2]);
                        row += 4;
                    }
                }
            }
        }

        static void RenderChildren(Control parent, Graphics g)
        {
            // Controls[0] == TOP of z-order, so iterate backwards (bottom first).
            for (int i = parent.Controls.Count - 1; i >= 0; i--)
            {
                var c = parent.Controls[i];
                if (!c.Visible || !c.IsHandleCreated) continue;      // hidden subtrees never render
                var h = Hwnd.ObjectFromHandle(c.Handle);
                if (h == null || h.zombie) continue;

                WinInfo wi;
                lock (Sync) windows.TryGetValue(c.Handle, out wi);
                if (wi != null)
                {
                    var off = OffsetInToplevel(h, false);
                    lock (wi.BufLock)
                        if (wi.Buffer != null)
                            g.DrawImage(wi.Buffer, off.X, off.Y);   // SrcOver: alpha just works
                }
                RenderChildren(c, g);
            }
        }

        static void PushRelated(WinInfo wi)
        {
            PushFrames(wi);
            if (desktopWi != null && wi != desktopWi) PushFrames(desktopWi);
            List<WinInfo> tops;
            lock (Sync) tops = topWindows.Values.ToList();
            foreach (var t in tops)
                if (t != wi && (IsPopupOf(t, wi) || IsPopupOf(wi, t)))
                    PushFrames(t);
        }

        static void Composite(WinInfo wi)
        {
            var hwnd = wi.Hwnd;
            var top = Toplevel(hwnd);
            if (hwnd == top)
            {
                PushRelated(wi);       // was PushFrames
                return;
            }

            CompositeBlit(wi);

            var parentHwnd = hwnd.parent;
            var parentCtrl = parentHwnd != null ? Control.FromHandle(parentHwnd.Handle) : null;
            var me = Control.FromHandle(hwnd.Handle);
            if (parentCtrl != null && me != null)
            {
                int myIdx = parentCtrl.Controls.IndexOf(me);
                for (int i = 0; i < myIdx; i++)
                {
                    var s = parentCtrl.Controls[i];
                    if (!s.Visible || !s.IsHandleCreated || !s.Bounds.IntersectsWith(me.Bounds)) continue;
                    WinInfo swi;
                    lock (Sync) windows.TryGetValue(s.Handle, out swi);
                    if (swi != null && swi.Buffer != null) CompositeBlit(swi);
                }
            }

            WinInfo topWi;
            lock (Sync) windows.TryGetValue(top.Handle, out topWi);
            if (topWi != null) PushRelated(topWi);   // was PushFrames
        }



        internal override void UpdateWindow(IntPtr handle)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null || !hwnd.expose_pending) return;
            SendMessageStatic(handle, Msg.WM_PAINT, IntPtr.Zero, IntPtr.Zero);
        }

        internal override void Invalidate(IntPtr handle, Rectangle rc, bool clear)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            if (clear) AddExpose(hwnd, true, 0, 0, hwnd.Width, hwnd.Height);
            else AddExpose(hwnd, true, rc.X, rc.Y, rc.Width, rc.Height);
        }

        internal override void InvalidateNC(IntPtr handle)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            AddExpose(hwnd, false, 0, 0, hwnd.Width, hwnd.Height);
        }

        internal override void ScrollWindow(IntPtr handle, Rectangle area, int XAmount, int YAmount, bool with_children)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;

            WinInfo wi;
            lock (Sync) windows.TryGetValue(hwnd.Handle, out wi);
            if (wi != null)
            {
                lock (wi.BufLock)
                {
                    if (wi.Buffer != null && area.Width > 0 && area.Height > 0)
                    {
                        var r = Rectangle.Intersect(area,
                            new Rectangle(0, 0, wi.Buffer.Width, wi.Buffer.Height));
                        if (r.Width > 0 && r.Height > 0)
                        {
                            using (var tmp = new Bitmap(r.Width, r.Height))
                            {
                                using (var g = Graphics.FromImage(tmp))
                                    g.DrawImage(wi.Buffer, 0, 0, r, GraphicsUnit.Pixel);
                                using (var g2 = Graphics.FromImage(wi.Buffer))
                                {
                                    g2.SetClip(r);
                                    g2.DrawImage(tmp, r.X + XAmount, r.Y + YAmount);
                                }
                            }
                        }
                    }
                }
                Composite(wi);   // uses the client-area variant of ScrollWindow's caller rect
            }
            // Repaint the scrolled region (exposed strip included) — cheap and correct.
            Invalidate(handle, area, false);
        }

        internal override void ScrollWindow(IntPtr handle, int XAmount, int YAmount, bool with_children)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            var rect = hwnd.ClientRect;
            rect.X = 0; rect.Y = 0;
            // shift within the client region of the control's own buffer
            var clientArea = new Rectangle(hwnd.ClientRect.X, hwnd.ClientRect.Y, rect.Width, rect.Height);
            ScrollWindow(handle, clientArea, XAmount, YAmount, with_children);
        }

        

        // ── Driver: message loop ─────────────────────────────────────────
        internal override object StartLoop(Thread thread) => ThreadQueue(thread);
        internal override void EndLoop(Thread thread) { }

        static void CheckTimers()
        {
            long now = Timer.StopWatchNowMilliseconds;
            Timer[] snapshot;
            lock (Sync) snapshot = timers.ToArray();
            foreach (var t in snapshot)
            {
                if (t.Enabled && t.Expires <= now && !t.Busy)
                {
                    if (in_doevents ||
                        (Application.MWFThread.Current.Context != null &&
                         (Application.MWFThread.Current.Context.MainForm == null ||
                          Application.MWFThread.Current.Context.MainForm.IsLoaded)))
                    {
                        t.Busy = true;
                        t.Update(now);
                        t.FireTick();
                        t.Busy = false;
                    }
                }
            }
        }

        static int NextTimeout(int fallback)
        {
            long now = Timer.StopWatchNowMilliseconds;
            int timeout = fallback;
            lock (Sync)
                foreach (var t in timers)
                    if (t.Enabled)
                        timeout = Math.Min(timeout, (int)Math.Max(0, t.Expires - now));
            return Math.Max(5, timeout);
        }

        internal override bool GetMessage(object queue_id, ref MSG msg, IntPtr hWnd, int wFilterMin, int wFilterMax)
        {
            var q = (SocketQueue)queue_id;
            for (; ; )
            {
                CheckTimers();
                if (q.TryDequeue(out msg))
                {
                    if (msg.message == Msg.WM_ASYNC_MESSAGE)
                    {
                        XplatUIDriverSupport.ExecuteClientMessage((GCHandle)msg.lParam);
                        continue;
                    }
                    if (msg.message == Msg.WM_QUIT) return false;
                    return true;
                }
                if (q.Quit)
                {
                    msg.hwnd = IntPtr.Zero;
                    msg.message = Msg.WM_QUIT;
                    msg.wParam = (IntPtr)q.ExitCode;
                    msg.lParam = IntPtr.Zero;
                    return false;
                }
                RaiseIdle(EventArgs.Empty);
                q.Evt.Wait(NextTimeout(250));
                q.Evt.Reset();
            }
        }

        internal override bool PeekMessage(object queue_id, ref MSG msg, IntPtr hWnd, int wFilterMin, int wFilterMax, uint flags)
        {
            var q = (SocketQueue)queue_id;
            CheckTimers();
            for (; ; )
            {
                if (!q.TryDequeue(out msg)) return false;
                if (msg.message == Msg.WM_ASYNC_MESSAGE)
                {
                    XplatUIDriverSupport.ExecuteClientMessage((GCHandle)msg.lParam);
                    continue;
                }
                return true;
            }
        }

        internal override void DoEvents()
        {
            MSG msg = new MSG();
            var q = ThreadQueue(Thread.CurrentThread);
            in_doevents = true;
            while (PeekMessage(q, ref msg, IntPtr.Zero, 0, 0, (uint)PeekMessageFlags.PM_REMOVE))
            {
                Message m = Message.Create(msg.hwnd, (int)msg.message, msg.wParam, msg.lParam);
                if (Application.FilterMessage(ref m)) continue;
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            in_doevents = false;
        }

        internal override bool TranslateMessage(ref MSG msg) => false;
        internal override IntPtr DispatchMessage(ref MSG msg) => NativeWindow.WndProc(msg.hwnd, msg.message, msg.wParam, msg.lParam);
        internal override IntPtr SendMessage(IntPtr hwnd, Msg message, IntPtr wParam, IntPtr lParam) => NativeWindow.WndProc(hwnd, message, wParam, lParam);

        internal override bool PostMessage(IntPtr hwnd, Msg message, IntPtr wParam, IntPtr lParam)
        {
            EnqueueMsg(hwnd, message, wParam, lParam);
            return true;
        }

        internal override void PostQuitMessage(int exitCode)
        {
            var q = ThreadQueue(Thread.CurrentThread);
            q.ExitCode = exitCode;
            q.Quit = true;
            q.Evt.Set();
        }

        internal override void SendAsyncMethod(AsyncMethodData method)
        {
            EnqueueMsg(method.Handle, Msg.WM_ASYNC_MESSAGE, IntPtr.Zero, (IntPtr)GCHandle.Alloc(method));
        }

        internal override int SendInput(IntPtr hwnd, Queue keys)
        {
            int count = keys.Count;
            while (keys.Count > 0)
            {
                var m = (MSG)keys.Dequeue();
                EnqueueMsg(hwnd, m.message, m.wParam, m.lParam);
            }
            return count;
        }

        internal override IntPtr DefWndProc(ref Message msg)
        {
            switch ((Msg)msg.Msg)
            {
                case Msg.WM_PAINT:
                    {
                        var hwnd = Hwnd.ObjectFromHandle(msg.HWnd);
                        if (hwnd != null) hwnd.expose_pending = false;
                        return IntPtr.Zero;
                    }
                case Msg.WM_NCPAINT:
                    {
                        var hwnd = Hwnd.ObjectFromHandle(msg.HWnd);
                        if (hwnd != null) hwnd.nc_expose_pending = false;
                        return IntPtr.Zero;
                    }
                case Msg.WM_NCCALCSIZE:
                    {
                        if (msg.WParam == (IntPtr)1)
                        {
                            var hwnd = Hwnd.ObjectFromHandle(msg.HWnd);
                            var ncp = (XplatUIWin32.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(msg.LParam, typeof(XplatUIWin32.NCCALCSIZE_PARAMS));
                            if (hwnd != null)
                            {
                                var ctrl = Control.FromHandle(hwnd.Handle);
                                if (ctrl != null)
                                {
                                    var rect = Hwnd.GetBorders(ctrl.GetCreateParams(), null);
                                    ncp.rgrc1.top += rect.top;
                                    ncp.rgrc1.bottom -= rect.bottom;
                                    ncp.rgrc1.left += rect.left;
                                    ncp.rgrc1.right -= rect.right;
                                    Marshal.StructureToPtr(ncp, msg.LParam, true);
                                }
                            }
                        }
                        return IntPtr.Zero;
                    }
                case Msg.WM_SETCURSOR:
                    return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        // ── Driver: geometry / state ─────────────────────────────────────
        internal override void SetWindowPos(IntPtr handle, int x, int y, int width, int height)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            if (width < 0) width = 0;
            if (height < 0) height = 0;
            if (hwnd.x == x && hwnd.y == y && hwnd.width == width && hwnd.height == height) return;
            if (hwnd.parent != null && (hwnd.x != x || hwnd.y != y))
                AddExpose(hwnd.parent, true, hwnd.x, hwnd.y, hwnd.width, hwnd.height);

            hwnd.x = x; hwnd.y = y; hwnd.width = width; hwnd.height = height;
            PerformNCCalc(hwnd);
            AddExpose(hwnd, true, 0, 0, width, height);
            SendMessageStatic(hwnd.client_window, Msg.WM_WINDOWPOSCHANGED, IntPtr.Zero, IntPtr.Zero);

            WinInfo wi;
            lock (Sync) windows.TryGetValue(handle, out wi);
            if (wi != null && wi.IsTop) { PushFrames(wi); BroadcastList("window-updated", wi); }
        }

        internal override void GetWindowPos(IntPtr handle, bool is_toplevel, out int x, out int y, out int width, out int height, out int client_width, out int client_height)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd != null)
            {
                PerformNCCalc(hwnd);
                x = hwnd.x; y = hwnd.y;
                width = hwnd.width; height = hwnd.height;
                client_width = hwnd.ClientRect.Width;
                client_height = hwnd.ClientRect.Height;
                return;
            }
            x = y = width = height = client_width = client_height = 0;
        }

        internal override FormWindowState GetWindowState(IntPtr handle)
        {
            lock (Sync)
                if (windows.TryGetValue(handle, out var wi)) return wi.State;
            return FormWindowState.Normal;
        }

        internal override void SetWindowState(IntPtr handle, FormWindowState state)
        {
            WinInfo wi;
            lock (Sync) windows.TryGetValue(handle, out wi);
            if (wi == null) return;
            var prev = wi.State;
            wi.State = state;
            var hwnd = wi.Hwnd;
            if (state == FormWindowState.Maximized && prev != FormWindowState.Maximized)
            {
                wi.SavedBounds = new Rectangle(hwnd.x, hwnd.y, hwnd.width, hwnd.height);
                SetWindowPos(handle, 0, 0, screenW, screenH);
            }
            else if (state == FormWindowState.Normal && prev == FormWindowState.Maximized && !wi.SavedBounds.IsEmpty)
            {
                SetWindowPos(handle, wi.SavedBounds.X, wi.SavedBounds.Y, wi.SavedBounds.Width, wi.SavedBounds.Height);
            }
            BroadcastList("window-updated", wi);
        }

        internal override void SetWindowMinMax(IntPtr handle, Rectangle maximized, Size min, Size max) { }

        internal override bool SetVisible(IntPtr handle, bool visible, bool activate)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null || hwnd.zombie) return false;
            hwnd.visible = visible;
            hwnd.mapped = visible;
            hwnd.Mapped = visible;

            if (visible)
                lock (Sync) { zOrder.Remove(handle); zOrder.Add(handle); }   // newly-shown is topmost

            if (!visible && hwnd.parent != null)
                AddExpose(hwnd.parent, true, 0, 0, hwnd.parent.width, hwnd.parent.height);

            SendMessageStatic(handle, Msg.WM_SHOWWINDOW, visible ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
            SendMessageStatic(handle, Msg.WM_WINDOWPOSCHANGED, IntPtr.Zero, IntPtr.Zero);
            if (!visible && hwnd.parent != null)
                AddExpose(hwnd.parent, true, hwnd.x, hwnd.y, hwnd.width, hwnd.height);
            if (visible)
            {
                AddExpose(hwnd, true, 0, 0, hwnd.width, hwnd.height);
                WinInfo wi;
                lock (Sync) windows.TryGetValue(handle, out wi);
                if (wi != null && wi.IsTop) { PushRelated(wi); BroadcastList("window-updated", wi); }
            }
            return true;
        }

        internal override bool IsVisible(IntPtr handle)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            return hwnd != null && hwnd.visible;
        }

        internal override bool IsEnabled(IntPtr handle)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            return hwnd != null && hwnd.Enabled;
        }

        internal override void EnableWindow(IntPtr handle, bool Enable)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd != null) hwnd.Enabled = Enable;
        }

        internal override void Activate(IntPtr handle) => ActivateInternal(handle);
        internal override IntPtr GetActive() => ActiveWindow;

        internal override void SetFocus(IntPtr handle)
        {
            if (FocusWindow == handle) return;
            var prev = FocusWindow;
            FocusWindow = handle;
            if (prev != IntPtr.Zero) SendMessageStatic(prev, Msg.WM_KILLFOCUS, handle, IntPtr.Zero);
            if (handle != IntPtr.Zero) SendMessageStatic(handle, Msg.WM_SETFOCUS, prev, IntPtr.Zero);
        }

        internal override IntPtr GetFocus() => FocusWindow;
        internal override IntPtr GetPreviousWindow(IntPtr handle) => handle;

        internal override IntPtr SetParent(IntPtr handle, IntPtr parent)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return IntPtr.Zero;
            var oldTop = Toplevel(hwnd);
            var oldP = oldTop; // (captured before reassignment)

            hwnd.parent = Hwnd.ObjectFromHandle(parent);
            if (oldP != null && oldP.Handle != handle)
                AddExpose(oldP, true, 0, 0, oldP.width, oldP.height);
            if (hwnd.parent != null)
                AddExpose(hwnd.parent, true, 0, 0, hwnd.parent.width, hwnd.parent.height);
            var newTop = Toplevel(hwnd);
            WinInfo w1, w2;
            lock (Sync) { windows.TryGetValue(oldTop?.Handle ?? IntPtr.Zero, out w1); windows.TryGetValue(newTop?.Handle ?? IntPtr.Zero, out w2); }
            if (w1 != null && w1.IsTop) PushFrames(w1);
            if (w2 != null && w2.IsTop) PushFrames(w2);
            return IntPtr.Zero;
        }

        internal override IntPtr GetParent(IntPtr handle, bool with_owner)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return IntPtr.Zero;
            if (hwnd.parent != null) return hwnd.parent.Handle;
            if (with_owner && hwnd.owner != null) return hwnd.owner.Handle;
            return IntPtr.Zero;
        }

        internal override bool SetZOrder(IntPtr handle, IntPtr AfterhWnd, bool Top, bool Bottom) => true;
        static WinInfo TopmostAt(int sx, int sy)
        {
            lock (Sync)
                for (int i = zOrder.Count - 1; i >= 0; i--)
                    if (topWindows.TryGetValue(zOrder[i], out var w) &&
                        w.Hwnd != null && w.Hwnd.visible && !w.Hwnd.zombie &&
                        sx >= w.Hwnd.x && sx < w.Hwnd.x + w.Hwnd.width &&
                        sy >= w.Hwnd.y && sy < w.Hwnd.y + w.Hwnd.height)
                        return w;
            return null;
        }

        static void ButtonMsgs(MouseButtons button, bool isDown, out Msg client, out Msg nc, out Msg dbl)
        {
            switch (button)
            {
                case MouseButtons.Right:
                    client = isDown ? Msg.WM_RBUTTONDOWN : Msg.WM_RBUTTONUP;
                    nc = isDown ? Msg.WM_NCRBUTTONDOWN : Msg.WM_NCRBUTTONUP;
                    dbl = Msg.WM_RBUTTONDBLCLK; break;
                case MouseButtons.Middle:
                    client = isDown ? Msg.WM_MBUTTONDOWN : Msg.WM_MBUTTONUP;
                    nc = isDown ? Msg.WM_NCMBUTTONDOWN : Msg.WM_NCMBUTTONUP;
                    dbl = Msg.WM_MBUTTONDBLCLK; break;
                default:
                    client = isDown ? Msg.WM_LBUTTONDOWN : Msg.WM_LBUTTONUP;
                    nc = isDown ? Msg.WM_NCLBUTTONDOWN : Msg.WM_NCLBUTTONUP;
                    dbl = Msg.WM_LBUTTONDBLCLK; break;
            }
        }



        internal override bool SetTopmost(IntPtr handle, bool Enabled)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd != null) hwnd.topmost = Enabled;
            return true;
        }
        internal override bool SetOwner(IntPtr handle, IntPtr hWndOwner)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd != null) hwnd.owner = Hwnd.ObjectFromHandle(hWndOwner);
            return true;
        }

        internal override void SetWindowStyle(IntPtr handle, CreateParams cp) => RequestNCRecalc(handle);
        internal override void SetBorderStyle(IntPtr handle, FormBorderStyle border_style)
        {
            var form = Control.FromHandle(handle) as Form;
            if (form != null && form.window_manager == null &&
                (border_style == FormBorderStyle.FixedToolWindow || border_style == FormBorderStyle.SizableToolWindow))
                form.window_manager = new ToolWindowManager(form);
            RequestNCRecalc(handle);
        }
        internal override void SetMenu(IntPtr handle, Menu menu)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd != null) hwnd.menu = menu;
            RequestNCRecalc(handle);
        }

        internal override void RequestNCRecalc(IntPtr handle)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            PerformNCCalc(hwnd);
            SendMessageStatic(handle, Msg.WM_WINDOWPOSCHANGED, IntPtr.Zero, IntPtr.Zero);
            InvalidateNC(handle);
        }

        internal override bool CalculateWindowRect(ref Rectangle ClientRect, CreateParams cp, Menu menu, out Rectangle WindowRect)
        {
            WindowRect = Hwnd.GetWindowRectangle(cp, menu, ClientRect);
            return true;
        }

        internal override void ClientToScreen(IntPtr handle, ref int x, ref int y)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            var off = OffsetInToplevel(hwnd, true);
            var top = Toplevel(hwnd);
            x += top.x + off.X;
            y += top.y + off.Y;
        }

        internal override void ScreenToClient(IntPtr handle, ref int x, ref int y)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            var off = OffsetInToplevel(hwnd, true);
            var top = Toplevel(hwnd);
            x -= top.x + off.X;
            y -= top.y + off.Y;
        }

        internal override void MenuToScreen(IntPtr handle, ref int x, ref int y)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            var off = OffsetInToplevel(hwnd, false);
            var top = Toplevel(hwnd);
            x += top.x + off.X;
            y += top.y + off.Y;
        }

        internal override void ScreenToMenu(IntPtr handle, ref int x, ref int y)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            var off = OffsetInToplevel(hwnd, false);
            var top = Toplevel(hwnd);
            x -= top.x + off.X;
            y -= top.y + off.Y;
        }

        internal override Point GetMenuOrigin(IntPtr handle)
        {
            var form = Control.FromHandle(handle) as Form;
            if (form != null && form.FormBorderStyle == FormBorderStyle.None) return Point.Empty;
            return new Point(SystemInformation.FrameBorderSize.Width,
                SystemInformation.FrameBorderSize.Height + ThemeEngine.Current.CaptionHeight);
        }

        // ── Driver: text / title ─────────────────────────────────────────
        internal override bool Text(IntPtr handle, string text)
        {
            lock (Sync)
                if (windows.TryGetValue(handle, out var wi))
                {
                    wi.Title = text ?? "";
                    if (wi.IsTop) BroadcastList("window-updated", wi);
                }
            return true;
        }

        internal override bool GetText(IntPtr handle, out string text)
        {
            lock (Sync)
                if (windows.TryGetValue(handle, out var wi)) { text = wi.Title; return true; }
            text = "";
            return false;
        }

        // ── Driver: mouse grab / cursor (virtual) ────────────────────────
        internal override void GrabWindow(IntPtr handle, IntPtr ConfineToHwnd)
        {
            GrabHwnd = handle;
            GrabConfined = ConfineToHwnd != IntPtr.Zero;
            GrabArea = Rectangle.Empty;
            if (GrabConfined)
            {
                var h = Hwnd.ObjectFromHandle(ConfineToHwnd);
                if (h != null) GrabArea = new Rectangle(h.x, h.y, h.width, h.height);
            }
        }
        internal override void UngrabWindow(IntPtr handle)
        {
            bool was = GrabHwnd != IntPtr.Zero;
            GrabHwnd = IntPtr.Zero;
            GrabConfined = false;
            if (was && handle != IntPtr.Zero)
                SendMessageStatic(handle, Msg.WM_CAPTURECHANGED, IntPtr.Zero, IntPtr.Zero);
        }
        internal override void GrabInfo(out IntPtr handle, out bool grabConfined, out Rectangle grabArea)
        {
            handle = GrabHwnd; grabConfined = GrabConfined; grabArea = GrabArea;
        }

        internal override void SetCursor(IntPtr hwnd, IntPtr cursor) { }
        internal override void ShowCursor(bool show) { }
        internal override void OverrideCursor(IntPtr cursor) { }
        internal override IntPtr DefineCursor(Bitmap bitmap, Bitmap mask, Color cursor_pixel, Color mask_pixel, int xHotSpot, int yHotSpot) => (IntPtr)1;
        internal override IntPtr DefineStdCursor(StdCursor id) => (IntPtr)((int)id + 1);
        internal override Bitmap DefineStdCursorBitmap(StdCursor id) => new Bitmap(16, 16);
        internal override void DestroyCursor(IntPtr cursor) { }
        internal override void GetCursorInfo(IntPtr cursor, out int width, out int height, out int hotspot_x, out int hotspot_y)
        { width = 16; height = 16; hotspot_x = 0; hotspot_y = 0; }
        internal override void GetCursorPos(IntPtr handle, out int x, out int y)
        {
            if (handle != IntPtr.Zero) { int sx = mouse_position.X, sy = mouse_position.Y; ScreenToClient(handle, ref sx, ref sy); x = sx; y = sy; }
            else { x = mouse_position.X; y = mouse_position.Y; }
        }
        internal override void SetCursorPos(IntPtr handle, int x, int y)
        {
            if (handle != IntPtr.Zero) ClientToScreen(handle, ref x, ref y);
            mouse_position = new Point(x, y);
        }

        // ── Driver: clip regions (virtual) ───────────────────────────────
        internal override Region GetClipRegion(IntPtr hwnd)
        {
            var h = Hwnd.ObjectFromHandle(hwnd);
            return h?.UserClip;
        }
        internal override void SetClipRegion(IntPtr hwnd, Region region)
        {
            var h = Hwnd.ObjectFromHandle(hwnd);
            if (h != null) h.UserClip = region;
        }

        // ── Driver: caret (tracked, not drawn) ───────────────────────────
        // ── Caret: drawn into the served frame at render time ───────────
        class CaretState
        {
            public IntPtr Hwnd;
            public int X, Y, W, H;
            public bool Visible;
            public bool On;                       // blink phase
            public System.Threading.Timer Timer;
        }
        static readonly CaretState caret = new CaretState();

        internal override void CreateCaret(IntPtr handle, int width, int height)
        {
            lock (caret)
            {
                caret.Hwnd = handle;
                caret.W = Math.Max(1, width);
                caret.H = Math.Max(1, height);
                caret.X = caret.Y = 0;
                caret.Visible = false;
                caret.On = false;
            }
        }

        internal override void DestroyCaret(IntPtr handle)
        {
            lock (caret)
            {
                if (caret.Hwnd != handle) return;
                caret.Visible = false;
                caret.On = false;
                caret.Hwnd = IntPtr.Zero;
                caret.Timer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        internal override void SetCaretPos(IntPtr handle, int x, int y)
        {
            lock (caret)
            {
                if (caret.Hwnd != handle) return;
                caret.X = x; caret.Y = y;
                if (caret.Visible && !caret.On) caret.On = true;   // re-show immediately on move
            }
            PushCaretFrame();
        }

        internal override void CaretVisible(IntPtr handle, bool visible)
        {
            lock (caret)
            {
                if (caret.Hwnd != handle || caret.Visible == visible) return;
                caret.Visible = visible;
                caret.On = visible;
                if (visible)
                {
                    caret.Timer ??= new System.Threading.Timer(_ => CaretBlink(), null,
                        Timeout.Infinite, Timeout.Infinite);
                    caret.Timer.Change(CaretBlinkTime, CaretBlinkTime);
                }
                else
                {
                    caret.Timer?.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
            PushCaretFrame();
        }

        static void CaretBlink()
        {
            bool push = false;
            lock (caret)
                if (caret.Visible && caret.Hwnd != IntPtr.Zero) { caret.On = !caret.On; push = true; }
            if (push) PushCaretFrame();
        }

        static void PushCaretFrame()
        {
            Hwnd h;
            lock (caret) h = Hwnd.ObjectFromHandle(caret.Hwnd);
            if (h == null) return;
            var top = Toplevel(h);
            WinInfo wi;
            lock (Sync) windows.TryGetValue(top.Handle, out wi);
            if (wi != null) PushRelated(wi);
        }

        // ── Driver: timers ───────────────────────────────────────────────
        internal override void SetTimer(Timer timer)
        {
            lock (Sync) if (!timers.Contains(timer)) timers.Add(timer);
            lock (Sync) foreach (var q in queues.Values) q.Evt.Set();
        }
        internal override void KillTimer(Timer timer)
        {
            lock (Sync) timers.Remove(timer);
        }

        // ── Driver: reversible drawing (no-op, no XOR in Skia) ───────────
        internal override void DrawReversibleLine(Point start, Point end, Color backColor) { }
        internal override void DrawReversibleRectangle(IntPtr handle, Rectangle rect, int line_width) { }
        internal override void FillReversibleRectangle(Rectangle rectangle, Color backColor) { }
        internal override void DrawReversibleFrame(Rectangle rectangle, Color backColor, FrameStyle style) { }

        // ── Driver: misc stubs ───────────────────────────────────────────
        internal override void AudibleAlert(AlertType alert) => Console.Beep();
        internal override void BeginMoveResize(IntPtr handle) { }
        internal override void EnableThemes() => themes_enabled = true;
        internal override void HandleException(Exception e) => Console.WriteLine("{0}{1}", e.Message, new StackTrace(e, true));
        internal override void SetModal(IntPtr handle, bool Modal) { }
        internal override void SetIcon(IntPtr handle, Icon icon) { }
        internal override void ResetMouseHover(IntPtr handle) { }
        internal override void RequestAdditionalWM_NCMessages(IntPtr handle, bool hover, bool leave) { }

        internal override double GetWindowTransparency(IntPtr handle) => 1.0;
        internal override void SetWindowTransparency(IntPtr handle, double transparency, Color key) { }
        internal override TransparencySupport SupportsTransparency() => TransparencySupport.None;

        internal override bool SystrayAdd(IntPtr hwnd, string tip, Icon icon, out ToolTip tt) { tt = null; return false; }
        internal override bool SystrayChange(IntPtr hwnd, string tip, Icon icon, ref ToolTip tt) => false;
        internal override void SystrayRemove(IntPtr hwnd, ref ToolTip tt) { }
        internal override void SystrayBalloon(IntPtr hwnd, int timeout, string title, string text, ToolTipIcon icon) { }

        // ── Driver: clipboard (process-local) ────────────────────────────
        static object clipObj;
        static int clipType;
        internal override IntPtr ClipboardOpen(bool primary_selection) => (IntPtr)0xC11B;
        internal override void ClipboardClose(IntPtr handle) { }
        internal override int ClipboardGetID(IntPtr handle, string format) => 0; // DataFormats.GetFormat(format).Id;
        internal override int[] ClipboardAvailableFormats(IntPtr handle) => clipObj == null ? new int[0] : new[] { clipType };
        internal override void ClipboardStore(IntPtr handle, object obj, int type, XplatUI.ObjectToClipboard converter, bool copy)
        { clipObj = obj; clipType = type; }
        internal override object ClipboardRetrieve(IntPtr handle, int type, XplatUI.ClipboardToObject converter)
            => type == clipType ? clipObj : null;

        // ── Driver: font metrics / autoscale ─────────────────────────────
        internal override bool GetFontMetrics(Graphics g, Font font, out int ascent, out int descent)
        {
            var ff = font.FontFamily;
            ascent = 0;//ff.GetCellAscent(font.Style);
            descent = 0;// ff.GetCellDescent(font.Style);
            return true;
        }

        internal override SizeF GetAutoScaleSize(Font font)
        {
            const string magic_string = "The quick brown fox jumped over the lazy dog.";
            const double magic_number = 1;// 44.549996948242189;
            using (var bmp = new Bitmap(128, 128))
            using (var g = Graphics.FromImage(bmp))
            {
                float width = (float)(g.MeasureString(magic_string, font).Width / magic_number);
                return new SizeF(width, font.Height);
            }
        }

        // ── System metrics ───────────────────────────────────────────────
        internal override int CaptionHeight => 19;
        internal override Size CursorSize => new Size(16, 16);
        internal override bool DragFullWindows => true;
        internal override Size DragSize => new Size(4, 4);
        internal override Size FrameBorderSize => new Size(4, 4);
        internal override Size IconSize => new Size(32, 32);
        internal override Size MaxWindowTrackSize => new Size(screenW, screenH);
        internal override bool MenuAccessKeysUnderlined => false;
        internal override Size MinimizedWindowSpacingSize => new Size(160, 28);
        internal override Size MinimumWindowSize => new Size(110, 22);
        internal override Size SmallIconSize => new Size(16, 16);
        internal override int MouseButtonCount => 3;
        internal override bool MouseButtonsSwapped => false;
        internal override bool MouseWheelPresent => true;
        internal override int KeyboardSpeed => 31;
        internal override int KeyboardDelay => 1;
        internal override bool ThemesEnabled => themes_enabled;
        internal override Rectangle VirtualScreen => new Rectangle(0, 0, screenW, screenH);
        internal override Rectangle WorkingArea => new Rectangle(0, 0, screenW, screenH);
        internal override void GetDisplaySize(out Size size) => size = new Size(screenW, screenH);
        internal override Screen[] AllScreens => new[] { new Screen(true, "SocketScreen", VirtualScreen, WorkingArea) };

        internal override Keys ModifierKeys => mods;
        internal override MouseButtons MouseButtons => mouse_state;
        internal override Point MousePosition => mouse_position;

        internal override void RaiseIdle(EventArgs e) => Idle?.Invoke(this, e);
        internal override event EventHandler Idle;
    }
}