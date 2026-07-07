using Core.Config;

namespace Core.RemoteAccess;

public interface IRemoteAccessProvider
{
    Task<RemoteAccessResult> OpenAsync(AppConfig config, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
