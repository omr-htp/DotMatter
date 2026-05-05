#nullable disable
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace DotMatter.Core.Mdns;

/// <summary>
///   Maintains a sequence of recent messages.
/// </summary>
/// <remarks>
///   <b>RecentMessages</b> is used to determine if a message has already been
///   processed within the specified <see cref="Interval"/>.
/// </remarks>
public class RecentMessages
{
    private readonly ConcurrentDictionary<string, DateTime> _messages = new();

    /// <summary>
    ///   Recent messages.
    /// </summary>
    /// <value>
    ///   The key is the Base64 encoding of the SHA-1 hash of
    ///   a message and the value is when the message was seen.
    /// </value>
    public IReadOnlyDictionary<string, DateTime> Messages => _messages;

    /// <summary>
    ///   The time interval used to determine if a message is recent.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///   Try adding a message to the recent message list.
    /// </summary>
    /// <param name="message">
    ///   The binary representation of a message.
    /// </param>
    /// <returns>
    ///   <b>true</b> if the message, did not already exist; otherwise,
    ///   <b>false</b> the message exists within the <see cref="Interval"/>.
    /// </returns>
    public bool TryAdd(byte[] message)
    {
        Prune();
        return _messages.TryAdd(GetId(message), DateTime.UtcNow);
    }

    /// <summary>
    ///   Remove any messages that are stale.
    /// </summary>
    /// <returns>
    ///   The number messages that were pruned.
    /// </returns>
    /// <remarks>
    ///   Anything older than an <see cref="Interval"/> ago is removed.
    /// </remarks>
    public int Prune()
    {
        var dead = DateTime.UtcNow - Interval;
        var count = 0;

        foreach (var stale in _messages.Where(x => x.Value < dead))
        {
            if (_messages.TryRemove(stale.Key, out _))
            {
                ++count;
            }
        }
        return count;
    }

    /// <summary>
    ///   Gets a unique ID for a message.
    /// </summary>
    /// <param name="message">
    ///   The binary representation of a message.
    /// </param>
    /// <returns>
    ///   The Base64 encoding of the SHA-1 hash of the <paramref name="message"/>.
    /// </returns>
    public static string GetId(byte[] message)
    {
        return Convert.ToBase64String(SHA1.HashData(message));
    }
}
