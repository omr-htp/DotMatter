using System.Security.Cryptography;

namespace DotMatter.Core.Sessions;

/// <summary>
/// Represents a PASE secure session established during commissioning.
/// </summary>
public class PaseSecureSession : ISession
{
    private readonly IConnection _connection;
    private readonly byte[] _encryptionKey;
    private readonly byte[] _decryptionKey;
    private readonly byte[] _attestationChallenge;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly HashSet<ushort> _activeExchangeIds = [];
    private int _messageCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaseSecureSession"/> class.
    /// </summary>
    public PaseSecureSession(
        IConnection connection,
        ushort sessionId,
        ushort peerSessionId,
        byte[] encryptionKey,
        byte[] decryptionKey,
        byte[]? attestationChallenge = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _encryptionKey = encryptionKey ?? throw new ArgumentNullException(nameof(encryptionKey));
        _decryptionKey = decryptionKey ?? throw new ArgumentNullException(nameof(decryptionKey));
        _attestationChallenge = attestationChallenge?.ToArray() ?? [];

        SessionId = sessionId;
        PeerSessionId = peerSessionId;

        _messageCounter = (int)BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));

        MatterLog.Info("Created PASE secure session {SessionId} with peer session {PeerSessionId}.", SessionId, PeerSessionId);
    }

    /// <inheritdoc />
    public IConnection CreateNewConnection()
    {
        return _connection.OpenConnection();
    }

    /// <inheritdoc />
    public IConnection Connection => _connection;

    /// <summary>
    /// Gets the attestation challenge derived from the PASE secure-session transcript.
    /// </summary>
    public byte[] AttestationChallenge => _attestationChallenge;

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
    public ushort SessionId { get; }

    /// <inheritdoc />
    public ushort PeerSessionId { get; }

    /// <inheritdoc />
    public bool UseMRP => true;

    /// <inheritdoc />
    public uint MessageCounter
    {
        get
        {
            var next = (uint)Interlocked.Increment(ref _messageCounter);
            if (next == 0)
            {
                Close();
                throw new MatterSessionException("PASE message counter overflow; session closed to prevent nonce reuse.");
            }

            return next;
        }
    }

    /// <inheritdoc />
    public void Close()
    {
        _cancellationTokenSource.Cancel();
        _connection.Close();
    }

    /// <inheritdoc />
    public MessageExchange CreateExchange()
    {
        ushort exchangeId;
        do
        {
            exchangeId = BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(2));
        } while (!_activeExchangeIds.Add(exchangeId));

        MatterLog.Info("Created secure exchange {ExchangeId}.", exchangeId);
        return new MessageExchange(exchangeId, this);
    }

    internal void ReleaseExchangeId(ushort exchangeId)
    {
        _activeExchangeIds.Remove(exchangeId);
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

        var memoryStream = new MemoryStream();
        var nonceWriter = new BinaryWriter(memoryStream);

        nonceWriter.Write((byte)messageFrame.SecurityFlags);
        nonceWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
        nonceWriter.Write(BitConverter.GetBytes(messageFrame.SourceNodeID));

        var nonce = memoryStream.ToArray();

        // AAD is the entire unencrypted message header (Matter spec §4.7.2)
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

        MatterLog.Debug("Decrypting MessagePayload [M: {0}, S: {1}] ...", messageFrame.MessageCounter, messageFrame.SessionID);

        var memoryStream = new MemoryStream();
        var nonceWriter = new BinaryWriter(memoryStream);

        nonceWriter.Write((byte)messageFrame.SecurityFlags);
        nonceWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
        nonceWriter.Write(BitConverter.GetBytes(messageFrame.SourceNodeID));

        var nonce = memoryStream.ToArray();

        // AAD is the entire unencrypted message header (Matter spec §4.7.2)
        var additionalData = parts.Header;

        byte[] decryptedPayload = new byte[parts.MessagePayload.Length - 16];

        var encryptedPayload = parts.MessagePayload.AsSpan()[..(parts.MessagePayload.Length - 16)];
        var tag = parts.MessagePayload.AsSpan().Slice(parts.MessagePayload.Length - 16, 16);

        try
        {
            var encryptor = new AesCcm(_decryptionKey);
            encryptor.Decrypt(nonce, encryptedPayload, tag, decryptedPayload, additionalData);

            //MatterLog.Debug("Decrypted MessagePayload: {0}", BitConverter.ToString(decryptedPayload));

            messageFrame.MessagePayload = new MessagePayload(decryptedPayload);

            return messageFrame;
        }
        catch (Exception ex)
        {
            MatterLog.Warn(ex, "PASE message decryption failed.");
            throw;
        }
    }
}
