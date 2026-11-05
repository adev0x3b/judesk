using System.Net;
using System.Net.Sockets;

namespace JuDesk.Api.Services;

public static class RateLimits
{
    /// <summary>Per-address window over every API call; see RateLimits:RequestsPerMinute.</summary>
    public const string ApiPolicy = "api";

    /// <summary>Concurrency cap for the auto-scheduler; see RateLimits:HeavyConcurrency.</summary>
    public const string HeavyPolicy = "heavy";

    /// <summary>
    /// The bucket a request counts against: the IPv4 address, or the /64 network of an IPv6 address.
    /// One IPv6 subscriber usually holds a whole /64, so keying on the full address would hand them
    /// as many fresh buckets as they like.
    /// </summary>
    public static string ClientKey(HttpContext http)
    {
        var address = http.Connection.RemoteIpAddress;
        if (address is null)
        {
            return "unknown";
        }
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
