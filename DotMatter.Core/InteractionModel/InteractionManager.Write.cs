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
            if (timedRequest)
            {
                var timedStatus = await PerformTimedRequestForStatusAsync(exchange, timedTimeoutMs, ct);
                if (timedStatus != (byte)MatterStatusCode.Success)
                {
                    return new WriteResponse(false, [new WriteAttributeStatus(attributeId, timedStatus)], timedStatus);
                }
            }

            var tlv = new MatterTLV();
            tlv.AddStructure();
            tlv.AddBool(1, timedRequest);
            tlv.AddArray(tagNumber: 2);
            tlv.AddStructure();
            tlv.AddList(tagNumber: 1);
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

            return ParseWriteResponse(response, endpointId, clusterId, attributeId);
        }
        finally
        {
            exchange.Close();
        }
    }

    internal static WriteResponse ParseWriteResponse(
        MessageFrame response,
        ushort endpointId,
        uint clusterId,
        uint attributeId)
    {
        if (response.MessagePayload.ProtocolOpCode == OpStatusResponse)
        {
            var status = ParseStatusResponse(response.MessagePayload.ApplicationPayload);
            var statusResponseStatuses = status == (byte)MatterStatusCode.Success
                ? Array.Empty<WriteAttributeStatus>()
                : [new WriteAttributeStatus(attributeId, status, endpointId, clusterId)];
            return new WriteResponse(status == (byte)MatterStatusCode.Success, statusResponseStatuses, status);
        }

        if (response.MessagePayload.ProtocolOpCode != OpWriteResponse)
        {
            return new WriteResponse(
                false,
                [new WriteAttributeStatus(attributeId, (byte)MatterStatusCode.Failure, endpointId, clusterId)],
                (byte)MatterStatusCode.Failure);
        }

        if (response.MessagePayload.ApplicationPayload is null)
        {
            return new WriteResponse(true, Array.Empty<WriteAttributeStatus>());
        }

        var message = global::DotMatter.Core.Protocol.WriteResponseMessage.Deserialize(response.MessagePayload.ApplicationPayload);
        var statuses = message.WriteResponses
            .Select(status => new WriteAttributeStatus(
                status.Path?.AttributeId ?? attributeId,
                status.Status?.Status ?? (byte)MatterStatusCode.Failure,
                status.Path?.EndpointId ?? endpointId,
                status.Path?.ClusterId ?? clusterId,
                status.Status?.ClusterStatus))
            .ToArray();

        return new WriteResponse(statuses.All(status => status.StatusCode == (byte)MatterStatusCode.Success), statuses);
    }
}
