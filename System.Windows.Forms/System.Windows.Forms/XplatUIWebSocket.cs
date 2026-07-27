// XplatUIWebSocket.cs — ASP.NET Core bridge that exposes the XplatUISocket Unix
// socket surface over HTTP + WebSocket. Same JSON wire protocol; browser-friendly.
//
// Environment variables:
//   XPLAT_UI_SOCKET_DIR   directory containing window-list + win-*.sock (default "windows")
//   XPLAT_UI_WS_LISTEN    TCP endpoint for the WS server, e.g. "127.0.0.1:5140"
//                         (default if XPLAT_UI_WS_UNIX is unset)
//   XPLAT_UI_WS_UNIX      Unix socket path for the WS server (wins over TCP if set)
//   XPLAT_UI_WS_HTML      path to a custom viewer HTML (optional; embedded default otherwise)

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Windows.Forms
{
    internal static class XplatUIWebSocket
    {
        static string SockDir;
        static string HtmlPath;
        static string WsUnixPath;
        static IPEndPoint WsTcpEndpoint;
        static WebApplication App;
        static readonly object StartLock = new object();
        static bool started;

        // ── Public API ────────────────────────────────────────────────
        public static void EnsureStarted()
        {
            lock (StartLock)
            {
                if (started) return;
                started = true;

                SockDir = Environment.GetEnvironmentVariable("XPLAT_UI_SOCKET_DIR") ?? "windows";
                HtmlPath = Environment.GetEnvironmentVariable("XPLAT_UI_WS_HTML");
                WsUnixPath = Environment.GetEnvironmentVariable("XPLAT_UI_WS_UNIX");
                var listen = Environment.GetEnvironmentVariable("XPLAT_UI_WS_LISTEN");

                if (string.IsNullOrEmpty(WsUnixPath) && string.IsNullOrEmpty(listen))
                    listen = "127.0.0.1:5140";

                if (!string.IsNullOrEmpty(listen))
                {
                    var colon = listen.LastIndexOf(':');
                    if (colon > 0 && int.TryParse(listen.Substring(colon + 1), out var port))
                        WsTcpEndpoint = new IPEndPoint(IPAddress.Parse(listen.Substring(0, colon)), port);
                    else
                        WsTcpEndpoint = new IPEndPoint(IPAddress.Loopback, 5140);
                }

                var builder = WebApplication.CreateBuilder(Array.Empty<string>());

                if (!string.IsNullOrEmpty(WsUnixPath))
                {
                    try { if (File.Exists(WsUnixPath)) File.Delete(WsUnixPath); } catch { }
                    builder.WebHost.ConfigureKestrel(o => o.ListenUnixSocket(WsUnixPath));
                }
                else
                {
                    builder.WebHost.ConfigureKestrel(o => o.Listen(WsTcpEndpoint));
                }

                App = builder.Build();
                ConfigureRoutes(App);
                _ = App.RunAsync();

                var where = WsUnixPath ?? WsTcpEndpoint.ToString();
                Console.Error.WriteLine($"[XplatUIWebSocket] serving at {(WsUnixPath != null ? "unix:" + WsUnixPath : "http://" + where)} (sockdir={Path.GetFullPath(SockDir)})");
            }
        }

        public static async Task StopAsync()
        {
            WebApplication a;
            lock (StartLock) { a = App; App = null; started = false; }
            if (a != null) { try { await a.StopAsync(); } catch { } }
        }

        // ── Routing ───────────────────────────────────────────────────
        static void ConfigureRoutes(WebApplication app)
        {
            app.MapGet("/", async (HttpContext ctx) =>
            {
                string html = null;
                if (!string.IsNullOrEmpty(HtmlPath) && File.Exists(HtmlPath))
                    html = await File.ReadAllTextAsync(HtmlPath, ctx.RequestAborted);
                else
                    html = await LoadEmbeddedHtmlAsync();

                if (html == null)
                {
                    ctx.Response.StatusCode = 404;
                    await ctx.Response.WriteAsync("viewer.html embedded resource not found. Check console output for available resources.", ctx.RequestAborted);
                    return;
                }

                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync(html, ctx.RequestAborted);
            });

            app.MapGet("/api/list", async (HttpContext ctx) =>
            {
                try
                {
                    var json = await DialOneShot(Path.Combine(SockDir, "window-list"), ctx.RequestAborted);
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(json ?? "{\"type\":\"list\",\"screen\":[1920,1080],\"windows\":[]}", ctx.RequestAborted);
                }
                catch (Exception ex)
                {
                    ctx.Response.StatusCode = 502;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"error\":\"" + JEsc(ex.Message) + "\"}", ctx.RequestAborted);
                }
            });

            app.MapGet("/list", async (HttpContext ctx) =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = 400;
                    return;
                }
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                await ProxyToUnix(ws, Path.Combine(SockDir, "window-list"), ctx.RequestAborted);
            });

            app.MapGet("/window/{handle}", async (HttpContext ctx) =>
            {
                var handle = ctx.GetRouteValue("handle") as string;
                var path = HandleToSocketPath(handle);
                if (path == null)
                {
                    ctx.Response.StatusCode = 404;
                    await ctx.Response.WriteAsync("no such window socket", ctx.RequestAborted);
                    return;
                }
                if (!ctx.WebSockets.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = 400;
                    return;
                }
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                await ProxyToUnix(ws, path, ctx.RequestAborted);
            });

            app.UseWebSockets();
        }

        // ── Helpers ───────────────────────────────────────────────────
        static string HandleToSocketPath(string handle)
        {
            if (string.IsNullOrEmpty(handle)) return null;
            string path;
            if (handle == "desktop")
                path = Path.Combine(SockDir, "compositor");
            else
            {
                string hex = handle.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? handle.Substring(2) : handle;
                path = Path.Combine(SockDir, "win-" + hex + ".sock");
            }
            return File.Exists(path) ? path : null;
        }

        static async Task<string> DialOneShot(string unixPath, CancellationToken ct)
        {
            using var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            sock.Connect(new UnixDomainSocketEndPoint(unixPath));
            using var ns = new NetworkStream(sock, true);
            using var rd = new StreamReader(ns, Encoding.UTF8);
            return await rd.ReadLineAsync() ?? "";
        }

        // Each WS text message ↔ one newline-delimited JSON line on the Unix side.
        static async Task ProxyToUnix(WebSocket ws, string unixPath, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Socket sock;
            try
            {
                sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                sock.Connect(new UnixDomainSocketEndPoint(unixPath));
            }
            catch (Exception ex)
            {
                try
                {
                    var err = Encoding.UTF8.GetBytes("{\"type\":\"error\",\"message\":\"connect: " + JEsc(ex.Message) + "\"}");
                    await ws.SendAsync(err, WebSocketMessageType.Text, true, ct);
                }
                catch { }
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "connect failed", ct); } catch { }
                return;
            }

            using (sock)
            using (var ns = new NetworkStream(sock, true))
            using (var rd = new StreamReader(ns, Encoding.UTF8))
            {
                // WS → Unix
                var wsToUnix = Task.Run(async () =>
                {
                    var buf = new byte[16 * 1024];
                    var sb = new StringBuilder();
                    try
                    {
                        while (true)
                        {
                            var r = await ws.ReceiveAsync(buf, cts.Token);
                            if (r.MessageType == WebSocketMessageType.Close) break;
                            if (r.Count > 0)
                                sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                            if (!r.EndOfMessage) continue;
                            var text = sb.ToString();
                            sb.Clear();
                            if (!string.IsNullOrEmpty(text))
                            {
                                var bytes = Encoding.UTF8.GetBytes(text + "\n");
                                await ns.WriteAsync(bytes, cts.Token);
                                await ns.FlushAsync(cts.Token);
                            }
                        }
                    }
                    catch { }
                    try { sock.Shutdown(SocketShutdown.Both); } catch { }
                    try { sock.Close(); } catch { }
                }, cts.Token);

                // Unix → WS (line-buffered)
                var unixToWs = Task.Run(async () =>
                {
                    try
                    {
                        string line;
                        while (true)
                        {
                            line = await rd.ReadLineAsync();
                            if (line == null) break;
                            if (line.Length == 0) continue;
                            var bytes = Encoding.UTF8.GetBytes(line);
                            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
                        }
                    }
                    catch { }
                    cts.Cancel();
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { }
                }, cts.Token);

                await Task.WhenAny(wsToUnix, unixToWs);
                cts.Cancel();
                try { await wsToUnix; } catch { }
                try { await unixToWs; } catch { }
            }
        }

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

        // ── Embedded default viewer HTML (single-quoted to survive verbatim @"…") ──

        // ── Embedded viewer HTML loader ────────────────────────────────
        static string _embeddedHtmlCache;
        static async Task<string> LoadEmbeddedHtmlAsync()
        {
            if (_embeddedHtmlCache != null) return _embeddedHtmlCache;

            var asm = typeof(XplatUIWebSocket).Assembly;
            var names = asm.GetManifestResourceNames();
            Console.Error.WriteLine("[XplatUIWebSocket] Manifest resources:");
            foreach (var n in names)
                Console.Error.WriteLine("  - " + n);

            // Look for a resource that ends with "viewer.html"
            string resName = names.FirstOrDefault(n => n.EndsWith("viewer.html", StringComparison.OrdinalIgnoreCase));
            if (resName == null)
            {
                Console.Error.WriteLine("[XplatUIWebSocket] WARNING: 'viewer.html' embedded resource not found.");
                Console.Error.WriteLine("[XplatUIWebSocket] Make sure your .csproj contains <EmbeddedResource Include=\"viewer.html\" />");
                return null;
            }

            using (var stream = asm.GetManifestResourceStream(resName))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                _embeddedHtmlCache = await reader.ReadToEndAsync();
            return _embeddedHtmlCache;
        }
    

