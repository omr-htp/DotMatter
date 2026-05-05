#nullable disable
using System.Net.NetworkInformation;

namespace DotMatter.Core.Mdns;

/// <summary>
///   The event data for <see cref="MulticastService.NetworkInterfaceDiscovered"/>.
/// </summary>
public class NetworkInterfaceEventArgs : EventArgs
{
    /// <summary>
    ///   The sequece of detected network interfaces.
    /// </summary>
    /// <value>
    ///   A sequence of network interfaces.
    /// </value>
    public IEnumerable<NetworkInterface> NetworkInterfaces
    {
        get; set;
    }
}

