using System.Net;
using System.Net.Sockets;
using System.Diagnostics.CodeAnalysis;

namespace Core.RemoteAccess;

internal static class RemoteUrlHelper
{
    public static bool TryCreate(IPAddress? address, int port, out string url, out string reason)
    {
        url = "";
        reason = "";

        if (address is null)
        {
            reason = "The router did not report an external IP address, and the public-address fallback was unavailable.";
            return false;
        }

        if (!IsPublic(address))
        {
            reason = $"The router reported {address}, which is not a public internet address. " +
                "This often means the connection is behind CGNAT or another upstream router.";
            return false;
        }

        if (port is < 1 or > 65535)
        {
            reason = "The configured external port is invalid.";
            return false;
        }

        url = new UriBuilder(Uri.UriSchemeHttps, address.ToString(), port)
            .Uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    public static bool IsValid([NotNullWhen(true)] string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(uri.Host)
        && uri.Port is >= 1 and <= 65535;

    private static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None))
            return false;

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] != 0
                && bytes[0] != 10
                && bytes[0] != 127
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && bytes[0] < 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            bool uniqueLocal = (bytes[0] & 0xFE) == 0xFC;
            return !address.IsIPv6LinkLocal
                && !address.IsIPv6SiteLocal
                && !address.IsIPv6Multicast
                && !uniqueLocal;
        }

        return false;
    }
}
