namespace DotMatter.Core;

/// <summary>NodeRegisterDetails class.</summary>
/// <summary>NodeRegisterDetails class.</summary>
public class NodeRegisterDetails(string nodeName, ushort discriminator, ushort port, string[] addresses)
{
    /// <summary>NodeName.</summary>
    /// <summary>Gets or sets the NodeName value.</summary>
    public string NodeName { get; set; } = nodeName;

    /// <summary>Discriminator.</summary>
    /// <summary>Gets or sets the Discriminator value.</summary>
    public ushort Discriminator { get; set; } = discriminator;

    /// <summary>Port.</summary>
    /// <summary>Gets or sets the Port value.</summary>
    public ushort Port { get; set; } = port;

    /// <summary>Addresses.</summary>
    /// <summary>Gets or sets the Addresses value.</summary>
    public string[] Addresses { get; set; } = addresses;
}
