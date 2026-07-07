using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Core.RemoteAccess;

public sealed class NetworkInfoService
{
    private static IEnumerable<NetworkInterface> ActiveAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                && adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                && adapter.NetworkInterfaceType is not NetworkInterfaceType.Tunnel);

    public IPAddress GetLanIp()
    {
        foreach (var adapter in ActiveAdapters().OrderByDescending(HasIpv4Gateway))
        {
            var address = adapter.GetIPProperties().UnicastAddresses
                .Select(item => item.Address)
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(ip));
            if (address is not null) return address;
        }

        return IPAddress.Loopback;
    }

    public IPAddress? GetGatewayIp() => ActiveAdapters()
        .OrderByDescending(HasIpv4Gateway)
        .SelectMany(adapter => adapter.GetIPProperties().GatewayAddresses)
        .Select(item => item.Address)
        .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork
            && !ip.Equals(IPAddress.Any));

    public string? GetRouterUrl()
    {
        var gateway = GetGatewayIp();
        return gateway is null ? null : $"http://{gateway}";
    }

    /// <summary>
    /// Returns true when an address belongs to one of this machine's active IPv4
    /// subnets. RemoteAccessEnabled controls Internet clients only; LAN clients must
    /// remain usable while that public-access switch is off.
    /// </summary>
    public bool IsLanAddress(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;

        foreach (var item in ActiveAdapters()
                     .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses))
        {
            var localAddress = item.Address;
            if (localAddress.IsIPv4MappedToIPv6) localAddress = localAddress.MapToIPv4();
            if (localAddress.AddressFamily != AddressFamily.InterNetwork) continue;
            if (address.Equals(localAddress)) return true;

            var mask = item.IPv4Mask;
            if (mask is not null && IsSameSubnet(address, localAddress, mask)) return true;
        }

        return false;
    }

    private static bool IsSameSubnet(IPAddress address, IPAddress localAddress, IPAddress mask)
    {
        byte[] addressBytes = address.GetAddressBytes();
        byte[] localBytes = localAddress.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        if (addressBytes.Length != localBytes.Length || addressBytes.Length != maskBytes.Length)
            return false;

        for (int index = 0; index < addressBytes.Length; index++)
        {
            if ((addressBytes[index] & maskBytes[index]) != (localBytes[index] & maskBytes[index]))
                return false;
        }

        return true;
    }

    private static bool HasIpv4Gateway(NetworkInterface adapter) =>
        adapter.GetIPProperties().GatewayAddresses.Any(item =>
            item.Address.AddressFamily == AddressFamily.InterNetwork
            && !item.Address.Equals(IPAddress.Any));
}
