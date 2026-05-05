using System.Net;
using Org.BouncyCastle.Math;

namespace DotMatter.Core;

/// <summary>
/// Represents a Matter node known to a fabric.
/// </summary>
public class Node
{
    /// <summary>
    /// Gets or sets the Matter node identifier.
    /// </summary>
    public BigInteger NodeId { get; set; } = default!;

    /// <summary>
    /// Gets the node identifier formatted as a hexadecimal node name.
    /// </summary>
    public string NodeName => Convert.ToHexString([.. NodeId.ToByteArray().Reverse()]);

    /// <summary>
    /// Gets or sets the last operational IP address discovered for this node.
    /// </summary>
    public IPAddress? LastKnownIpAddress
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the last operational port discovered for this node.
    /// </summary>
    public ushort? LastKnownPort
    {
        get; set;
    }
}
