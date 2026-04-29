using System.Buffers.Binary;

namespace DotMatter.Core;

/// <summary>MessageFrameParts class.</summary>
public class MessageFrameParts
{
    /// <summary>MessageFrameParts.</summary>
    public MessageFrameParts(MessageFrame messageFrame)
    {
        var headerWriter = new MatterMessageWriter();

        headerWriter.Write((byte)messageFrame.MessageFlags);
        headerWriter.Write(messageFrame.SessionID);
        headerWriter.Write((byte)messageFrame.SecurityFlags);
        headerWriter.Write(messageFrame.MessageCounter);

        if ((messageFrame.MessageFlags & MessageFlags.S) != 0)
        {
            headerWriter.Write(messageFrame.SourceNodeID);
        }

        var dsiz = messageFrame.MessageFlags.GetDsiz();
        if (dsiz == 1)
        {
            headerWriter.Write(messageFrame.DestinationNodeId);
        }
        else if (dsiz == 2)
        {
            headerWriter.Write(messageFrame.DestinationNodeId);
        }

        Header = headerWriter.GetBytes();

        var payloadWriter = new MatterMessageWriter();
        messageFrame.MessagePayload.Serialize(payloadWriter);

        MessagePayload = payloadWriter.GetBytes();
    }

    /// <summary>MessageFrameParts.</summary>
    public MessageFrameParts(byte[] messageFrameBytes)
        : this(messageFrameBytes.AsSpan())
    {
    }

    /// <summary>MessageFrameParts.</summary>
    public MessageFrameParts(ReadOnlySpan<byte> messageFrameBytes)
    {
        MatterLog.Debug("┌─────────────────────────────────── {0} ──────────────────────────────────────────\n│ {1}\n└──────────────────────────────────────────────────────────────────────────────", messageFrameBytes.Length, Convert.ToHexString(messageFrameBytes));

        if (messageFrameBytes.Length < 8)
        {
            throw new ArgumentException($"Message frame too short: {messageFrameBytes.Length} bytes (minimum 8)");
        }

        var messageFlags = (MessageFlags)messageFrameBytes[0];
        //var SessionID = BitConverter.ToUInt16(messageFrameBytes, 1);
        var SecurityFlags = (SecurityFlags)messageFrameBytes[3];
        //var MessageCounter = BitConverter.ToUInt32(messageFrameBytes, 4);

        var headerLength = 8; // MessageFlags (1), SessionId (2), SecurityFlags(1), MessageCounter (4)

        if ((messageFlags & MessageFlags.S) != 0)
        {
            headerLength += 8; // SourceNodeId (8 bytes)
        }

        var dsiz = messageFlags.GetDsiz();
        switch (dsiz)
        {
            case 0: break; // No destination node ID
            case 1: headerLength += 8; break; // 64-bit destination node ID
            case 2: headerLength += 2; break; // 16-bit group ID
            case 3: headerLength += 8; break; // Reserved, treat as 64-bit
        }

        if ((SecurityFlags & SecurityFlags.MessageExtensions) != 0)
        {
            MatterLog.Info("Message Extensions present!!!!");
        }

        if (messageFrameBytes.Length < headerLength)
        {
            throw new ArgumentException($"Message frame too short for header: {messageFrameBytes.Length} bytes (need {headerLength})");
        }

        Header = messageFrameBytes[..headerLength].ToArray();
        MessagePayload = messageFrameBytes[headerLength..].ToArray();
    }

    /// <summary>Header.</summary>
    /// <summary>Gets or sets the Header value.</summary>
    public byte[] Header { get; set; }

    /// <summary>MessagePayload.</summary>
    /// <summary>Gets or sets the MessagePayload value.</summary>
    public byte[] MessagePayload { get; set; }

    internal MessageFrame MessageFrameWithHeaders()
    {
        var messageFrame = new MessageFrame
        {
            MessageFlags = (MessageFlags)Header[0],
            SessionID = BinaryPrimitives.ReadUInt16LittleEndian(Header.AsSpan(1)),
            SecurityFlags = (SecurityFlags)Header[3],
            MessageCounter = BinaryPrimitives.ReadUInt32LittleEndian(Header.AsSpan(4))
        };

        var headerIndex = 8;

        if ((messageFrame.MessageFlags & MessageFlags.S) != 0)
        {
            // Process for the SourceNodeId (8 bytes)
            messageFrame.SourceNodeID = BinaryPrimitives.ReadUInt64LittleEndian(Header.AsSpan(8));
            headerIndex += 8;
        }

        var dsiz = messageFrame.MessageFlags.GetDsiz();
        if (dsiz == 1)
        {
            messageFrame.DestinationNodeId = BinaryPrimitives.ReadUInt64LittleEndian(Header.AsSpan(headerIndex));
            headerIndex += 8;
        }

        if (dsiz == 2)
        {
            // GroupId — 2 bytes
            headerIndex += 2;
        }

        if (dsiz == 3)
        {
            // Reserved — treat as 64-bit
            messageFrame.DestinationNodeId = BinaryPrimitives.ReadUInt64LittleEndian(Header.AsSpan(headerIndex));
            //headerIndex += 8;
        }

        // Return an instance of the MessageFrame with just the headers populated from the parts.
        //
        return messageFrame;
    }
}
