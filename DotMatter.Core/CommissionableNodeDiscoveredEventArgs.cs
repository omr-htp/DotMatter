namespace DotMatter.Core;

/// <summary>CommissionableNodeDiscoveredEventArgs class.</summary>
/// <summary>CommissionableNodeDiscoveredEventArgs class.</summary>
public class CommissionableNodeDiscoveredEventArgs(string discriminator)
{
    /// <summary>Discriminator.</summary>
    /// <summary>Gets or sets the Discriminator value.</summary>
    public string Discriminator { get; } = discriminator;
}
