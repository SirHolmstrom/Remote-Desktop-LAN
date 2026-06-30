namespace Core.Capture;

public sealed record MonitorInfo(
    int Index,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary);

public sealed record CapturedFrame(
    byte[] JpegBytes,
    int Width,
    int Height);

/// <summary>A changed rectangular region of the screen, JPEG-encoded.</summary>
public sealed record Tile(
    int X,
    int Y,
    int W,
    int H,
    byte[] Jpeg);

/// <summary>The set of tiles that changed since the last capture, plus frame size.</summary>
public sealed record DeltaFrame(
    int Width,
    int Height,
    IReadOnlyList<Tile> Tiles);

/// <summary>
/// Abstraction over screen capture so the MVP GDI implementation can be swapped
/// for a Windows.Graphics.Capture (WGC) implementation later without touching the
/// streaming, auth, or input code.
/// </summary>
public interface IScreenCapturer : IDisposable
{
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Capture + JPEG-encode a whole monitor. quality is 1..100.</summary>
    CapturedFrame CaptureJpeg(int monitorIndex, long quality);

    /// <summary>
    /// Capture and return only the tiles that changed since the last call (per the
    /// capturer's internal per-tile hashes). When keyframe is true, all tiles are
    /// returned (full repaint). This is the low-latency path used by streaming.
    /// </summary>
    DeltaFrame CaptureDelta(int monitorIndex, long quality, bool keyframe, int tileSize);
}