static string DefaultHtml() => @"<!doctype html>
<html><head><meta charset='utf-8'>
<title>XplatUI WebSocket Viewer</title>
<style>
body { margin:0; background:#1e1e22; color:#ddd; font:13px sans-serif; display:flex; height:100vh; }
#sidebar { width:240px; background:#26262c; padding:8px; overflow:auto; box-sizing:border-box; }
#sidebar h2 { font-size:14px; margin:0 0 8px; }
#sidebar div.win { padding:6px; margin:2px 0; cursor:pointer; border-radius:4px; }
#sidebar div.win:hover { background:#3a3a44; }
#sidebar div.win.active { background:#4a6fa5; color:#fff; }
#sidebar label { display:block; margin:6px 0; }
#stage { flex:1; display:flex; align-items:center; justify-content:center; overflow:hidden; position:relative; background:#18181c; }
#frame { max-width:100%; max-height:100%; image-rendering:pixelated; }
#svgwrap { width:100%; height:100%; }
#svgwrap svg { width:100%; height:100%; }
#bar { position:absolute; top:8px; right:8px; background:rgba(0,0,0,0.5); padding:6px 10px; border-radius:6px; font-family:monospace; font-size:11px; }
#log { position:absolute; bottom:0; left:0; right:0; max-height:120px; overflow:auto; background:rgba(0,0,0,0.7); padding:6px; font:11px monospace; display:none; color:#9fd0ff; }
</style></head><body>
<div id='sidebar'>
  <h2>Windows</h2>
  <div id='winlist'></div>
  <hr>
  <label>Format <select id='fmt'><option>jpeg</option><option>png</option><option>svg</option></select></label>
  <label><input type='checkbox' id='gray'> Gray</label>
  <label>Quality <input type='range' id='q' min='1' max='100' value='75'></label>
  <label><input type='checkbox' id='live' checked> Live</label>
  <button id='refresh'>Refresh</button>
  <button id='save'>Save frame</button>
</div>
<div id='stage'>
  <div id='bar'>not connected</div>
  <img id='frame' style='display:none'>
  <div id='svgwrap' style='display:none'></div>
  <div id='log'></div>
</div>
<script>
const $ = s => document.querySelector(s);
const log = (...a) => { const el = $('#log'); el.style.display='block'; el.innerHTML += a.join(' ') + '<br>'; el.scrollTop = el.scrollHeight; };

let listWs = null, winWs = null, curHandle = null, frameCount = 0, lastSvg = null;

function setBar(t) { $('#bar').textContent = t; }
function sendWin(obj) { if (winWs && winWs.readyState === 1) winWs.send(JSON.stringify(obj)); }
function resub() {
  if ($('#live').checked) sendWin({type:'subscribe', format:$('#fmt').value, quality:+$('#q').value, gray:$('#gray').checked});
  sendWin({type:'refresh', format:$('#fmt').value, quality:+$('#q').value, gray:$('#gray').checked, full:true});
}

function connectList() {
  try { if (listWs) listWs.close(); } catch {}
  listWs = new WebSocket((location.protocol==='https:'?'wss:':'ws:') + '//' + location.host + '/list');
  listWs.onopen = () => setBar('list: connected');
  listWs.onmessage = ev => {
    let m; try { m = JSON.parse(ev.data); } catch { return; }
    if (m.type === 'list') renderList(m.windows);
    else if (m.type === 'window-added') addWin(m.window);
    else if (m.type === 'window-removed') removeWin(m.window.handle);
    else if (m.type === 'window-updated') updateWin(m.window);
  };
  listWs.onclose = () => { setBar('list: disconnected (retrying)'); setTimeout(connectList, 1500); };
  listWs.onerror = () => log('list error');
}

let windows = [];
function renderList(ws) { windows = ws.slice(); redrawList(); }
function addWin(w) { windows = windows.filter(x => x.handle !== w.handle); windows.push(w); redrawList(); }
function removeWin(h) { windows = windows.filter(x => x.handle !== h); if (curHandle === h) closeWin(); redrawList(); }
function updateWin(w) { windows = windows.map(x => x.handle === w.handle ? w : x); redrawList(); }
function redrawList() {
  const c = $('#winlist'); c.innerHTML = '';
  windows.forEach(w => {
    const d = document.createElement('div');
    d.className = 'win' + (w.handle === curHandle ? ' active' : '');
    d.textContent = w.title + '  [' + w.handle + ']  ' + w.width + 'x' + w.height;
    d.onclick = () => connectWin(w);
    c.appendChild(d);
  });
}

function closeWin() {
  if (winWs) { try { winWs.close(); } catch {} winWs = null; }
  curHandle = null;
  $('#frame').style.display = 'none';
  $('#svgwrap').style.display = 'none';
  $('#svgwrap').innerHTML = '';
  redrawList();
}

function connectWin(w) {
  closeWin();
  curHandle = w.handle;
  setBar('window: ' + w.title + ' ' + w.width + 'x' + w.height);
  const url = (location.protocol==='https:'?'wss:':'ws:') + '//' + location.host + '/window/' + encodeURIComponent(w.handle);
  winWs = new WebSocket(url);
  winWs.onopen = () => resub();
  winWs.onmessage = ev => {
    let m; try { m = JSON.parse(ev.data); } catch { return; }
    if (m.type === 'frame') handleFrame(m);
    else if (m.type === 'hello' || m.type === 'info') {
      if (m.window) setBar('window: ' + m.window.title + ' ' + m.window.width + 'x' + m.window.height);
    }
    else if (m.type === 'bye') closeWin();
    else if (m.type === 'pong') {}
  };
  winWs.onclose = () => { setBar('window: closed'); if (curHandle) { const w2 = windows.find(x => x.handle === curHandle); if (w2) setTimeout(() => connectWin(w2), 2000); } };
  winWs.onerror = () => log('win error');
}

function handleFrame(m) {
  frameCount++;
  if (m.format === 'svg') {
    lastSvg = m.data;
    $('#frame').style.display = 'none';
    $('#svgwrap').style.display = 'block';
    $('#svgwrap').innerHTML = m.data;
    setBar('frame #' + frameCount + ' svg ' + m.width + 'x' + m.height);
  } else {
    $('#svgwrap').style.display = 'none';
    $('#svgwrap').innerHTML = '';
    const img = $('#frame');
    img.style.display = 'block';
    img.src = 'data:image/' + m.format + ';base64,' + m.data;
    setBar('frame #' + frameCount + ' ' + m.format + ' ' + m.width + 'x' + m.height);
  }
}

function imgPos(e) {
  const img = $('#frame');
  if (img.style.display === 'none' || !img.naturalWidth) return null;
  const r = img.getBoundingClientRect();
  const scale = Math.min(r.width / img.naturalWidth, r.height / img.naturalHeight);
  const dw = img.naturalWidth * scale, dh = img.naturalHeight * scale;
  const ox = r.left + (r.width - dw) / 2, oy = r.top + (r.height - dh) / 2;
  const x = Math.floor((e.clientX - ox) / scale);
  const y = Math.floor((e.clientY - oy) / scale);
  if (x < 0 || y < 0 || x >= img.naturalWidth || y >= img.naturalHeight) return null;
  return {x, y};
}
function svgPos(e) {
  const svg = $('#svgwrap').querySelector('svg');
  if (!svg) return null;
  const r = svg.getBoundingClientRect();
  const vb = svg.viewBox && svg.viewBox.baseVal;
  const w = (vb && vb.width) || svg.width.baseVal.value;
  const h = (vb && vb.height) || svg.height.baseVal.value;
  const x = Math.floor((e.clientX - r.left) / r.width * w);
  const y = Math.floor((e.clientY - r.top) / r.height * h);
  if (x < 0 || y < 0 || x >= w || y >= h) return null;
  return {x, y};
}
function pos(e) { return $('#frame').style.display !== 'none' ? imgPos(e) : svgPos(e); }
function btnName(b) { return b === 2 ? 'right' : b === 1 ? 'middle' : 'left'; }

$('#stage').addEventListener('mousedown', e => { const p = pos(e); if (p) sendWin({type:'mousedown', x:p.x, y:p.y, button:btnName(e.button)}); });
$('#stage').addEventListener('mouseup',   e => { const p = pos(e); if (p) sendWin({type:'mouseup',   x:p.x, y:p.y, button:btnName(e.button)}); });
$('#stage').addEventListener('mousemove', e => { const p = pos(e); if (p) sendWin({type:'mousemove', x:p.x, y:p.y, button:'left'}); });
$('#stage').addEventListener('wheel',     e => { const p = pos(e); if (p) { sendWin({type:'wheel', x:p.x, y:p.y, delta:e.deltaY}); e.preventDefault(); } }, {passive:false});
$('#stage').addEventListener('contextmenu', e => e.preventDefault());

const codeToKey = {
  Enter:'Return', Backspace:'Back', Tab:'Tab', Escape:'Escape', Space:'Space',
  ShiftLeft:'LShiftKey', ShiftRight:'RShiftKey',
  ControlLeft:'LControlKey', ControlRight:'RControlKey',
  AltLeft:'LMenu', AltRight:'RMenu',
  MetaLeft:'LWin', MetaRight:'RWin',
  CapsLock:'CapitalLock', ScrollLock:'Scroll', NumLock:'NumLock', Pause:'Pause',
  Home:'Home', End:'End', PageUp:'PageUp', PageDown:'PageDown',
  Insert:'Insert', Delete:'Delete', ContextMenu:'Apps',
  Semicolon:'Oem1', Equal:'Oemplus', Comma:'Oemcomma', Minus:'OemMinus',
  Period:'OemPeriod', Slash:'OemQuestion', Backquote:'Oem8',
  BracketLeft:'OemOpenBrackets', BracketRight:'OemCloseBrackets',
  Backslash:'Oem5', Quote:'Oem7'
};
function keyName(e) {
  const c = e.code || '';
  if (c.startsWith('Key')) return c.slice(3);
  if (c.startsWith('Digit')) return c.slice(5);
  if (c.startsWith('Arrow')) return c.slice(5);
  if (c.startsWith('Numpad')) return 'NumPad' + c.slice(6);
  if (/^F\d$/.test(c)) return c;
  return codeToKey[c] || (e.key && e.key.length === 1 ? e.key.toUpperCase() : (e.key || ''));
}

document.addEventListener('keydown', e => {
  if (e.ctrlKey && (e.key === '+' || e.key === '=' || e.key === '-' || e.key === '0')) return;
  sendWin({type:'keydown', key:keyName(e)});
  if (e.key && e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey)
    sendWin({type:'text', text:e.key});
  if (['Tab','ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Backspace',' '].includes(e.key)) e.preventDefault();
});
document.addEventListener('keyup', e => { sendWin({type:'keyup', key:keyName(e)}); });

$('#refresh').onclick = () => resub();
$('#save').onclick = () => {
  if (lastSvg) {
    const blob = new Blob([lastSvg], {type:'image/svg+xml'});
    const a = document.createElement('a'); a.href = URL.createObjectURL(blob);
    a.download = 'frame.svg'; a.click();
  } else {
    const img = $('#frame'); if (!img.src) return;
    const a = document.createElement('a'); a.href = img.src; a.download = 'frame.png'; a.click();
  }
};
$('#fmt').onchange = () => resub();
$('#q').onchange = () => resub();
$('#gray').onchange = () => resub();
$('#live').onchange = () => {
  if ($('#live').checked) sendWin({type:'subscribe', format:$('#fmt').value, quality:+$('#q').value, gray:$('#gray').checked});
  else sendWin({type:'unsubscribe'});
};

connectList();
</script></body></html>";
    }
}