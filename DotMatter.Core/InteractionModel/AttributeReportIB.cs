using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>AttributeReportIB class.</summary>
public class AttributeReportIB
{
    /// <summary>AttributeReportIB.</summary>
    public AttributeReportIB(MatterTLV payload)
    {
        payload.OpenStructure();

        if (payload.IsNextTag(0))
        {
            AttributeStatus = new AttributeStatusIB(0, payload);
        }

        if (payload.IsNextTag(1))
        {
            AttributeData = new AttributeDataIB(1, payload);
        }

        payload.CloseContainer();
    }

    /// <summary>AttributeStatus.</summary>
    /// <summary>Gets or sets the AttributeStatus value.</summary>
    public AttributeStatusIB? AttributeStatus
    {
        get;
    }

    /// <summary>AttributeData.</summary>
    /// <summary>Gets or sets the AttributeData value.</summary>
    public AttributeDataIB? AttributeData
    {
        get;
    }
}
