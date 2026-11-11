using System.Net;

namespace Workbench.Service;

/// <summary>Loopback is the only bind and peer the preview service accepts.</summary>
public static class LoopbackGuard
{
    public static bool IsLoopback(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IPAddress.IsLoopback(address);
    }

    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var name = host.Trim();
        if (name.StartsWith('['))
        {
            var end = name.IndexOf(']');
            if (end <= 1) return false;
            name = name[1..end];
        }
        else
        {
            var colon = name.LastIndexOf(':');
            if (colon > 0 && name.IndexOf(':') == colon) name = name[..colon];
        }
        return name is "localhost" or "127.0.0.1" or "::1";
    }
}
