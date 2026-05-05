using DotMatter.Core.Clusters;
namespace DotMatter.Tests;

[TestFixture]
public class ClusterBaseTypedReadTests
{
    [Test]
    public void TypedEnumReaderConvertsUnderlyingNumericValue()
    {
        var value = ClusterBase.ConvertAttributeValue<TestEnum>((byte)1);

        Assert.That(value, Is.EqualTo(TestEnum.On));
    }

    private enum TestEnum : byte
    {
        Off = 0,
        On = 1,
    }
}
