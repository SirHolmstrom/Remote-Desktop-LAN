using Core.Capture;
using Core.RemoteAccess;

var network = new NetworkInfoService();
var lanAddress = network.GetLanIp();
if (!network.IsLanAddress(lanAddress) || !network.IsLanAddress(System.Net.IPAddress.Loopback))
{
    Console.Error.WriteLine($"Network policy probe failed: {lanAddress} was not recognized as LAN.");
    return 3;
}

Console.WriteLine($"LAN policy OK: {lanAddress} is recognized as local.");

using var capturer = new GdiScreenCapturer();
var monitors = capturer.GetMonitors();

if (monitors.Count == 0)
{
    Console.Error.WriteLine("Capture probe failed: no monitors were discovered.");
    return 1;
}

foreach (var monitor in monitors)
{
    Console.WriteLine(
        $"Monitor {monitor.Index}: {monitor.Width}x{monitor.Height} at {monitor.X},{monitor.Y} " +
        $"primary={monitor.IsPrimary}");
}

var first = monitors[0];
var frame = capturer.CaptureJpeg(first.Index, 60);
bool isJpeg = frame.JpegBytes.Length >= 4
    && frame.JpegBytes[0] == 0xFF
    && frame.JpegBytes[1] == 0xD8
    && frame.JpegBytes[^2] == 0xFF
    && frame.JpegBytes[^1] == 0xD9;

if (!isJpeg || frame.Width != first.Width || frame.Height != first.Height)
{
    Console.Error.WriteLine("Capture probe failed: the captured frame is invalid.");
    return 2;
}

Console.WriteLine($"Capture OK: JPEG {frame.JpegBytes.Length:N0} bytes.");
return 0;
