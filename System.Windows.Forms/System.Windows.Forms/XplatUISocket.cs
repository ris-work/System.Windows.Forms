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
            public Bitmap Buffer;                 // toplevel only
            public readonly object BufLock = new object();
            public FormWindowState State = FormWindowState.Normal;
            public Rectangle SavedBounds;
        }

        class ClientConn
        {
            public Socket Sock;
            public NetworkStream Ns;
            public StreamReader Rd;
            public WinInfo Win;                   // null for window-list watchers
            public bool Subscribed;
            public string Format = "jpeg";
            public int Quality = 80;
            public readonly object WLock = new object();
            public volatile bool Dead;
            public void Send(string s)
            {
                if (Dead) return;
                try { var b = Encoding.UTF8.GetBytes(s + "\n"); lock (WLock) { Ns.Write(b, 0, b.Length); Ns.Flush(); } }
                catch { Dead = true; }
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
            var h = wi.Hwnd;
            return "{\"handle\":\"0x" + wi.Handle.ToInt64().ToString("X") +
                   "\",\"title\":\"" + JEsc(wi.Title) +
                   "\",\"x\":" + h.x + ",\"y\":" + h.y +
                   ",\"width\":" + h.width + ",\"height\":" + h.height +
                   ",\"visible\":" + (h.visible ? "true" : "false") +
                   ",\"state\":\"" + wi.State +
                   "\",\"socket\":\"" + JEsc(wi.SockPath) + "\"}";
        }

        static string ListJson()
        {
            lock (Sync)
            {
                var sb = new StringBuilder("{\"type\":\"list\",\"screen\":[" + screenW + "," + screenH + "],\"windows\":[");
                bool first = true;
                foreach (var wi in topWindows.Values)
                {
                    if (!first) sb.Append(',');
                    sb.Append(WindowJson(wi));
                    first = false;
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
                cc.Send("{\"type\":\"hello\",\"protocol\":1,\"screen\":[" + screenW + "," + screenH + "],\"window\":" + WindowJson(wi) + "}");
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
            try { cc.Sock.Close(); } catch { }
        }

        // ── Frame encoding / pushing ─────────────────────────────────────
        static byte[] EncodeFrame(Bitmap bmp, string fmt, int quality)
        {
            using (var img = SKImage.FromBitmap(bmp._skBitmap))
            using (var data = img.Encode(fmt == "jpeg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, quality))
                return data.ToArray();
        }

        static void SendFrame(ClientConn cc)
        {
            var wi = cc.Win;
            if (wi == null) return;
            byte[] bytes = null;
            int w = 0, h = 0;
            lock (wi.BufLock)
            {
                if (wi.Buffer != null)
                {
                    w = wi.Buffer.Width; h = wi.Buffer.Height;
                    bytes = EncodeFrame(wi.Buffer, cc.Format, cc.Quality);
                }
            }
            if (bytes == null)
                cc.Send("{\"type\":\"frame\",\"format\":\"" + cc.Format + "\",\"width\":" + wi.Hwnd.width + ",\"height\":" + wi.Hwnd.height + ",\"data\":\"\"}");
            else
                cc.Send("{\"type\":\"frame\",\"format\":\"" + cc.Format + "\",\"width\":" + w + ",\"height\":" + h + ",\"data\":\"" + Convert.ToBase64String(bytes) + "\"}");
        }

        static void PushFrames(WinInfo wi)
        {
            ClientConn[] subs;
            lock (Sync) subs = wi.Clients.FindAll(c => c.Subscribed && !c.Dead).ToArray();
            foreach (var c in subs) SendFrame(c);
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
                    cc.Send("{\"type\":\"info\",\"window\":" + WindowJson(wi) + "}");
                    break;

                case "refresh":
                    if (root.TryGetProperty("format", out var f)) cc.Format = f.GetString() == "png" ? "png" : "jpeg";
                    if (root.TryGetProperty("quality", out var q)) cc.Quality = Math.Max(1, Math.Min(100, q.GetInt32()));
                    SendFrame(cc);
                    break;

                case "subscribe":
                    cc.Subscribed = true;
                    if (root.TryGetProperty("format", out var f2)) cc.Format = f2.GetString() == "png" ? "png" : "jpeg";
                    if (root.TryGetProperty("quality", out var q2)) cc.Quality = Math.Max(1, Math.Min(100, q2.GetInt32()));
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
                        int rw = root.GetProperty("width").GetInt32();
                        int rh = root.GetProperty("height").GetInt32();
                        var handle = wi.Handle;
                        var c = Control.FromHandle(handle);
                        if (c != null)
                            c.BeginInvoke((Action)(() => { try { c.ClientSize = new Size(rw, rh); } catch { } }));
                        break;
                    }

                case "close":
                    {
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
            if (prev != IntPtr.Zero) SendMessageStatic(prev, Msg.WM_ACTIVATE, (IntPtr)WindowActiveFlags.WA_INACTIVE, IntPtr.Zero);
            SendMessageStatic(handle, Msg.WM_ACTIVATE, (IntPtr)WindowActiveFlags.WA_ACTIVE, IntPtr.Zero);
        }

        static IntPtr SendMessageStatic(IntPtr hwnd, Msg m, IntPtr w, IntPtr l) => NativeWindow.WndProc(hwnd, m, w, l);

        static Control HitTestDeep(Control parent, Point p)
        {
            for (int i = parent.Controls.Count - 1; i >= 0; i--)
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
            var topHwnd = wi.Hwnd;
            int cx = fx - topHwnd.ClientRect.X;
            int cy = fy - topHwnd.ClientRect.Y;

            Msg msgClient, msgNC, msgDbl;
            switch (button)
            {
                case MouseButtons.Right:
                    msgClient = isDown ? Msg.WM_RBUTTONDOWN : Msg.WM_RBUTTONUP;
                    msgNC = isDown ? Msg.WM_NCRBUTTONDOWN : Msg.WM_NCRBUTTONUP;
                    msgDbl = Msg.WM_RBUTTONDBLCLK; break;
                case MouseButtons.Middle:
                    msgClient = isDown ? Msg.WM_MBUTTONDOWN : Msg.WM_MBUTTONUP;
                    msgNC = isDown ? Msg.WM_NCMBUTTONDOWN : Msg.WM_NCMBUTTONUP;
                    msgDbl = Msg.WM_MBUTTONDBLCLK; break;
                default:
                    msgClient = isDown ? Msg.WM_LBUTTONDOWN : Msg.WM_LBUTTONUP;
                    msgNC = isDown ? Msg.WM_NCLBUTTONDOWN : Msg.WM_NCLBUTTONUP;
                    msgDbl = Msg.WM_LBUTTONDBLCLK; break;
            }

            // NC area (frame coords outside client rect)
            if (cx < 0 || cy < 0 || cx >= topHwnd.ClientRect.Width || cy >= topHwnd.ClientRect.Height)
            {
                EnqueueMsg(topHwnd.Handle, msgNC, (IntPtr)HitTest.HTCLIENT, PackLP(fx, fy));
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

            Msg finalMsg = msgClient;
            if (isDown)
            {
                int now = Environment.TickCount;
                if (clickPending && clickHwnd == target && clickMsg == msgClient && clickL == PackLP(tx, ty) &&
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

            // Win32 splurts a mousemove after up/down; some apps rely on it
            EnqueueMsg(target, Msg.WM_MOUSEMOVE, MouseWParam(), PackLP(tx, ty));
        }

        static void RouteMouseMove(WinInfo wi, JsonElement root)
        {
            int fx = root.TryGetProperty("x", out var xe) ? xe.GetInt32() : 0;
            int fy = root.TryGetProperty("y", out var ye) ? ye.GetInt32() : 0;
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
                if (!string.IsNullOrEmpty(text))
                    foreach (var ch in text)
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
                if (isTop) topWindows[wi.Handle] = wi;
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
                    if (wi.IsTop) topWindows.Remove(handle);
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
                h.zombie = true;
                h.expose_pending = h.nc_expose_pending = false;
                h.Dispose();
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
            var hwnd = Hwnd.ObjectFromHandle(msg.HWnd) ?? Hwnd.ObjectFromHandle(handle);
            if (hwnd == null)
                return new PaintEventArgs(Graphics.FromImage(new Bitmap(1, 1)), Rectangle.Empty);

            var top = Toplevel(hwnd);
            WinInfo wi;
            lock (Sync) windows.TryGetValue(top.Handle, out wi);
            if (wi == null)
                return new PaintEventArgs(Graphics.FromImage(new Bitmap(1, 1)), Rectangle.Empty);

            Monitor.Enter(wi.BufLock);
            try
            {
                var bmp = GetBuffer(wi, top.width, top.height);
                var off = OffsetInToplevel(hwnd, client);
                Graphics dc = Graphics.FromImage(bmp);
                if (off.X != 0 || off.Y != 0) dc.TranslateTransform(off.X, off.Y);

                Rectangle clip;
                if (client)
                {
                    clip = hwnd.Invalid.IsEmpty ? new Rectangle(0, 0, hwnd.ClientRect.Width, hwnd.ClientRect.Height) : hwnd.Invalid;
                    if (!clip.IsEmpty) dc.SetClip(clip);
                    hwnd.expose_pending = false;
                    hwnd.ClearInvalidArea();
                }
                else
                {
                    clip = !hwnd.nc_invalid.IsEmpty ? hwnd.nc_invalid : new Rectangle(0, 0, hwnd.width, hwnd.height);
                    if (!clip.IsEmpty) dc.SetClip(clip);
                    hwnd.nc_expose_pending = false;
                    hwnd.ClearNcInvalidArea();
                }
                return new SocketPaintEventArgs(dc, clip, wi);
            }
            catch
            {
                Monitor.Exit(wi.BufLock);
                throw;
            }
        }

        internal override void PaintEventEnd(ref Message msg, IntPtr handle, bool client, PaintEventArgs pevent)
        {
            if (pevent.Graphics != null) { pevent.Graphics.Flush(); pevent.Graphics.Dispose(); }
            pevent.SetGraphics(null);
            if (pevent is SocketPaintEventArgs spea && spea.Win != null)
            {
                var wi = spea.Win;
                Monitor.Exit(wi.BufLock);
                PushFrames(wi);
            }
            pevent.Dispose();
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
            Invalidate(handle, area, false);
        }

        internal override void ScrollWindow(IntPtr handle, int XAmount, int YAmount, bool with_children)
        {
            var hwnd = Hwnd.ObjectFromHandle(handle);
            if (hwnd == null) return;
            Invalidate(handle, new Rectangle(0, 0, hwnd.Width, hwnd.Height), false);
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
            SendMessageStatic(handle, Msg.WM_SHOWWINDOW, visible ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
            SendMessageStatic(handle, Msg.WM_WINDOWPOSCHANGED, IntPtr.Zero, IntPtr.Zero);
            if (visible)
            {
                AddExpose(hwnd, true, 0, 0, hwnd.width, hwnd.height);
                WinInfo wi;
                lock (Sync) windows.TryGetValue(handle, out wi);
                if (wi != null && wi.IsTop) { PushFrames(wi); BroadcastList("window-updated", wi); }
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
        internal override void CreateCaret(IntPtr hwnd, int width, int height) { }
        internal override void DestroyCaret(IntPtr hwnd) { }
        internal override void SetCaretPos(IntPtr hwnd, int x, int y) { }
        internal override void CaretVisible(IntPtr hwnd, bool visible) { }

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
        internal override int ClipboardGetID(IntPtr handle, string format) => DataFormats.GetFormat(format).Id;
        internal override int[] ClipboardAvailableFormats(IntPtr handle) => clipObj == null ? new int[0] : new[] { clipType };
        internal override void ClipboardStore(IntPtr handle, object obj, int type, XplatUI.ObjectToClipboard converter, bool copy)
        { clipObj = obj; clipType = type; }
        internal override object ClipboardRetrieve(IntPtr handle, int type, XplatUI.ClipboardToObject converter)
            => type == clipType ? clipObj : null;

        // ── Driver: font metrics / autoscale ─────────────────────────────
        internal override bool GetFontMetrics(Graphics g, Font font, out int ascent, out int descent)
        {
            var ff = font.FontFamily;
            ascent = ff.GetCellAscent(font.Style);
            descent = ff.GetCellDescent(font.Style);
            return true;
        }

        internal override SizeF GetAutoScaleSize(Font font)
        {
            const string magic_string = "The quick brown fox jumped over the lazy dog.";
            const double magic_number = 44.549996948242189;
            using (var bmp = new Bitmap(1, 1))
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