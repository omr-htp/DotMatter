using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>AttributeStatusIB class.</summary>
public class AttributeStatusIB
{
    /// <summary>AttributeStatusIB.</summary>
    public AttributeStatusIB(int tag, MatterTLV payload)
    {
        payload.OpenStructure(tag);

        if (payload.IsNextTag(0))
        {
            Path = new AttributePathIB(0, payload);
        }

        if (payload.IsNextTag(1))
        {
            Status = new StatusIB(1, payload);
        }

        payload.CloseContainer();
    }

    /// <summary>Path.</summary>
    /// <summary>Gets or sets the Path value.</summary>
    public AttributePathIB Path { get; } = default!;

    /// <summary>Status.</summary>
    /// <summary>Gets or sets the Status value.</summary>
    public StatusIB Status { get; } = default!;
}
