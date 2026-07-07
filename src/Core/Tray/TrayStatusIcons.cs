using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Core.Tray;

/// <summary>Menu status dots plus DPI/theme-aware tray artwork.</summary>
internal sealed class TrayStatusIcons : IDisposable
{
    private static readonly Color Green = Color.FromArgb(34, 197, 94);
    private static readonly Color Yellow = Color.FromArgb(234, 179, 8);
    private static readonly Color Red = Color.FromArgb(239, 68, 68);
    private static readonly Color Gray = Color.FromArgb(148, 163, 184);

    public Image GreenDot { get; } = CreateDot(Green);
    public Image YellowDot { get; } = CreateDot(Yellow);
    public Image RedDot { get; } = CreateDot(Red);
    public Image GrayDot { get; } = CreateDot(Gray);

    private readonly Icon m_IdleWhite = LoadIcon("tray-idle-white.ico");
    private readonly Icon m_IdleBlack = LoadIcon("tray-idle-black.ico");
    private readonly Icon m_AccessOnWhite = LoadIcon("tray-on-white.ico");
    private readonly Icon m_AccessOnBlack = LoadIcon("tray-on-black.ico");

    public Icon AppIcon { get; } = LoadIcon("app.ico", 32);
    public Icon IdleTrayIcon => UsesLightTaskbar() ? m_IdleBlack : m_IdleWhite;
    public Icon AccessOnTrayIcon => UsesLightTaskbar() ? m_AccessOnBlack : m_AccessOnWhite;

    private static Bitmap CreateDot(Color color)
    {
        var bitmap = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shadow = new SolidBrush(Color.FromArgb(55, Color.Black));
        using var fill = new SolidBrush(color);
        using var outline = new Pen(Color.FromArgb(120, Color.Black), 1f);
        graphics.FillEllipse(shadow, 3, 4, 11, 11);
        graphics.FillEllipse(fill, 2, 2, 11, 11);
        graphics.DrawEllipse(outline, 2, 2, 11, 11);
        return bitmap;
    }

    private static Icon LoadIcon(string fileName, int? requestedSize = null)
    {
        string resourceName = $"Core.Assets.{fileName}";
        using Stream stream = typeof(TrayStatusIcons).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded icon resource: {resourceName}");
        int size = requestedSize ?? GetTrayPixelSize();
        using var borrowed = new Icon(stream, new Size(size, size));
        return (Icon)borrowed.Clone();
    }

    private static int GetTrayPixelSize()
    {
        int dpi;
        try { dpi = GetDpiForSystem(); }
        catch { dpi = 96; }
        int desired = (int)Math.Round(16d * dpi / 96d);
        return new[] { 16, 20, 24, 32 }.MinBy(size => Math.Abs(size - desired));
    }

    private static bool UsesLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        GreenDot.Dispose();
        YellowDot.Dispose();
        RedDot.Dispose();
        GrayDot.Dispose();
        AppIcon.Dispose();
        m_IdleWhite.Dispose();
        m_IdleBlack.Dispose();
        m_AccessOnWhite.Dispose();
        m_AccessOnBlack.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern int GetDpiForSystem();
}
