// XplatUISocketWasm.cs — XplatUI driver for browser WASM (Wystem.Wet transport).
//
// Presents each top-level window as an AF_UNIX socket whose wire is JavaScript
// (globalThis.AF_UNIX). Rendering is 100% virtual (Skia back buffers); frames are
// served as base64 PNG/JPEG (or inline SVG) inside newline-delimited JSON.
// No X11/Win32 FFI, no Kestrel, no real threads, no filesystem.
//
// Socket paths (all AF_UNIX, all served by this driver):
//   /ui/window-list            window enumeration + change notifications + log
//   /ui/compositor             the whole desktop (all visible top-levels, z-order)
//   /ui/win-<HANDLE hex>       one top-level window
//
// Wire protocol (protocol 1) — one JSON object per line, both directions:
//   server -> client : hello | list | window-added | window-removed | window-updated
//                      frame | info | pong | bye | log | error
//   client -> server : subscribe | unsubscribe | refresh | getinfo | ping
//                      mousedown | mouseup | mousemove | wheel
//                      keydown | keyup | key | char | text
//                      resize | close | log
//
// Environment / configuration:
//   XPLAT_UI_SOCKET_DIR     AF_UNIX prefix                      (default "/ui")
//   XPLAT_UI_SOCKET_SCREEN  "WxH" virtual screen                (default 1920x1080)
//   XPLAT_UI_SOCKET_LOG     "0" disables log broadcast          (default on)
//
// Threading model (single-threaded browser-wasm safe):
//   * Nothing here ever blocks. Accept / read / write loops are async tasks whose
//     continuations are driven by the JS interop (WetSocket completions arrive from
//     JS callbacks into the runtime).
//   * Socket I/O is awaited, never waited on.
//   * The WinForms queue is filled exactly as before. On desktop (or a wasm build
//     with threads) GetMessage blocks as usual; on single-threaded browser-wasm a
//     blocking wait throws PlatformNotSupportedException, so GetMessage returns
//     control instead and the host drives the UI with PumpMessages()/DoEvents()
//     (form.Show() + [JSExport] Pump). See WaitForWork / GetMessage below.

#if BROWSER_WASM || TRUE

using SkiaSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Wystem.Wet;

namespace System.Windows.Forms
{
    public class XplatUISocketWasm : XplatUIDriver
    {
        // ── Singleton ────────────────────────────────────────────────────
        static volatile XplatUISocketWasm Instance;
        static int RefCount;
        static readonly object instancelock = new object();

        static WinInfo desktopWi;

        public static XplatUISocketWasm GetInstance()
        {
            lock (instancelock)
            {
                if (Instance == null) Instance = new XplatUISocketWasm();
                RefCount++;
            }
            return Instance;
        }

        // ── Global state ─────────────────────────────────────────────────

        // ── threading capability probe (runs once, then cached) ─────────
        static bool? _threads;

        /// <summary>True when the runtime can block — i.e. a threaded wasm build
        /// or any desktop runtime. False on single-threaded browser-wasm.</summary>
        public static bool ThreadsAvailable
        {
            get
            {
                if (!_threads.HasValue)
                {
                    try
                    {
                        using var probe = new ManualResetEventSlim(false);
                        probe.Wait(1);
                        _threads = true;
                    }
                    catch (PlatformNotSupportedException) { _threads = false; }
                    Log("threading: " + (_threads.Value ? "available — blocking loop" : "NOT available — pump model"));
                }
                return _threads.Value;
            }
        }

        static void WaitForWork(SocketQueue q, int timeoutMs)
        {
            if (!ThreadsAvailable) return;   // single-threaded browser: never block
            q.Evt.Wait(timeoutMs);
            q.Evt.Reset();
        }
        static readonly object Sync = new object();
        static string SockDir = "/ui";
        static bool serverStarted;
        static WetSocket listListener;
        static readonly List<ClientConn> listWatchers = new List<ClientConn>();

        static readonly Dictionary<IntPtr, WinInfo> windows = new Dictionary<IntPtr, WinInfo>();      // every hwnd
        static readonly Dictionary<IntPtr, WinInfo> topWindows = new Dictionary<IntPtr, WinInfo>();   // socketed toplevels
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

        // Nothing shapes the native window here, so a user clip WILL expose the
        // parent. Returning false keeps stock PaintControlBackground from
        // repainting the parent into the child and from installing a canvas clip
        // in the paint path (corrupt/invisible colored buttons and textboxes).
        // Rounding is applied at presentation time instead.
        internal override bool UserClipWontExposeParent => true;

        // ── Logging (browser-visible, UI-safe) ───────────────────────────
        static readonly object LogLock = new object();
        static readonly Queue<string> LogRing = new Queue<string>();
        const int MaxLogLines = 500;
        static bool logBroadcast = true;

        internal static void Log(string message)
        {
            if (message == null) return;
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message;
            lock (LogLock)
            {
                LogRing.Enqueue(line);
                while (LogRing.Count > MaxLogLines) LogRing.Dequeue();
            }
            try { Console.WriteLine("[XplatUISocketWasm] " + message); } catch { }

            if (!logBroadcast) return;
            // Log lines ride the *window-list* connection only, so the frame
            // stream of a live window is never interleaved with log noise.
            var msg = "{\"type\":\"log\",\"text\":\"" + JEsc(line) + "\"}";
            lock (Sync)
            {
                foreach (var w in listWatchers.ToArray())
                {
                    w.Send(msg);
                    if (w.Dead) listWatchers.Remove(w);
                }
            }
        }

