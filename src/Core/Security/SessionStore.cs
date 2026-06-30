using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Core.Security;

public sealed record SessionInfo(
    string ClientIp,
    DateTime CreatedUtc)
{
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// In-memory session store with sliding-expiry timeout. Tokens are 256 bits of
/// CSPRNG output, URL-safe base64. Sessions live only for the process lifetime,
/// so a restart invalidates everyone (acceptable for a personal tool).
/// </summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, SessionInfo> m_Sessions = new();
    private readonly TimeSpan m_IdleTimeout;

    public SessionStore(TimeSpan idleTimeout)
    {
        m_IdleTimeout = idleTimeout;
    }

    public IReadOnlyDictionary<string, SessionInfo> Active => m_Sessions;

    public string Create(string clientIp)
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        m_Sessions[token] = new SessionInfo(clientIp, DateTime.UtcNow);
        return token;
    }

    public bool Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || !m_Sessions.TryGetValue(token, out var session))
            return false;

        if (DateTime.UtcNow - session.LastSeenUtc > m_IdleTimeout)
        {
            m_Sessions.TryRemove(token, out _);
            return false;
        }

        session.LastSeenUtc = DateTime.UtcNow; // sliding renewal keeps active viewers logged in
        return true;
    }

    public void Revoke(string? token)
    {
        if (token != null) m_Sessions.TryRemove(token, out _);
    }
}
