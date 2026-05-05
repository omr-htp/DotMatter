using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Core.InteractionModel;

/// <summary>
/// Represents an active Matter subscription.
/// Handles the subscribe handshake and ongoing ReportData delivery.
/// </summary>
public sealed class Subscription : IAsyncDisposable
{
    private const byte ProtocolIdIM = 0x01;
    private const byte OpSubscribeRequest = 0x03;
    private const byte OpSubscribeResponse = 0x04;
    private const byte OpReportData = 0x05;
    private const byte OpStatusResponse = 0x01;
    private const byte IMRevision = 12;

    private readonly ISession _session;
    private readonly MessageExchange _exchange;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    /// <summary>Server-assigned subscription ID.</summary>
    public uint SubscriptionId
    {
        get; private set;
    }

    /// <summary>Negotiated min reporting interval (seconds).</summary>
    public ushort MinIntervalSeconds
    {
        get; private set;
    }

    /// <summary>Negotiated max reporting interval (seconds).</summary>
    public ushort MaxIntervalSeconds
    {
        get; private set;
    }

    /// <summary>Raised when a new ReportData arrives from the device.</summary>
    public event Action<ReportDataAction>? OnReport;

    /// <summary>Raised when event reports arrive from the device.</summary>
    public event Action<IReadOnlyList<EventReportIB>>? OnEvent;

    /// <summary>Raised when the subscription is terminated (liveness timeout or error).</summary>
    public event Action<Exception?>? OnTerminated;

    /// <summary>True if actively receiving reports.</summary>
    public bool IsActive => _listenTask is { IsCompleted: false };

    private Subscription(ISession session, MessageExchange exchange)
    {
        _session = session;
        _exchange = exchange;
    }

    /// <summary>
    /// Subscribe to one or more attributes on a device.
    /// Performs the full handshake: SubscribeRequest → ReportData → StatusResponse → SubscribeResponse.
    /// </summary>
    public static async Task<Subscription> CreateAsync(
        ISession session,
        ushort endpointId,
        uint clusterId,
        uint[]? attributeIds,
        uint[]? eventIds = null,
        ushort minInterval = 1,
        ushort maxInterval = 60,
        bool fabricFiltered = false,
        CancellationToken ct = default)
    {
        var exchange = session.CreateExchange();
        var sub = new Subscription(session, exchange);

        try
        {
            // Build SubscribeRequest TLV
            var tlv = new MatterTLV();
            tlv.AddStructure();
            tlv.AddBool(0, false);              // KeepSubscriptions
            tlv.AddUInt16(1, minInterval);      // MinIntervalFloor
            tlv.AddUInt16(2, maxInterval);      // MaxIntervalCeiling

            // Tag 3: AttributeRequests[]
            tlv.AddArray(tagNumber: 3);
            if (attributeIds == null || attributeIds.Length == 0)
            {
                // Wildcard: subscribe to all attributes on this endpoint/cluster
                tlv.AddList();
                tlv.AddUInt16(tagNumber: 2, endpointId);
                tlv.AddUInt32(tagNumber: 3, clusterId);
                tlv.EndContainer();
            }
            else
            {
                foreach (uint attrId in attributeIds)
                {
                    tlv.AddList();
                    tlv.AddUInt16(tagNumber: 2, endpointId);
                    tlv.AddUInt32(tagNumber: 3, clusterId);
                    tlv.AddUInt32(tagNumber: 4, attrId);
                    tlv.EndContainer();
                }
            }
            tlv.EndContainer();                 // /AttributeRequests

            // Tag 4: EventRequests[] (optional)
            if (eventIds != null)
            {
                tlv.AddArray(tagNumber: 4);
                foreach (uint evtId in eventIds)
                {
                    tlv.AddList();
                    tlv.AddUInt16(tagNumber: 1, endpointId);
                    tlv.AddUInt32(tagNumber: 2, clusterId);
                    tlv.AddUInt32(tagNumber: 3, evtId);
                    tlv.EndContainer();
                }
                tlv.EndContainer();
            }

            tlv.AddBool(7, fabricFiltered);     // IsFabricFiltered (tag 7 per spec)
            tlv.AddUInt8(255, IMRevision);
            tlv.EndContainer();                 // /Root

            var payload = new MessagePayload(tlv);
            payload.ExchangeFlags |= ExchangeFlags.Initiator;
            payload.ProtocolId = ProtocolIdIM;
            payload.ProtocolOpCode = OpSubscribeRequest;
            var frame = new MessageFrame(payload);
            frame.MessageFlags |= MessageFlags.S;
            frame.SecurityFlags = 0x00;
            frame.SourceNodeID = 0x00;

            await exchange.SendAsync(frame);

            var primingReports = new List<ReportDataAction>();
            var subRespFrame = await WaitForSubscribeResponseAsync(exchange, primingReports, ct);

            if (subRespFrame.MessagePayload.ApplicationPayload is { } subTlv)
            {
                subTlv.OpenStructure();
                if (subTlv.IsNextTag(0))
                {
                    sub.SubscriptionId = (uint)subTlv.GetUnsignedInt(0);
                }

                if (subTlv.IsNextTag(2))
                {
                    sub.MaxIntervalSeconds = (ushort)subTlv.GetUnsignedInt(2);
                }
            }
            sub.MinIntervalSeconds = minInterval;

            sub.DispatchReports(primingReports);

            // Start background listener for ongoing reports
            sub._cts = new CancellationTokenSource();
            sub._listenTask = sub.ListenForReportsAsync(sub._cts.Token);

            return sub;
        }
        catch
        {
            exchange.Close();
            throw;
        }
    }

