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

    [Test]
    public void EventReportIB_CapturesTag7PayloadAndDeltaSystemTimestamp()
    {
        var eventPayload = new MatterTLV();
        eventPayload.AddStructure(7);
        eventPayload.AddUInt8(0, 2);
        eventPayload.EndContainer();

        var writer = new MatterTLV();
        writer.AddStructure();
        writer.AddStructure(1);
        writer.AddList(0);
        writer.AddUInt16(1, 1);
        writer.AddUInt32(2, 0x003B);
        writer.AddUInt32(3, 0x0001);
        writer.EndContainer();
        writer.AddUInt64(1, 42);
        writer.AddUInt8(2, 3);
        writer.AddUInt64(6, 1234);
        writer.AddStructure(7);
        writer.AddUInt8(0, 2);
        writer.EndContainer();
        writer.EndContainer();
        writer.EndContainer();

        var report = new EventReportIB(new MatterTLV(writer.GetBytes()));
        var mapped = MatterEventReport.From(report);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.EventData, Is.Not.Null);
            Assert.That(report.EventData!.DeltaSystemTimestamp, Is.EqualTo(1234ul));
            Assert.That(report.EventData.RawData, Is.Not.Null);
            Assert.That(report.EventData.RawData!.GetBytes(), Is.EqualTo(eventPayload.GetBytes()));
            Assert.That(mapped.DeltaSystemTimestamp, Is.EqualTo(1234ul));
            Assert.That(mapped.RawData, Is.Not.Null);
            Assert.That(mapped.RawData!.GetBytes(), Is.EqualTo(eventPayload.GetBytes()));
        }
    }

    [Test]
    public void ReportDataAction_ParsesEventReports_WhenInteractionModelRevisionComesFirst()
    {
        var writer = new MatterTLV();
        writer.AddStructure();
        writer.AddUInt8(255, 12);
        writer.AddUInt32(0, 0x10203040);
        writer.AddArray(2);
        writer.AddStructure();
        writer.AddStructure(1);
        writer.AddList(0);
        writer.AddUInt16(1, 1);
        writer.AddUInt32(2, 0x003B);
        writer.AddUInt32(3, 0x0001);
        writer.EndContainer();
        writer.AddUInt64(1, 42);
        writer.AddUInt8(2, 3);
        writer.AddStructure(7);
        writer.AddUInt8(0, 2);
        writer.EndContainer();
        writer.EndContainer();
        writer.EndContainer();
        writer.EndContainer();
        writer.AddBool(4, true);
        writer.EndContainer();

        var report = new ReportDataAction(new MatterTLV(writer.GetBytes()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.InteractionModelRevision, Is.EqualTo(12));
            Assert.That(report.SubscriptionId, Is.EqualTo(0x10203040u));
            Assert.That(report.EventReports, Has.Count.EqualTo(1));
            Assert.That(report.EventReports[0].EventData, Is.Not.Null);
            Assert.That(report.EventReports[0].EventData!.ClusterId, Is.EqualTo(0x003Bu));
            Assert.That(report.EventReports[0].EventData!.EventId, Is.EqualTo(0x0001u));
            Assert.That(report.SuppressResponse, Is.True);
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
