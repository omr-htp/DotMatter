using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace DotMatter.Core;

/// <summary>Message header fields used to route received UDP payloads.</summary>
public readonly record struct UdpMessageHeader(
    ushort SessionId,
    ushort ExchangeId,
    uint Counter,
    bool Reliable,
    bool Initiator);

/// <summary>UdpConnection class.</summary>
public class UdpConnection : IConnection, IDisposable
{
    private readonly record struct UdpExchangeRoute(ushort ExchangeId, bool IncomingInitiator);

    private UdpClient? _udpClient;
    private readonly IPAddress _ipAddress;
    private readonly ushort _port;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _closeLock = new();
    private readonly Task _receiveTask;
    private bool _disposed;

    // Exchange-based routing: exchange IDs are scoped by the peer's initiator bit.
    private readonly ConcurrentDictionary<UdpExchangeRoute, Channel<byte[]>> _exchangeChannels = new();

    // Fallback channel for messages with no registered exchange
    private readonly Channel<byte[]> _unroutedChannel = Channel.CreateBounded<byte[]>(20);

    // ACK table: ExchangeID -> counter to ACK (piggybacked on next outgoing message)
    private readonly ConcurrentDictionary<ushort, uint> _pendingAcks = new();

    // Header parser set by the session for routing
    private Func<byte[], UdpMessageHeader>? _headerParser;

    /// <summary>ConnectionClosed.</summary>
    /// <summary>Raised when ConnectionClosed occurs.</summary>
    public event EventHandler? ConnectionClosed;

    /// <summary>AcknowledgementReceived.</summary>
    public SemaphoreSlim AcknowledgementReceived { get; init; } = new SemaphoreSlim(0);

    /// <summary>UdpConnection.</summary>
    public UdpConnection(IPAddress address, ushort port)
    {
        _ipAddress = address;
        _port = port;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            _udpClient = new UdpClient(0, AddressFamily.InterNetwork);
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _udpClient = new UdpClient(0, AddressFamily.InterNetworkV6);
            // Link-local addresses need a scope ID to route correctly
            if (address.IsIPv6LinkLocal && address.ScopeId == 0)
            {
                // Try to find a valid scope ID from available network interfaces
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    {
                        continue;
                    }

                    var props = ni.GetIPProperties();
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetworkV6
                            && ua.Address.IsIPv6LinkLocal)
                        {
                            _ipAddress = new IPAddress(address.GetAddressBytes(), ua.Address.ScopeId);
                            MatterLog.Info("UDP link-local scope: {0}%{1}", _ipAddress, ua.Address.ScopeId);
                            goto scopeSet;
                        }
                    }
                }
            scopeSet:
                ;
            }
        }

        _receiveTask = Task.Run(ReceiveLoop);
    }

    /// <summary>
    /// Register a header parser so the receive loop can route by ExchangeID.
    /// </summary>
    public void SetHeaderParser(Func<byte[], UdpMessageHeader> parser)
    {
        Volatile.Write(ref _headerParser, parser);
    }

    /// <summary>
    /// Register an exchange channel. Returns the channel to read from.
    /// </summary>
    public Channel<byte[]> RegisterExchange(ushort exchangeId, bool incomingInitiator = false)
    {
        var ch = Channel.CreateBounded<byte[]>(10);
        _exchangeChannels[new UdpExchangeRoute(exchangeId, incomingInitiator)] = ch;
        return ch;
    }

    /// <summary>
    /// Unregister an exchange channel.
    /// </summary>
    public void UnregisterExchange(ushort exchangeId, bool incomingInitiator = false)
    {
        _exchangeChannels.TryRemove(new UdpExchangeRoute(exchangeId, incomingInitiator), out _);
    }

    /// <summary>
    /// Get any pending ACK counter for this exchange (for piggybacking).
    /// </summary>
    public uint? ConsumePendingAck(ushort exchangeId)
    {
        if (_pendingAcks.TryRemove(exchangeId, out var counter))
        {
            return counter;
        }

        return null;
    }

    /// <summary>
    /// Read from the unrouted channel (subscription reports, unsolicited messages).
    /// </summary>
    public ChannelReader<byte[]> UnroutedMessages => _unroutedChannel.Reader;

    /// <summary>
    /// True when a header parser is registered and exchange-based routing is active.
    /// </summary>
    public bool IsRoutingEnabled => _headerParser != null;

    /// <summary>OpenConnection.</summary>
    public IConnection OpenConnection() => new UdpConnection(_ipAddress, _port);

    /// <summary>Close.</summary>
    public void Close()
    {
        Task receiveTask;
        Channel<byte[]>[] exchangeChannels;
        lock (_closeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cts.Cancel();
            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;
            receiveTask = _receiveTask;
        }

        _unroutedChannel.Writer.TryComplete();
        exchangeChannels = _exchangeChannels.Values.ToArray();
        foreach (var channel in exchangeChannels)
        {
            channel.Writer.TryComplete();
        }
        _exchangeChannels.Clear();

        try
        {
            receiveTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException or ObjectDisposedException))
        {
        }

        _cts.Dispose();
    }

    /// <summary>Dispose.</summary>
    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    /// <summary>IsConnectionEstablished.</summary>
    public bool IsConnectionEstablished
    {
        get
        {
            lock (_closeLock)
            {
                return _udpClient != null && !_disposed;
            }
        }
    }

    private async Task ReceiveLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                UdpClient? client;
                lock (_closeLock)
                {
                    client = _udpClient;
                }
                if (client == null)
                {
                    break;
                }

                var result = await client.ReceiveAsync(_cts.Token);
                var bytes = result.Buffer;

                var headerParser = Volatile.Read(ref _headerParser);
                if (headerParser != null)
                {
                    try
                    {
                        var header = headerParser(bytes);

                        if (header.Reliable)
                        {
                            _pendingAcks[header.ExchangeId] = header.Counter;
                        }

                        if (_exchangeChannels.TryGetValue(new UdpExchangeRoute(header.ExchangeId, header.Initiator), out var ch))
                        {
                            ch.Writer.TryWrite(bytes);
                        }
                        else
                        {
                            _unroutedChannel.Writer.TryWrite(bytes);
                        }
                    }
                    catch
                    {
                        _unroutedChannel.Writer.TryWrite(bytes);
                    }
                }
                else
                {
                    _unroutedChannel.Writer.TryWrite(bytes);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MatterLog.Warn("UdpConnection receive error: {0}", ex.Message);
            ConnectionClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Legacy ReadAsync for unrouted messages (PASE/CASE handshake, monitoring).
    /// </summary>
    public async Task<byte[]> ReadAsync(CancellationToken token)
    {
        return await _unroutedChannel.Reader.ReadAsync(token);
    }

    /// <summary>SendAsync.</summary>
    public async Task SendAsync(byte[] bytes)
    {
        UdpClient? client;
        lock (_closeLock)
        {
            client = _udpClient;
        }

        ObjectDisposedException.ThrowIf(client == null, nameof(UdpConnection));

        MatterLog.Debug("UDP SEND [{0} bytes]", bytes.Length);
        await client.SendAsync(bytes, bytes.Length, new IPEndPoint(_ipAddress, _port));
    }
}
