using DotMatter.Core;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.TLV;

namespace DotMatter.Tests;

[TestFixture]
public class InteractionModelTests
{
    [Test]
    public void StatusIB_WithClusterStatus_ParsesBothFields()
    {
        // Build a StatusIB TLV: struct { uint8(tag=0): 0x00, uint8(tag=1): 0x05 }
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddStructure(0); // Wrap in a parent structure with tag 0
        tlv.AddUInt8(0, 0x00); // Status = SUCCESS
        tlv.AddUInt8(1, 0x05); // ClusterStatus
        tlv.EndContainer();
        tlv.EndContainer();

        // Reset for reading
        tlv.OpenStructure();
        var statusIb = new StatusIB(0, tlv);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statusIb.Status, Is.Zero);
            Assert.That(statusIb.ClusterStatus, Is.EqualTo(0x05));
        }
    }

    [Test]
    public void StatusIB_WithoutClusterStatus_DoesNotCrash()
    {
        // Build a StatusIB TLV with only Status, no ClusterStatus (tag 1 missing)
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddStructure(0);
        tlv.AddUInt8(0, 0x81); // Status = FAILURE
        tlv.EndContainer();
        tlv.EndContainer();

        tlv.OpenStructure();
        var statusIb = new StatusIB(0, tlv);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statusIb.Status, Is.EqualTo(0x81));
            Assert.That(statusIb.ClusterStatus, Is.Zero); // Default
        }
    }

    [Test]
    public void ExchangeFlags_IndividualBits_AreCorrect()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That((byte)ExchangeFlags.Initiator, Is.EqualTo(0x01));
            Assert.That((byte)ExchangeFlags.Acknowledgement, Is.EqualTo(0x02));
            Assert.That((byte)ExchangeFlags.Reliability, Is.EqualTo(0x04));
            Assert.That((byte)ExchangeFlags.SecuredExtensions, Is.EqualTo(0x08));
            Assert.That((byte)ExchangeFlags.VendorPresent, Is.EqualTo(0x10));
        }
    }

    [Test]
    public void ExchangeFlags_AckAndReliability_CanCoexist()
    {
        // StatusResponse needs both Ack (piggybacked) and Reliability (want ACK back)
        var flags = ExchangeFlags.Acknowledgement | ExchangeFlags.Reliability;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(flags.HasFlag(ExchangeFlags.Acknowledgement), Is.True);
            Assert.That(flags.HasFlag(ExchangeFlags.Reliability), Is.True);
            Assert.That((byte)flags, Is.EqualTo(0x06));
        }
    }

    [Test]
    public void ReportDataAction_ChunkedRead_AccumulatesReports()
    {
        // Simulate a chunked ReportData with MoreChunkedMessages=true
        var writer = new MatterTLV();
        writer.AddStructure();
        writer.AddArray(1);  // Tag 1: AttributeReports (empty)
        writer.EndContainer();
        writer.AddBool(3, true);  // Tag 3: MoreChunkedMessages = true
        writer.EndContainer();

        var reader = new MatterTLV(writer.GetBytes());
        var report = new ReportDataAction(reader);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.MoreChunkedMessages, Is.True);
            Assert.That(report.AttributeReports, Is.Empty);
        }
    }

    [Test]
    public void ReportDataAction_FinalChunk_HasNoMoreChunked()
    {
        var writer = new MatterTLV();
        writer.AddStructure();
        writer.AddArray(1);  // empty attribute reports
        writer.EndContainer();
        writer.EndContainer();

        var reader = new MatterTLV(writer.GetBytes());
        var report = new ReportDataAction(reader);
        Assert.That(report.MoreChunkedMessages, Is.False);
    }

    [Test]
    public void ReportDataAction_WithEvents_ParsesEventReports()
    {
        var writer = new MatterTLV();
        writer.AddStructure();
        writer.AddArray(2);  // Tag 2: EventReports (empty)
        writer.EndContainer();
        writer.EndContainer();

        var reader = new MatterTLV(writer.GetBytes());
        var report = new ReportDataAction(reader);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.EventReports, Is.Empty);
            Assert.That(report.AttributeReports, Is.Empty);
        }
    }

    [Test]
    public void EventPath_WithWildcard_AcceptsNullFields()
    {
        var path = new EventPath(endpointId: 1, clusterId: 0x0006);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(path.EndpointId, Is.EqualTo((ushort)1));
            Assert.That(path.ClusterId, Is.EqualTo(0x0006u));
            Assert.That(path.EventId, Is.Null);
        }
    }

    [Test]
    public void AttributePath_WithWildcard_AcceptsNullFields()
    {
        var path = new AttributePath(endpointId: 1);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(path.EndpointId, Is.EqualTo((ushort)1));
            Assert.That(path.ClusterId, Is.Null);
            Assert.That(path.AttributeId, Is.Null);
        }
    }

    [Test]
    public void AclEntry_TLV_EncodesCorrectly()
    {
        // Build an ACL entry TLV as done in MatterCommissioner
        var tlv = new MatterTLV();
        tlv.AddStructure();
        // ACL array with one entry
        tlv.AddArray(0);
        tlv.AddStructure();
        tlv.AddUInt8(1, 5);                // Privilege: Administer (5)
        tlv.AddUInt8(2, 2);                // AuthMode: CASE (2)
        tlv.AddArray(3);                   // Subjects
        tlv.AddUInt64(0x112233UL);         // Anonymous array element
        tlv.EndContainer();                // /Subjects
        tlv.EndContainer();                // /ACL entry
        tlv.EndContainer();                // /ACL array
        tlv.EndContainer();                // /root

        var bytes = tlv.GetBytes();
        Assert.That(bytes, Has.Length.GreaterThan(10));

        // Verify by re-reading
        var reader = new MatterTLV(bytes);
        reader.OpenStructure();
        reader.OpenArray(0);
        reader.OpenStructure();

        var privilege = reader.GetUnsignedInt8(1);
        Assert.That(privilege, Is.EqualTo(5));

        var authMode = reader.GetUnsignedInt8(2);
        Assert.That(authMode, Is.EqualTo(2));
    }
}
