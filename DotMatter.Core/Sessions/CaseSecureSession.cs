using System.Security.Cryptography;

namespace DotMatter.Core.Sessions;

/// <summary>
/// Represents a CASE secure session used for operational device communication.
/// </summary>
public class CaseSecureSession : ISession
{
    private readonly IConnection _connection;
    private readonly byte[] _encryptionKey;
    private readonly byte[] _decryptionKey;
    private readonly Lock _exchangeLock = new();
    private readonly HashSet<ushort> _activeExchangeIds = [];
    private uint _messageCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="CaseSecureSession"/> class.
    /// </summary>
    public CaseSecureSession(
        IConnection connection,
        ulong sourceNodeId,
        ulong destinationNodeId,
        ushort sessionId,
        ushort peerSessionId,
        byte[] encryptionKey,
        byte[] decryptionKey)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _encryptionKey = encryptionKey ?? throw new ArgumentNullException(nameof(encryptionKey));
        _decryptionKey = decryptionKey ?? throw new ArgumentNullException(nameof(decryptionKey));

        SourceNodeId = sourceNodeId;
        DestinationNodeId = destinationNodeId;

        SessionId = sessionId;
        PeerSessionId = peerSessionId;

        _messageCounter = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));

        MatterLog.Info("Created CASE secure session {SessionId}.", SessionId);

        if (_connection is UdpConnection udp)
        {
            udp.SetHeaderParser(ParseHeader);
        }
    }

    /// <summary>
    /// Parse the unencrypted message header to extract ExchangeID for routing.
    /// The exchange payload header is encrypted, but we can decrypt it here
    /// since we have the keys.
    /// </summary>
    private UdpMessageHeader ParseHeader(byte[] raw)
    {
        if (raw.Length < 8)
        {
            return new UdpMessageHeader(0, 0, 0, false, false);
        }

        // Message header format:
        // [0] MessageFlags
        // [1-2] SessionID
        // [3] SecurityFlags
        // [4-7] MessageCounter
        // [8-15] SourceNodeID (if S flag set)
        // Then encrypted payload...

        var flags = (MessageFlags)raw[0];
        ushort sessionId = BitConverter.ToUInt16(raw, 1);
        uint counter = BitConverter.ToUInt32(raw, 4);

        int headerLen = 8;
        if ((flags & MessageFlags.S) != 0)
        {
            headerLen += 8; // SourceNodeID
        }

        var dsiz = flags.GetDsiz();
        if (dsiz == 1 || dsiz == 3)
        {
            headerLen += 8;
        }
        else if (dsiz == 2)
        {
            headerLen += 2;
        }

        if (headerLen > raw.Length)
        {
            return new UdpMessageHeader(sessionId, 0, counter, false, false);
        }

        // Need to decrypt the payload to get ExchangeID
        var header = raw.AsSpan(0, headerLen).ToArray();
        var encPayload = raw.AsSpan(headerLen).ToArray();

        if (encPayload.Length < 17) // min: 1+1+2+2 exchange header + 16 tag
        {
            return new UdpMessageHeader(sessionId, 0, counter, false, false);
        }

        // Build nonce: SecurityFlags(1) + Counter(4) + SourceNodeId(8)
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(raw[3]); // SecurityFlags
        w.Write(BitConverter.GetBytes(counter));
        w.Write(BitConverter.GetBytes(DestinationNodeId)); // source is the peer
        var nonce = ms.ToArray();

        byte[] decrypted = new byte[encPayload.Length - 16];
        var ciphertext = encPayload.AsSpan(0, encPayload.Length - 16);
        var tag = encPayload.AsSpan(encPayload.Length - 16, 16);

        try
        {
            var aes = new AesCcm(_decryptionKey);
            aes.Decrypt(nonce, ciphertext, tag, decrypted, header);
        }
        catch
        {
            // Decryption failed - might be for a different session
            return new UdpMessageHeader(sessionId, 0, counter, false, false);
        }

        // Exchange header: ExchangeFlags(1) + OpCode(1) + ExchangeID(2) + ...
        var exchangeFlags = (ExchangeFlags)decrypted[0];
        ushort exchangeId = BitConverter.ToUInt16(decrypted, 2);
        bool reliable = (exchangeFlags & ExchangeFlags.Reliability) != 0;
        bool initiator = (exchangeFlags & ExchangeFlags.Initiator) != 0;

        return new UdpMessageHeader(sessionId, exchangeId, counter, reliable, initiator);
    }

    /// <inheritdoc />
    public void Close()
    {
        _connection.Close();
    }

    /// <inheritdoc />
    public IConnection CreateNewConnection()
    {
        return _connection.OpenConnection();
    }

    /// <inheritdoc />
    public IConnection Connection => _connection;

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
        get;
    }

    /// <inheritdoc />
    public ushort PeerSessionId
    {
        get;
    }

    /// <inheritdoc />
    public bool UseMRP => true;

    /// <inheritdoc />
    public TimeSpan? PeerMrpIdleRetransTimeout
    {
        get; set;
    }

    /// <inheritdoc />
    public TimeSpan? PeerMrpActiveRetransTimeout
    {
        get; set;
    }

    /// <inheritdoc />
    public uint MessageCounter
    {
        get
        {
            var next = Interlocked.Increment(ref _messageCounter);
            if (next == 0)
            {
                Close();
                throw new MatterSessionException("CASE message counter overflow; session closed to prevent nonce reuse.");
            }

            return next;
        }
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

        MatterLog.Info("Created CASE exchange {ExchangeId}.", exchangeId);
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

        // Nonce = SecurityFlags(1) + MessageCounter(4) + SourceNodeId(8)
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)messageFrame.SecurityFlags);
        w.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
        w.Write(BitConverter.GetBytes(messageFrame.SourceNodeID));
        var nonce = ms.ToArray();

        var additionalData = parts.Header;

        byte[] encryptedPayload = new byte[parts.MessagePayload.Length];
        byte[] tag = new byte[16];

        var encryptor = new AesCcm(_encryptionKey);
        encryptor.Encrypt(nonce, parts.MessagePayload, encryptedPayload, tag, additionalData);

        var totalPayload = encryptedPayload.Concat(tag);
        return [.. parts.Header, .. totalPayload];
    }

    /// <inheritdoc />
    public MessageFrame Decode(MessageFrameParts parts)
    {
        var messageFrame = parts.MessageFrameWithHeaders();

        // Nonce = SecurityFlags(1) + MessageCounter(4) + SourceNodeId(8)
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)messageFrame.SecurityFlags);
        w.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
        w.Write(BitConverter.GetBytes(DestinationNodeId));
        var nonce = ms.ToArray();

        var additionalData = parts.Header;

        byte[] decryptedPayload = new byte[parts.MessagePayload.Length - 16];
        var encryptedPayload = parts.MessagePayload.AsSpan()[..(parts.MessagePayload.Length - 16)];
        var tag = parts.MessagePayload.AsSpan().Slice(parts.MessagePayload.Length - 16, 16);

        var decryptor = new AesCcm(_decryptionKey);
        decryptor.Decrypt(nonce, encryptedPayload, tag, decryptedPayload, additionalData);

        messageFrame.MessagePayload = new MessagePayload(decryptedPayload);
        return messageFrame;
    }
}
