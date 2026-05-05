namespace DotMatter.Core;

/// <summary>
/// Represents a transport connection capable of sending and receiving Matter frames.
/// </summary>
public interface IConnection
{
    /// <summary>
    /// Occurs when the underlying connection closes.
    /// </summary>
    event EventHandler ConnectionClosed;

    /// <summary>
    /// Closes the connection.
    /// </summary>
    void Close();

    /// <summary>
    /// Opens a new connection for an exchange or session.
    /// </summary>
    IConnection OpenConnection();

    /// <summary>
    /// Reads the next message from the connection.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the read operation.</param>
    Task<byte[]> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends a message over the connection.
    /// </summary>
    /// <param name="message">Encoded message bytes.</param>
    Task SendAsync(byte[] message);
}
