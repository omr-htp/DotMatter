using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>AttributePathIB class.</summary>
public class AttributePathIB
{
    /// <summary>AttributePathIB.</summary>
    public AttributePathIB(int tag, MatterTLV payload)
    {
        payload.OpenList(tag);

        while (!payload.IsEndContainerNext())
        {
            if (payload.IsNextTag(0))
            {
                EnableTagCompression = payload.GetBoolean(0);
            }

            if (payload.IsNextTag(1))
            {
                NodeId = payload.GetUnsignedInt(1);
            }

            if (payload.IsNextTag(2))
            {
                EndpointId = (uint)payload.GetUnsignedInt(2);
            }

            if (payload.IsNextTag(3))
            {
                ClusterId = (uint)payload.GetUnsignedInt(3);
            }

            if (payload.IsNextTag(4))
            {
                AttributeId = (uint)payload.GetUnsignedInt(4);
            }

            if (payload.IsNextTag(5))
            {
                ListIndex = (ushort)payload.GetUnsignedInt(5);
            }

            if (payload.IsNextTag(6))
            {
                WildcardPathFlags = (uint)payload.GetUnsignedInt(6);
            }
        }

        payload.CloseContainer();
    }

    /// <summary>EnableTagCompression.</summary>
    /// <summary>Gets or sets the EnableTagCompression value.</summary>
    public bool EnableTagCompression
    {
        get;
    }

    /// <summary>NodeId.</summary>
    /// <summary>Gets or sets the NodeId value.</summary>
    public ulong NodeId
    {
        get;
    }

    /// <summary>EndpointId.</summary>
    /// <summary>Gets or sets the EndpointId value.</summary>
    public uint EndpointId
    {
        get;
    }

    /// <summary>ClusterId.</summary>
    /// <summary>Gets or sets the ClusterId value.</summary>
    public uint ClusterId
    {
        get;
    }

    /// <summary>AttributeId.</summary>
    /// <summary>Gets or sets the AttributeId value.</summary>
    public uint AttributeId
    {
        get;
    }

    /// <summary>ListIndex.</summary>
    /// <summary>Gets or sets the ListIndex value.</summary>
    public ushort ListIndex
    {
        get;
    }

    /// <summary>WildcardPathFlags.</summary>
    /// <summary>Gets or sets the WildcardPathFlags value.</summary>
    public uint WildcardPathFlags
    {
        get;
    }
}
