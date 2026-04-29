#nullable disable
using System.Text;

namespace DotMatter.Core.Mdns;

/// <summary>
///   Text strings.
/// </summary>
/// <remarks>
///   TXT RRs are used to hold descriptive text.  The semantics of the text
///   depends on the domain where it is found.
/// </remarks>
public class TXTRecord : ResourceRecord
{
    /// <summary>
    ///   Creates a new instance of the <see cref="TXTRecord"/> class.
    /// </summary>
    public TXTRecord() : base()
    {
        Type = DnsType.TXT;
    }

    /// <summary>
    ///  The sequence of strings.
    /// </summary>
    public List<string> Strings { get; set; } = [];

    /// <inheritdoc />
    public override void ReadData(WireReader reader, int length)
    {
        while (length > 0)
        {
            var s = reader.ReadUTF8String();
            Strings.Add(s);
            length -= Encoding.UTF8.GetByteCount(s) + 1;
        }
    }

    /// <inheritdoc />
    public override void WriteData(WireWriter writer)
    {
        foreach (var s in Strings)
        {
            writer.WriteStringUTF8(s);
        }
    }

}

