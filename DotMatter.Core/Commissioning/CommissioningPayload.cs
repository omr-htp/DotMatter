namespace DotMatter.Core.Commissioning;

/// <summary>CommissioningPayload class.</summary>
public class CommissioningPayload
{
    /// <summary>Discriminator.</summary>
    /// <summary>Gets or sets the Discriminator value.</summary>
    public ushort Discriminator { get; set; }

    /// <summary>Passcode.</summary>
    /// <summary>Gets or sets the Passcode value.</summary>
    public uint Passcode { get; set; }
}
