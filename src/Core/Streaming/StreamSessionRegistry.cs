using System.Collections.Concurrent;
using System.Runtime.Versioning;

namespace Core.Streaming;

public sealed record ClientView(
    string Id,
    string ClientIp,
    DateTime ConnectedUtc,
    int Fps,
    int Quality,
    int Monitor);

/// <summary>
/// Tracks active stream sessions so the host (tray app) and the /api/clients
/// endpoints can list connected clients and forcibly disconnect them.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StreamSessionRegistry
{
    private readonly ConcurrentDictionary<string, StreamSession> m_Sessions = new();

    public int Count => m_Sessions.Count;

    public void Add(StreamSession session) => m_Sessions[session.Id] = session;
    public void Remove(string id) => m_Sessions.TryRemove(id, out _);

    public IReadOnlyList<ClientView> Snapshot() => m_Sessions.Values
        .Select(session => new ClientView(
            session.Id, session.ClientIp, session.ConnectedUtc, session.Fps, session.Quality, session.Monitor))
        .ToList();

    public bool Disconnect(string id)
    {
        if (m_Sessions.TryGetValue(id, out var session))
        {
            session.Cancel();
            return true;
        }
        return false;
    }

    public void DisconnectAll()
    {
        foreach (var session in m_Sessions.Values)
            session.Cancel();
    }
}
