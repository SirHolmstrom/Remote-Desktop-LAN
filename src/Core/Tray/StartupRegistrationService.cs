using Microsoft.Win32;

namespace Core.Tray;

public static class StartupRegistrationService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RemoteDesktopLAN";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey);

        if (enabled)
        {
            string packagedExecutable = Path.Combine(AppContext.BaseDirectory, "RemoteDesktopLAN.exe");
            string executable = File.Exists(packagedExecutable)
                ? packagedExecutable
                : Environment.ProcessPath
                    ?? throw new InvalidOperationException("The executable path is unavailable.");
            key.SetValue(ValueName, $"\"{executable}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
