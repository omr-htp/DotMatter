using DotMatter.Core;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Tests;

[TestFixture]
public class SubscriptionTests
{
    [Test]
    public void CreateUnsolicitedStatusResponseFrame_UsesReliableAckOnMrpSessions()
    {
        var session = new StubSession(useMrp: true);

        var frame = Subscription.CreateUnsolicitedStatusResponseFrame(session, 0x1234, 0x89ABCDEF);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(frame.MessageFlags.HasFlag(MessageFlags.S), Is.True);
            Assert.That(frame.SessionID, Is.EqualTo(session.PeerSessionId));
            Assert.That(frame.SourceNodeID, Is.EqualTo(session.SourceNodeId));
            Assert.That(frame.DestinationNodeId, Is.EqualTo(session.DestinationNodeId));
            Assert.That(frame.MessagePayload.ExchangeID, Is.EqualTo((ushort)0x1234));
            Assert.That(frame.MessagePayload.AcknowledgedMessageCounter, Is.EqualTo(0x89ABCDEF));
            Assert.That(frame.MessagePayload.ProtocolId, Is.EqualTo((ushort)0x0001));
            Assert.That(frame.MessagePayload.ProtocolOpCode, Is.EqualTo(0x01));
            Assert.That(frame.MessagePayload.ExchangeFlags.HasFlag(ExchangeFlags.Acknowledgement), Is.True);
            Assert.That(frame.MessagePayload.ExchangeFlags.HasFlag(ExchangeFlags.Reliability), Is.True);
            Assert.That(frame.MessagePayload.ExchangeFlags.HasFlag(ExchangeFlags.Initiator), Is.False);
        }
    }

    [Test]
    public void ReportDataAction_WithOnlySubscriptionId_ParsesWithoutReports()
    {
        var writer = new MatterTLV();
        writer.AddStructure();
        writer.AddUInt32(0, 0x10203040);
        writer.EndContainer();

        var report = new ReportDataAction(new MatterTLV(writer.GetBytes()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.SubscriptionId, Is.EqualTo(0x10203040u));
            Assert.That(report.AttributeReports, Is.Empty);
            Assert.That(report.EventReports, Is.Empty);
            Assert.That(report.MoreChunkedMessages, Is.False);
            Assert.That(report.SuppressResponse, Is.False);
        }
    }

    private sealed class StubSession(bool useMrp) : ISession
    {
        private uint _messageCounter = 41;

        public IConnection Connection { get; } = new FakeConnection();

        public ulong SourceNodeId { get; } = 0x1111222233334444;

        public ulong DestinationNodeId { get; } = 0xAAAABBBBCCCCDDDD;

        public ushort SessionId { get; } = 0x1357;

        public ushort PeerSessionId { get; } = 0x2468;

        public bool UseMRP { get; } = useMrp;

        public uint MessageCounter => ++_messageCounter;

        public MessageExchange CreateExchange() => throw new NotSupportedException();

        public byte[] Encode(MessageFrame message) => throw new NotSupportedException();

        public MessageFrame Decode(MessageFrameParts messageFrameParts) => throw new NotSupportedException();

        public Task<byte[]> ReadAsync(CancellationToken token) => throw new NotSupportedException();

        public Task SendAsync(byte[] payload) => throw new NotSupportedException();

        public void Close() => throw new NotSupportedException();

        public IConnection CreateNewConnection() => throw new NotSupportedException();
    }
}