        internal static string[] GetLog(int max)
        {
            lock (LogLock) return LogRing.Reverse().Take(Math.Max(1, max)).Reverse().ToArray();
        }

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
            public bool TryDequeue(out MSG m)
            {
                lock (Lock) { if (Msgs.Count > 0) { m = Msgs.Dequeue(); return true; } }
                m = default;
                return false;
            }
        }

        class WinInfo
        {
            public IntPtr Handle;
            public Hwnd Hwnd;
            public string Title = "";
            public bool IsTop;
            public WetSocket Listener;
            public string SockPath;
            public readonly List<ClientConn> Clients = new List<ClientConn>();
            public Bitmap Buffer;
            public readonly object BufLock = new object();
            public FormWindowState State = FormWindowState.Normal;
            public Rectangle SavedBounds;
            public bool IsDesktop;
        }

        // One accepted AF_UNIX connection. Control messages are queued; frames are
        // coalesced into a single "wanted" slot and encoded at send time on the
        // writer task, so what goes out is always the freshest state.
        class ClientConn
        {
            public WetSocket Sock;
            public WinInfo Win;
            public bool Subscribed;
            public string Format = "jpeg";
            public int Quality = 80;
            public bool Gray;
            public int MinFrameMs;              // 0 = uncapped (fps throttle)
            public int LastFrameTick;           // Environment.TickCount of the last frame we wrote
            public volatile bool Dead;

            public readonly object QueueLock = new object();
            public readonly Queue<string> OutQueue = new Queue<string>();   // control msgs
            public bool FrameWanted;                                        // coalesced frame slot
            public readonly SemaphoreSlim Work = new SemaphoreSlim(0);
            public Task Writer;
            public Task Reader;
            public long BytesIn, BytesOut, FramesOut;
            const int MaxCtrl = 64;

            public void StartWriter() => Writer = Task.Run(WriterLoopAsync);

            // Control messages (hello/info/pong/bye/log) — queued, capped.
            public void Send(string s)
            {
                if (Dead) return;
                lock (QueueLock)
                {
                    if (OutQueue.Count >= MaxCtrl) { Dead = true; Work.Release(); return; } // hopeless client: cut it, don't OOM
                    OutQueue.Enqueue(s);
                }
                Work.Release();
            }

            // Frames — coalesced to a single "wanted" flag.
            public void RequestFrame()
            {
                if (Dead) return;
                lock (QueueLock) FrameWanted = true;
                Work.Release();
            }

            async Task WriterLoopAsync()
            {
                try
                {
                    while (!Dead)
                    {
                        bool signalled;
                        try { signalled = await Work.WaitAsync(500).ConfigureAwait(false); }
                        catch { break; }

                        // Heartbeat: a live subscriber gets a frame even if nothing
                        // invalidated — this is the "video output" path.
                        if (!signalled && Subscribed && !Dead)
                        {
                            lock (QueueLock) FrameWanted = true;
                        }

                        for (; ; )
                        {
                            string msg = null;
                            bool wantFrame = false;
                            lock (QueueLock)
                            {
                                if (OutQueue.Count > 0) msg = OutQueue.Dequeue();
                                else if (FrameWanted) { FrameWanted = false; wantFrame = true; }
                            }
                            if (msg != null) await WriteRawAsync(msg).ConfigureAwait(false);
                            else if (wantFrame) await SendFrameAsync(this).ConfigureAwait(false);
                            else break;
                        }
                    }
                }
                catch { }
                Dead = true;
            }

            // One newline-terminated message. WetSocket.SendAsync accepts as much as
            // the JS window allows and returns the byte count (0 == window full,
            // <0 == gone), so loop until the message is out. AWAITED — never blocks,
            // which is essential on single-threaded browser-wasm. Only the writer
            // task calls this, so writes stay ordered without a lock.
            public async Task WriteRawAsync(string s)
            {
                if (Dead) return;
                var b = Encoding.UTF8.GetBytes(s + "\n");
                int off = 0;
                while (off < b.Length)
                {
                    int n;
                    try { n = await Sock.SendAsync(b.AsMemory(off)).ConfigureAwait(false); }
                    catch { Dead = true; return; }
                    if (n <= 0) { Dead = true; return; }
                    off += n;
                    BytesOut += n;
                }
            }
        }

        sealed class SocketPaintEventArgs : PaintEventArgs
        {
            public SocketPaintEventArgs(Graphics g, Rectangle clip, WinInfo wi) : base(g, clip) { Win = wi; }
            public WinInfo Win;
        }

        // ── Ctor / init ──────────────────────────────────────────────────
        XplatUISocketWasm()
        {
            var scr = Environment.GetEnvironmentVariable("XPLAT_UI_SOCKET_SCREEN");
            if (!string.IsNullOrEmpty(scr))
            {
                var p = scr.Split('x', 'X', ',');
                if (p.Length == 2) { int.TryParse(p[0], out screenW); int.TryParse(p[1], out screenH); }
            }
            var lg = Environment.GetEnvironmentVariable("XPLAT_UI_SOCKET_LOG");
            if (lg == "0" || string.Equals(lg, "false", StringComparison.OrdinalIgnoreCase)) logBroadcast = false;
        }

