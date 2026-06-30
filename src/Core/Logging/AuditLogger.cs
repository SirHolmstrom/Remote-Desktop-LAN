using Core.Config;

namespace Core.Logging;

/// <summary>
/// Append-only, tab-separated audit log. METADATA ONLY — this must never receive
/// passwords, session tokens, keystrokes, or clipboard contents. Lines are:
/// &lt;utc-iso8601&gt; \t &lt;eventType&gt; \t &lt;clientIp&gt; \t &lt;detail&gt;
/// </summary>
public static class AuditLogger
{
    private static readonly object Gate = new();

    public static void Log(string eventType, string clientIp, string detail = "")
    {
        string line = $"{DateTime.UtcNow:O}\t{eventType}\t{clientIp}\t{detail}";
        try
        {
            lock (Gate) File.AppendAllText(AppPaths.AuditLog, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never throw into the request path.
        }
    }

    /// <summary>Returns the last N lines of the audit log (for the tray "open logs" view).</summary>
    public static IReadOnlyList<string> Tail(int lines = 200)
    {
        try
        {
            if (!File.Exists(AppPaths.AuditLog)) return Array.Empty<string>();
            var all = File.ReadAllLines(AppPaths.AuditLog);
            return all.Length <= lines ? all : all[^lines..];
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
