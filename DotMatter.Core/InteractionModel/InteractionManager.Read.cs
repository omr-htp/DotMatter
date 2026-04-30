using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

internal static partial class InteractionManager
{
    internal static async Task<object?> ReadAttributeAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        CancellationToken ct = default)
    {
        var exchange = session.CreateExchange();
        try
        {
            var tlv = new MatterTLV();
            tlv.AddStructure();
            tlv.AddArray(tagNumber: 0);
            tlv.AddList();
            tlv.AddUInt16(tagNumber: 2, endpointId);
            tlv.AddUInt32(tagNumber: 3, clusterId);
            tlv.AddUInt32(tagNumber: 4, attributeId);
            tlv.EndContainer();
            tlv.EndContainer();
            tlv.AddBool(3, false);
            tlv.AddUInt8(255, IMRevision);
            tlv.EndContainer();

            var response = await SendAndReceiveAsync(
                exchange,
                CreateSecuredFrame(CreatePayload(tlv, OpReadRequest)),
                ct);

            if (response.MessagePayload.ProtocolOpCode != OpReportData)
            {
                return null;
            }

            object? result = null;
            var report = new ReportDataAction(response.MessagePayload.ApplicationPayload!);
            foreach (var attributeReport in report.AttributeReports)
            {
                if (attributeReport.AttributeData != null)
                {
                    result = attributeReport.AttributeData.Data;
                }
            }

            return result;
        }
        finally
        {
            exchange.Close();
        }
    }

    internal static async Task<MatterTLV?> ReadAttributeTlvAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint attributeId,
        CancellationToken ct = default)
    {
        var exchange = session.CreateExchange();
        try
        {
            var tlv = new MatterTLV();
            tlv.AddStructure();
            tlv.AddArray(tagNumber: 0);
            tlv.AddList();
            tlv.AddUInt16(tagNumber: 2, endpointId);
            tlv.AddUInt32(tagNumber: 3, clusterId);
            tlv.AddUInt32(tagNumber: 4, attributeId);
            tlv.EndContainer();
            tlv.EndContainer();
            tlv.AddBool(3, false);
            tlv.AddUInt8(255, IMRevision);
            tlv.EndContainer();

            var response = await SendAndReceiveAsync(
                exchange,
                CreateSecuredFrame(CreatePayload(tlv, OpReadRequest)),
                ct);

            if (response.MessagePayload.ProtocolOpCode != OpReportData)
            {
                return null;
            }

            var report = new ReportDataAction(response.MessagePayload.ApplicationPayload!);
            foreach (var attributeReport in report.AttributeReports)
            {
                if (attributeReport.AttributeData?.RawData is { } rawData)
                {
                    return rawData;
                }
            }

            return null;
        }
        finally
        {
            exchange.Close();
        }
    }

    internal static async Task<IReadOnlyList<AttributeReport>> ReadAttributesAsync(
        ISession session,
        IReadOnlyList<AttributePath> paths,
        CancellationToken ct = default)
    {
        var exchange = session.CreateExchange();
        try
        {
            var tlv = new MatterTLV();
            tlv.AddStructure();
            tlv.AddArray(tagNumber: 0);

            foreach (var path in paths)
            {
                tlv.AddList();
                if (path.EndpointId.HasValue)
                {
                    tlv.AddUInt16(tagNumber: 2, path.EndpointId.Value);
                }

                if (path.ClusterId.HasValue)
                {
                    tlv.AddUInt32(tagNumber: 3, path.ClusterId.Value);
                }

                if (path.AttributeId.HasValue)
                {
                    tlv.AddUInt32(tagNumber: 4, path.AttributeId.Value);
                }

                tlv.EndContainer();
            }

            tlv.EndContainer();
            tlv.AddBool(3, false);
            tlv.AddUInt8(255, IMRevision);
            tlv.EndContainer();

            await exchange.SendAsync(CreateSecuredFrame(CreatePayload(tlv, OpReadRequest)));
            var response = await exchange.WaitForNextMessageAsync(ct);
            var results = new List<AttributeReport>();

            while (response.MessagePayload.ProtocolOpCode == OpReportData)
            {
                var report = new ReportDataAction(response.MessagePayload.ApplicationPayload!);
                foreach (var attributeReport in report.AttributeReports)
                {
                    if (attributeReport.AttributeData != null)
                    {
                        results.Add(new AttributeReport(
                            (ushort)attributeReport.AttributeData.Path.EndpointId,
                            attributeReport.AttributeData.Path.ClusterId,
                            attributeReport.AttributeData.Path.AttributeId,
                            attributeReport.AttributeData.Data,
                            attributeReport.AttributeData.DataVersion));
                    }
                }

                if (!report.MoreChunkedMessages)
                {
                    await exchange.AcknowledgeMessageAsync(response.MessageCounter);
                    break;
                }

                await exchange.SendAsync(CreateStatusResponseFrame(0x00));
                response = await exchange.WaitForNextMessageAsync(ct);
            }

            return results;
        }
        finally
        {
            exchange.Close();
        }
    }
}
