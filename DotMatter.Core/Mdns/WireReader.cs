#nullable disable
using System.Net;
using System.Text;

namespace DotMatter.Core.Mdns;

/// <summary>
///   Methods to read DNS wire formatted data items.
/// </summary>
/// <remarks>
///   Creates a new instance of the <see cref="WireReader"/> on the
///   specified <see cref="Stream"/>.
/// </remarks>
/// <param name="stream">
///   The source for data items.
/// </param>
public class WireReader(Stream stream)
{
    private static readonly DateTime _unixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Stream _stream = stream;
    private readonly Dictionary<int, List<string>> _names = [];

    /// <summary>
    ///   The reader relative position within the stream.
    /// </summary>
    public int Position;

    /// <summary>
    ///   Read a byte.
    /// </summary>
    /// <returns>
    ///   The next byte in the stream.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    public byte ReadByte()
    {
        var value = _stream.ReadByte();
        if (value < 0)
        {
            throw new EndOfStreamException();
        }

        ++Position;
        return (byte)value;
    }

    /// <summary>
    ///   Read the specified number of bytes.
    /// </summary>
    /// <param name="length">
    ///   The number of bytes to read.
    /// </param>
    /// <returns>
    ///   The next <paramref name="length"/> bytes in the stream.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    public byte[] ReadBytes(int length)
    {
        var buffer = new byte[length];
        for (var offset = 0; length > 0;)
        {
            var n = _stream.Read(buffer, offset, length);
            if (n == 0)
            {
                throw new EndOfStreamException();
            }

            offset += n;
            length -= n;
            Position += n;
        }

        return buffer;
    }

    /// <summary>
    ///   Read the bytes with a byte length prefix.
    /// </summary>
    /// <returns>
    ///   The next N bytes.
    /// </returns>
    public byte[] ReadByteLengthPrefixedBytes()
    {
        int length = ReadByte();
        return ReadBytes(length);
    }

    /// <summary>
    ///   Read the bytes with an uint16 length prefix.
    /// </summary>
    /// <returns>
    ///   The next N bytes.
    /// </returns>
    public byte[] ReadUInt16LengthPrefixedBytes()
    {
        int length = ReadUInt16();
        return ReadBytes(length);
    }

    /// <summary>
    ///   Read an unsigned short.
    /// </summary>
    /// <returns>
    ///   The two byte little-endian value as an unsigned short.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    public ushort ReadUInt16()
        => (ushort)((ReadByte() << 8) | ReadByte());

    /// <summary>
    ///   Read an unsigned int.
    /// </summary>
    /// <returns>
    ///   The four byte little-endian value as an unsigned int.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    public uint ReadUInt32()
    {
        int value = ReadByte();
        value = value << 8 | ReadByte();
        value = value << 8 | ReadByte();
        value = value << 8 | ReadByte();
        return (uint)value;
    }

    /// <summary>
    ///   Read an unsigned long from 48 bits.
    /// </summary>
    /// <returns>
    ///   The six byte little-endian value as an unsigned long.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    public ulong ReadUInt48()
    {
        ulong value = ReadByte();
        value = value << 8 | ReadByte();
        value = value << 8 | ReadByte();
        value = value << 8 | ReadByte();
        value = value << 8 | ReadByte();
        value = value << 8 | ReadByte();
        return value;
    }

    /// <summary>
    ///   Read a domain name.
    /// </summary>
    /// <returns>
    ///   The domain name.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    /// <exception cref="InvalidDataException">
    ///   Only ASCII characters are allowed.
    /// </exception>
    /// <remarks>
    ///   A domain name is represented as a sequence of labels, where
    ///   each label consists of a length octet followed by that
    ///   number of octets. The domain name terminates with the
    ///   zero length octet for the null label of the root.
    ///   <note>
    ///   Compressed domain names are also supported.
    ///   </note>
    /// </remarks>
    public DomainName ReadDomainName()
    {
        var labels = ReadLabels();
        var name = new DomainName([.. labels]);
        return name;
    }

    List<string> ReadLabels()
    {
        var pointer = Position;
        var length = ReadByte();

        // Do we have a compressed pointer?
        if ((length & 0xC0) == 0xC0)
        {
            var cpointer = ((length & 0x3F) << 8) | ReadByte();
            if (cpointer >= pointer)
            {
                throw new InvalidDataException("Compressed domain name pointers must point to an earlier label.");
            }

            if (!_names.TryGetValue(cpointer, out var cname))
            {
                throw new InvalidDataException($"Compressed domain name pointer '{cpointer}' does not reference a known label.");
            }

            _names[pointer] = cname;
            return cname;
        }

        if ((length & 0xC0) != 0)
        {
            throw new InvalidDataException("Invalid domain label length.");
        }

        var labels = new List<string>();
        // End of labels?
        if (length == 0)
        {
            return labels;
        }

        if (length > 63)
        {
            throw new InvalidDataException("Domain labels cannot exceed 63 octets.");
        }

        // Read current label and remaining labels.
        labels.Add(ReadUTF8String(length));
        labels.AddRange(ReadLabels());

        // Add to compressed names.
        _names[pointer] = labels;

        return labels;
    }

