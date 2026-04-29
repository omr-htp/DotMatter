using DotMatter.Core.Sessions;

namespace DotMatter.Tests;

[TestFixture]
public class MessageCounterWindowTests
{
    [Test]
    public void FirstMessage_AlwaysAccepted()
    {
        var window = new MessageCounterWindow();
        Assert.That(window.Validate(42), Is.True);
    }

    [Test]
    public void DuplicateCounter_Rejected()
    {
        var window = new MessageCounterWindow();
        Assert.That(window.Validate(100), Is.True);
        Assert.That(window.Validate(100), Is.False);
    }

    [Test]
    public void SequentialCounters_AllAccepted()
    {
        var window = new MessageCounterWindow();
        for (uint i = 0; i < 50; i++)
        {
            Assert.That(window.Validate(i), Is.True, $"Counter {i} should be accepted");
        }
    }

    [Test]
    public void OutOfOrder_WithinWindow_Accepted()
    {
        var window = new MessageCounterWindow();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Validate(10), Is.True);
            Assert.That(window.Validate(15), Is.True);
            Assert.That(window.Validate(12), Is.True); // out-of-order but in window
            Assert.That(window.Validate(11), Is.True);
        }
    }

    [Test]
    public void OutOfOrder_DuplicateInWindow_Rejected()
    {
        var window = new MessageCounterWindow();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Validate(10), Is.True);
            Assert.That(window.Validate(15), Is.True);
            Assert.That(window.Validate(12), Is.True);
        }
        Assert.That(window.Validate(12), Is.False); // duplicate
    }

    [Test]
    public void CounterBelowWindow_Rejected()
    {
        var window = new MessageCounterWindow();
        // Advance window past counter 0
        for (uint i = 0; i < 40; i++)
        {
            window.Validate(i);
        }

        using (Assert.EnterMultipleScope())
        {
            // Counter 0 is now below the window
            Assert.That(window.Validate(0), Is.False);
            Assert.That(window.Validate(5), Is.False);
        }
    }

    [Test]
    public void LargeJump_AdvancesWindow()
    {
        var window = new MessageCounterWindow();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Validate(0), Is.True);
            Assert.That(window.Validate(1000), Is.True); // big jump
            Assert.That(window.Validate(1001), Is.True);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Validate(0), Is.False); // way below window
            Assert.That(window.Validate(1000), Is.False); // duplicate
        }
    }

    [Test]
    public void WindowEdge_ExactlyAt31_Accepted()
    {
        var window = new MessageCounterWindow();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Validate(0), Is.True);
            Assert.That(window.Validate(31), Is.True); // bit 31 — edge of window
            Assert.That(window.Validate(15), Is.True); // mid-window still works
        }
    }

    [Test]
    public void WindowEdge_At32_AdvancesWindow()
    {
        var window = new MessageCounterWindow();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Validate(0), Is.True);
            Assert.That(window.Validate(32), Is.True); // beyond window — advances
        }
        Assert.That(window.Validate(0), Is.False); // now below window
    }

    [Test]
    public void Reset_AllowsReuseOfCounters()
    {
        var window = new MessageCounterWindow();
        Assert.That(window.Validate(5), Is.True);
        Assert.That(window.Validate(5), Is.False);
        window.Reset();
        Assert.That(window.Validate(5), Is.True); // accepted after reset
    }

    [Test]
    public void CounterWrap_ForwardCountersAccepted()
    {
        var window = new MessageCounterWindow();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Validate(0xFFFF_FFF0), Is.True);
            Assert.That(window.Validate(0x0000_0010), Is.True);
            Assert.That(window.Validate(0xFFFF_FFF0), Is.False);
        }
    }
}
