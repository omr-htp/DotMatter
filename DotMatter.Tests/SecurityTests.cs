using DotMatter.Core;
using DotMatter.Core.TLV;

namespace DotMatter.Tests;

[TestFixture]
public class SecurityTests
{
    [Test]
    public void MalformedTLV_TooShort_ThrowsException()
    {
        var badTlv = new byte[] { 0x15 }; // Structure start but no content/end
        var tlv = new MatterTLV(badTlv);
        tlv.OpenStructure();
        // Attempting to read past the buffer should throw
        Assert.Throws<MatterTlvException>(() => tlv.GetUnsignedInt8(0));
    }

    [Test]
    public void MalformedTLV_LengthPastBuffer_ThrowsMatterTlvException()
    {
        var tlv = new MatterTLV([0x15, 0x30, 0x01, 0xFF, 0x18]);
        tlv.OpenStructure();

        Assert.Throws<MatterTlvException>(() => tlv.GetOctetString(1));
    }

    [Test]
    public void MalformedTLV_WrongType_ThrowsException()
    {
        // Write a bool but try to read as uint8
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddBool(0, true);
        tlv.EndContainer();

        tlv.OpenStructure();
        Assert.Throws<MatterTlvException>(() => tlv.GetUnsignedInt8(0));
    }

    [Test]
    public void TruncatedFrame_BelowMinimum_Rejected()
    {
        // 7 bytes is below the 8-byte minimum for a Matter message frame
        var truncated = new byte[7];
        Assert.Throws<ArgumentException>(() => new MessageFrameParts(truncated));
    }

    [Test]
    public void TruncatedPayload_BelowMinimum_Rejected()
    {
        // 5 bytes is below the 6-byte minimum for a Matter message payload
        var truncated = new byte[5];
        Assert.Throws<ArgumentException>(() => new MessagePayload(truncated));
    }

    [Test]
    public void SecurityFlags_GetSessionType_ReturnsCorrectValues()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((SecurityFlags)0x00).GetSessionType(), Is.EqualTo(SecurityFlagsExtensions.SessionType_Unicast));
            Assert.That(((SecurityFlags)0x01).GetSessionType(), Is.EqualTo(SecurityFlagsExtensions.SessionType_Group));
        }
    }

    [Test]
    public void MessageFlags_SFlag_IndependentOfDsiz()
    {
        // S flag (0x04) should be independent of DSIZ (bits 0-1)
        var flags = MessageFlags.S;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(flags.GetDsiz(), Is.Zero);
            Assert.That(flags.HasFlag(MessageFlags.S), Is.True);
        }

        var withDsiz = flags.WithDsiz(2);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withDsiz.HasFlag(MessageFlags.S), Is.True);
            Assert.That(withDsiz.GetDsiz(), Is.EqualTo(2));
        }
    }
}
