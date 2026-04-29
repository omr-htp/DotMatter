using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

internal static partial class InteractionManager
{
    internal static async Task<WriteResponse> WriteAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        Action<MatterTLV> writeValue,
        bool timedRequest = false,
        ushort timedTimeoutMs = 5000,
        CancellationToken ct = default)
    {
        var exchange = session.CreateExchange();
        try
        {
            if (timedRequest && !await PerformTimedRequestAsync(exchange, timedTimeoutMs, ct))
            {
                return new WriteResponse(false, [new WriteAttributeStatus(attributeId, 0x01)]);
            }

            var tlv = new MatterTLV();
            tlv.AddStructure();
            tlv.AddBool(0, false);
            tlv.AddBool(1, timedRequest);
            tlv.AddArray(tagNumber: 2);
            tlv.AddStructure();
            tlv.AddList(tagNumber: 0);
            tlv.AddUInt16(tagNumber: 2, endpointId);
            tlv.AddUInt32(tagNumber: 3, clusterId);
            tlv.AddUInt32(tagNumber: 4, attributeId);
            tlv.EndContainer();
            writeValue(tlv);
            tlv.EndContainer();
            tlv.EndContainer();
            tlv.AddUInt8(255, IMRevision);
            tlv.EndContainer();

            var response = await SendAndReceiveAsync(
                exchange,
                CreateSecuredFrame(CreatePayload(tlv, OpWriteRequest)),
                ct);

            var success = response.MessagePayload.ProtocolOpCode == OpWriteResponse;
            var statuses = new List<WriteAttributeStatus>();

            if (success && response.MessagePayload.ApplicationPayload is { } appTlv)
            {
                try
                {
                    appTlv.OpenStructure();
                    if (appTlv.IsNextTag(0))
                    {
                        appTlv.OpenArray(0);
                        while (!appTlv.IsEndContainerNext())
                        {
                            appTlv.OpenStructure();
                            byte statusCode = 0;
                            var statusAttrId = attributeId;

                            while (!appTlv.IsEndContainerNext())
                            {
                                if (appTlv.IsNextTag(0))
                                {
                                    appTlv.OpenStructure(0);
                                    if (appTlv.IsNextTag(0))
                                    {
                                        statusCode = appTlv.GetUnsignedInt8(0);
                                    }

                                    while (!appTlv.IsEndContainerNext())
                                    {
                                        appTlv.SkipElement();
                                    }

                                    appTlv.CloseContainer();
                                    continue;
                                }

                                if (appTlv.IsNextTag(1))
                                {
                                    appTlv.OpenList(1);
                                    while (!appTlv.IsEndContainerNext())
                                    {
                                        if (appTlv.IsNextTag(4))
                                        {
                                            statusAttrId = appTlv.GetUnsignedInt32(4);
                                        }
                                        else
                                        {
                                            appTlv.SkipElement();
                                        }
                                    }

                                    appTlv.CloseContainer();
                                    continue;
                                }

                                appTlv.SkipElement();
                            }

                            appTlv.CloseContainer();
                            statuses.Add(new WriteAttributeStatus(statusAttrId, statusCode));
                        }

                        appTlv.CloseContainer();
                    }
                }
                catch
                {
                    // Best-effort parsing keeps wire errors from hiding the primary write outcome.
                }
            }

            return new WriteResponse(success && statuses.All(status => status.StatusCode == 0), statuses);
        }
        finally
        {
            exchange.Close();
        }
    }
}
