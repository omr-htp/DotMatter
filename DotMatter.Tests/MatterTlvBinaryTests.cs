using DotMatter.Core.TLV;

namespace DotMatter.Tests;

[TestFixture]
public class MatterTlvBinaryTests
{
    [Test]
    public void AsSpanExposesLiveViewOfBackingStorage()
    {
        var bytes = new List<byte> { 0x10, 0x20, 0x30 };
        var span = MatterTlvBinary.AsSpan(bytes);

        bytes[1] = 0x99;

        Assert.That(span[1], Is.EqualTo(0x99));
    }

    [Test]
    public void PrimitiveReadersUseLittleEndianWithoutIntermediateCopies()
    {
        var bytes = new List<byte> { 0x34, 0x12, 0x78, 0x56, 0xEF, 0xCD, 0xAB, 0x90 };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(MatterTlvBinary.ReadUInt16(bytes, 0), Is.EqualTo(0x1234));
            Assert.That(MatterTlvBinary.ReadUInt32(bytes, 2), Is.EqualTo(0xCDEF5678u));
        }
    }
}
