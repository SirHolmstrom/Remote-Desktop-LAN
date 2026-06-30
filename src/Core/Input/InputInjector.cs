using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Core.Input;

/// <summary>
/// Win32 SendInput-based input injection. Mouse coordinates are mapped over the
/// entire virtual desktop so multi-monitor works. NOTE: a user-session process
/// cannot inject into the UAC secure desktop or the lock screen — that's an OS
/// security boundary we intentionally do not try to defeat.
/// </summary>
[SupportedOSPlatform("windows")]
public static class InputInjector
{
    /// <summary>Move the cursor to an absolute virtual-desktop pixel coordinate.</summary>
    public static void MoveMouseAbsolute(int x, int y)
    {
        int virtualLeft = GetSystemMetrics(SM_XVIRT), virtualTop = GetSystemMetrics(SM_YVIRT);
        int virtualWidth = GetSystemMetrics(SM_CXVIRT), virtualHeight = GetSystemMetrics(SM_CYVIRT);
        int normalizedX = (int)((x - virtualLeft) * 65535.0 / Math.Max(1, virtualWidth - 1));
        int normalizedY = (int)((y - virtualTop) * 65535.0 / Math.Max(1, virtualHeight - 1));
        SendMouse(normalizedX, normalizedY, 0, MOVE | ABSOLUTE | VIRTUALDESK);
    }

    public static void MouseButton(string button, bool down)
    {
        uint flag = button switch
        {
            "left" => down ? LDOWN : LUP,
            "right" => down ? RDOWN : RUP,
            "middle" => down ? MDOWN : MUP,
            _ => 0
        };
        if (flag != 0) SendMouse(0, 0, 0, flag);
    }

    public static void Scroll(int delta) => SendMouse(0, 0, (uint)(delta * 120), WHEEL);

    public static void Key(ushort virtualKey, bool down)
    {
        uint flags = down ? 0u : KEYUP;
        if (IsExtendedKey(virtualKey)) flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT { type = INPUT_KEYBOARD };
        input.U.ki = new KEYBDINPUT
        {
            wVk = virtualKey,
            wScan = (ushort)MapVirtualKey(virtualKey, MAPVK_VK_TO_VSC),
            dwFlags = flags
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Press a combo (e.g. Ctrl+Shift+Esc): modifiers down, key down+up, modifiers up
    /// in reverse order.
    /// </summary>
    public static void KeyCombo(ushort[] modifiers, ushort key)
    {
        foreach (var modifier in modifiers) Key(modifier, true);
        Key(key, true);
        Key(key, false);
        for (int i = modifiers.Length - 1; i >= 0; i--) Key(modifiers[i], false);
    }

    /// <summary>
    /// Types arbitrary text by injecting Unicode code units directly (no VK / layout
    /// mapping). This is what the on-screen keyboard uses, so any character works.
    /// </summary>
    public static void TypeUnicode(string text)
    {
        foreach (char c in text)
        {
            SendUnicode(c, down: true);
            SendUnicode(c, down: false);
        }
    }

    private static void SendUnicode(char c, bool down)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.U.ki = new KEYBDINPUT
        {
            wVk = 0,
            wScan = c,
            dwFlags = KEYEVENTF_UNICODE | (down ? 0 : KEYUP)
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendMouse(int dx, int dy, uint data, uint flags)
    {
        var input = new INPUT { type = INPUT_MOUSE };
        input.U.mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    // Keys that live in the keyboard's "extended" block (Win keys, the nav cluster,
    // arrows, right-hand modifiers). Injected without the extended flag, Windows
    // misreads them — which is why the Win key in particular did nothing.
    private static bool IsExtendedKey(ushort vk) => vk is
        91 or 92 or 93           // LWin, RWin, Apps
        or 33 or 34 or 35 or 36  // PageUp, PageDown, End, Home
        or 37 or 38 or 39 or 40  // Left, Up, Right, Down
        or 45 or 46              // Insert, Delete
        or 144 or 163 or 165;    // NumLock, RControl, RMenu (AltGr)

    // ---------- Win32 interop ----------
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int i);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint MOVE = 0x0001, ABSOLUTE = 0x8000, VIRTUALDESK = 0x4000;
    private const uint LDOWN = 0x0002, LUP = 0x0004, RDOWN = 0x0008, RUP = 0x0010;
    private const uint MDOWN = 0x0020, MUP = 0x0040, WHEEL = 0x0800;
    private const uint KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint MAPVK_VK_TO_VSC = 0;
    private const int SM_XVIRT = 76, SM_YVIRT = 77, SM_CXVIRT = 78, SM_CYVIRT = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }
}
