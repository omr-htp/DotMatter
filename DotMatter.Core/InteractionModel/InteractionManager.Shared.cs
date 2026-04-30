using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>
/// Internal hub for Matter Interaction Model request/response helpers used by clusters and subscriptions.
/// Public cluster APIs delegate here; the protocol plumbing itself is intentionally not part of the supported surface.
/// </summary>
internal static partial class InteractionManager
{
    private const byte ProtocolIdIM = 0x01;
    private const byte OpStatusResponse = 0x01;
    private const byte OpReadRequest = 0x02;
    private const byte OpSubscribeRequest = 0x03;
    private const byte OpSubscribeResponse = 0x04;
    private const byte OpReportData = 0x05;
    private const byte OpWriteRequest = 0x06;
    private const byte OpWriteResponse = 0x07;
    private const byte OpInvokeRequest = 0x08;
    private const byte OpInvokeResponse = 0x09;
    private const byte OpTimedRequest = 0x0A;
    private const byte IMRevision = 12;

    private static MessagePayload CreatePayload(MatterTLV tlv, byte opCode)
        => new(tlv)
        {
            ExchangeFlags = ExchangeFlags.Initiator,
            ProtocolId = ProtocolIdIM,
            ProtocolOpCode = opCode,
        };

    private static MessageFrame CreateSecuredFrame(MessagePayload payload)
        => new(payload)
        {
            MessageFlags = MessageFlags.S,
            SecurityFlags = 0x00,
            SourceNodeID = 0x00,
        };

    private static async Task<MessageFrame> SendAndReceiveAsync(
        MessageExchange exchange,
        MessageFrame frame,
        CancellationToken ct,
        bool acknowledge = true)
    {
        await exchange.SendAsync(frame);
        var response = await exchange.WaitForNextMessageAsync(ct);
        if (acknowledge)
        {
            await exchange.AcknowledgeMessageAsync(response.MessageCounter);
        }

        return response;
    }

    private static async Task<bool> PerformTimedRequestAsync(
        MessageExchange exchange,
        ushort timedTimeoutMs,
        CancellationToken ct)
        => await PerformTimedRequestForStatusAsync(exchange, timedTimeoutMs, ct) == (byte)MatterStatusCode.Success;

    private static async Task<byte> PerformTimedRequestForStatusAsync(
        MessageExchange exchange,
        ushort timedTimeoutMs,
        CancellationToken ct)
    {
        var timedTlv = new MatterTLV();
        timedTlv.AddStructure();
        timedTlv.AddUInt16(0, timedTimeoutMs);
        timedTlv.AddUInt8(255, IMRevision);
        timedTlv.EndContainer();

        var timedResponse = await SendAndReceiveAsync(
            exchange,
            CreateSecuredFrame(CreatePayload(timedTlv, OpTimedRequest)),
            ct,
            acknowledge: false);

        if (timedResponse.MessagePayload.ProtocolOpCode != OpStatusResponse)
        {
            await exchange.AcknowledgeMessageAsync(timedResponse.MessageCounter);
            return (byte)MatterStatusCode.Failure;
        }

        var status = ParseStatusResponse(timedResponse.MessagePayload.ApplicationPayload);
        if (status != (byte)MatterStatusCode.Success)
        {
            await exchange.AcknowledgeMessageAsync(timedResponse.MessageCounter);
        }

        return status;
    }

    internal static byte ParseStatusResponse(MatterTLV? appTlv)
    {
        if (appTlv is null)
        {
            return (byte)MatterStatusCode.Failure;
        }

        return global::DotMatter.Core.Protocol.StatusResponseMessage.Deserialize(appTlv).Status;
    }

    private static MessageFrame CreateStatusResponseFrame(byte statusCode)
    {
        var statusTlv = new MatterTLV();
        statusTlv.AddStructure();
        statusTlv.AddUInt8(0, statusCode);
        statusTlv.AddUInt8(255, IMRevision);
        statusTlv.EndContainer();
        return CreateSecuredFrame(CreatePayload(statusTlv, OpStatusResponse));
    }
}
