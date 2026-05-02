using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>A single event report from a ReportData message (spec §10.6.6).</summary>
public sealed class EventReportIB
{
    /// <summary>EventData.</summary>
    /// <summary>Gets or sets the EventData value.</summary>
    public EventDataIB? EventData { get; }
    /// <summary>StatusCode.</summary>
    /// <summary>Gets or sets the StatusCode value.</summary>
    public byte? StatusCode { get; }

    /// <summary>EventReportIB.</summary>
    public EventReportIB(MatterTLV payload)
    {
        payload.OpenStructure();

        while (!payload.IsEndContainerNext())
        {
            if (payload.IsNextTag(0))
            {
                // Tag 0: EventStatusIB
                payload.OpenStructure(0);
                if (payload.IsNextTag(0))
                {
                    payload.SkipElement(); // EventPath
                }

                if (payload.IsNextTag(1))
                {
                    payload.OpenStructure(1); // StatusIB
                    if (payload.IsNextTag(0))
                    {
                        StatusCode = payload.GetUnsignedInt8(0);
                    }

                    while (!payload.IsEndContainerNext())
                    {
                        payload.SkipElement();
                    }

                    payload.CloseContainer();
                }
                while (!payload.IsEndContainerNext())
                {
                    payload.SkipElement();
                }

                payload.CloseContainer();
                continue;
            }
            if (payload.IsNextTag(1))
            {
                EventData = new EventDataIB(payload);
                continue;
            }
            payload.SkipElement();
        }

        payload.CloseContainer();
    }
}

/// <summary>Event data from a device (spec §10.6.7).</summary>
public sealed class EventDataIB
{
    /// <summary>EndpointId.</summary>
    /// <summary>Gets or sets the EndpointId value.</summary>
    public ushort EndpointId { get; }
    /// <summary>ClusterId.</summary>
    /// <summary>Gets or sets the ClusterId value.</summary>
    public uint ClusterId { get; }
    /// <summary>EventId.</summary>
    /// <summary>Gets or sets the EventId value.</summary>
    public uint EventId { get; }
    /// <summary>EventNumber.</summary>
    /// <summary>Gets or sets the EventNumber value.</summary>
    public ulong EventNumber { get; }
    /// <summary>Priority.</summary>
    /// <summary>Gets or sets the Priority value.</summary>
    public byte Priority { get; }
    /// <summary>EpochTimestamp.</summary>
    /// <summary>Gets or sets the EpochTimestamp value.</summary>
    public ulong? EpochTimestamp { get; }
    /// <summary>SystemTimestamp.</summary>
    /// <summary>Gets or sets the SystemTimestamp value.</summary>
    public ulong? SystemTimestamp { get; }
    /// <summary>DeltaEpochTimestamp.</summary>
    /// <summary>Gets or sets the DeltaEpochTimestamp value.</summary>
    public ulong? DeltaEpochTimestamp { get; }
    /// <summary>DeltaSystemTimestamp.</summary>
    /// <summary>Gets or sets the DeltaSystemTimestamp value.</summary>
    public ulong? DeltaSystemTimestamp { get; }
    /// <summary>Compatibility timestamp view. Prefers epoch/system timestamps when present.</summary>
    public ulong Timestamp => EpochTimestamp ?? SystemTimestamp ?? DeltaEpochTimestamp ?? DeltaSystemTimestamp ?? 0;
    /// <summary>Data.</summary>
    /// <summary>Gets or sets the Data value.</summary>
    public object? Data { get; }
    /// <summary>Raw TLV element for the Data field, including its context tag, when present.</summary>
    public MatterTLV? RawData { get; }

    /// <summary>EventDataIB.</summary>
    public EventDataIB(MatterTLV payload)
    {
        payload.OpenStructure(1);

        while (!payload.IsEndContainerNext())
        {
            if (payload.IsNextTag(0))
            {
                // EventPath: list { tag0: NodeID, tag1: EndpointId, tag2: ClusterId, tag3: EventId }
                payload.OpenList(0);
                while (!payload.IsEndContainerNext())
                {
                    if (payload.IsNextTag(1))
                    {
                        EndpointId = payload.GetUnsignedInt16(1);
                    }
                    else if (payload.IsNextTag(2))
                    {
                        ClusterId = (uint)payload.GetUnsignedInt32(2);
                    }
                    else if (payload.IsNextTag(3))
                    {
                        EventId = (uint)payload.GetUnsignedInt32(3);
                    }
                    else
                    {
                        payload.SkipElement();
                    }
                }
                payload.CloseContainer();
                continue;
            }
            if (payload.IsNextTag(1))
            {
                EventNumber = payload.GetUnsignedInt64(1);
                continue;
            }
            if (payload.IsNextTag(2))
            {
                Priority = payload.GetUnsignedInt8(2);
                continue;
            }
            if (payload.IsNextTag(3))
            {
                EpochTimestamp = payload.GetUnsignedInt64(3);
                continue;
            }
            if (payload.IsNextTag(4))
            {
                SystemTimestamp = payload.GetUnsignedInt64(4);
                continue;
            }
            if (payload.IsNextTag(5))
            {
                DeltaEpochTimestamp = payload.GetUnsignedInt64(5);
                continue;
            }
            if (payload.IsNextTag(6))
            {
                DeltaSystemTimestamp = payload.GetUnsignedInt64(6);
                continue;
            }
            if (payload.IsNextTag(7))
            {
                var dataStart = payload.Position;
                Data = payload.GetData(7);
                RawData = new MatterTLV(payload.AsSpan()[dataStart..payload.Position]);
                continue;
            }
            payload.SkipElement();
        }

        payload.CloseContainer();
    }
}
