using DotMatter.Core.TLV;
using Org.BouncyCastle.X509;

namespace DotMatter.Core;

/// <summary>Extensions class.</summary>
public static class Extensions
{
    /// <summary>ToEpochTime.</summary>
    public static uint ToEpochTime(this DateTimeOffset dt)
    {
        var epochStart = 946684800; // 2000-01-01T00:00:00Z
        return (uint)(dt.ToUnixTimeSeconds() - epochStart);
    }

    /// <summary>DebugInfo.</summary>
    public static string DebugInfo(this MessageFrame message)
    {
        var (protocolName, protocolOpName) = GetFriendlyNames(message.MessagePayload);
        return string.Format("[S: {0}] {1} | {2} | {3} | ack:{4}", message.SessionID, message.MessageCounter, protocolName, protocolOpName, message.MessagePayload.AcknowledgedMessageCounter);
    }

    /// <summary>DebugInfo.</summary>
    public static string DebugInfo(this MessagePayload message)
    {
        var (protocolName, protocolOpName) = GetFriendlyNames(message);
        return string.Format("[E: {0}] {1} | {2} | {3} | {4}", message.ExchangeID, message.ExchangeFlags, message.ExchangeID, protocolName, protocolOpName);
    }

    private static (string protocolId, string protocolOpCode) GetFriendlyNames(MessagePayload messagePayload)
    {
        var protocolName = messagePayload.ProtocolId.ToProtocolName();
        var protocolOpName = messagePayload.ProtocolOpCode.ToProtocolOpName(messagePayload.ProtocolId);

        return (protocolName, protocolOpName);
    }

    /// <summary>ToProtocolName.</summary>
    public static string ToProtocolName(this ushort protocolId)
    {
        return protocolId switch
        {
            0x00 => "Secure",
            0x01 => "InteractionModel",
            _ => "Unmapped Protocol Id: " + protocolId.ToString("X4"),
        };
    }

    /// <summary>ToProtocolOpName.</summary>
    public static string ToProtocolOpName(this byte opCode, ushort protocolId)
    {
        if (protocolId == 0x00)
        {
            switch (opCode)
            {
                case 0x10:
                    return "MRP Standalone Acknowledgement";
                case 0x20:
                    return "PBKDFParamRequest";
                case 0x21:
                    return "PBKDFParamResponse";
                case 0x22:
                    return "PASE Pake1";
                case 0x23:
                    return "PASE Pake2";
                case 0x24:
                    return "PASE Pake3";
                case 0x30:
                    return "CASE Sigma1";
                case 0x31:
                    return "CASE Sigma2";
                case 0x32:
                    return "CASE Sigma3";
                case 0x40:
                    return "StatusReport";
            }
        }
        else if (protocolId == 0x01)
        {
            switch (opCode)
            {
                case 0x01:
                    return "Status Response";
                case 0x02:
                    return "Read Request";
                case 0x03:
                    return "Subscribe Request";
                case 0x04:
                    return "Subscribe Response";
                case 0x05:
                    return "Report Data";
                case 0x06:
                    return "Write Request";
                case 0x07:
                    return "Write Response";
                case 0x08:
                    return "Invoke Request";
                case 0x09:
                    return "Invoke Response";
                case 0x0A:
                    return "Timed Request";
            }
        }

        return $"Unknown ProtocolId: {protocolId}, OpCode: {opCode}";
    }

    /// <summary>ToMatterCertificate.</summary>
    public static MatterTLV ToMatterCertificate(this X509Certificate certificate)
    {
        _ = certificate.GetEncoded();
        var tlv = new MatterTLV();
        return tlv;
    }
}
