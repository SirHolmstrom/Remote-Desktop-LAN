using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;
using Core.Capture;
using Core.Config;
using Core.Files;
using Core.Input;
using Core.Logging;
using Core.Security;
using Core.Streaming;

// ---------------------------------------------------------------------------
// Remote Desktop LAN — in-session ASP.NET Core host.
// LAN-only, login-gated, TLS, audit-logged. NOT for public internet exposure.
// ---------------------------------------------------------------------------

AppPaths.EnsureDirectories();
var config = AppConfig.Load();

// Resolve a LAN IPv4 for the cert SAN and the displayed access URL.
string hostname = Dns.GetHostName();
IPAddress lanIp = Dns.GetHostAddresses(hostname)
    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                         && !IPAddress.IsLoopback(a)) ?? IPAddress.Loopback;

var cert = Certificates.LoadOrCreate($"{hostname}.local", lanIp);
Certificates.EnsureTrusted(cert);
var sessions = new SessionStore(TimeSpan.FromMinutes(config.SessionTimeoutMinutes));
var throttle = new LoginThrottle();
var registry = new StreamSessionRegistry();
var capturer = new GdiScreenCapturer(); // used only by the /api/frame debug endpoint

const int MaxSessions = 4; // cap concurrent viewers (simple DoS guard)
const string CookieName = "rd_session";

var builder = WebApplication.CreateBuilder(args);
// Quiet console, but keep Warning+ so TLS/Kestrel/handshake errors are visible.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 1L << 30; // 1 GB, for file uploads
    // Bind to the configured address. After setup this is pinned to the LAN IP.
    // Avoid binding a public/routable interface.
    IPAddress bindAddress = IPAddress.TryParse(config.BindAddress, out var parsed) ? parsed : IPAddress.Any;
    kestrel.Listen(bindAddress, config.Port, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1; // WebSockets are reliable over HTTP/1.1
        listenOptions.UseHttps(cert);
    });
});

var app = builder.Build();

string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
bool IsAuthed(HttpContext ctx) => sessions.Validate(ctx.Request.Cookies[CookieName]);

// ---- First-run setup: create the admin password locally ----
app.MapGet("/setup", async ctx =>
{
    if (config.IsConfigured) { ctx.Response.Redirect("/login.html"); return; }
    await ctx.Response.SendFileAsync(Path.Combine(app.Environment.ContentRootPath, "web", "setup.html"));
});

app.MapPost("/api/setup", async ctx =>
{
    if (config.IsConfigured) { ctx.Response.StatusCode = 409; return; }

    var body = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(ctx.Request.Body);
    string password = body?.GetValueOrDefault("password") ?? "";
    if (password.Length < 12)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("Password must be at least 12 characters.");
        return;
    }

    config.PasswordHash = PasswordHasher.Hash(password);
    config.BindAddress = lanIp.ToString(); // pin to LAN after setup
    config.Save();
    AuditLogger.Log("SETUP_COMPLETE", ClientIp(ctx));
    ctx.Response.StatusCode = 200;
});

// ---- Login ----
app.MapPost("/api/login", async ctx =>
{
    string ip = ClientIp(ctx);

    var (blocked, retryAfter) = throttle.CheckLockout(ip);
    if (blocked)
    {
        ctx.Response.StatusCode = 429;
        ctx.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        AuditLogger.Log("LOGIN_BLOCKED", ip);
        return;
    }

    var body = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(ctx.Request.Body);
    string password = body?.GetValueOrDefault("password") ?? "";

    if (config.PasswordHash is null || !PasswordHasher.Verify(password, config.PasswordHash))
    {
        throttle.RecordFailure(ip);
        AuditLogger.Log("LOGIN_FAIL", ip);
        ctx.Response.StatusCode = 401;
        return;
    }

    throttle.RecordSuccess(ip);
    string token = sessions.Create(ip);
    ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/"
    });
    AuditLogger.Log("LOGIN_OK", ip);
    ctx.Response.StatusCode = 200;
});

app.MapPost("/api/logout", ctx =>
{
    string? token = ctx.Request.Cookies[CookieName];
    sessions.Revoke(token);
    ctx.Response.Cookies.Delete(CookieName);
    AuditLogger.Log("LOGOUT", ClientIp(ctx));
    return Task.CompletedTask;
});

// Lightweight auth probe so the viewer can detect a stale/expired session on load
// and redirect to login instead of showing a dead screen.
app.MapGet("/api/session", ctx =>
{
    ctx.Response.StatusCode = IsAuthed(ctx) ? 200 : 401;
    return Task.CompletedTask;
});

