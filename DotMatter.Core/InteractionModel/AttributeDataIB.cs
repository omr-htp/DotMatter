using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>AttributeDataIB class.</summary>
public class AttributeDataIB
{
    /// <summary>AttributeDataIB.</summary>
    public AttributeDataIB(int tag, MatterTLV payload)
    {
        payload.OpenStructure(tag);

        DataVersion = payload.GetUnsignedInt32(0);

        Path = new AttributePathIB(1, payload);

        var dataStart = payload.Position;
        Data = payload.GetData(2)!;
        RawData = new MatterTLV(payload.AsSpan()[dataStart..payload.Position]);

        payload.CloseContainer();
    }

    /// <summary>DataVersion.</summary>
    /// <summary>Gets or sets the DataVersion value.</summary>
    public uint DataVersion { get; }

    /// <summary>Path.</summary>
    /// <summary>Gets or sets the Path value.</summary>
    public AttributePathIB Path { get; }

    /// <summary>Data.</summary>
    /// <summary>Gets or sets the Data value.</summary>
    public object Data { get; } = default!;

    /// <summary>Raw TLV element for the Data field, including its context tag.</summary>
    public MatterTLV RawData { get; } = default!;
}
