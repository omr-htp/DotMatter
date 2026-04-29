using DotMatter.Core;
using DotMatter.Core.Sessions;
using System.Reflection;

namespace DotMatter.Tests;

[TestFixture]
public class SessionTests
{
    [Test]
    public void UnsecureSession_MessageCounter_IsUnique()
    {
        var conn = new FakeConnection();
        var session = new UnsecureSession(conn);

        var counters = new HashSet<uint>();
        for (int i = 0; i < 1000; i++)
        {
            var counter = session.MessageCounter;
            Assert.That(counters.Add(counter), Is.True, $"Duplicate counter: {counter}");
        }
    }

    [Test]
    public void UnsecureSession_CreateExchange_ReturnsValidExchange()
    {
        var conn = new FakeConnection();
        var session = new UnsecureSession(conn);

        var ex1 = session.CreateExchange();
        var ex2 = session.CreateExchange();
        Assert.That(ex1.ExchangeId, Is.Not.EqualTo(ex2.ExchangeId));

        ex1.Close();
        ex2.Close();
    }

    [Test]
    public void MessageExchange_Timeouts_AreConfigurable()
    {
        var original = MessageExchange.ExchangeTimeout;
        try
        {
            MessageExchange.ExchangeTimeout = TimeSpan.FromSeconds(60);
            Assert.That(MessageExchange.ExchangeTimeout.TotalSeconds, Is.EqualTo(60));

            MessageExchange.MrpMaxRetransmissions = 3;
            Assert.That(MessageExchange.MrpMaxRetransmissions, Is.EqualTo(3));
        }
        finally
        {
            MessageExchange.ExchangeTimeout = original;
            MessageExchange.MrpMaxRetransmissions = 5;
        }
    }

    [Test]
    public void PaseSession_MessageCounterOverflow_ClosesSession()
    {
        var session = new PaseSecureSession(new FakeConnection(), 1, 2, new byte[16], new byte[16]);
        typeof(PaseSecureSession)
            .GetField("_messageCounter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, -1);

        Assert.Throws<MatterSessionException>(() => _ = session.MessageCounter);
    }

    [Test]
    public void CaseSession_MessageCounterOverflow_ClosesSession()
    {
        var session = new CaseSecureSession(new FakeConnection(), 1, 2, 1, 2, new byte[16], new byte[16]);
        typeof(CaseSecureSession)
            .GetField("_messageCounter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, uint.MaxValue);

        Assert.Throws<MatterSessionException>(() => _ = session.MessageCounter);
    }
}
