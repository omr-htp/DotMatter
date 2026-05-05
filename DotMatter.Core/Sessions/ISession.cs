namespace DotMatter.Core.Sessions;

/// <summary>
/// Represents a Matter session that can encode, decode, send, and receive messages.
/// </summary>
public interface ISession
{
    /// <summary>Gets the underlying transport connection.</summary>
    IConnection Connection
    {
        get;
    }

    /// <summary>Gets the local Matter node identifier.</summary>
    ulong SourceNodeId
    {
        get;
    }

    /// <summary>Gets the peer Matter node identifier.</summary>
    ulong DestinationNodeId
    {
        get;
    }

    /// <summary>Gets the local session identifier.</summary>
    ushort SessionId
    {
        get;
    }

    /// <summary>Gets the peer session identifier.</summary>
    ushort PeerSessionId
    {
        get;
    }

    /// <summary>Gets a value indicating whether messages should use reliable messaging.</summary>
    bool UseMRP
    {
        get;
    }

    /// <summary>Gets the peer idle retransmission timeout, or null to use the global default.</summary>
    TimeSpan? PeerMrpIdleRetransTimeout => null;

    /// <summary>Gets the peer active retransmission timeout, or null to use the global default.</summary>
    TimeSpan? PeerMrpActiveRetransTimeout => null;

    /// <summary>Gets the next message counter value.</summary>
    uint MessageCounter
    {
        get;
    }

    /// <summary>Creates a new exchange on this session.</summary>
    MessageExchange CreateExchange();

    /// <summary>Encodes a message frame for transport.</summary>
    /// <param name="message">The message frame to encode.</param>
    byte[] Encode(MessageFrame message);

    /// <summary>Decodes a received message frame.</summary>
    /// <param name="messageFrameParts">The parsed frame parts.</param>
    MessageFrame Decode(MessageFrameParts messageFrameParts);

    /// <summary>Reads the next encoded message from the session transport.</summary>
    /// <param name="cancellationToken">Token used to cancel the read operation.</param>
    Task<byte[]> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Sends an encoded message through the session transport.</summary>
    /// <param name="payload">Encoded message payload.</param>
    Task SendAsync(byte[] payload);

    /// <summary>Closes the session.</summary>
    void Close();

    /// <summary>Creates a new transport connection for this session.</summary>
    IConnection CreateNewConnection();
}