// Receive a file from a client into the inbox folder. action: store | open.
app.MapPost("/api/upload", async ctx =>
{
    if (!IsAuthed(ctx)) { ctx.Response.StatusCode = 403; return; }
    string action = ctx.Request.Query["action"].ToString();
    string name = ctx.Request.Query["name"].ToString();

    string saved;
    try { saved = await Inbox.SaveAsync(ctx.Request.Body, name); }
    catch (Exception ex) { AuditLogger.Log("UPLOAD_ERROR", ClientIp(ctx), ex.Message); ctx.Response.StatusCode = 500; return; }

    try { if (action == "open") Inbox.OpenFolder(); }
    catch (Exception ex) { AuditLogger.Log("UPLOAD_ACTION_ERROR", ClientIp(ctx), ex.Message); }

    AuditLogger.Log("UPLOAD", ClientIp(ctx), $"{Path.GetFileName(saved)} action={action}");
    await ctx.Response.WriteAsJsonAsync(new { ok = true, savedAs = Path.GetFileName(saved) });
});

// ---- Debug single-frame endpoint (the viewer uses the WS stream, not this) ----
app.MapGet("/api/frame", ctx =>
{
    if (!IsAuthed(ctx) || !config.RemoteAccessEnabled) { ctx.Response.StatusCode = 403; return Task.CompletedTask; }
    var frame = capturer.CaptureJpeg(0, 70);
    ctx.Response.ContentType = "image/jpeg";
    return ctx.Response.Body.WriteAsync(frame.JpegBytes).AsTask();
});

// ---- WebSocket stream ----
app.MapGet("/ws", async ctx =>
{
    if (!IsAuthed(ctx) || !config.RemoteAccessEnabled) { ctx.Response.StatusCode = 403; return; }
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
    if (registry.Count >= MaxSessions) { ctx.Response.StatusCode = 503; return; }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var session = new StreamSession(ws, config, ClientIp(ctx), ctx.RequestAborted);
    registry.Add(session);
    try { await session.RunAsync(); }
    finally { registry.Remove(session.Id); session.Dispose(); }
});

// ---- Host-side visibility + kick (authed). Tray app will call the registry directly. ----
app.MapGet("/api/clients", ctx =>
{
    if (!IsAuthed(ctx)) { ctx.Response.StatusCode = 403; return Task.CompletedTask; }
    return ctx.Response.WriteAsJsonAsync(registry.Snapshot());
});

app.MapPost("/api/clients/{id}/disconnect", (HttpContext ctx, string id) =>
{
    if (!IsAuthed(ctx)) { ctx.Response.StatusCode = 403; return Task.CompletedTask; }
    bool ok = registry.Disconnect(id);
    AuditLogger.Log("CLIENT_KICKED", ClientIp(ctx), $"id={id} ok={ok}");
    ctx.Response.StatusCode = ok ? 200 : 404;
    return Task.CompletedTask;
});

// ---- WebSockets + static frontend (served from web/, not wwwroot) ----
app.UseWebSockets();

// First-run redirect: force setup until a password exists. MUST run before the
// static middleware, otherwise a direct GET /login.html would be served instead
// of redirecting to setup.
app.Use(async (ctx, next) =>
{
    if (!config.IsConfigured
        && !ctx.Request.Path.StartsWithSegments("/setup")
        && !ctx.Request.Path.StartsWithSegments("/api/setup"))
    {
        ctx.Response.Redirect("/setup");
        return;
    }
    await next();
});

// Gate the viewer page behind a valid session. Without this, a stale cookie (e.g.
// after a server restart wipes in-memory sessions) would load a dead viewer whose
// stream WebSocket is silently refused. Now it redirects to login instead.
app.Use(async (ctx, next) =>
{
    if ((ctx.Request.Path == "/index.html" || ctx.Request.Path == "/")
        && config.IsConfigured && !IsAuthed(ctx))
    {
        ctx.Response.Redirect("/login.html");
        return;
    }
    await next();
});

var webRoot = Path.Combine(app.Environment.ContentRootPath, "web");
var fileProvider = new PhysicalFileProvider(webRoot);

// "/" serves the login page (the viewer at /index.html requires a session anyway).
var defaultFiles = new DefaultFilesOptions { FileProvider = fileProvider };
defaultFiles.DefaultFileNames.Clear();
defaultFiles.DefaultFileNames.Add("login.html");
app.UseDefaultFiles(defaultFiles);

app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });

Console.WriteLine("============================================================");
Console.WriteLine(" Remote Desktop LAN");
Console.WriteLine($" Access URL : https://{lanIp}:{config.Port}");
Console.WriteLine($" Hostname   : https://{hostname}.local:{config.Port}");
Console.WriteLine($" Configured : {config.IsConfigured}");
Console.WriteLine(" LAN-ONLY. Do not expose this to the public internet.");
Console.WriteLine("============================================================");

app.Run();
