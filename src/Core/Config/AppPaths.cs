namespace Core.Config;

/// <summary>
/// Central place for on-disk locations, all under the current user's profile
/// (%LOCALAPPDATA%\RemoteDesktopLAN). Keeping everything user-scoped means no
/// admin rights are needed and secrets stay tied to this Windows account.
/// </summary>
public static class AppPaths
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RemoteDesktopLAN");

    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string CertPfx => Path.Combine(Root, "certs", "server.pfx");
    public static string CertPwd => Path.Combine(Root, "certs", "server.pwd"); // DPAPI-protected
    public static string AuditLog => Path.Combine(Root, "logs", "audit.log");
    public static string ConfigFolder => Root;

    /// <summary>Where files uploaded from clients are saved.</summary>
    public static string Inbox => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "RemoteDesktopLAN");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "certs"));
        Directory.CreateDirectory(Path.Combine(Root, "logs"));
        Directory.CreateDirectory(Inbox);
        // TODO (hardening): tighten ACLs on Root to the current user only via
        // DirectoryInfo + DirectorySecurity so other accounts can't read the cert/config.
    }
}
