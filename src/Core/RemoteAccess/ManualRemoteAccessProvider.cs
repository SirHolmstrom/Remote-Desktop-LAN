using Core.Config;

namespace Core.RemoteAccess;

public sealed class ManualRemoteAccessProvider : IRemoteAccessProvider
{
    public Task<RemoteAccessResult> OpenAsync(AppConfig config, CancellationToken cancellationToken = default) =>
        Task.FromResult(RemoteAccessResult.Manual(
            "Automatic router setup is not being used. Forward the shown TCP port in your router, then check remote access."));

    public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
