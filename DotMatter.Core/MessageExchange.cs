using DotMatter.Core.Sessions;
using System.Threading.Channels;

namespace DotMatter.Core;

/// <summary>MessageExchange class.</summary>
public class MessageExchange
{
    private readonly ushort _exchangeId;
    private readonly ISession _session;
    private readonly Channel<byte[]>? _routedChannel;
    private readonly bool _incomingInitiator;
    private readonly CancellationTokenSource _cts = new();

    private readonly MessageCounterWindow _counterWindow = new();
    private uint _lastReceivedCounter;
    private bool _hasReceived;
    private uint _acknowledgedMessageCounter;
    private TaskCompletionSource? _ackTcs;
    private bool _isInitiator;

    /// <summary>MRP idle retransmission timeout (spec default 500ms).</summary>
    public static TimeSpan MrpIdleRetransTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>MRP active retransmission timeout (spec default 300ms).</summary>
    public static TimeSpan MrpActiveRetransTimeout { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Default timeout for WaitForNextMessageAsync when no external CT is provided.</summary>
    public static TimeSpan ExchangeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Max MRP retransmission attempts (spec default 5).</summary>
    public static int MrpMaxRetransmissions { get; set; } = 5;

    /// <summary>ExchangeId.</summary>
    /// <summary>The ExchangeId value.</summary>
    public ushort ExchangeId => _exchangeId;

    /// <summary>MessageExchange.</summary>
    public MessageExchange(ushort exchangeId, ISession session)
    {
        _exchangeId = exchangeId;
        _session = session;
        _incomingInitiator = false;

        // Only register for exchange-based routing if the connection supports it
        if (_session.Connection is UdpConnection udp && udp.IsRoutingEnabled)
        {
            _routedChannel = udp.RegisterExchange(exchangeId, _incomingInitiator);
        }
    }

    /// <summary>Close.</summary>
    public void Close()
    {
        _cts.Cancel();

        if (_routedChannel != null && _session.Connection is UdpConnection udp)
        {
            udp.UnregisterExchange(_exchangeId, _incomingInitiator);
        }

        MatterLog.Info("Closed MessageExchange [E: {0}]", _exchangeId);
    }

    /// <summary>SendAsync.</summary>
    public async Task SendAsync(MessageFrame message)
    {
        message.SessionID = _session.PeerSessionId;
        message.SourceNodeID = _session.SourceNodeId;
        message.DestinationNodeId = _session.DestinationNodeId;
        message.MessagePayload.ExchangeID = _exchangeId;
        message.MessageCounter = _session.MessageCounter;

        // Once the first outgoing message sets the Initiator flag, all subsequent
        // messages on this exchange must also carry it (Matter spec 4.10.2).
        if (message.MessagePayload.ExchangeFlags.HasFlag(ExchangeFlags.Initiator))
        {
            _isInitiator = true;
        }

        if (_isInitiator)
        {
            message.MessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
        }

        // Piggyback any pending ACK
        if (_hasReceived && _acknowledgedMessageCounter != _lastReceivedCounter)
        {
            _acknowledgedMessageCounter = _lastReceivedCounter;
            message.MessagePayload.ExchangeFlags |= ExchangeFlags.Acknowledgement;
            message.MessagePayload.AcknowledgedMessageCounter = _acknowledgedMessageCounter;
            if (_session.Connection is UdpConnection udp2)
            {
                udp2.ConsumePendingAck(_exchangeId);
            }
        }
        else if (_session.Connection is UdpConnection udp)
        {
            var pendingAck = udp.ConsumePendingAck(_exchangeId);
            if (pendingAck.HasValue)
            {
                message.MessagePayload.ExchangeFlags |= ExchangeFlags.Acknowledgement;
                message.MessagePayload.AcknowledgedMessageCounter = pendingAck.Value;
            }
        }

        // MRP Reliability flag: set on all messages EXCEPT StandaloneAck (spec 4.11.2)
        bool isStandaloneAck = message.MessagePayload.ProtocolId == 0x00
                            && message.MessagePayload.ProtocolOpCode == 0x10;
        if (_session.UseMRP && !isStandaloneAck)
        {
            message.MessagePayload.ExchangeFlags |= ExchangeFlags.Reliability;
        }

        MatterLog.Info(" >>> Sending Message {0}", message.DebugInfo());

        var bytes = _session.Encode(message);
        await _session.SendAsync(bytes);

        // MRP: run retransmits in background so WaitForNextMessageAsync can read
        // the response concurrently (it sets _ackReceived to stop retransmits)
        if (_session.UseMRP && message.MessagePayload.ExchangeFlags.HasFlag(ExchangeFlags.Reliability)
            && message.MessagePayload.ProtocolOpCode != 0x10)
        {
            _ackTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = MrpRetransmitAsync(bytes, message.MessageCounter, _ackTcs.Task);
        }
    }

    /// <summary>
    /// Background MRP retransmitter. Runs concurrently with WaitForNextMessageAsync.
    /// Stops immediately when <paramref name="ackTask"/> completes (ACK received).
    /// </summary>
    private async Task MrpRetransmitAsync(byte[] bytes, uint messageCounter, Task ackTask)
    {
        var baseTimeout = _session.PeerMrpActiveRetransTimeout ?? MrpActiveRetransTimeout;
        for (int attempt = 1; attempt <= MrpMaxRetransmissions; attempt++)
        {
            var timeout = baseTimeout * Math.Pow(1.6, attempt - 1);
            var delayTask = Task.Delay(timeout, _cts.Token);
            var completed = await Task.WhenAny(delayTask, ackTask);

            if (completed == ackTask || _cts.IsCancellationRequested)
            {
                return;
            }

            // Ensure the delay actually completed (not just WhenAny selecting it first)
            try
            {
                await delayTask;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            MatterLog.Info("MRP retransmit attempt {0}/{1} for counter {2}",
                attempt, MrpMaxRetransmissions, messageCounter);
            try
            {
                await _session.SendAsync(bytes);
            }
            catch
            {
                return;
            }
        }
    }

    /// <summary>Wait for the next message on this exchange's channel.</summary>
    public async Task<MessageFrame> WaitForNextMessageAsync(CancellationToken externalCt = default)
    {
        MatterLog.Info("Waiting for incoming message...");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, externalCt);
        if (!externalCt.CanBeCanceled)
        {
            linked.CancelAfter(ExchangeTimeout);
        }

        while (!linked.IsCancellationRequested)
        {
            byte[] bytes;

            if (_routedChannel != null)
            {
                bytes = await _routedChannel.Reader.ReadAsync(linked.Token);
            }
            else
            {
                bytes = await _session.ReadAsync(linked.Token);
            }

            try
            {
                var parts = new MessageFrameParts(bytes);
                var header = parts.MessageFrameWithHeaders();

                if (header.SessionID != _session.SessionId)
                {
                    continue;
                }

                var messageFrame = _session.Decode(parts);

                MatterLog.Info(" <<< Received Message {0}", messageFrame.DebugInfo());

                // Sliding window duplicate/replay detection (Matter spec §4.7.2)
                if (!_counterWindow.Validate(messageFrame.MessageCounter))
                {
                    MatterLog.Info("Message {0} is a duplicate/replay. Dropping.", messageFrame.MessageCounter);
                    continue;
                }

                _lastReceivedCounter = messageFrame.MessageCounter;
                _hasReceived = true;

                // Signal ACK received for MRP retransmit cancellation
                if (messageFrame.MessagePayload.ExchangeFlags.HasFlag(ExchangeFlags.Acknowledgement))
                {
                    _ackTcs?.TrySetResult();
                }

                // Standalone ACK - consume silently
                if (messageFrame.MessagePayload.ProtocolId == 0x00 &&
                    messageFrame.MessagePayload.ProtocolOpCode == 0x10)
                {
                    MatterLog.Debug("Standalone ACK for {0}", messageFrame.MessagePayload.AcknowledgedMessageCounter);
                    continue;
                }

                return messageFrame;
            }
            catch (Exception ex)
            {
                MatterLog.Warn("Exchange [{0}] decode error: {1}", _exchangeId, ex.Message);
            }
        }

        throw new OperationCanceledException();
    }

    /// <summary>AcknowledgeMessageAsync.</summary>
    public async Task AcknowledgeMessageAsync(uint messageCounter)
    {
        var payload = new MessagePayload();
        payload.ExchangeFlags |= ExchangeFlags.Acknowledgement;
        payload.ExchangeFlags |= ExchangeFlags.Initiator;
        payload.ExchangeID = _exchangeId;
        payload.AcknowledgedMessageCounter = messageCounter;
        payload.ProtocolId = 0x00; // Secure Channel
        payload.ProtocolOpCode = 0x10; // MRP Standalone Acknowledgement

        var messageFrame = new MessageFrame(payload);
        messageFrame.MessageFlags |= MessageFlags.S;
        messageFrame.SecurityFlags = 0x00;
        // Don't pre-set SessionID/SourceNodeID/MessageCounter here;
        // SendAsync sets them from the session (avoids double MessageCounter consumption)

        await SendAsync(messageFrame);
    }
}
