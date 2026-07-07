using Core.Config;
using Core.Logging;

namespace Core.RemoteAccess;

public sealed class RemoteAccessController
{
    private readonly AppConfig m_Config;
    private readonly OpenNatRemoteAccessProvider m_OpenNat;
    private readonly ManualRemoteAccessProvider m_Manual;
    private readonly NetworkInfoService m_Network;
    private readonly FirewallRuleService m_Firewall;
    private readonly SemaphoreSlim m_Gate = new(1, 1);

    public RemoteAccessState State { get; private set; } = RemoteAccessState.Closed;
    public string StatusMessage { get; private set; } = "Remote access is closed.";
    public string? RemoteUrl { get; private set; }

    public event EventHandler? StatusChanged;

    public RemoteAccessController(
        AppConfig config,
        OpenNatRemoteAccessProvider openNat,
        ManualRemoteAccessProvider manual,
        NetworkInfoService network,
        FirewallRuleService firewall)
    {
        m_Config = config;
        m_OpenNat = openNat;
        m_Manual = manual;
        m_Network = network;
        m_Firewall = firewall;

        if (!RemoteUrlHelper.IsValid(m_Config.LastRemoteUrl))
        {
            m_Config.LastRemoteUrl = null;
            m_Config.Save();
        }
    }

    public async Task<RemoteAccessResult> OpenAsync(CancellationToken cancellationToken = default)
    {
        await m_Gate.WaitAsync(cancellationToken);
        try
        {
            if (m_Config.AccessMode is RemoteAccessMode.LanOnly or RemoteAccessMode.Disabled)
                return Set(RemoteAccessResult.Failure("Choose Automatic or Manual Remote Access first."));

            Set(new(false, RemoteAccessState.Opening, "Opening remote access..."));

            m_Config.LastRouterUrl = m_Network.GetRouterUrl();
            m_Config.RemoteAccessEnabled = true;
            m_Config.Save();

            var firewall = await m_Firewall.EnsurePublicRuleAsync(m_Config.Port);
            if (!firewall.Success)
            {
                m_Config.RemoteAccessEnabled = false;
                m_Config.Save();
                return Set(RemoteAccessResult.Failure(firewall.Message));
            }

            IRemoteAccessProvider provider = m_Config.AccessMode == RemoteAccessMode.Automatic
                && m_Config.TryAutomaticPortForwarding
                    ? m_OpenNat
                    : m_Manual;

            RemoteAccessResult result = await provider.OpenAsync(m_Config, cancellationToken);
            if (result.Success && RemoteUrlHelper.IsValid(result.RemoteUrl))
            {
                RemoteUrl = result.RemoteUrl;
                m_Config.LastRemoteUrl = result.RemoteUrl;
            }
            else
            {
                RemoteUrl = null;
                if (result.Success)
                {
                    result = RemoteAccessResult.Manual(
                        "The router mapping was created, but no valid external URL was returned. Manual setup is required.");
                }
            }

            m_Config.Save();
            AuditLogger.Log(
                "REMOTE_OPEN",
                "local",
                $"mode={m_Config.AccessMode} state={result.State} message={result.Message}");
            return Set(result);
        }
        finally
        {
            m_Gate.Release();
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await m_Gate.WaitAsync(cancellationToken);
        try
        {
            if (State == RemoteAccessState.Closed && !m_Config.RemoteAccessEnabled)
                return;

            await m_OpenNat.CloseAsync(cancellationToken);
            await m_Manual.CloseAsync(cancellationToken);
            await m_Firewall.RemovePublicRuleAsync();

            RemoteUrl = null;
            m_Config.RemoteAccessEnabled = false;
            m_Config.Save();
            AuditLogger.Log("REMOTE_CLOSE", "local");
            Set(new(true, RemoteAccessState.Closed, "Remote access is closed."));
        }
        finally
        {
            m_Gate.Release();
        }
    }

    public RemoteAccessResult Check(bool hostIsRunning)
    {
        // TODO: add an opt-in probe from outside the LAN. This deliberately only
        // validates local state so a third-party service never learns the URL.
        if (!hostIsRunning)
            return Set(RemoteAccessResult.Failure("The local server is not running."));
        if (!m_Config.RemoteAccessEnabled)
            return Set(new(false, RemoteAccessState.Closed, "Remote access is closed."));
        if (m_Network.GetLanIp().Equals(System.Net.IPAddress.Loopback))
            return Set(RemoteAccessResult.Failure("No LAN IPv4 address was detected."));
        if (m_Config.AccessMode == RemoteAccessMode.ManualOnly
            || State == RemoteAccessState.ManualSetupRequired)
            return Set(RemoteAccessResult.Manual(
                "The server and public firewall rule are enabled. External reachability still needs to be verified from another network."));
        if (State == RemoteAccessState.Active && RemoteUrl is not null)
            return Set(RemoteAccessResult.Active(RemoteUrl));

        return Set(RemoteAccessResult.Failure("Remote access is enabled, but no active router mapping is known."));
    }

    private RemoteAccessResult Set(RemoteAccessResult result)
    {
        State = result.State;
        StatusMessage = result.Message;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }
}
