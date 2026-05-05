using System.Security.Cryptography;

namespace DotMatter.Core.Sessions;

/// <summary>
/// Represents an unauthenticated Matter session used before secure session establishment.
/// </summary>
public class UnsecureSession : ISession
{
    private readonly IConnection _connection;
    private readonly Lock _exchangeLock = new();
    private readonly HashSet<ushort> _activeExchangeIds = [];

    private static uint _messageCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsecureSession"/> class.
    /// </summary>
    /// <param name="connection">Transport connection used by the session.</param>
    public UnsecureSession(IConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Interlocked.Exchange(ref _messageCounter, BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)));

        SessionId = 0x00;
        PeerSessionId = 0x00;
    }

    /// <inheritdoc />
    public IConnection Connection => _connection;

    /// <inheritdoc />
    public IConnection CreateNewConnection()
    {
        return _connection.OpenConnection();
    }

    /// <inheritdoc />
    public ulong SourceNodeId
    {
        get;
    }

    /// <inheritdoc />
    public ulong DestinationNodeId
    {
        get;
    }

    /// <inheritdoc />
    public ushort SessionId
    {
        get; set;
    }

    /// <inheritdoc />
    public ushort PeerSessionId
    {
        get;
    }

    /// <inheritdoc />
    public bool UseMRP => false;

    /// <inheritdoc />
    public uint MessageCounter => Interlocked.Increment(ref _messageCounter);

    /// <inheritdoc />
    public void Close()
    {
        _connection.Close();
    }

    /// <inheritdoc />
    public MessageExchange CreateExchange()
    {
        ushort exchangeId;
        lock (_exchangeLock)
        {
            do
            {
                exchangeId = BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(2));
            } while (!_activeExchangeIds.Add(exchangeId));
        }

        MatterLog.Info("Created unsecure exchange {ExchangeId}.", exchangeId);
        return new MessageExchange(exchangeId, this);
    }

    internal void ReleaseExchangeId(ushort exchangeId)
    {
        lock (_exchangeLock)
        {
            _activeExchangeIds.Remove(exchangeId);
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(byte[] message)
    {
        await _connection.SendAsync(message);
    }

    /// <inheritdoc />
    public async Task<byte[]> ReadAsync(CancellationToken cancellationToken)
    {
        return await _connection.ReadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public byte[] Encode(MessageFrame messageFrame)
    {
        var parts = new MessageFrameParts(messageFrame);
        return [.. parts.Header, .. parts.MessagePayload];
    }

    /// <inheritdoc />
    public MessageFrame Decode(MessageFrameParts parts)
    {
        var messageFrame = parts.MessageFrameWithHeaders();
        messageFrame.MessagePayload = new MessagePayload(parts.MessagePayload);
        return messageFrame;
    }
}
