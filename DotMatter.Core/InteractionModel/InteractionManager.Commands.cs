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
            if (timedRequest && !await PerformTimedRequestAsync(exchange, timedTimeoutMs, ct))
            {
                var failedFrame = CreateSecuredFrame(CreatePayload(new MatterTLV(), OpStatusResponse));
                return new InvokeResponse(false, 0x01, failedFrame, null);
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

            var success = response.MessagePayload.ProtocolOpCode == OpInvokeResponse;
            var statusCode = (byte)0;
            MatterTLV? responseFields = null;

            if (success && response.MessagePayload.ApplicationPayload is { } appTlv)
            {
                try
                {
                    var appTlvBytes = appTlv.AsSpan();
                    var rawHex = Convert.ToHexString(appTlvBytes);
                    MatterLog.Info("[IM] InvokeResponse raw TLV ({0} bytes): {1}",
                        appTlvBytes.Length, rawHex.Length > 200 ? rawHex[..200] + "..." : rawHex);

                    appTlv.OpenStructure();
                    if (appTlv.IsNextTag(0))
                    {
                        appTlv.SkipElement();
                    }

                    if (appTlv.IsNextTag(1))
                    {
                        appTlv.OpenArray(1);
                        if (!appTlv.IsEndContainerNext())
                        {
                            appTlv.OpenStructure();
                            if (appTlv.IsNextTag(0))
                            {
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
                                appTlv.OpenStructure(1);
                                if (appTlv.IsNextTag(0))
                                {
                                    appTlv.SkipElement();
                                }

                                if (appTlv.IsNextTag(1))
                                {
                                    appTlv.OpenStructure(1);
                                    if (appTlv.IsNextTag(0))
                                    {
                                        statusCode = appTlv.GetUnsignedInt8(0);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MatterLog.Info("[IM] InvokeResponse parse error: {0}", ex.Message);
                }
            }

            return new InvokeResponse(success && statusCode == 0, statusCode, response, responseFields);
        }
        finally
        {
            exchange.Close();
        }
    }
}
