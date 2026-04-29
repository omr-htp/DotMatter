using DotMatter.Core;
using DotMatter.Core.Commissioning;

namespace DotMatter.Tests;

[TestFixture]
public class CommissioningPayloadHelperTests
{
    [Test]
    public void ParseManualSetupCode_DecodesPayload()
    {
        var commissioningPayload = CommissioningPayloadHelper.ParseManualSetupCode("34970112332");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(commissioningPayload.Discriminator, Is.EqualTo(3840));
            Assert.That(commissioningPayload.Passcode, Is.EqualTo(20202021));
        }
    }

    [Test]
    public void ParseManualSetupCode_AllowsSeparators()
    {
        var commissioningPayload = CommissioningPayloadHelper.ParseManualSetupCode("3497-0112 332");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(commissioningPayload.Discriminator, Is.EqualTo(3840));
            Assert.That(commissioningPayload.Passcode, Is.EqualTo(20202021));
        }
    }

    [Test]
    public void ParseManualCode_AllowsSeparators()
    {
        var parsedCode = PairingCodeParser.ParseManualCode("3497-0112 332");

        Assert.That(parsedCode, Is.EqualTo((15, 20202021u, true)));
    }
}
