using DotMatter.Core;

namespace DotMatter.Tests;

[TestFixture]
public class MessageFrameTests
{
    [Test]
    public void MessageFrameParts_TooShort_ThrowsArgumentException()
    {
        // Minimum valid frame is 8 bytes
        var tooShort = new byte[] { 0x00, 0x00, 0x00, 0x01 };
        Assert.Throws<ArgumentException>(() => new MessageFrameParts(tooShort));
    }

    [Test]
    public void MessageFrameParts_EmptyArray_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new MessageFrameParts([]));
    }

    [Test]
    public void MessageFrameParts_MinimumValidFrame_Parses()
    {
        // 8 bytes minimum: flags(1) + secflags(1) + sessionId(2) + counter(4)
        var frame = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };
        var parts = new MessageFrameParts(frame);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parts.Header, Is.Not.Null);
            Assert.That((MessageFlags)parts.Header[0], Is.EqualTo(MessageFlags.None));
        }
    }

    [Test]
    public void MessagePayload_TooShort_ThrowsArgumentException()
    {
        // MessagePayload needs at least 6 bytes
        var tooShort = new byte[] { 0x00, 0x00, 0x00 };
        Assert.Throws<ArgumentException>(() => new MessagePayload(tooShort));
    }

    [Test]
    public void MessageFlags_GetDsiz_ReturnsCorrectValues()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((MessageFlags)0x00).GetDsiz(), Is.Zero);
            Assert.That(((MessageFlags)0x01).GetDsiz(), Is.EqualTo(1)); // 8-byte node ID
            Assert.That(((MessageFlags)0x02).GetDsiz(), Is.EqualTo(2)); // 2-byte group ID
            Assert.That(((MessageFlags)0x03).GetDsiz(), Is.EqualTo(3)); // reserved
        }
    }

    [Test]
    public void MessageFlags_WithDsiz_SetsCorrectly()
    {
        var flags = MessageFlags.S; // 0x04
        var result = flags.WithDsiz(2);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetDsiz(), Is.EqualTo(2));
            Assert.That(result.HasFlag(MessageFlags.S), Is.True);
        }
    }

    [Test]
    public void MessageFlags_WithDsiz_ClearsOldValue()
    {
        var flags = ((MessageFlags)0x03); // DSIZ=3
        var result = flags.WithDsiz(1);
        Assert.That(result.GetDsiz(), Is.EqualTo(1));
    }

    [Test]
    public void MessageFrameParts_ValidPayload_DecodesCorrectly()
    {
        // Known good frame from existing test
        var payload = "01-00-00-00-5A-D2-F8-02-00-00-00-00-00-00-00-00-04-21-E8-EF-00-00-15-30-01-20-A3-74-0F-F2-CD-24-67-C3-75-66-EA-7C-A3-B1-D0-04-3B-C8-88-F1-E7-2F-54-EB-A7-2B-54-58-D8-75-9D-10-30-02-20-AC-66-29-AC-EC-48-40-76-79-30-E5-FC-48-9B-75-E5-D2-93-2A-61-1C-6E-C9-C0-50-9E-1A-5A-D7-41-DA-C7-25-03-A2-35-35-04-25-01-E8-03-30-02-20-32-7F-9E-4B-E6-66-63-23-1B-FD-68-F1-7B-62-9B-43-E2-3B-22-D4-CF-32-2B-BF-5F-21-09-B3-F1-71-6A-60-18-35-05-25-01-F4-01-25-02-2C-01-25-03-A0-0F-24-04-13-24-05-0C-26-06-00-00-05-01-24-07-01-18-18";
        var bytes = HexToBytes(payload);

        var parts = new MessageFrameParts(bytes);
        var header = parts.MessageFrameWithHeaders();
        Assert.That(header.MessageFlags.GetDsiz(), Is.EqualTo(1));
    }

    private static byte[] HexToBytes(string hex)
        => Convert.FromHexString(hex.Replace("-", "", StringComparison.Ordinal));
}