    /// <summary>
    ///   Read a string.
    /// </summary>
    /// <remarks>
    ///   Strings are encoded in ASCII with a length prefixed byte.
    /// </remarks>
    /// <returns>
    ///   The string.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    /// <exception cref="InvalidDataException">
    ///   Only ASCII characters are allowed.
    /// </exception>
    public string ReadString()
    {
        var bytes = ReadByteLengthPrefixedBytes();
        if (bytes.Any(c => c > 0x7F))
        {
            throw new InvalidDataException("Only ASCII characters are allowed.");
        }
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>
    ///   Read a string.
    /// </summary>
    /// <remarks>
    ///   Strings are encoded in UTF8 with a length prefixed byte.
    /// </remarks>
    /// <returns>
    ///   The string.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    public string ReadUTF8String()
    {
        return Encoding.UTF8.GetString(ReadByteLengthPrefixedBytes());
    }

    /// <summary>
    ///   Read a string of a given length.
    /// </summary>
    /// <remarks>
    ///   Strings are encoded in UTF8.
    /// </remarks>
    /// <returns>
    ///   The string.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    public string ReadUTF8String(int length)
    {
        if (length == 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(ReadBytes(length));
    }

    /// <summary>
    ///   Read a time span (interval) with 16-bits.
    /// </summary>
    /// <returns>
    ///   A <see cref="TimeSpan"/> with second resolution.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    /// <remarks>
    ///   The interval is represented as the number of seconds in two bytes.
    /// </remarks>
    public TimeSpan ReadTimeSpan16()
    {
        return TimeSpan.FromSeconds(ReadUInt16());
    }

    /// <summary>
    ///   Read a time span (interval) with 32-bits.
    /// </summary>
    /// <returns>
    ///   A <see cref="TimeSpan"/> with second resolution.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    /// <remarks>
    ///   The interval is represented as the number of seconds in four bytes.
    /// </remarks>
    public TimeSpan ReadTimeSpan32()
    {
        return TimeSpan.FromSeconds(ReadUInt32());
    }

    /// <summary>
    ///   Read an Internet address.
    /// </summary>
    /// <returns>
    ///   An <see cref="IPAddress"/>.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    /// <remarks>
    ///   Use a <paramref name="length"/> of 4 to read an IPv4 address and
    ///   16 to read an IPv6 address.
    /// </remarks>
    public IPAddress ReadIPAddress(int length = 4)
    {
        if (length != 4 && length != 16)
        {
            throw new InvalidDataException("IP addresses must contain either 4 bytes (IPv4) or 16 bytes (IPv6).");
        }

        var address = ReadBytes(length);
        return new IPAddress(address);
    }

    /// <summary>
    ///   Reads a bitmap.
    /// </summary>
    /// <returns>
    ///   The sequence of values encoded by the bitmap.
    /// </returns>
    /// <remarks>
    ///   <see href="https://tools.ietf.org/html/rfc3845#section-2.1.2"/> for the
    ///   encoding details.
    /// </remarks>
    public List<ushort> ReadBitmap()
    {
        var values = new List<ushort>();
        var block = ReadByte();
        var length = ReadByte();
        int offset = block * 256;
        for (int i = 0; i < length; ++i, offset += 8)
        {
            var bits = ReadByte();
            for (int bit = 0; bit < 8; ++bit)
            {
                if ((bits & (1 << Math.Abs(bit - 7))) != 0)
                {
                    values.Add((ushort)(offset + bit));
                }
            }
        }
        return values;
    }

    /// <summary>
    ///   Read a <see cref="DateTime"/> that is represented in
    ///   seconds (32 bits) from the Unix epoch. 
    /// </summary>
    /// <returns>
    ///   A <see cref="DateTime"/> in <see cref="DateTimeKind.Utc"/>.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    public DateTime ReadDateTime32()
    {
        var seconds = ReadUInt32();
        return _unixEpoch.AddSeconds(seconds);
    }

    /// <summary>
    ///   Read a <see cref="DateTime"/> that is represented in
    ///   seconds (48 bits) from the Unix epoch. 
    /// </summary>
    /// <returns>
    ///   A <see cref="DateTime"/> in <see cref="DateTimeKind.Utc"/>.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    ///   When no more data is available.
    /// </exception>
    public DateTime ReadDateTime48()
    {
        var seconds = ReadUInt48();
        return _unixEpoch.AddSeconds(seconds);
    }
}

