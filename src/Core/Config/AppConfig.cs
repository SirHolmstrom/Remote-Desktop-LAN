using System.Text.Json;

namespace Core.Config;

/// <summary>
/// Persisted settings. The password field stores ONLY an Argon2id hash string —
/// never a plaintext or reversible password.
/// </summary>
public sealed class AppConfig
{
    public int Port { get; set; } = 8443;

    /// <summary>
    /// Interface to bind to. Defaults to loopback-safe "0.0.0.0" only until setup
    /// completes; setup pins this to the detected LAN IP. Avoid leaving it on a
    /// public/routable NIC.
    /// </summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Argon2id hash string, or null when first-run setup is required.</summary>
    public string? PasswordHash { get; set; }

    public int SessionTimeoutMinutes { get; set; } = 15;
    public bool RemoteAccessEnabled { get; set; } = true;
    public bool ClipboardSyncEnabled { get; set; } = false;

    public bool IsConfigured => !string.IsNullOrEmpty(PasswordHash);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        if (!File.Exists(AppPaths.ConfigFile)) return new AppConfig();
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigFile))
                   ?? new AppConfig();
        }
        catch
        {
            // Corrupt config shouldn't brick startup; fall back to defaults (forces re-setup).
            return new AppConfig();
        }
    }

    public void Save() =>
        File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(this, JsonOptions));
}
