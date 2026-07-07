using System.Net;
using System.Net.Http;

namespace Core.RemoteAccess;

internal static class PublicIpAddressService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    public static async Task<IPAddress?> GetAsync(CancellationToken cancellationToken)
    {
        // This fallback runs only after the user explicitly opens remote access and
        // the router omits NewExternalIPAddress. No URL, port, or app data is sent.
        foreach (string endpoint in new[]
                 {
                     "https://api.ipify.org",
                     "https://checkip.amazonaws.com"
                 })
        {
            try
            {
                string text = (await Http.GetStringAsync(endpoint, cancellationToken)).Trim();
                if (IPAddress.TryParse(text, out var address)) return address;
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Try the next independent address service.
            }
        }

        return null;
    }
}
