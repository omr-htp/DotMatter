using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>StatusIB class.</summary>
public class StatusIB
{
    /// <summary>StatusIB.</summary>
    public StatusIB(int tag, MatterTLV payload)
    {
        payload.OpenStructure(tag);

        Status = payload.GetUnsignedInt8(0);

        // ClusterStatus (tag 1) is optional per Matter spec
        if (!payload.IsEndContainerNext() && payload.IsNextTag(1))
        {
            ClusterStatus = payload.GetUnsignedInt8(1);
        }

        payload.CloseContainer();
    }

    /// <summary>Status.</summary>
    /// <summary>Gets or sets the Status value.</summary>
    public byte Status
    {
        get;
    }

    /// <summary>ClusterStatus.</summary>
    /// <summary>Gets or sets the ClusterStatus value.</summary>
    public byte ClusterStatus
    {
        get;
    }
}