        internal override IntPtr InitializeDriver()
        {
            EnsureServer();
            return IntPtr.Zero;
        }

        internal override void ShutdownDriver(IntPtr token) => CleanupSockets();

        static string PathFor(string name) => SockDir + "/" + name;

        static void EnsureServer()
        {
            if (serverStarted) return;
            serverStarted = true;

            var dir = Environment.GetEnvironmentVariable("XPLAT_UI_SOCKET_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                dir = dir.Replace('\\', '/').TrimEnd('/');
                if (dir.Length == 0) dir = "/";
                SockDir = dir;
            }

            listListener = ListenWet(PathFor("window-list"));
            _ = ListAcceptLoopAsync(listListener);

            desktopWi = new WinInfo { IsDesktop = true, SockPath = PathFor("compositor") };
            desktopWi.Listener = ListenWet(desktopWi.SockPath);
            _ = WinAcceptLoopAsync(desktopWi);

            Log("serving windows on unix:" + SockDir + "/* (screen " + screenW + "x" + screenH + ")");
        }

        static void CleanupSockets()
        {
            try
            {
                lock (Sync)
                {
                    foreach (var wi in topWindows.Values) CloseWinSockets(wi);
                    foreach (var w in listWatchers.ToArray()) { w.Dead = true; try { w.Sock.Close(); } catch { } }
                    listWatchers.Clear();
                }
                try { listListener?.Close(); } catch { }
                if (desktopWi != null) CloseWinSockets(desktopWi);
            }
            catch { }
        }

        static void CloseWinSockets(WinInfo wi)
        {
            ClientConn[] cs;
            lock (Sync)
            {
                cs = wi.Clients.ToArray();
                wi.Clients.Clear();
            }
            foreach (var c in cs)
            {
                c.Dead = true;
                c.Send("{\"type\":\"bye\"}");
                try { c.Sock.Close(); } catch { }
            }
            try { wi.Listener?.Close(); } catch { }
            lock (wi.BufLock) { wi.Buffer?.Dispose(); wi.Buffer = null; }
        }