    /// <summary>
    /// Subscribe to attributes across multiple clusters in a single subscription request.
    /// The handshake exchange is closed after setup; use <see cref="ProcessIncomingBytesAsync"/>
    /// from the connection's UnroutedMessages channel for ongoing reports.
    /// </summary>
    internal static async Task<Subscription> CreateMultiAsync(
        ISession session,
        IReadOnlyList<AttributePath> attributePaths,
        IReadOnlyList<EventPath>? eventPaths = null,
        ushort minInterval = 1,
        ushort maxInterval = 60,
        bool keepSubscriptions = true,
        bool fabricFiltered = false,
        CancellationToken ct = default)
    {
        if ((attributePaths == null || attributePaths.Count == 0) && (eventPaths == null || eventPaths.Count == 0))
        {
            throw new ArgumentException("At least one attribute or event path is required.");
        }

        var exchange = session.CreateExchange();
        var sub = new Subscription(session, exchange);

        try
        {
            var tlv = new MatterTLV();
            tlv.AddStructure();
            tlv.AddBool(0, keepSubscriptions);
            tlv.AddUInt16(1, minInterval);
            tlv.AddUInt16(2, maxInterval);

            // Tag 3: AttributeRequests[]
            if (attributePaths is { Count: > 0 })
            {
                tlv.AddArray(tagNumber: 3);
                foreach (var path in attributePaths)
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
            }

            // Tag 4: EventRequests[] (optional)
            if (eventPaths is { Count: > 0 })
            {
                tlv.AddArray(tagNumber: 4);
                foreach (var ep in eventPaths)
                {
                    tlv.AddList();
                    if (ep.EndpointId.HasValue)
                    {
                        tlv.AddUInt16(tagNumber: 1, ep.EndpointId.Value);
                    }

                    if (ep.ClusterId.HasValue)
                    {
                        tlv.AddUInt32(tagNumber: 2, ep.ClusterId.Value);
                    }

                    if (ep.EventId.HasValue)
                    {
                        tlv.AddUInt32(tagNumber: 3, ep.EventId.Value);
                    }

                    tlv.EndContainer();
                }
                tlv.EndContainer();
            }

            tlv.AddBool(7, fabricFiltered);     // IsFabricFiltered (tag 7 per spec)
            tlv.AddUInt8(255, IMRevision);
            tlv.EndContainer();

            var payload = new MessagePayload(tlv);
            payload.ExchangeFlags |= ExchangeFlags.Initiator;
            payload.ProtocolId = ProtocolIdIM;
            payload.ProtocolOpCode = OpSubscribeRequest;
            var frame = new MessageFrame(payload);
            frame.MessageFlags |= MessageFlags.S;
            frame.SecurityFlags = 0x00;
            frame.SourceNodeID = 0x00;

            await exchange.SendAsync(frame);

            var primingReports = new List<ReportDataAction>();
            var subRespFrame = await WaitForSubscribeResponseAsync(exchange, primingReports, ct);

            if (subRespFrame.MessagePayload.ApplicationPayload is { } subTlv)
            {
                subTlv.OpenStructure();
                if (subTlv.IsNextTag(0))
                {
                    sub.SubscriptionId = (uint)subTlv.GetUnsignedInt(0);
                }

                if (subTlv.IsNextTag(2))
                {
                    sub.MaxIntervalSeconds = (ushort)subTlv.GetUnsignedInt(2);
                }
            }
            sub.MinIntervalSeconds = minInterval;

            sub.DispatchReports(primingReports);

            // Close handshake exchange— ongoing reports arrive on new device-initiated exchanges
            // via the connection's UnroutedMessages channel. Use ProcessIncomingBytesAsync() to handle them.
            exchange.Close();

            return sub;
        }
        catch
        {
            exchange.Close();
            throw;
        }
    }

