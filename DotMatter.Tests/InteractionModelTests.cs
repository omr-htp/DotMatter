using System.Reflection;
using DotMatter.Core;
using DotMatter.Core.Clusters;
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
    public void ReportDataAction_WithMultipleAttributeStatuses_ClosesStatusContainers()
    {
        var writer = new MatterTLV();
        writer.AddStructure();
        writer.AddArray(1);
        AddAttributeStatusReport(writer, OnOffCluster.Attributes.OnOff, MatterStatusCode.UnsupportedAttribute);
        AddAttributeStatusReport(writer, OnOffCluster.Attributes.GlobalSceneControl, MatterStatusCode.ConstraintError);
        writer.EndContainer();
        writer.EndContainer();

        var report = new ReportDataAction(new MatterTLV(writer.GetBytes()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.AttributeReports, Has.Count.EqualTo(2));
            Assert.That(report.AttributeReports[0].AttributeStatus!.Path.AttributeId, Is.EqualTo(OnOffCluster.Attributes.OnOff));
            Assert.That(report.AttributeReports[0].AttributeStatus!.Status.Status, Is.EqualTo((byte)MatterStatusCode.UnsupportedAttribute));
            Assert.That(report.AttributeReports[1].AttributeStatus!.Path.AttributeId, Is.EqualTo(OnOffCluster.Attributes.GlobalSceneControl));
            Assert.That(report.AttributeReports[1].AttributeStatus!.Status.Status, Is.EqualTo((byte)MatterStatusCode.ConstraintError));
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

    [Test]
    public void WriteResponse_StatusResponseSuccess_IsSuccessfulWithNoAttributeStatuses()
    {
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddUInt8(0, 0x00);
        tlv.AddUInt8(255, 12);
        tlv.EndContainer();

        var response = InteractionManager.ParseWriteResponse(
            CreateImFrame(0x01, tlv),
            endpointId: 1,
            clusterId: AccessControlCluster.ClusterId,
            attributeId: AccessControlCluster.Attributes.ACL);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Success, Is.True);
            Assert.That(response.StatusCode, Is.Zero);
            Assert.That(response.AttributeStatuses, Is.Empty);
        }
    }

    [Test]
    public void WriteResponse_StatusResponseFailure_ExposesStatusForAttribute()
    {
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddUInt8(0, (byte)MatterStatusCode.NeedsTimedInteraction);
        tlv.AddUInt8(255, 12);
        tlv.EndContainer();

        var response = InteractionManager.ParseWriteResponse(
            CreateImFrame(0x01, tlv),
            endpointId: 1,
            clusterId: BindingCluster.ClusterId,
            attributeId: BindingCluster.Attributes.Binding);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.StatusCode, Is.EqualTo((byte)MatterStatusCode.NeedsTimedInteraction));
            Assert.That(response.AttributeStatuses, Has.Count.EqualTo(1));
            Assert.That(response.AttributeStatuses[0].StatusCode, Is.EqualTo((byte)MatterStatusCode.NeedsTimedInteraction));
            Assert.That(response.AttributeStatuses[0].AttributeId, Is.EqualTo(BindingCluster.Attributes.Binding));
        }
    }

    [Test]
    public void WriteResponse_WriteResponseStatus_ParsesPathAndClusterStatus()
    {
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddArray(0);
        tlv.AddStructure();
        tlv.AddList(0);
        tlv.AddUInt16(2, 1);
        tlv.AddUInt32(3, AccessControlCluster.ClusterId);
        tlv.AddUInt32(4, AccessControlCluster.Attributes.ACL);
        tlv.EndContainer();
        tlv.AddStructure(1);
        tlv.AddUInt8(0, (byte)MatterStatusCode.ConstraintError);
        tlv.AddUInt8(1, 0x05);
        tlv.EndContainer();
        tlv.EndContainer();
        tlv.EndContainer();
        tlv.AddUInt8(255, 12);
        tlv.EndContainer();

        var response = InteractionManager.ParseWriteResponse(
            CreateImFrame(0x07, tlv),
            endpointId: 0,
            clusterId: 0,
            attributeId: 0xFFFF);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.AttributeStatuses, Has.Count.EqualTo(1));
            Assert.That(response.AttributeStatuses[0].EndpointId, Is.EqualTo((ushort)1));
            Assert.That(response.AttributeStatuses[0].ClusterId, Is.EqualTo(AccessControlCluster.ClusterId));
            Assert.That(response.AttributeStatuses[0].AttributeId, Is.EqualTo(AccessControlCluster.Attributes.ACL));
            Assert.That(response.AttributeStatuses[0].StatusCode, Is.EqualTo((byte)MatterStatusCode.ConstraintError));
            Assert.That(response.AttributeStatuses[0].ClusterStatusCode, Is.EqualTo(0x05));
        }
    }

    [Test]
    public void InvokeResponse_CommandStatusWithoutStatusCode_FailsInsteadOfReportingSuccess()
    {
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddArray(1);
        tlv.AddStructure();
        tlv.AddStructure(1);
        tlv.AddList(0);
        tlv.AddUInt16(2, 1);
        tlv.AddUInt32(3, GeneralCommissioningCluster.ClusterId);
        tlv.AddUInt32(4, GeneralCommissioningCluster.Commands.ArmFailSafe);
        tlv.EndContainer();
        tlv.AddStructure(1);
        tlv.EndContainer();
        tlv.EndContainer();
        tlv.EndContainer();
        tlv.AddUInt8(255, 12);
        tlv.EndContainer();

        var response = ParseInvokeResponse(CreateImFrame(0x09, tlv));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("missing status code"));
        }
    }

    [Test]
    public void InvokeResponse_StatusResponse_IsFailureWithDetail()
    {
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddUInt8(0, (byte)MatterStatusCode.ConstraintError);
        tlv.AddUInt8(255, 12);
        tlv.EndContainer();

        var response = ParseInvokeResponse(CreateImFrame(0x01, tlv));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.StatusCode, Is.EqualTo((byte)MatterStatusCode.ConstraintError));
            Assert.That(response.Error, Does.Contain("Unexpected StatusResponse"));
        }
    }

    [Test]
    public void AccessControlEntryStruct_ReadsFabricScopedAclEntry()
    {
        const ulong subject = 1606652096310243993UL;
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddUInt8(1, (byte)AccessControlCluster.AccessControlEntryPrivilegeEnum.Operate);
        tlv.AddUInt8(2, (byte)AccessControlCluster.AccessControlEntryAuthModeEnum.CASE);
        tlv.AddArray(3);
        tlv.AddUInt64(subject);
        tlv.EndContainer();
        tlv.AddArray(4);
        tlv.AddStructure();
        tlv.AddUInt32(0, 0x0006);
        tlv.AddUInt16(1, 1);
        tlv.AddNull(2);
        tlv.EndContainer();
        tlv.EndContainer();
        tlv.AddUInt8(254, 1);
        tlv.EndContainer();

        var entry = InvokePrivateStructReader<AccessControlCluster.AccessControlEntryStruct>(
            typeof(AccessControlCluster),
            "ReadAccessControlEntryStruct",
            tlv);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Privilege, Is.EqualTo(AccessControlCluster.AccessControlEntryPrivilegeEnum.Operate));
            Assert.That(entry.AuthMode, Is.EqualTo(AccessControlCluster.AccessControlEntryAuthModeEnum.CASE));
            Assert.That(entry.Subjects, Is.EquivalentTo([subject]));
            Assert.That(entry.Targets, Has.Length.EqualTo(1));
            Assert.That(entry.Targets![0].Cluster, Is.EqualTo(0x0006));
            Assert.That(entry.Targets[0].Endpoint, Is.EqualTo((ushort)1));
            Assert.That(entry.FabricIndex, Is.EqualTo(1));
        }
    }

    [Test]
    public void BindingTargetStruct_ReadsNodeEndpointClusterBinding()
    {
        const ulong h2NodeId = 13720259805670301903UL;
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddUInt64(1, h2NodeId);
        tlv.AddUInt16(3, 1);
        tlv.AddUInt32(4, 0x0006);
        tlv.AddUInt8(254, 1);
        tlv.EndContainer();

        var target = InvokePrivateStructReader<BindingCluster.TargetStruct>(
            typeof(BindingCluster),
            "ReadTargetStruct",
            tlv);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Node, Is.EqualTo(h2NodeId));
            Assert.That(target.Group, Is.Null);
            Assert.That(target.Endpoint, Is.EqualTo((ushort)1));
            Assert.That(target.Cluster, Is.EqualTo(0x0006));
            Assert.That(target.FabricIndex, Is.EqualTo(1));
        }
    }

    [Test]
    public void AccessControlEntryStruct_WritesNullTargetsForAdminEntry()
    {
        var entry = new AccessControlCluster.AccessControlEntryStruct
        {
            Privilege = AccessControlCluster.AccessControlEntryPrivilegeEnum.Administer,
            AuthMode = AccessControlCluster.AccessControlEntryAuthModeEnum.CASE,
            Subjects = [0x112233UL],
            Targets = null,
        };
        var tlv = new MatterTLV();

        InvokePrivateStructWriter(typeof(AccessControlCluster), "WriteAccessControlEntryStruct", tlv, entry);

        tlv.OpenStructure();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tlv.SkipToTag(4), Is.True);
            Assert.That(tlv.IsNextNull(), Is.True);
        }
        tlv.GetNull(4);
        Assert.That(tlv.SkipToTag(254), Is.False);
    }

    [Test]
    public void AccessControlTargetStruct_WritesRequiredNullableFields()
    {
        var target = new AccessControlCluster.AccessControlTargetStruct
        {
            Cluster = 0x0006,
            Endpoint = 1,
            DeviceType = null,
        };
        var tlv = new MatterTLV();

        InvokePrivateStructWriter(typeof(AccessControlCluster), "WriteAccessControlTargetStruct", tlv, target);

        tlv.OpenStructure();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tlv.SkipToTag(2), Is.True);
            Assert.That(tlv.IsNextNull(), Is.True);
        }
    }

    private static MessageFrame CreateImFrame(byte opCode, MatterTLV tlv)
        => new(new MessagePayload(tlv)
        {
            ProtocolId = 0x01,
            ProtocolOpCode = opCode,
        });

    private static InvokeResponse ParseInvokeResponse(MessageFrame frame)
        => (InvokeResponse)typeof(InteractionManager)
            .GetMethod("ParseInvokeResponse", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [frame])!;

    private static void AddAttributeStatusReport(MatterTLV tlv, uint attributeId, MatterStatusCode status)
    {
        tlv.AddStructure();
        tlv.AddStructure(0);
        tlv.AddList(0);
        tlv.AddUInt16(2, 1);
        tlv.AddUInt32(3, OnOffCluster.ClusterId);
        tlv.AddUInt32(4, attributeId);
        tlv.EndContainer();
        tlv.AddStructure(1);
        tlv.AddUInt8(0, (byte)status);
        tlv.EndContainer();
        tlv.EndContainer();
        tlv.EndContainer();
    }

    private static T InvokePrivateStructReader<T>(Type clusterType, string methodName, MatterTLV tlv)
    {
        var method = clusterType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(method => method.Name == methodName
                && method.GetParameters() is [{ ParameterType: var tlvType }]
                && tlvType == typeof(MatterTLV))
            ?? throw new MissingMethodException(clusterType.FullName, methodName);
        return (T)method.Invoke(null, [tlv])!;
    }

    private static void InvokePrivateStructWriter(Type clusterType, string methodName, MatterTLV tlv, object value)
    {
        var method = clusterType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == methodName
                && method.GetParameters() is [{ ParameterType: var tlvType }, { ParameterType: var valueType }]
                && tlvType == typeof(MatterTLV)
                && valueType == value.GetType());
        method.Invoke(null, [tlv, value]);
    }
}
