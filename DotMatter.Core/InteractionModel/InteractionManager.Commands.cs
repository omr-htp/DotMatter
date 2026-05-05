using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

internal static partial class InteractionManager
{
    internal static async Task<InvokeResponse> ExecCommandAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint commandId,
        Action<MatterTLV>? addFields = null,
        bool timedRequest = false,
        ushort timedTimeoutMs = 5000,
        CancellationToken ct = default)
    {
        var exchange = session.CreateExchange();
        try
        {
            if (timedRequest)
            {
                var timedStatus = await PerformTimedRequestForStatusAsync(exchange, timedTimeoutMs, ct);
                if (timedStatus != (byte)MatterStatusCode.Success)
                {
                    var failedFrame = CreateSecuredFrame(CreatePayload(new MatterTLV(), OpStatusResponse));
                    return new InvokeResponse(false, timedStatus, failedFrame, null, "Timed invoke precondition failed.");
                }
            }

            var tlv = new MatterTLV();
            tlv.AddStructure();
            tlv.AddBool(0, false);
            tlv.AddBool(1, timedRequest);
            tlv.AddArray(tagNumber: 2);
            tlv.AddStructure();
            tlv.AddList(tagNumber: 0);
            tlv.AddUInt16(tagNumber: 0, endpointId);
            tlv.AddUInt32(tagNumber: 1, clusterId);
            tlv.AddUInt32(tagNumber: 2, commandId);
            tlv.EndContainer();
            tlv.AddStructure(1);
            addFields?.Invoke(tlv);
            tlv.EndContainer();
            tlv.EndContainer();
            tlv.EndContainer();
            tlv.AddUInt8(255, IMRevision);
            tlv.EndContainer();

            var response = await SendAndReceiveAsync(
                exchange,
                CreateSecuredFrame(CreatePayload(tlv, OpInvokeRequest)),
                ct);

            return ParseInvokeResponse(response);
        }
        finally
        {
            exchange.Close();
        }
    }

    private static InvokeResponse ParseInvokeResponse(MessageFrame response)
    {
        if (response.MessagePayload.ProtocolOpCode != OpInvokeResponse)
        {
            if (response.MessagePayload.ProtocolOpCode == OpStatusResponse)
            {
                var status = ParseStatusResponse(response.MessagePayload.ApplicationPayload);
                return new InvokeResponse(
                    false,
                    status,
                    response,
                    null,
                    $"Unexpected StatusResponse (0x{status:X2}) returned for invoke command.");
            }

            return new InvokeResponse(
                false,
                (byte)MatterStatusCode.Failure,
                response,
                null,
                $"Unexpected invoke opcode 0x{response.MessagePayload.ProtocolOpCode:X2}.");
        }

        if (response.MessagePayload.ApplicationPayload is not { } appTlv)
        {
            return new InvokeResponse(
                false,
                (byte)MatterStatusCode.Failure,
                response,
                null,
                "InvokeResponse was missing an application payload.");
        }

        try
        {
            var appTlvBytes = appTlv.AsSpan();
            var rawHex = Convert.ToHexString(appTlvBytes);
            MatterLog.Info("[IM] InvokeResponse raw TLV ({0} bytes): {1}",
                appTlvBytes.Length, rawHex.Length > 200 ? rawHex[..200] + "..." : rawHex);

            byte statusCode = 0;
            MatterTLV? responseFields = null;
            var foundResult = false;

            appTlv.OpenStructure();
            if (appTlv.IsNextTag(0))
            {
                appTlv.SkipElement();
            }

            if (!appTlv.IsNextTag(1))
            {
                return new InvokeResponse(
                    false,
                    (byte)MatterStatusCode.Failure,
                    response,
                    null,
                    "InvokeResponse did not contain an InvokeResponses array.");
            }

            appTlv.OpenArray(1);
            if (appTlv.IsEndContainerNext())
            {
                return new InvokeResponse(
                    false,
                    (byte)MatterStatusCode.Failure,
                    response,
                    null,
                    "InvokeResponse contained an empty InvokeResponses array.");
            }

            appTlv.OpenStructure();
            if (appTlv.IsNextTag(0))
            {
                foundResult = true;
                appTlv.OpenStructure(0);
                if (appTlv.IsNextTag(0))
                {
                    appTlv.SkipElement();
                }

                if (appTlv.IsNextTag(1))
                {
                    var fieldsStart = appTlv.Position;
                    appTlv.SkipElement();
                    var fieldsEnd = appTlv.Position;
                    responseFields = new MatterTLV(appTlvBytes[fieldsStart..fieldsEnd]);
                }
            }
            else if (appTlv.IsNextTag(1))
            {
                foundResult = true;
                appTlv.OpenStructure(1);
                if (appTlv.IsNextTag(0))
                {
                    appTlv.SkipElement();
                }

                if (!appTlv.IsNextTag(1))
                {
                    return new InvokeResponse(
                        false,
                        (byte)MatterStatusCode.Failure,
                        response,
                        null,
                        "InvokeResponse command status was missing StatusIB.");
                }

                appTlv.OpenStructure(1);
                if (!appTlv.IsNextTag(0))
                {
                    return new InvokeResponse(
                        false,
                        (byte)MatterStatusCode.Failure,
                        response,
                        null,
                        "InvokeResponse StatusIB was missing status code.");
                }

                statusCode = appTlv.GetUnsignedInt8(0);
            }

            if (!foundResult)
            {
                return new InvokeResponse(
                    false,
                    (byte)MatterStatusCode.Failure,
                    response,
                    null,
                    "InvokeResponse did not contain CommandDataIB or CommandStatusIB.");
            }

            return new InvokeResponse(statusCode == 0, statusCode, response, responseFields);
        }
        catch (Exception ex)
        {
            MatterLog.Info("[IM] InvokeResponse parse error: {0}", ex.Message);
            return new InvokeResponse(
                false,
                (byte)MatterStatusCode.Failure,
                response,
                null,
                $"InvokeResponse parse error: {ex.Message}");
        }
    }
}