    /// <summary>
    /// Process raw bytes from the connection's UnroutedMessages channel.
    /// If the message is a ReportData for this session, sends StatusResponse and fires <see cref="OnReport"/>.
    /// </summary>
    /// <returns>True if the message was a ReportData and was processed.</returns>
    public async Task<bool> ProcessIncomingBytesAsync(byte[] bytes)
    {
        try
        {
            var parts = new MessageFrameParts(bytes);
            var msgFrame = _session.Decode(parts);

            if (msgFrame.MessagePayload.ProtocolId != ProtocolIdIM ||
                msgFrame.MessagePayload.ProtocolOpCode != OpReportData)
            {
                return false;
            }

            // Send StatusResponse on the device-initiated exchange
            await SendUnsolicitedStatusResponseAsync(
                msgFrame.MessagePayload.ExchangeID,
                msgFrame.MessageCounter);

            if (msgFrame.MessagePayload.ApplicationPayload is not { } payload)
            {
                return true;
            }

            ReportDataAction report;
            try
            {
                report = new ReportDataAction(payload);
            }
            catch (Exception ex)
            {
                MatterLog.Warn(
                    "Failed to parse unsolicited ReportData [E:{0}] after acking it: {1}",
                    msgFrame.MessagePayload.ExchangeID,
                    ex.Message);
                return true;
            }

            if (report.AttributeReports.Count == 0 && report.EventReports.Count == 0)
            {
                MatterLog.Debug(
                    "Received unsolicited ReportData with no attribute/event entries [E:{0}] TLV={1}",
                    msgFrame.MessagePayload.ExchangeID,
                    MatterLog.FormatBytes(payload.AsSpan()));
            }

            DispatchReport(report);

            return true;
        }
        catch (Exception ex)
        {
            MatterLog.Warn("Failed to process incoming subscription bytes: {0}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Send a StatusResponse for an unsolicited (device-initiated) report.
    /// Unlike the handshake StatusResponse, this uses the device's exchange ID and
    /// keeps the response reliable on CASE sessions so the peer accepts it as a normal IM response.
    /// </summary>
    private async Task SendUnsolicitedStatusResponseAsync(ushort exchangeId, uint ackCounter)
    {
        var frame = CreateUnsolicitedStatusResponseFrame(_session, exchangeId, ackCounter);
        MatterLog.Info(">>> Sending unsolicited StatusResponse [E:{0}] ACK counter={1}", exchangeId, ackCounter);
        var msgBytes = _session.Encode(frame);
        await _session.SendAsync(msgBytes);
    }

    internal static MessageFrame CreateUnsolicitedStatusResponseFrame(ISession session, ushort exchangeId, uint ackCounter)
    {
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddUInt8(0, 0x00);          // Status = SUCCESS
        tlv.AddUInt8(255, IMRevision);
        tlv.EndContainer();

        var payload = new MessagePayload(tlv)
        {
            ExchangeFlags = ExchangeFlags.Acknowledgement |
                            (session.UseMRP ? ExchangeFlags.Reliability : 0),
            ExchangeID = exchangeId,
            AcknowledgedMessageCounter = ackCounter,
            ProtocolId = ProtocolIdIM,
            ProtocolOpCode = OpStatusResponse
        };

        var frame = new MessageFrame(payload);
        frame.MessageFlags |= MessageFlags.S;
        frame.SecurityFlags = 0x00;
        frame.SessionID = session.PeerSessionId;
        frame.SourceNodeID = session.SourceNodeId;
        frame.DestinationNodeId = session.DestinationNodeId;
        frame.MessageCounter = session.MessageCounter;
        return frame;
    }

    private async Task ListenForReportsAsync(CancellationToken ct)
    {
        try
        {
            // Liveness timeout: spec says 1.5 * maxInterval + padding
            var livenessTimeout = TimeSpan.FromSeconds(MaxIntervalSeconds * 1.5 + 10);

            while (!ct.IsCancellationRequested)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(livenessTimeout);

                MessageFrame reportFrame;
                try
                {
                    reportFrame = await _exchange.WaitForNextMessageAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    OnTerminated?.Invoke(new TimeoutException("Subscription liveness timeout"));
                    return;
                }

                if (reportFrame.MessagePayload.ProtocolOpCode != OpReportData)
                {
                    continue;
                }

                ReportDataAction? report = null;
                if (reportFrame.MessagePayload.ApplicationPayload is { } tlv)
                {
                    report = new ReportDataAction(tlv);
                }

                // Ack the report with StatusResponse
                await SendStatusResponseAsync(_exchange, 0);

                if (report != null)
                {
                    DispatchReport(report);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            OnTerminated?.Invoke(ex);
        }
    }

    private static async Task<MessageFrame> WaitForSubscribeResponseAsync(
        MessageExchange exchange,
        List<ReportDataAction> primingReports,
        CancellationToken ct)
    {
        while (true)
        {
            var frame = await exchange.WaitForNextMessageAsync(ct);
            switch (frame.MessagePayload.ProtocolOpCode)
            {
                case OpReportData:
                    if (frame.MessagePayload.ApplicationPayload is { } reportTlv)
                    {
                        primingReports.Add(new ReportDataAction(reportTlv));
                    }

                    await SendStatusResponseAsync(exchange, 0);
                    continue;

                case OpSubscribeResponse:
                    await exchange.AcknowledgeMessageAsync(frame.MessageCounter);
                    return frame;

                default:
                    throw new InvalidOperationException(BuildUnexpectedSubscribeHandshakeDetail(frame));
            }
        }
    }

    private void DispatchReports(IEnumerable<ReportDataAction> reports)
    {
        foreach (var report in reports)
        {
            DispatchReport(report);
        }
    }

    private void DispatchReport(ReportDataAction report)
    {
        OnReport?.Invoke(report);
        if (report.EventReports.Count > 0)
        {
            OnEvent?.Invoke(report.EventReports);
        }
    }

    private static string BuildUnexpectedSubscribeHandshakeDetail(MessageFrame frame)
    {
        string detail =
            $"Expected ReportData (0x{OpReportData:X2}) or SubscribeResponse (0x{OpSubscribeResponse:X2}), got 0x{frame.MessagePayload.ProtocolOpCode:X2}";
        if (frame.MessagePayload.ProtocolOpCode == OpStatusResponse &&
            frame.MessagePayload.ApplicationPayload is { } errTlv)
        {
            try
            {
                errTlv.OpenStructure();
                byte status = errTlv.IsNextTag(0) ? errTlv.GetUnsignedInt8(0) : (byte)0xFF;
                detail += $" (status=0x{status:X2})";
            }
            catch
            {
            }
        }

        return detail;
    }

    private static async Task SendStatusResponseAsync(MessageExchange exchange, byte status)
    {
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddUInt8(0, status);            // Status
        tlv.AddUInt8(255, IMRevision);
        tlv.EndContainer();

        var payload = new MessagePayload(tlv)
        {
            ProtocolId = ProtocolIdIM,
            ProtocolOpCode = OpStatusResponse
        };
        var frame = new MessageFrame(payload);
        frame.MessageFlags |= MessageFlags.S;
        frame.SecurityFlags = 0x00;
        frame.SourceNodeID = 0x00;

        await exchange.SendAsync(frame);
    }

    /// <summary>DisposeAsync.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            if (_listenTask != null)
            {
                try
                {
                    await _listenTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
            _cts.Dispose();
        }
        _exchange.Close();
    }
}
