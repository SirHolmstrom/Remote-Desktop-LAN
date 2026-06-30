using System.Collections.Concurrent;

namespace Core.Security;

/// <summary>
/// Per-key (typically client IP) failed-login tracking with exponential backoff
/// and temporary lockout. Brute-force defense to pair with the slow Argon2id hash.
/// </summary>
public sealed class LoginThrottle
{
    private sealed class LockoutState
    {
        public int FailureCount;
        public DateTime LockedUntilUtc;
    }

    private readonly ConcurrentDictionary<string, LockoutState> m_StateByKey = new();

    private const int LockThreshold = 5;
    private static readonly TimeSpan BaseLock = TimeSpan.FromSeconds(30);

    /// <summary>Returns whether the key is currently locked out, and for how long.</summary>
    public (bool blocked, TimeSpan retryAfter) CheckLockout(string key)
    {
        if (m_StateByKey.TryGetValue(key, out var state) && DateTime.UtcNow < state.LockedUntilUtc)
            return (true, state.LockedUntilUtc - DateTime.UtcNow);

        return (false, TimeSpan.Zero);
    }

    public void RecordFailure(string key)
    {
        var state = m_StateByKey.GetOrAdd(key, _ => new LockoutState());

        lock (state)
        {
            state.FailureCount++;
            if (state.FailureCount < LockThreshold)
                return;

            // Each failure past the threshold doubles the wait: 30s, 60s, 120s, 240s,
            // ... capped at 15 minutes so a typo storm can still recover in a session.
            int stepsOverThreshold = state.FailureCount - LockThreshold;
            double waitSeconds = Math.Min(BaseLock.TotalSeconds * Math.Pow(2, stepsOverThreshold), 900);
            state.LockedUntilUtc = DateTime.UtcNow + TimeSpan.FromSeconds(waitSeconds);
        }
    }

    public void RecordSuccess(string key) => m_StateByKey.TryRemove(key, out _);
}
