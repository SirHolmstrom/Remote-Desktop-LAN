using Core.Config;
using Core.Hosting;
using Core.RemoteAccess;
using Core.Tray;
using WinForms = System.Windows.Forms;

namespace Core;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        WinForms.Application.EnableVisualStyles();
        WinForms.Application.SetCompatibleTextRenderingDefault(false);
        AppPaths.EnsureDirectories();

        var config = AppConfig.Load();
        // A router mapping belongs to a single process lifetime. After a restart,
        // require an explicit tray action before accepting streaming requests.
        config.RemoteAccessEnabled = false;
        config.Save();

        var network = new NetworkInfoService();
        var host = new RemoteDesktopHost(config, network);
        var firewall = new FirewallRuleService();
        var remoteAccess = new RemoteAccessController(
            config,
            new OpenNatRemoteAccessProvider(),
            new ManualRemoteAccessProvider(),
            network,
            firewall);

        try
        {
            host.StartAsync().GetAwaiter().GetResult();
            using var tray = new TrayAppContext(config, host, remoteAccess, network, firewall);
            WinForms.Application.Run(tray);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                $"RemoteDesktopLAN could not start.\n\n{ex.Message}",
                "RemoteDesktopLAN",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
        finally
        {
            try { remoteAccess.CloseAsync().GetAwaiter().GetResult(); } catch { }
            try { host.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        }
    }
}
