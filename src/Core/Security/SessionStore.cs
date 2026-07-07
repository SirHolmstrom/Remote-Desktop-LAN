using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Core.Security;

public sealed record SessionInfo(
    string ClientIp,
    DateTime CreatedUtc,
    SessionRole Role,
    GuestAccessLevel? GuestAccessLevel,
    Guid? GuestInviteId)
{
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public SessionPermissions Permissions => SessionPermissions.For(Role, GuestAccessLevel);
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
    private readonly Func<Guid, bool> m_IsGuestInviteActive;

    public SessionStore(TimeSpan idleTimeout, Func<Guid, bool>? isGuestInviteActive = null)
    {
        m_IdleTimeout = idleTimeout;
        m_IsGuestInviteActive = isGuestInviteActive ?? (_ => false);
    }

    public IReadOnlyDictionary<string, SessionInfo> Active => m_Sessions;

    public string CreateOwner(string clientIp) =>
        Create(clientIp, SessionRole.Owner, null, null);

    public string CreateGuest(string clientIp, Guid inviteId, GuestAccessLevel accessLevel) =>
        Create(clientIp, SessionRole.Guest, accessLevel, inviteId);

    private string Create(
        string clientIp,
        SessionRole role,
        GuestAccessLevel? guestAccessLevel,
        Guid? guestInviteId)
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        m_Sessions[token] = new SessionInfo(
            clientIp, DateTime.UtcNow, role, guestAccessLevel, guestInviteId);
        return token;
    }

    public bool TryGet(string? token, out SessionInfo session)
    {
        session = default!;
        if (string.IsNullOrEmpty(token) || !m_Sessions.TryGetValue(token, out var found))
            return false;

        if (DateTime.UtcNow - found.LastSeenUtc > m_IdleTimeout
            || (found.GuestInviteId is Guid inviteId && !m_IsGuestInviteActive(inviteId)))
        {
            m_Sessions.TryRemove(token, out _);
            return false;
        }

        found.LastSeenUtc = DateTime.UtcNow; // sliding renewal keeps active viewers logged in
        session = found;
        return true;
    }

    public bool Validate(string? token) => TryGet(token, out _);

    public void Revoke(string? token)
    {
        if (token != null) m_Sessions.TryRemove(token, out _);
    }

    public void RevokeAll() => m_Sessions.Clear();

    public void RevokeGuestInvite(Guid inviteId)
    {
        foreach (var entry in m_Sessions.Where(entry => entry.Value.GuestInviteId == inviteId))
            m_Sessions.TryRemove(entry.Key, out _);
    }
}