        static WetSocket ListenWet(string path)
        {
            try
            {
                var s = new WetSocket(WetAddressFamily.Unix, WetSocketType.Stream, WetProtocolType.Unspecified);
                s.Bind(new WetUnixEndPoint(path));
                s.Listen(16);
                Log("listening unix:" + path);
                return s;
            }
            catch (WetException e)
            {
                Log("listen failed for unix:" + path + " — " + e.Message);
                return null;
            }
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
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
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
            JEsc(desktopWi != null ? desktopWi.SockPath : PathFor("compositor")) + "\"}";

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

        // ── AF_UNIX accept loops (async, no threads) ─────────────────────
        static async Task ListAcceptLoopAsync(WetSocket listener)
        {
            if (listener == null) return;
            try
            {
                for (; ; )
                {
                    WetSocket s;
                    try { s = await listener.AcceptAsync().ConfigureAwait(false); }
                    catch (WetException) { break; }
                    if (s == null) break;

                    var cc = new ClientConn { Sock = s };
                    lock (Sync) listWatchers.Add(cc);
                    cc.StartWriter();
                    cc.Send(ListJson());
                    cc.Reader = Task.Run(() => ListClientLoopAsync(cc));
                    Log("window-list client #" + s.Handle + " connected");
                }
            }
            catch (Exception e) { Log("window-list accept loop: " + e.Message); }
        }

        static async Task ListClientLoopAsync(ClientConn cc)
        {
            try
            {
                await foreach (var text in ReadLinesAsync(cc).ConfigureAwait(false))
                {
                    if (text == null) break;
                    if (text.IndexOf("\"list\"", StringComparison.Ordinal) >= 0) cc.Send(ListJson());
                }
            }
            catch { }
            cc.Dead = true;
            lock (Sync) listWatchers.Remove(cc);
            try { cc.Sock.Close(); } catch { }
            Log("window-list client disconnected");
        }

        static async Task WinAcceptLoopAsync(WinInfo wi)
        {
            if (wi.Listener == null) return;
            try
            {
                for (; ; )
                {
                    WetSocket s;
                    try { s = await wi.Listener.AcceptAsync().ConfigureAwait(false); }
                    catch (WetException) { break; }
                    if (s == null) break;

                    var cc = new ClientConn { Sock = s, Win = wi };
                    lock (Sync) wi.Clients.Add(cc);
                    cc.StartWriter();
                    cc.Send("{\"type\":\"hello\",\"protocol\":1,\"screen\":[" + screenW + "," + screenH +
                            "],\"window\":" + (wi.IsDesktop ? DesktopJson() : WindowJson(wi)) + "}");
                    cc.Reader = Task.Run(() => WinClientLoopAsync(cc));
                    Log("client #" + s.Handle + " attached to " + (wi.IsDesktop ? "compositor" : "0x" + wi.Handle.ToInt64().ToString("X")));
                }
            }
            catch (Exception e) { Log("accept loop (" + wi.SockPath + "): " + e.Message); }
        }

        static async Task WinClientLoopAsync(ClientConn cc)
        {
            try
            {
                await foreach (var text in ReadLinesAsync(cc).ConfigureAwait(false))
                {
                    if (text == null) break;
                    if (text.Length == 0) continue;
                    try
                    {
                        using (var doc = JsonDocument.Parse(text))
                            HandleClientMessage(cc, doc.RootElement);
                    }
                    catch (Exception e)
                    {
                        Log("bad client message: " + e.Message);
                    }
                }
            }
            catch { }
            cc.Dead = true;
            lock (Sync) cc.Win?.Clients.Remove(cc);
            try { cc.Sock.Close(); } catch { }
            Log("client #" + (cc.Sock?.Handle ?? 0) + " disconnected");
        }

        // Newline-delimited UTF-8 framing over a raw WetSocket byte stream.
        // Fully async: receives are awaited, never blocked on — the JS interop is
        // what completes them, and a blocking wait here would starve it.
        static async IAsyncEnumerable<string> ReadLinesAsync(ClientConn cc)
        {
            var pending = new List<byte>(8 * 1024);
            var chunk = new byte[16 * 1024];
            for (; ; )
            {
                int n;
                try { n = await cc.Sock.ReceiveAsync(chunk).ConfigureAwait(false); }
                catch { n = 0; }
                if (n <= 0) yield break;
                cc.BytesIn += n;
                for (int i = 0; i < n; i++) pending.Add(chunk[i]);

                int cut;
                while ((cut = pending.IndexOf((byte)'\n')) >= 0)
                {
                    var line = Encoding.UTF8.GetString(pending.GetRange(0, cut).ToArray());
                    pending.RemoveRange(0, cut + 1);
                    if (line.Length > 0) yield return line;
                }
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
                    byte* s = (byte*)src.GetPixels();
                    byte* d = (byte*)use.GetPixels();
                    for (int y = 0; y < src.Height; y++)
                    {
                        byte* sr = s + y * src.RowBytes;
                        byte* dr = d + y * use.RowBytes;
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
            Bitmap src = BuildCompositedFrame(wi);
            if (src == null) return null;
            var bytes = EncodeFrame(src, fmt, q, gray);
            src.Dispose();
            return bytes;
        }

        // Called on the connection's writer task: build + encode + write here,
        // off the UI path.
        static async Task SendFrameAsync(ClientConn cc)
        {
            var wi = cc.Win;
            if (wi == null || cc.Dead) return;

            // live throttle (optional): pace the writer instead of dropping frames
            if (cc.MinFrameMs > 0)
            {
                int wait = cc.MinFrameMs - unchecked(Environment.TickCount - cc.LastFrameTick);
                if (wait > 0) await Task.Delay(wait).ConfigureAwait(false);
            }

            // SVG requested but server-side SVG is off: negotiate down to PNG
            if (cc.Format == "svg" && !SvgSupport.Enabled)
                cc.Format = "png";

            string payload;
            int w, h;

            if (wi.IsDesktop)
            {
                w = screenW; h = screenH;
                if (cc.Format == "svg")
                {
                    payload = BuildDesktopSvg();
                }
                else
                {
                    var desk = BuildDesktopFrame();
                    var dbytes = EncodeFrame(desk, cc.Format, cc.Quality, cc.Gray);
                    desk.Dispose();
                    payload = dbytes != null ? Convert.ToBase64String(dbytes) : "";
                }
            }
            else if (cc.Format == "svg")
            {
                w = wi.Hwnd != null ? wi.Hwnd.width : 0;
                h = wi.Hwnd != null ? wi.Hwnd.height : 0;
                payload = BuildTopFrameSvg(wi);
            }
            else
            {
                w = wi.Hwnd != null ? wi.Hwnd.width : 0;
                h = wi.Hwnd != null ? wi.Hwnd.height : 0;
                var bytes = EncodeWindowFrame(wi, cc.Format, cc.Quality, cc.Gray);
                if (bytes != null && wi.Buffer != null) { w = wi.Buffer.Width; h = wi.Buffer.Height; }
                payload = bytes != null ? Convert.ToBase64String(bytes) : "";
            }

            await cc.WriteRawAsync("{\"type\":\"frame\",\"format\":\"" + cc.Format +
                                   "\",\"width\":" + w + ",\"height\":" + h +
                                   ",\"data\":\"" + (cc.Format == "svg" ? JEsc(payload) : payload) + "\"}").ConfigureAwait(false);

            cc.LastFrameTick = Environment.TickCount;
            cc.FramesOut++;
        }

        static void PushFrames(WinInfo wi)
        {
            if (wi == null) return;
            ClientConn[] subs;
            lock (Sync) subs = wi.Clients.FindAll(c => c.Subscribed && !c.Dead).ToArray();
            foreach (var c in subs) c.RequestFrame();
        }

        void FullRedraw(WinInfo topWi)
        {
            if (topWi == null) return;
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

        static readonly List<IntPtr> zOrder = new List<IntPtr>();   // back = topmost

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

        static bool LooksPopup(WinInfo w) =>
            w.Hwnd != null && ((int)w.Hwnd.initial_style & (int)WindowStyles.WS_POPUP) != 0;

        static bool IsPopupOf(WinInfo popup, WinInfo owner)
        {
            if (popup?.Hwnd == null || owner?.Hwnd == null) return false;
            for (var o = popup.Hwnd.owner; o != null; o = o.owner)
                if (o.Handle == owner.Handle) return true;
            for (var p = popup.Hwnd.parent; p != null; p = p.parent)
                if (p.Handle == owner.Handle) return true;
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
                    if (!yes && LooksPopup(w))
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

        // ── Messages read from the HTML side ─────────────────────────────
        static void HandleClientMessage(ClientConn cc, JsonElement root)
        {
            var wi = cc.Win;
            string type = root.TryGetProperty("type", out var te) ? te.GetString() : "";
            int t = (int)(root.TryGetProperty("t", out var timeEl) ? timeEl.GetInt64() : Environment.TickCount);
            switch (type)
            {
                case "ping":
                    cc.Send("{\"type\":\"pong\"}");
                    break;

                case "getinfo":
                    cc.Send("{\"type\":\"info\",\"window\":" + (wi.IsDesktop ? DesktopJson() : WindowJson(wi)) + "}");
                    break;

                case "log":
                    {
                        // Browser-side log line: keep it out of the UI, just record it.
                        string txt = root.TryGetProperty("text", out var lte) ? lte.GetString() : "";
                        Log("[js] " + txt);
                        break;
                    }

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
                    if (root.TryGetProperty("fps", out var fe))
                    {
                        int fps = fe.GetInt32();
                        cc.MinFrameMs = fps > 0 ? Math.Max(1, 1000 / fps) : 0;
                    }
                    if (root.TryGetProperty("full", out var full) && full.GetBoolean())
                        Instance?.FullRedraw(wi);
                    cc.RequestFrame();
                    break;

                case "unsubscribe":
                    cc.Subscribed = false;
                    break;

                case "mousedown": RouteMouse(wi, root, down: true, up: false, t: t); break;
                case "mouseup": RouteMouse(wi, root, down: false, up: true, t: t); break;
                case "click": RouteMouse(wi, root, down: true, up: true, t: t); break;
                case "mousemove": RouteMouseMove(wi, root, t); break;
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

                default:
                    Log("unhandled client message '" + type + "'");
                    break;
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
            for (int i = 0; i < parent.Controls.Count; i++)
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

        static void RouteMouse(WinInfo wi, JsonElement root, bool down, bool up, int t)
        {
            int x = root.TryGetProperty("x", out var xe) ? xe.GetInt32() : 0;
            int y = root.TryGetProperty("y", out var ye) ? ye.GetInt32() : 0;
            if (!Retarget(ref wi, ref x, ref y)) return;
            string btn = root.TryGetProperty("button", out var be) ? be.GetString() : "left";
            MouseButtons button = btn == "right" ? MouseButtons.Right : btn == "middle" ? MouseButtons.Middle : MouseButtons.Left;

            ActivateInternal(wi.Handle);
            mouse_position = new Point(wi.Hwnd.x + x, wi.Hwnd.y + y);

            if (down) { mouse_state |= button; DoButton(wi, x, y, button, true, t); }
            if (up) { DoButton(wi, x, y, button, false, t); mouse_state &= ~button; }
        }

        static void RouteMouseMove(WinInfo wi, JsonElement root, int t)
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

        static void DoButton(WinInfo wi, int fx, int fy, MouseButtons button, bool isDown, int t)
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
                    var goff = OffsetInToplevel(gh, true);
                    int tx = sx - gtop.x - goff.X;
                    int ty = sy - gtop.y - goff.Y;

                    Msg finalMsg = msgClient;
                    if (isDown) finalMsg = NextClickMsg(GrabHwnd, msgClient, msgDbl, tx, ty, t);

                    EnqueueMsg(GrabHwnd, finalMsg, MouseWParam(), PackLP(tx, ty));
                    EnqueueMsg(GrabHwnd, Msg.WM_MOUSEMOVE, MouseWParam(), PackLP(tx, ty));
                    return;
                }
                GrabHwnd = IntPtr.Zero;
            }

            // (2) topmost window under the point (popups beat their owner)
            var hit = TopmostAt(sx, sy);
            if (hit == null) return;                                // bare desktop
            DeliverButton(hit, sx - hit.Hwnd.x, sy - hit.Hwnd.y, msgClient, msgNC, msgDbl, isDown, t);
        }

        static Msg NextClickMsg(IntPtr target, Msg msgClient, Msg msgDbl, int tx, int ty, int now)
        {
            if (clickPending && clickHwnd == target && clickMsg == msgClient &&
                Math.Abs((clickL.ToInt32() & 0xFFFF) - (tx & 0xFFFF)) <= 4 &&
                Math.Abs(((clickL.ToInt32() >> 16) & 0xFFFF) - (ty & 0xFFFF)) <= 4 &&
                unchecked((uint)(now - clickTime)) < DoubleClickInterval)
            {
                clickPending = false;
                return msgDbl;
            }
            clickPending = true;
            clickHwnd = target; clickMsg = msgClient; clickL = PackLP(tx, ty); clickTime = now;
            return msgClient;
        }

        static void DeliverButton(WinInfo wi, int fx, int fy, Msg msgClient, Msg msgNC, Msg msgDbl, bool isDown, int t)
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
            if (isDown) finalMsg = NextClickMsg(target, msgClient, msgDbl, tx, ty, t);

            EnqueueMsg(target, finalMsg, MouseWParam(), PackLP(tx, ty));
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
                wi.SockPath = PathFor("win-" + wi.Handle.ToInt64().ToString("X"));
                wi.Listener = ListenWet(wi.SockPath);
                _ = WinAcceptLoopAsync(wi);
                BroadcastList("window-added", wi);
                Log("window 0x" + wi.Handle.ToInt64().ToString("X") + " '" + wi.Title + "' -> unix:" + wi.SockPath);
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

        // ── Rendering: desktop compositor ────────────────────────────────
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
                g._suppressSvg = true;
                g.Clear(Color.FromArgb(58, 58, 64));
                foreach (var w in vis)
                {
                    bool ridesAlong = vis.Any(o => o != w && IsPopupOf(w, o));
                    if (ridesAlong) continue;
                    using (var f = BuildCompositedFrame(w))
                        if (f != null) g.DrawImage(f, w.Hwnd.x, w.Hwnd.y);
                }
            }
            return bmp;
        }

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

            var wi = RegisterWindow(hwnd, cp);
            PerformNCCalc(hwnd);
            AddExpose(hwnd, false, 0, 0, hwnd.width, hwnd.height);   // initial NC/border paint

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
                    CloseWinSockets(wi);
                    Log("window 0x" + handle.ToInt64().ToString("X") + " destroyed");
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
                        if (hw.IsTop) { topWindows.Remove(h.Handle); zOrder.Remove(h.Handle); }
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
            if (paint_hwnd == null || !paint_hwnd.visible)
                return new PaintEventArgs(Graphics.FromImage(new Bitmap(1, 1)), Rectangle.Empty);
            if (hwnd == null) hwnd = paint_hwnd;

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

                Rectangle bounds = client
                    ? new Rectangle(0, 0, paint_hwnd.ClientRect.Width, paint_hwnd.ClientRect.Height)
                    : new Rectangle(0, 0, paint_hwnd.width, paint_hwnd.height);
                Rectangle clip = bounds;

                if (client && wi.Buffer != null && !hwnd.Invalid.IsEmpty &&
                    hwnd.Invalid.Width >= bounds.Width * 0.9f &&
                    hwnd.Invalid.Height >= bounds.Height * 0.9f)
                    wi.Buffer.ClearSvg();
                if (client && !hwnd.Invalid.IsEmpty) clip = Rectangle.Intersect(bounds, hwnd.Invalid);
                else if (!client && !hwnd.nc_invalid.IsEmpty) clip = Rectangle.Intersect(bounds, hwnd.nc_invalid);
                if (!clip.IsEmpty) dc.SetClip(clip);

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
                    g._suppressSvg = true;
                    g.SetClip(dest);

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

        static Bitmap BuildCompositedFrame(WinInfo wi)
        {
            Bitmap frame = BuildTopFrame(wi);
            if (frame == null) return null;
            var pops = PopupsFor(wi);
            if (pops.Count == 0) return frame;
            var comp = new Bitmap(frame.Width, frame.Height);
            using (var g = Graphics.FromImage(comp))
            {
                g._suppressSvg = true;
                g.DrawImage(frame, 0, 0);
                foreach (var p in pops)
                    using (var pf = BuildTopFrame(p))
                        if (pf != null)
                            g.DrawImage(pf, p.Hwnd.x - wi.Hwnd.x, p.Hwnd.y - wi.Hwnd.y);
            }
            frame.Dispose();
            return comp;
        }

        static Bitmap BuildTopFrame(WinInfo topWi)
        {
            var top = topWi.Hwnd;
            var frame = new Bitmap(Math.Max(1, top.width), Math.Max(1, top.height));
            using (var g = Graphics.FromImage(frame))
            {
                g._suppressSvg = true;
                lock (topWi.BufLock)
                    if (topWi.Buffer != null)
                        g.DrawImage(topWi.Buffer, 0, 0);

                var c = Control.FromHandle(top.Handle);
                if (c != null) RenderChildren(topWi, g);
            }
            DrawCaretIfAny(top, frame);
            return frame;
        }

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
            if (c != null) SvgChildren(wi, sb);
            sb.Append("</svg>");
            return sb.ToString();
        }

        static string BuildTopFrameSvg(WinInfo topWi)
        {
            var top = topWi.Hwnd;
            var own = WindowOwnSvg(topWi);
            var pops = PopupsFor(topWi);
            if (pops.Count == 0) return own;

            int close = own.LastIndexOf("</svg>", StringComparison.Ordinal);
            var sb = new StringBuilder(close >= 0 ? own.Substring(0, close) : own);
            foreach (var p in pops)
                sb.Append(Nest(WindowOwnSvg(p),
                               p.Hwnd.x - top.x, p.Hwnd.y - top.y,
                               p.Hwnd.width, p.Hwnd.height));
            sb.Append("</svg>");
            return sb.ToString();
        }

        static void SvgChildren(WinInfo parentWi, StringBuilder sb)
        {
            foreach (var wi in GetChildWindows(parentWi))
            {
                var h = wi.Hwnd;
                if (h == null || h.zombie || !h.visible) continue;
                var off = OffsetInToplevel(h, false);
                lock (wi.BufLock)
                    if (wi.Buffer != null)
                        sb.Append(Nest(wi.Buffer.AsSvg(), off.X, off.Y, h.width, h.height));
                SvgChildren(wi, sb);
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

            var off = OffsetInToplevel(h, true);
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

        static WinInfo GetWinInfo(IntPtr handle)
        {
            lock (Sync) return windows.TryGetValue(handle, out var wi) ? wi : null;
        }

        static List<WinInfo> GetChildWindows(WinInfo parentWi)
        {
            var parent = parentWi.Hwnd;
            var list = new List<WinInfo>();
            var ctrl = Control.FromHandle(parent.Handle);
            var explicitHandles = new HashSet<IntPtr>();
            if (ctrl != null)
            {
                for (int i = ctrl.Controls.Count - 1; i >= 0; i--)
                {
                    var c = ctrl.Controls[i];
                    if (!c.IsHandleCreated) continue;
                    explicitHandles.Add(c.Handle);
                    WinInfo wi;
                    lock (Sync) windows.TryGetValue(c.Handle, out wi);
                    if (wi != null) list.Add(wi);
                }
            }

            lock (Sync)
            {
                foreach (var kv in windows)
                {
                    if (kv.Value.Hwnd == null || kv.Value.Hwnd.parent != parent || kv.Value.Hwnd.zombie) continue;
                    if (explicitHandles.Contains(kv.Key)) continue;
                    list.Add(kv.Value);
                }
            }
            return list;
        }

        static void RenderChildren(WinInfo parentWi, Graphics g)
        {
            foreach (var wi in GetChildWindows(parentWi))
            {
                var h = wi.Hwnd;
                if (h == null || h.zombie || !h.visible) continue;
                var off = OffsetInToplevel(h, false);
                lock (wi.BufLock)
                    if (wi.Buffer != null)
                        g.DrawImage(wi.Buffer, off.X, off.Y);
                RenderChildren(wi, g);
            }
        }

        static void PushRelated(WinInfo wi)
        {
            PushFrames(wi);
            if (desktopWi != null && wi != desktopWi) PushFrames(desktopWi);
            List<WinInfo> tops;
            lock (Sync) tops = topWindows.Values.ToList();
            foreach (var t in tops)
            {
                if (t == wi || t.Hwnd == null || !t.Hwnd.visible || t.Hwnd.zombie) continue;
                bool related = IsPopupOf(t, wi) || IsPopupOf(wi, t);
                if (!related && wi.IsTop && wi.Hwnd != null && wi.Hwnd.visible && !wi.Hwnd.zombie && LooksPopup(wi))
                {
                    var a = new Rectangle(wi.Hwnd.x, wi.Hwnd.y, wi.Hwnd.width, wi.Hwnd.height);
                    var b = new Rectangle(t.Hwnd.x, t.Hwnd.y, t.Hwnd.width, t.Hwnd.height);
                    related = a.IntersectsWith(b);
                }
                if (related) PushFrames(t);
            }
        }

        static void Composite(WinInfo wi)
        {
            var hwnd = wi.Hwnd;
            var top = Toplevel(hwnd);
            if (hwnd == top)
            {
                PushRelated(wi);
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
            if (topWi != null) PushRelated(topWi);
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
                        wi.Buffer.ClearSvg();
                        var r = Rectangle.Intersect(area, new Rectangle(0, 0, wi.Buffer.Width, wi.Buffer.Height));
                        if (r.Width > 0 && r.Height > 0)
                        {
                            using (var tmp = new Bitmap(r.Width, r.Height))
                            {
                                using (var g = Graphics.FromImage(tmp))
                                {
                                    g._suppressSvg = true;
                                    g.DrawImage(wi.Buffer, 0, 0, r, GraphicsUnit.Pixel);
                                }
                                using (var g2 = Graphics.FromImage(wi.Buffer))
                                {
                                    g2.SetClip(r);
                                    g2.DrawImage(tmp, r.X + XAmount, r.Y + YAmount);
                                }
                            }
                        }
                    }
                }
                Composite(wi);
            }
            Invalidate(handle, area, false);
        }

        internal override void ScrollWindow(IntPtr handle, int XAmount, int YAmount, bool with_children)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            var rect = hwnd.ClientRect;
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

        // ── Waiting without blocking ─────────────────────────────────────
        // Desktop and threaded wasm: a real blocking wait is fine. Single-threaded
        // browser-wasm: ManualResetEventSlim.Wait throws PlatformNotSupportedException
        // (and even where it is legal on the main thread it would starve the JS
        // interop that feeds this queue). We probe once and remember.
        static bool canBlock = true;

        /*static void WaitForWork(SocketQueue q, int timeoutMs)
        {
            if (!canBlock) return;
            try
            {
                q.Evt.Wait(timeoutMs);
                q.Evt.Reset();
            }
            catch (PlatformNotSupportedException)
            {
                canBlock = false;
                Log("blocking waits unavailable (single-threaded browser-wasm) — " +
                    "the UI is driven by PumpMessages()/DoEvents(); see PumpMessages().");
            }
        }*/

        internal static Action JsYield;

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
                JsYield?.Invoke();

                WaitForWork(q, NextTimeout(250));

                if (!canBlock)
                {
                    // Single-threaded browser-wasm: we cannot wait here. End the
                    // Run loop cleanly and let the browser host keep dispatching
                    // through PumpMessages()/DoEvents() (form.Show() + [JSExport] Pump).
                    msg.hwnd = IntPtr.Zero;
                    msg.message = Msg.WM_QUIT;
                    msg.wParam = IntPtr.Zero;
                    msg.lParam = IntPtr.Zero;
                    return false;
                }
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

        /// <summary>
        /// Cooperative pump for the browser: drain the queue once without blocking.
        /// Export this to JS ([JSExport] Pump) and call it from a timer
        /// (requestAnimationFrame / setInterval) — that is the ST message loop.
        /// </summary>
        public static void PumpMessages()
        {
            try { Instance?.DoEvents(); } catch { }
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
                lock (Sync) { zOrder.Remove(handle); zOrder.Add(handle); }

            if (!visible && hwnd.parent != null)
                AddExpose(hwnd.parent, true, 0, 0, hwnd.parent.width, hwnd.parent.height);

            SendMessageStatic(handle, Msg.WM_SHOWWINDOW, visible ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
            SendMessageStatic(handle, Msg.WM_WINDOWPOSCHANGED, IntPtr.Zero, IntPtr.Zero);
            if (!visible && hwnd.parent != null)
                AddExpose(hwnd.parent, true, hwnd.x, hwnd.y, hwnd.width, hwnd.height);
            if (visible)
            {
                AddExpose(hwnd, true, 0, 0, hwnd.width, hwnd.height);
                UpdateWindow(hwnd.Handle);   // paint BEFORE pushing frames so the buffer isn't empty

                WinInfo wi;
                lock (Sync) windows.TryGetValue(handle, out wi);
                if (wi != null)
                {
                    if (wi.IsTop) { PushRelated(wi); BroadcastList("window-updated", wi); }
                    else
                    {
                        var top = Toplevel(hwnd);
                        WinInfo topWi;
                        lock (Sync) windows.TryGetValue(top.Handle, out topWi);
                        if (topWi != null) PushRelated(topWi);
                    }
                }
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

            hwnd.parent = Hwnd.ObjectFromHandle(parent);
            if (oldTop != null && oldTop.Handle != handle)
                AddExpose(oldTop, true, 0, 0, oldTop.width, oldTop.height);
            if (hwnd.parent != null)
                AddExpose(hwnd.parent, true, 0, 0, hwnd.parent.width, hwnd.parent.height);

            var newTop = Toplevel(hwnd);
            WinInfo w1, w2;
            lock (Sync)
            {
                windows.TryGetValue(oldTop?.Handle ?? IntPtr.Zero, out w1);
                windows.TryGetValue(newTop?.Handle ?? IntPtr.Zero, out w2);
            }
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

        // ── Driver: caret (drawn into the served frame at render time) ───
        class CaretState
        {
            public IntPtr Hwnd;
            public int X, Y, W, H;
            public bool Visible;
            public bool On;
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
                if (caret.Visible && !caret.On) caret.On = true;
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
                    caret.Timer ??= new System.Threading.Timer(_ => CaretBlink(), null, Timeout.Infinite, Timeout.Infinite);
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
        internal override void AudibleAlert(AlertType alert) { try { Console.Beep(); } catch { } }
        internal override void BeginMoveResize(IntPtr handle) { }
        internal override void EnableThemes() => themes_enabled = true;
        internal override void HandleException(Exception e)
        {
            Log("exception: " + e.Message);
            try { Console.WriteLine("{0}{1}", e.Message, new StackTrace(e, true)); } catch { }
        }
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
        internal override int ClipboardGetID(IntPtr handle, string format) => 0;
        internal override int[] ClipboardAvailableFormats(IntPtr handle) => clipObj == null ? new int[0] : new[] { clipType };
        internal override void ClipboardStore(IntPtr handle, object obj, int type, XplatUI.ObjectToClipboard converter, bool copy)
        { clipObj = obj; clipType = type; }
        internal override object ClipboardRetrieve(IntPtr handle, int type, XplatUI.ClipboardToObject converter)
            => type == clipType ? clipObj : null;

        // ── Driver: font metrics / autoscale ─────────────────────────────
        internal override bool GetFontMetrics(Graphics g, Font font, out int ascent, out int descent)
        {
            ascent = 0;
            descent = 0;
            return true;
        }

        internal override SizeF GetAutoScaleSize(Font font)
        {
            const string magic_string = "The quick brown fox jumped over the lazy dog.";
            const double magic_number = 1;
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
        internal override Screen[] AllScreens => new[] { new Screen(true, "WetScreen", VirtualScreen, WorkingArea) };

        internal override Keys ModifierKeys => mods;
        internal override MouseButtons MouseButtons => mouse_state;
        internal override Point MousePosition => mouse_position;

        internal override void RaiseIdle(EventArgs e) => Idle?.Invoke(this, e);
        internal override event EventHandler Idle;
    }
}

#endif