using System.Buffers.Binary;
using DotMatter.Core.TLV;

namespace DotMatter.Core;

/// <summary>MessagePayload class.</summary>
public class MessagePayload
{
    /// <summary>MessagePayload.</summary>
    public MessagePayload()
    {
        ApplicationPayload = null;
    }

    /// <summary>MessagePayload.</summary>
    public MessagePayload(MatterTLV payload)
    {
        ApplicationPayload = payload;
    }

    /// <summary>MessagePayload.</summary>
    public MessagePayload(byte[] messagePayload)
        : this(messagePayload.AsSpan())
    {
    }

    /// <summary>MessagePayload.</summary>
    public MessagePayload(ReadOnlySpan<byte> messagePayload)
    {
        if (messagePayload.Length < 6) // ExchangeFlags(1) + OpCode(1) + ExchangeID(2) + ProtocolId(2)
        {
            throw new ArgumentException($"Message payload too short: {messagePayload.Length} bytes (minimum 6)");
        }

        var index = 0;

        ExchangeFlags = (ExchangeFlags)messagePayload[index];
        index++;

        ProtocolOpCode = messagePayload[index];
        index++;

        ExchangeID = BinaryPrimitives.ReadUInt16LittleEndian(messagePayload[index..]);
        index += 2;

        if ((ExchangeFlags & ExchangeFlags.VendorPresent) != 0)
        {
            ProtocolVendorId = BinaryPrimitives.ReadUInt16LittleEndian(messagePayload[index..]);
            index += 2;
        }

        ProtocolId = BinaryPrimitives.ReadUInt16LittleEndian(messagePayload[index..]);
        index += 2;

        if ((ExchangeFlags & ExchangeFlags.Acknowledgement) != 0)
        {
            AcknowledgedMessageCounter = BinaryPrimitives.ReadUInt32LittleEndian(messagePayload[index..]);
            index += 4;
        }

        if ((ExchangeFlags & ExchangeFlags.SecuredExtensions) != 0)
        {
            var securedExtensionsLength = BinaryPrimitives.ReadUInt16LittleEndian(messagePayload[index..]);
            index += 2; // Length ushort
            index += securedExtensionsLength;
        }

        try
        {
            ApplicationPayload = new MatterTLV(messagePayload[index..]);
        }
        catch (ArgumentOutOfRangeException)
        {
            MatterLog.Warn("Error extracting ApplicationPayload from MessagePayload: {0}", ExchangeFlags);
        }
    }

    /// <summary>ExchangeFlags.</summary>
    /// <summary>Gets or sets the ExchangeFlags value.</summary>
    public ExchangeFlags ExchangeFlags
    {
        get; set;
    }

    /// <summary>ProtocolOpCode.</summary>
    /// <summary>Gets or sets the ProtocolOpCode value.</summary>
    public byte ProtocolOpCode
    {
        get; set;
    }

    /// <summary>ExchangeID.</summary>
    /// <summary>Gets or sets the ExchangeID value.</summary>
    public ushort ExchangeID
    {
        get; set;
    }

    /// <summary>ProtocolId.</summary>
    /// <summary>Gets or sets the ProtocolId value.</summary>
    public ushort ProtocolId
    {
        get; set;
    }

    /// <summary>ProtocolVendorId.</summary>
    /// <summary>Gets or sets the ProtocolVendorId value.</summary>
    public ushort? ProtocolVendorId
    {
        get; set;
    }

    /// <summary>AcknowledgedMessageCounter.</summary>
    /// <summary>Gets or sets the AcknowledgedMessageCounter value.</summary>
    public uint AcknowledgedMessageCounter
    {
        get; set;
    }

    /// <summary>ApplicationPayload.</summary>
    /// <summary>Gets or sets the ApplicationPayload value.</summary>
    public MatterTLV? ApplicationPayload
    {
        get; set;
    }

    internal void Serialize(MatterMessageWriter writer)
    {
        writer.Write((byte)ExchangeFlags);
        writer.Write(ProtocolOpCode);
        writer.Write(ExchangeID);

        if ((ExchangeFlags & ExchangeFlags.VendorPresent) != 0 && ProtocolVendorId.HasValue)
        {
            writer.Write(ProtocolVendorId.Value);
        }

        writer.Write(ProtocolId);

        if ((ExchangeFlags & ExchangeFlags.Acknowledgement) != 0)
        {
            writer.Write(AcknowledgedMessageCounter);
        }

        // Write the bytes of the payload!
        //
        ApplicationPayload?.Serialize(writer);
    }
}
