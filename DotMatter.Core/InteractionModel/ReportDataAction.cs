using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>ReportDataAction class.</summary>
public class ReportDataAction
{
    /// <summary>ReportDataAction.</summary>
    public ReportDataAction(MatterTLV payload)
    {
        payload.OpenStructure();

        if (payload.IsNextTag(0))
        {
            SubscriptionId = payload.GetUnsignedInt32(0);
        }

        if (payload.IsNextTag(1))
        {
            payload.OpenArray(1);

            while (!payload.IsEndContainerNext())
            {
                AttributeReports.Add(new AttributeReportIB(payload));
            }

            payload.CloseContainer();
        }

        // Tag 2: EventReports
        if (payload.IsNextTag(2))
        {
            payload.OpenArray(2);
            while (!payload.IsEndContainerNext())
            {
                EventReports.Add(new EventReportIB(payload));
            }
            payload.CloseContainer();
        }

        if (payload.IsNextTag(3))
        {
            MoreChunkedMessages = payload.GetBoolean(3);
        }

        if (payload.IsNextTag(4))
        {
            SuppressResponse = payload.GetBoolean(4);
        }
    }

    /// <summary>SubscriptionId.</summary>
    /// <summary>Gets or sets the SubscriptionId value.</summary>
    public uint SubscriptionId { get; set; }

    /// <summary>AttributeReports.</summary>
    /// <summary>Gets or sets the AttributeReports value.</summary>
    public List<AttributeReportIB> AttributeReports { get; set; } = [];

    /// <summary>EventReports.</summary>
    /// <summary>Gets or sets the EventReports value.</summary>
    public List<EventReportIB> EventReports { get; set; } = [];

    /// <summary>MoreChunkedMessages.</summary>
    /// <summary>Gets or sets the MoreChunkedMessages value.</summary>
    public bool MoreChunkedMessages { get; set; }

    /// <summary>SuppressResponse.</summary>
    /// <summary>Gets or sets the SuppressResponse value.</summary>
    public bool SuppressResponse { get; set; }

    /// <summary>InteractionModelRevision.</summary>
    /// <summary>Gets or sets the InteractionModelRevision value.</summary>
    public uint InteractionModelRevision { get; set; }
}
