using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.Versioning;
using System.Text.Json;
using Core.Capture;
using Core.Config;
using Core.Input;
using Core.Logging;

namespace Core.Streaming;

/// <summary>
/// One connected client. Runs a receive loop (input + control messages) concurrently
/// with a send loop (capture -> encode -> push) at a target FPS. Honors
/// RemoteAccessEnabled live and supports quality/FPS/monitor changes mid-session.
///
/// Concurrency note: a WebSocket permits only ONE outstanding send at a time, and we
/// send from both loops (frames vs pong/status). All sends are funneled through a
/// single SemaphoreSlim — without it you get intermittent InvalidOperationException
/// under load.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StreamSession : IDisposable
{
    // Control state: written by the receive loop, read by the send loop. volatile
    // int/bool reads & writes are atomic and visible across threads — enough here.
    private volatile int m_Quality = 70;    // 10..95
    private volatile int m_Fps = 20;        // 1..30 (GDI is CPU-bound at high res)
    private volatile int m_Monitor = 0;
    private volatile bool m_ForceKeyframe = true; // resend a full frame after a control change

    private readonly WebSocket m_Socket;
    private readonly AppConfig m_Config;
    private readonly IScreenCapturer m_Capturer;
    private readonly CancellationTokenSource m_Cancellation;
    private readonly SemaphoreSlim m_SendLock = new(1, 1); // serialize ALL sends

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public string ClientIp { get; }
    public DateTime ConnectedUtc { get; } = DateTime.UtcNow;

    public int Quality => m_Quality;
    public int Fps => m_Fps;
    public int Monitor => m_Monitor;

    public StreamSession(
        WebSocket socket,
        AppConfig config,
        string clientIp,
        CancellationToken outerToken)
    {
        m_Socket = socket;
        m_Config = config;
        ClientIp = clientIp;
        m_Capturer = new GdiScreenCapturer(); // per-session: avoids cross-thread bitmap races
        m_Cancellation = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
    }

    /// <summary>Drives the session until either loop ends, then tears down cleanly.</summary>
    public async Task RunAsync()
    {
        AuditLogger.Log("STREAM_CONNECT", ClientIp, $"id={Id}");
        await SendMonitorListAsync();
        await SendStatusAsync("connected");

        var receive = ReceiveLoopAsync(m_Cancellation.Token);
        var send = SendLoopAsync(m_Cancellation.Token);

        await Task.WhenAny(receive, send); // whichever ends first…
        m_Cancellation.Cancel();           // …tears down the other
        try { await Task.WhenAll(receive, send); } catch { /* expected on cancel */ }

        await CloseAsync();
        AuditLogger.Log("STREAM_DISCONNECT", ClientIp, $"id={Id}");
    }

    /// <summary>Used by the registry / tray "disconnect" action.</summary>
    public void Cancel() => m_Cancellation.Cancel();

    // ---------- send loop ----------
    private async Task SendLoopAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        long lastKeyframeMs = long.MinValue;
        const int TileSize = 128;
        const long KeyframeIntervalMs = 7000; // periodic full refresh (cheap insurance)

        while (!ct.IsCancellationRequested && m_Socket.State == WebSocketState.Open)
        {
            if (!m_Config.RemoteAccessEnabled) // host flipped the kill switch
            {
                await SendStatusAsync("disabled");
                break;
            }

            long frameStart = stopwatch.ElapsedMilliseconds;
            bool keyframe = m_ForceKeyframe || (frameStart - lastKeyframeMs) >= KeyframeIntervalMs;
            if (m_ForceKeyframe) m_ForceKeyframe = false;

            DeltaFrame frame;
            try
            {
                frame = m_Capturer.CaptureDelta(m_Monitor, m_Quality, keyframe, TileSize);
            }
            catch (Exception ex)
            {
                AuditLogger.Log("CAPTURE_ERROR", ClientIp, ex.Message);
                break;
            }

            if (keyframe) lastKeyframeMs = frameStart;

            // Empty tile list => nothing changed; skip the send (bandwidth saver).
            if (frame.Tiles.Count > 0)
            {
                try { await SendRawAsync(SerializeFrame(frame), WebSocketMessageType.Binary, ct); }
                catch { break; } // client gone
            }

            int interval = 1000 / Math.Clamp(m_Fps, 1, 30);
            int elapsed = (int)(stopwatch.ElapsedMilliseconds - frameStart);
            int delay = interval - elapsed;
            if (delay > 0)
            {
                try { await Task.Delay(delay, ct); }
                catch { break; }
            }
        }
    }

    /// <summary>
    /// Binary frame format (little-endian): byte type=1, u16 width, u16 height,
    /// u16 tileCount, then per tile: u16 x, u16 y, u16 w, u16 h, i32 jpegLen, jpeg bytes.
    /// </summary>
    private static byte[] SerializeFrame(DeltaFrame frame)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write((ushort)frame.Width);
        writer.Write((ushort)frame.Height);
        writer.Write((ushort)frame.Tiles.Count);
        foreach (var tile in frame.Tiles)
        {
            writer.Write((ushort)tile.X);
            writer.Write((ushort)tile.Y);
            writer.Write((ushort)tile.W);
            writer.Write((ushort)tile.H);
            writer.Write(tile.Jpeg.Length); // int32
            writer.Write(tile.Jpeg);
        }
        writer.Flush();
        return stream.ToArray();
    }

    // ---------- receive loop ----------
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];

        while (!ct.IsCancellationRequested && m_Socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try { result = await m_Socket.ReceiveAsync(buffer, ct); }
            catch { break; }

            if (result.MessageType == WebSocketMessageType.Close) break;
            if (result.MessageType != WebSocketMessageType.Text) continue;

            Dictionary<string, JsonElement>? message;
            try { message = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(buffer.AsSpan(0, result.Count)); }
            catch { continue; }
            if (message is null || !message.TryGetValue("t", out var typeElement)) continue;

            switch (typeElement.GetString())
            {
                // ----- input -----
                case "move":
                {
                    var monitor = m_Capturer.GetMonitors().FirstOrDefault(x => x.Index == m_Monitor)
                                  ?? m_Capturer.GetMonitors()[0];
                    int pixelX = monitor.X + (int)(message["x"].GetDouble() * monitor.Width);
                    int pixelY = monitor.Y + (int)(message["y"].GetDouble() * monitor.Height);
                    InputInjector.MoveMouseAbsolute(pixelX, pixelY);
                    break;
                }
                case "btn": InputInjector.MouseButton(message["b"].GetString()!, message["d"].GetBoolean()); break;
                case "scroll": InputInjector.Scroll(message["delta"].GetInt32()); break;
                case "key": InputInjector.Key((ushort)message["vk"].GetInt32(), message["d"].GetBoolean()); break;
                case "text": InputInjector.TypeUnicode(message["s"].GetString() ?? ""); break;
                case "combo":
                {
                    var modifiers = message["mods"].EnumerateArray().Select(e => (ushort)e.GetInt32()).ToArray();
                    InputInjector.KeyCombo(modifiers, (ushort)message["key"].GetInt32());
                    break;
                }

                // ----- live controls -----
                case "quality": m_Quality = Math.Clamp(message["v"].GetInt32(), 10, 95); m_ForceKeyframe = true; break;
                case "fps": m_Fps = Math.Clamp(message["v"].GetInt32(), 1, 30); break;
                case "monitor":
                {
                    int index = message["v"].GetInt32();
                    if (m_Capturer.GetMonitors().Any(x => x.Index == index))
                    {
                        m_Monitor = index;
                        m_ForceKeyframe = true;
                        await SendMonitorListAsync();
                    }
                    break;
                }

                // ----- latency -----
                case "ping": await SendTextAsync(new { t = "pong", ts = message["ts"].GetDouble() }, ct); break;

                // ----- read focused field text (UI Automation), for the keyboard echo -----
                case "getFocusText":
                {
                    var focusText = FocusedText.TryRead();
                    if (focusText != null) await SendTextAsync(new { t = "focusText", text = focusText }, ct);
                    break;
                }
            }
        }
    }

    // ---------- send helpers (all sends serialized through m_SendLock) ----------
    private async Task SendRawAsync(ReadOnlyMemory<byte> data, WebSocketMessageType type, CancellationToken ct)
    {
        await m_SendLock.WaitAsync(ct);
        try
        {
            if (m_Socket.State == WebSocketState.Open)
                await m_Socket.SendAsync(data, type, true, ct);
        }
        finally
        {
            m_SendLock.Release();
        }
    }

    private Task SendTextAsync(object payload, CancellationToken ct = default) =>
        SendRawAsync(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions), WebSocketMessageType.Text, ct);

    private Task SendStatusAsync(string state) => SendTextAsync(new { t = "status", state });

    private Task SendMonitorListAsync()
    {
        var monitors = m_Capturer.GetMonitors()
            .Select(x => new { index = x.Index, w = x.Width, h = x.Height, primary = x.IsPrimary })
            .ToArray();
        return SendTextAsync(new { t = "monitors", list = monitors, active = m_Monitor });
    }

    private async Task CloseAsync()
    {
        try
        {
            if (m_Socket.State == WebSocketState.Open)
                await m_Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch { }
    }

    public void Dispose()
    {
        m_Cancellation.Dispose();
        m_Capturer.Dispose();
        m_SendLock.Dispose();
    }
}
