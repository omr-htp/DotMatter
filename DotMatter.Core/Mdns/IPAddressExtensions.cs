using System.Net;
using System.Net.Sockets;

namespace DotMatter.Core.Mdns;

/// <summary>
///   Extension methods for <see cref="IPAddress"/>.
/// </summary>
public static class IPAddressExtensions
{
    /// <summary>
    ///  Gets the ARPA name for the specified IP address.
    /// </summary>
    /// <param name="ip"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static string GetArpaName(this IPAddress ip)
    {
        ArgumentNullException.ThrowIfNull(ip);

        return ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => GetIPv4ArpaName(ip),
            AddressFamily.InterNetworkV6 => GetIPv6ArpaName(ip),
            _ => throw new ArgumentException(
                $"Unsupported address family '{ip.AddressFamily}'.",
                nameof(ip))
        };
    }

    private static string GetIPv4ArpaName(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();

        return $"{bytes[3]}.{bytes[2]}.{bytes[1]}.{bytes[0]}.in-addr.arpa";
    }

    private static string GetIPv6ArpaName(IPAddress ip)
    {
        Span<byte> bytes = stackalloc byte[16];

        if (!ip.TryWriteBytes(bytes, out var bytesWritten) || bytesWritten != 16)
        {
            throw new ArgumentException("Invalid IPv6 address.", nameof(ip));
        }

        const string suffix = "ip6.arpa";

        var chars = new char[16 * 4 + suffix.Length];

        var index = 0;

        for (var i = 15; i >= 0; i--)
        {
            var b = bytes[i];

            chars[index++] = GetHexChar(b & 0x0F);
            chars[index++] = '.';
            chars[index++] = GetHexChar(b >> 4);
            chars[index++] = '.';
        }

        suffix.CopyTo(chars.AsSpan(index));

        return new string(chars);
    }

    private static char GetHexChar(int value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + value - 10);
    }
}
