namespace DotMatter.Core;

/// <summary>MessageFrame class.</summary>
public class MessageFrame
{
    /// <summary>MessageFrame.</summary>
    public MessageFrame()
    {
    }

    /// <summary>MessageFrame.</summary>
    public MessageFrame(MessagePayload messagePayload)
    {
        MessagePayload = messagePayload;
    }

    /// <summary>MessageFlags.</summary>
    /// <summary>Gets or sets the MessageFlags value.</summary>
    public MessageFlags MessageFlags
    {
        get; set;
    }

    /// <summary>SessionID.</summary>
    /// <summary>Gets or sets the SessionID value.</summary>
    public ushort SessionID
    {
        get; set;
    }

    /// <summary>SecurityFlags.</summary>
    /// <summary>Gets or sets the SecurityFlags value.</summary>
    public SecurityFlags SecurityFlags
    {
        get; set;
    }

    /// <summary>MessageCounter.</summary>
    /// <summary>Gets or sets the MessageCounter value.</summary>
    public uint MessageCounter
    {
        get; set;
    }

    /// <summary>SourceNodeID.</summary>
    /// <summary>Gets or sets the SourceNodeID value.</summary>
    public ulong SourceNodeID
    {
        get; set;
    }

    /// <summary>DestinationNodeId.</summary>
    /// <summary>Gets or sets the DestinationNodeId value.</summary>
    public ulong DestinationNodeId
    {
        get; set;
    }

    /// <summary>MessagePayload.</summary>
    /// <summary>Gets or sets the MessagePayload value.</summary>
    public MessagePayload MessagePayload { get; set; } = default!;

    /// <summary>EncryptedMessagePayload.</summary>
    /// <summary>Gets or sets the EncryptedMessagePayload value.</summary>
    public byte[]? EncryptedMessagePayload
    {
        get; set;
    }

    /// <summary>IsStatusReport.</summary>
    public static bool IsStatusReport(MessageFrame successMessageFrame)
    {
        return successMessageFrame.MessagePayload.ProtocolId == 0x00 &&
               successMessageFrame.MessagePayload.ProtocolOpCode == 0x40;
    }

    /// <summary>ToString.</summary>
    public override string ToString()
        => string.Empty;
}
