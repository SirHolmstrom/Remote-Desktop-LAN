using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Core.Security;

public sealed record GuestInvite(
    Guid Id,
    string Code,
    GuestAccessLevel AccessLevel,
    DateTime CreatedUtc,
    DateTime ExpiresUtc,
    bool IsRevoked)
{
    public bool IsExpired => DateTime.UtcNow >= ExpiresUtc;
    public bool IsActive => !IsRevoked && !IsExpired;
}

/// <summary>
/// Process-local guest invitations. Codes intentionally disappear on restart: an
/// invitation is temporary access, unlike the permanent owner password.
/// </summary>
public sealed class GuestInviteStore
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly ConcurrentDictionary<Guid, GuestInvite> m_ById = new();
    private readonly ConcurrentDictionary<string, Guid> m_IdByCodeHash = new(StringComparer.Ordinal);

    public GuestInvite Create(GuestAccessLevel accessLevel, TimeSpan lifetime)
    {
        if (!Enum.IsDefined(accessLevel)) throw new ArgumentOutOfRangeException(nameof(accessLevel));
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));

        while (true)
        {
            string code = GenerateCode();
            string codeHash = HashCode(NormalizeCode(code));
            var invite = new GuestInvite(
                Guid.NewGuid(), code, accessLevel, DateTime.UtcNow, DateTime.UtcNow.Add(lifetime), false);

            if (!m_IdByCodeHash.TryAdd(codeHash, invite.Id)) continue;
            m_ById[invite.Id] = invite;
            return invite;
        }
    }

    public bool TryRedeem(string? code, out GuestInvite invite)
    {
        invite = default!;
        string normalized = NormalizeCode(code);
        if (normalized.Length != 8) return false;
        if (!m_IdByCodeHash.TryGetValue(HashCode(normalized), out Guid id)) return false;
        if (!m_ById.TryGetValue(id, out var found) || !found.IsActive) return false;
        invite = found;
        return true;
    }

    public bool IsActive(Guid id) => m_ById.TryGetValue(id, out var invite) && invite.IsActive;

    public IReadOnlyList<GuestInvite> Snapshot() => m_ById.Values
        .OrderByDescending(invite => invite.CreatedUtc)
        .ToList();

    public bool Revoke(Guid id)
    {
        while (m_ById.TryGetValue(id, out var current))
        {
            if (current.IsRevoked) return false;
            if (m_ById.TryUpdate(id, current with { IsRevoked = true }, current)) return true;
        }

        return false;
    }

    public void RevokeAll()
    {
        foreach (Guid id in m_ById.Keys) Revoke(id);
    }

    public static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        return new string(code
            .Where(character => character != '-' && !char.IsWhiteSpace(character))
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string GenerateCode()
    {
        Span<char> characters = stackalloc char[9];
        for (int index = 0; index < 8; index++)
        {
            int outputIndex = index < 4 ? index : index + 1;
            characters[outputIndex] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        characters[4] = '-';
        return new string(characters);
    }

    private static string HashCode(string normalizedCode) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode)));
}
