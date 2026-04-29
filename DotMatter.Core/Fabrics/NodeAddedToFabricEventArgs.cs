using Org.BouncyCastle.Math;

namespace DotMatter.Core.Fabrics;

/// <summary>NodeAddedToFabricEventArgs class.</summary>
public class NodeAddedToFabricEventArgs
{
    /// <summary>NodeId.</summary>
    /// <summary>Gets or sets the NodeId value.</summary>
    public BigInteger NodeId { get; internal set; } = default!;
}
