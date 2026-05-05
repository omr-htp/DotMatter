namespace DotMatter.Core;

/// <summary>Endpoint class.</summary>
/// <summary>Endpoint class.</summary>
public class Endpoint(uint endpointId)
{
    /// <summary>EndpointId.</summary>
    /// <summary>Gets or sets the EndpointId value.</summary>
    public uint EndpointId { get; } = endpointId;

    /// <summary>DeviceType.</summary>
    /// <summary>Gets or sets the DeviceType value.</summary>
    public ulong DeviceType
    {
        get; set;
    }
}
