using DotMatter.Core;
using DotMatter.Core.Fabrics;
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
    public void UnsecureSession_Close_ReleasesExchangeId()
    {
        var session = new UnsecureSession(new FakeConnection());
        var exchange = session.CreateExchange();

        Assert.That(GetActiveExchangeCount(session), Is.EqualTo(1));

        exchange.Close();

        Assert.That(GetActiveExchangeCount(session), Is.Zero);
    }

    [Test]
    public void PaseSession_Close_ReleasesExchangeId()
    {
        var session = new PaseSecureSession(new FakeConnection(), 1, 2, new byte[16], new byte[16]);
        var exchange = session.CreateExchange();

        Assert.That(GetActiveExchangeCount(session), Is.EqualTo(1));

        exchange.Close();

        Assert.That(GetActiveExchangeCount(session), Is.Zero);
    }

    [Test]
    public void CaseSession_Close_ReleasesExchangeId()
    {
        var session = new CaseSecureSession(new FakeConnection(), 1, 2, 1, 2, new byte[16], new byte[16]);
        var exchange = session.CreateExchange();

        Assert.That(GetActiveExchangeCount(session), Is.EqualTo(1));

        exchange.Close();

        Assert.That(GetActiveExchangeCount(session), Is.Zero);
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

    [Test]
    public async Task CaseClient_EstablishSessionAsync_HonorsExternalCancellation()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();
        var storage = new FabricDiskStorage(tempDirectory.Path);
        var manager = new FabricManager(storage);
        var fabric = await manager.GetAsync("case-timeout");
        var node = Fabric.CreateNode();
        var client = new CASEClient(node, fabric, new UnsecureSession(new BlockingConnection()));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.That(
            async () => await client.EstablishSessionAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void UdpConnection_Close_CompletesUnroutedReaders()
    {
        using var conn = new UdpConnection(System.Net.IPAddress.Loopback, 5540);

        var readTask = conn.ReadAsync(CancellationToken.None);

        conn.Close();

        Assert.That(
            async () => await readTask.WaitAsync(TimeSpan.FromSeconds(1)),
            Throws.InstanceOf<System.Threading.Channels.ChannelClosedException>());
    }

    private static int GetActiveExchangeCount(object session)
    {
        var exchangeIds = session.GetType()
            .GetField("_activeExchangeIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(session)!;
        return (int)exchangeIds.GetType().GetProperty("Count")!.GetValue(exchangeIds)!;
    }

    private sealed class BlockingConnection : IConnection
    {
        public event EventHandler ConnectionClosed = delegate { };

        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        }

        public Task SendAsync(byte[] message) => Task.CompletedTask;

        public void Close() => ConnectionClosed(this, EventArgs.Empty);

        public IConnection OpenConnection() => new BlockingConnection();
    }
}
