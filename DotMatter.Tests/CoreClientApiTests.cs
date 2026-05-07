using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DotMatter.Core;
using DotMatter.Core.Clusters;
using DotMatter.Core.Discovery;
using DotMatter.Core.Fabrics;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Mdns;
using DotMatter.Core.Sessions;
using Org.BouncyCastle.Math;

namespace DotMatter.Tests;

[TestFixture]
public class CoreClientApiTests
{
    [Test]
    public void ReadAttributesAsync_RequiresAtLeastOnePath()
    {
        Assert.That(
            () => MatterInteractions.ReadAttributesAsync(new StubSession(), []),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("At least one attribute path is required"));
    }

    [Test]
    public void SubscribeAsync_RequiresAtLeastOnePath()
    {
        Assert.That(
            () => MatterInteractions.SubscribeAsync(new StubSession(), [], []),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("At least one attribute or event path is required"));
    }

    [Test]
    public async Task CommissionableDiscovery_BrowseAsync_ParsesTxtRecordsAndAddresses()
    {
        using var mdns = new FakeMulticastService();
        using var discovery = new CommissionableDiscovery(mdns);

        var browseTask = discovery.BrowseAsync(TimeSpan.FromMilliseconds(20));
        mdns.RaiseAnswer(CreateCommissionableAnswer());

        var nodes = await browseTask;
        var node = nodes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mdns.SentQueries, Has.Count.EqualTo(1));
            Assert.That(mdns.SentQueries[0].Questions.Single().Name.ToString(), Does.Contain("_matterc._udp.local"));
            Assert.That(node.InstanceName, Is.EqualTo("ABC123"));
            Assert.That(node.Port, Is.EqualTo(5540));
            Assert.That(node.Addresses.Select(address => address.ToString()), Does.Contain("fd00::1234"));
            Assert.That(node.LongDiscriminator, Is.EqualTo(3840));
            Assert.That(node.VendorId, Is.EqualTo((ushort)65521));
            Assert.That(node.ProductId, Is.EqualTo((ushort)32769));
            Assert.That(node.DeviceType, Is.EqualTo(257u));
            Assert.That(node.DeviceName, Is.EqualTo("Demo Device"));
            Assert.That(node.CommissioningMode, Is.EqualTo((byte)1));
            Assert.That(node.PairingHint, Is.EqualTo((ushort)33));
            Assert.That(node.PairingInstruction, Is.EqualTo("Hold button"));
            Assert.That(node.TxtRecords["VP"], Is.EqualTo("65521+32769"));
        }
    }

    [Test]
    public async Task CommissionableDiscovery_ResolveByDiscriminatorAsync_MatchesShortDiscriminator()
    {
        using var mdns = new FakeMulticastService();
        using var discovery = new CommissionableDiscovery(mdns);

        var resolveTask = discovery.ResolveByDiscriminatorAsync(0x000F, TimeSpan.FromMilliseconds(20));
        mdns.RaiseAnswer(CreateCommissionableAnswer());

        var node = await resolveTask;

        Assert.That(node?.InstanceName, Is.EqualTo("ABC123"));
    }

    [Test]
    public async Task OperationalDiscovery_ResolveNodeAsync_AssemblesSrvAndAddressAcrossResponses()
    {
        const ulong fabricId = 0xC6A4C11E27732C48;
        const ulong nodeId = 0x45FDAABF3955252F;
        var targetName = new DomainName("matter-node.local");

        using var mdns = new FakeMulticastService();
        using var discovery = new OperationalDiscovery(mdns);

        var resolveTask = discovery.ResolveNodeAsync(fabricId, nodeId, TimeSpan.FromSeconds(1));
        mdns.RaiseAnswer(CreateOperationalSrvAnswer(fabricId, nodeId, targetName, 5541));
        mdns.RaiseAnswer(CreateOperationalAddressAnswer(targetName));

        var node = await resolveTask.WaitAsync(TimeSpan.FromSeconds(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Is.Not.Null);
            Assert.That(node!.CompressedFabricId, Is.EqualTo(fabricId));
            Assert.That(node.NodeId, Is.EqualTo(nodeId));
            Assert.That(node.Address, Is.EqualTo(IPAddress.Parse("fd00::1234")));
            Assert.That(node.Port, Is.EqualTo(5541));
        }
    }

#pragma warning disable IDE0039
    [Test]
    public void GeneratedControllerApis_UseTypedStructAndBitmapSignatures()
    {
        Func<BasicInformationCluster, CancellationToken, Task<BasicInformationCluster.CapabilityMinimaStruct?>> readCapabilityMinima
            = static (cluster, ct) => cluster.ReadCapabilityMinimaAsync(ct);
        Func<GeneralCommissioningCluster, CancellationToken, Task<GeneralCommissioningCluster.BasicCommissioningInfo?>> readBasicCommissioningInfo
            = static (cluster, ct) => cluster.ReadBasicCommissioningInfoAsync(ct);
        Func<GeneralCommissioningCluster, CancellationToken, Task<ushort>> readTcAcknowledgements
            = static (cluster, ct) => cluster.ReadTCAcknowledgementsAsync(ct);
        Func<GeneralCommissioningCluster, ushort, ushort, CancellationToken, Task<InvokeResponse>> setTcAcknowledgements
            = static (cluster, version, response, ct) => cluster.SetTCAcknowledgementsAsync(version, response, ct);
        Func<NetworkCommissioningCluster, CancellationToken, Task<NetworkCommissioningCluster.NetworkInfoStruct[]?>> readNetworks
            = static (cluster, ct) => cluster.ReadNetworksAsync(ct);
        Func<NetworkCommissioningCluster, CancellationToken, Task<NetworkCommissioningCluster.WiFiBandEnum[]?>> readSupportedWiFiBands
            = static (cluster, ct) => cluster.ReadSupportedWiFiBandsAsync(ct);
        Func<NetworkCommissioningCluster, byte[]?, ulong?, CancellationToken, Task<InvokeResponse>> scanNetworks
            = static (cluster, ssid, breadcrumb, ct) => cluster.ScanNetworksAsync(ssid, breadcrumb, ct);
        Func<NetworkCommissioningCluster, byte[], byte[], ulong?, byte[]?, byte[]?, byte[]?, CancellationToken, Task<InvokeResponse>> addOrUpdateWiFiNetwork
            = static (cluster, ssid, credentials, breadcrumb, networkIdentity, clientIdentifier, possessionNonce, ct)
                => cluster.AddOrUpdateWiFiNetworkAsync(ssid, credentials, breadcrumb, networkIdentity, clientIdentifier, possessionNonce, ct);
        Func<NetworkCommissioningCluster, byte[], ulong?, CancellationToken, Task<InvokeResponse>> addOrUpdateThreadNetwork
            = static (cluster, dataset, breadcrumb, ct) => cluster.AddOrUpdateThreadNetworkAsync(dataset, breadcrumb, ct);
        Func<NetworkCommissioningCluster, byte[], ulong?, CancellationToken, Task<InvokeResponse>> removeNetwork
            = static (cluster, networkId, breadcrumb, ct) => cluster.RemoveNetworkAsync(networkId, breadcrumb, ct);
        Func<NetworkCommissioningCluster, byte[], ulong?, CancellationToken, Task<InvokeResponse>> connectNetwork
            = static (cluster, networkId, breadcrumb, ct) => cluster.ConnectNetworkAsync(networkId, breadcrumb, ct);
        Func<NetworkCommissioningCluster, byte[], byte, ulong?, CancellationToken, Task<InvokeResponse>> reorderNetwork
            = static (cluster, networkId, networkIndex, breadcrumb, ct) => cluster.ReorderNetworkAsync(networkId, networkIndex, breadcrumb, ct);
        Func<NetworkCommissioningCluster, bool, bool, ushort, CancellationToken, Task<WriteResponse>> writeInterfaceEnabled
            = static (cluster, enabled, timedRequest, timedTimeoutMs, ct) => cluster.WriteInterfaceEnabledAsync(enabled, timedRequest, timedTimeoutMs, ct);
        Func<GroupsCluster, CancellationToken, Task<GroupsCluster.NameSupportBitmap>> readGroupNameSupport
            = static (cluster, ct) => cluster.ReadNameSupportAsync(ct);
        Func<GroupsCluster, ushort, string, CancellationToken, Task<InvokeResponse>> addGroup
            = static (cluster, groupId, groupName, ct) => cluster.AddGroupAsync(groupId, groupName, ct);
        Func<GroupsCluster, ushort[], CancellationToken, Task<InvokeResponse>> getGroupMembership
            = static (cluster, groupIds, ct) => cluster.GetGroupMembershipAsync(groupIds, ct);
        Func<GroupKeyManagementCluster, CancellationToken, Task<GroupKeyManagementCluster.GroupKeyMapStruct[]?>> readGroupKeyMap
            = static (cluster, ct) => cluster.ReadGroupKeyMapAsync(ct);
        Func<GroupKeyManagementCluster, CancellationToken, Task<GroupKeyManagementCluster.GroupInfoMapStruct[]?>> readGroupTable
            = static (cluster, ct) => cluster.ReadGroupTableAsync(ct);
        Func<GroupKeyManagementCluster, GroupKeyManagementCluster.GroupKeySetStruct, CancellationToken, Task<InvokeResponse>> keySetWrite
            = static (cluster, groupKeySet, ct) => cluster.KeySetWriteAsync(groupKeySet, ct);
        Func<GroupKeyManagementCluster, ushort, CancellationToken, Task<InvokeResponse>> keySetRead
            = static (cluster, groupKeySetId, ct) => cluster.KeySetReadAsync(groupKeySetId, ct);
        Func<GroupKeyManagementCluster, CancellationToken, Task<InvokeResponse>> keySetReadAllIndices
            = static (cluster, ct) => cluster.KeySetReadAllIndicesAsync(ct);
        Func<ScenesManagementCluster, CancellationToken, Task<ScenesManagementCluster.SceneInfoStruct[]?>> readFabricSceneInfo
            = static (cluster, ct) => cluster.ReadFabricSceneInfoAsync(ct);
        Func<ScenesManagementCluster, ushort, byte, uint, string, ScenesManagementCluster.ExtensionFieldSetStruct[], CancellationToken, Task<InvokeResponse>> addScene
            = static (cluster, groupId, sceneId, transitionTime, sceneName, extensionFieldSets, ct)
                => cluster.AddSceneAsync(groupId, sceneId, transitionTime, sceneName, extensionFieldSets, ct);
        Func<ScenesManagementCluster, ushort, byte, CancellationToken, Task<InvokeResponse>> viewScene
            = static (cluster, groupId, sceneId, ct) => cluster.ViewSceneAsync(groupId, sceneId, ct);
        Func<ScenesManagementCluster, ushort, byte, uint?, CancellationToken, Task<InvokeResponse>> recallScene
            = static (cluster, groupId, sceneId, transitionTime, ct) => cluster.RecallSceneAsync(groupId, sceneId, transitionTime, ct);
        Func<ScenesManagementCluster, ushort, CancellationToken, Task<InvokeResponse>> getSceneMembership
            = static (cluster, groupId, ct) => cluster.GetSceneMembershipAsync(groupId, ct);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(readCapabilityMinima, Is.Not.Null);
            Assert.That(readBasicCommissioningInfo, Is.Not.Null);
            Assert.That(readTcAcknowledgements, Is.Not.Null);
            Assert.That(setTcAcknowledgements, Is.Not.Null);
            Assert.That(readNetworks, Is.Not.Null);
            Assert.That(readSupportedWiFiBands, Is.Not.Null);
            Assert.That(scanNetworks, Is.Not.Null);
            Assert.That(addOrUpdateWiFiNetwork, Is.Not.Null);
            Assert.That(addOrUpdateThreadNetwork, Is.Not.Null);
            Assert.That(removeNetwork, Is.Not.Null);
            Assert.That(connectNetwork, Is.Not.Null);
            Assert.That(reorderNetwork, Is.Not.Null);
            Assert.That(writeInterfaceEnabled, Is.Not.Null);
            Assert.That(readGroupNameSupport, Is.Not.Null);
            Assert.That(addGroup, Is.Not.Null);
            Assert.That(getGroupMembership, Is.Not.Null);
            Assert.That(readGroupKeyMap, Is.Not.Null);
            Assert.That(readGroupTable, Is.Not.Null);
            Assert.That(keySetWrite, Is.Not.Null);
            Assert.That(keySetRead, Is.Not.Null);
            Assert.That(keySetReadAllIndices, Is.Not.Null);
            Assert.That(readFabricSceneInfo, Is.Not.Null);
            Assert.That(addScene, Is.Not.Null);
            Assert.That(viewScene, Is.Not.Null);
            Assert.That(recallScene, Is.Not.Null);
            Assert.That(getSceneMembership, Is.Not.Null);
        }
    }

    [Test]
    public void MatterAdministration_ExposesSessionAndResilientSessionApis()
    {
        Func<ISession, ushort, CancellationToken, Task<MatterCommissioningState>> readCommissioningState
            = static (session, endpointId, ct) => MatterAdministration.ReadCommissioningStateAsync(session, endpointId, ct);
        Func<ResilientSession, ushort, CancellationToken, Task<MatterCommissioningState>> readCommissioningStateResilient
            = static (session, endpointId, ct) => MatterAdministration.ReadCommissioningStateAsync(session, endpointId, ct);
        Func<ISession, ushort, CancellationToken, Task<MatterOperationalCredentialsState>> readOperationalCredentials
            = static (session, endpointId, ct) => MatterAdministration.ReadOperationalCredentialsAsync(session, endpointId, ct);
        Func<ISession, ushort, CancellationToken, Task<MatterNetworkCommissioningState>> readNetworkCommissioningState
            = static (session, endpointId, ct) => MatterAdministration.ReadNetworkCommissioningStateAsync(session, endpointId, ct);
        Func<ResilientSession, ushort, CancellationToken, Task<MatterNetworkCommissioningState>> readNetworkCommissioningStateResilient
            = static (session, endpointId, ct) => MatterAdministration.ReadNetworkCommissioningStateAsync(session, endpointId, ct);
        Func<ISession, byte[]?, ulong?, ushort, CancellationToken, Task<MatterNetworkScanCommandResult>> scanNetworks
            = static (session, ssid, breadcrumb, endpointId, ct) => MatterAdministration.ScanNetworksAsync(session, ssid, breadcrumb, endpointId, ct);
        Func<ResilientSession, byte[], byte[], ulong?, byte[]?, byte[]?, byte[]?, ushort, CancellationToken, Task<MatterNetworkConfigCommandResult>> addOrUpdateWiFiNetwork
            = static (session, ssid, credentials, breadcrumb, networkIdentity, clientIdentifier, possessionNonce, endpointId, ct)
                => MatterAdministration.AddOrUpdateWiFiNetworkAsync(session, ssid, credentials, breadcrumb, networkIdentity, clientIdentifier, possessionNonce, endpointId, ct);
        Func<ISession, byte[], ulong?, ushort, CancellationToken, Task<MatterNetworkConfigCommandResult>> addOrUpdateThreadNetwork
            = static (session, dataset, breadcrumb, endpointId, ct) => MatterAdministration.AddOrUpdateThreadNetworkAsync(session, dataset, breadcrumb, endpointId, ct);
        Func<ISession, byte[], ulong?, ushort, CancellationToken, Task<MatterNetworkConfigCommandResult>> removeNetwork
            = static (session, networkId, breadcrumb, endpointId, ct) => MatterAdministration.RemoveNetworkAsync(session, networkId, breadcrumb, endpointId, ct);
        Func<ISession, byte[], ulong?, ushort, CancellationToken, Task<MatterConnectNetworkCommandResult>> connectNetwork
            = static (session, networkId, breadcrumb, endpointId, ct) => MatterAdministration.ConnectNetworkAsync(session, networkId, breadcrumb, endpointId, ct);
        Func<ResilientSession, byte[], byte, ulong?, ushort, CancellationToken, Task<MatterNetworkConfigCommandResult>> reorderNetwork
            = static (session, networkId, networkIndex, breadcrumb, endpointId, ct) => MatterAdministration.ReorderNetworkAsync(session, networkId, networkIndex, breadcrumb, endpointId, ct);
        Func<ISession, bool, bool, ushort, ushort, CancellationToken, Task<WriteResponse>> writeNetworkInterfaceEnabled
            = static (session, enabled, timedRequest, timedTimeoutMs, endpointId, ct) => MatterAdministration.WriteNetworkInterfaceEnabledAsync(session, enabled, timedRequest, timedTimeoutMs, endpointId, ct);
        Func<ISession, MatterEnhancedCommissioningWindowParameters, ushort, CancellationToken, Task<InvokeResponse>> openCommissioningWindow
            = static (session, parameters, endpointId, ct) => MatterAdministration.OpenCommissioningWindowAsync(session, parameters, endpointId, ct);
        Func<ResilientSession, string, ushort, CancellationToken, Task<InvokeResponse>> updateFabricLabel
            = static (session, label, endpointId, ct) => MatterAdministration.UpdateFabricLabelAsync(session, label, endpointId, ct);
        Func<ISession, ushort, CancellationToken, Task<MatterGroupsState>> readGroupsState
            = static (session, endpointId, ct) => MatterAdministration.ReadGroupsStateAsync(session, endpointId, ct);
        Func<ResilientSession, ushort, CancellationToken, Task<MatterGroupKeyManagementState>> readGroupKeyState
            = static (session, endpointId, ct) => MatterAdministration.ReadGroupKeyManagementStateAsync(session, endpointId, ct);
        Func<ISession, ushort, CancellationToken, Task<MatterScenesState>> readScenesState
            = static (session, endpointId, ct) => MatterAdministration.ReadScenesStateAsync(session, endpointId, ct);
        Func<ISession, ushort, ushort, string, CancellationToken, Task<MatterGroupCommandResult>> addGroupAdmin
            = static (session, endpointId, groupId, groupName, ct) => MatterAdministration.AddGroupAsync(session, endpointId, groupId, groupName, ct);
        Func<ISession, ushort, ushort, CancellationToken, Task<MatterGroupMembershipCommandResult>> getGroupMembershipAdmin
            = static (session, endpointId, groupId, ct) => MatterAdministration.GetGroupMembershipAsync(session, endpointId, [groupId], ct);
        Func<ResilientSession, ushort, CancellationToken, Task<MatterInvokeCommandResult>> removeAllGroupsAdmin
            = static (session, endpointId, ct) => MatterAdministration.RemoveAllGroupsAsync(session, endpointId, ct);
        Func<ISession, MatterGroupKeySet, ushort, CancellationToken, Task<MatterInvokeCommandResult>> writeGroupKeySet
            = static (session, groupKeySet, endpointId, ct) => MatterAdministration.WriteGroupKeySetAsync(session, groupKeySet, endpointId, ct);
        Func<ISession, ushort, ushort, CancellationToken, Task<MatterGroupKeySetReadCommandResult>> readGroupKeySet
            = static (session, groupKeySetId, endpointId, ct) => MatterAdministration.ReadGroupKeySetAsync(session, groupKeySetId, endpointId, ct);
        Func<ResilientSession, ushort, CancellationToken, Task<MatterGroupKeySetReadAllIndicesCommandResult>> readAllGroupKeySetIndices
            = static (session, endpointId, ct) => MatterAdministration.ReadAllGroupKeySetIndicesAsync(session, endpointId, ct);
        Func<ISession, ushort, ushort, byte, uint, string, IReadOnlyList<MatterSceneExtensionFieldSet>, CancellationToken, Task<MatterSceneCommandResult>> addSceneAdmin
            = static (session, endpointId, groupId, sceneId, transitionTime, sceneName, extensionFieldSets, ct)
                => MatterAdministration.AddSceneAsync(session, endpointId, groupId, sceneId, transitionTime, sceneName, extensionFieldSets, ct);
        Func<ISession, ushort, ushort, byte, CancellationToken, Task<MatterViewSceneCommandResult>> viewSceneAdmin
            = static (session, endpointId, groupId, sceneId, ct) => MatterAdministration.ViewSceneAsync(session, endpointId, groupId, sceneId, ct);
        Func<ISession, ushort, ushort, byte, uint?, CancellationToken, Task<MatterInvokeCommandResult>> recallSceneAdmin
            = static (session, endpointId, groupId, sceneId, transitionTime, ct) => MatterAdministration.RecallSceneAsync(session, endpointId, groupId, sceneId, transitionTime, ct);
        Func<ResilientSession, ushort, ushort, CancellationToken, Task<MatterSceneMembershipCommandResult>> getSceneMembershipAdmin
            = static (session, endpointId, groupId, ct) => MatterAdministration.GetSceneMembershipAsync(session, endpointId, groupId, ct);
        Func<ISession, ushort, bool, ushort, byte, ushort, byte, CancellationToken, Task<MatterSceneCopyCommandResult>> copySceneAdmin
            = static (session, endpointId, copyAllScenes, groupFrom, sceneFrom, groupTo, sceneTo, ct)
                => MatterAdministration.CopySceneAsync(session, endpointId, copyAllScenes, groupFrom, sceneFrom, groupTo, sceneTo, ct);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(readCommissioningState, Is.Not.Null);
            Assert.That(readCommissioningStateResilient, Is.Not.Null);
            Assert.That(readOperationalCredentials, Is.Not.Null);
            Assert.That(readNetworkCommissioningState, Is.Not.Null);
            Assert.That(readNetworkCommissioningStateResilient, Is.Not.Null);
            Assert.That(scanNetworks, Is.Not.Null);
            Assert.That(addOrUpdateWiFiNetwork, Is.Not.Null);
            Assert.That(addOrUpdateThreadNetwork, Is.Not.Null);
            Assert.That(removeNetwork, Is.Not.Null);
            Assert.That(connectNetwork, Is.Not.Null);
            Assert.That(reorderNetwork, Is.Not.Null);
            Assert.That(writeNetworkInterfaceEnabled, Is.Not.Null);
            Assert.That(openCommissioningWindow, Is.Not.Null);
            Assert.That(updateFabricLabel, Is.Not.Null);
            Assert.That(readGroupsState, Is.Not.Null);
            Assert.That(readGroupKeyState, Is.Not.Null);
            Assert.That(readScenesState, Is.Not.Null);
            Assert.That(addGroupAdmin, Is.Not.Null);
            Assert.That(getGroupMembershipAdmin, Is.Not.Null);
            Assert.That(removeAllGroupsAdmin, Is.Not.Null);
            Assert.That(writeGroupKeySet, Is.Not.Null);
            Assert.That(readGroupKeySet, Is.Not.Null);
            Assert.That(readAllGroupKeySetIndices, Is.Not.Null);
            Assert.That(addSceneAdmin, Is.Not.Null);
            Assert.That(viewSceneAdmin, Is.Not.Null);
            Assert.That(recallSceneAdmin, Is.Not.Null);
            Assert.That(getSceneMembershipAdmin, Is.Not.Null);
            Assert.That(copySceneAdmin, Is.Not.Null);
        }
    }
#pragma warning restore IDE0039

    [Test]
    public async Task ResilientSession_UseSessionAsync_UsesExistingConnectedSession()
    {
        var resilientSession = new ResilientSession(
            new Fabric(),
            BigInteger.Zero,
            compressedFabricId: "0",
            nodeOperationalId: "0",
            initialIp: IPAddress.Loopback);
        var secureSession = new CaseSecureSession(new FakeConnection(), 1, 2, 3, 4, new byte[16], new byte[16]);

        SetPrivateField(resilientSession, "_session", secureSession);
        SetPrivateField(resilientSession, "_lastActivity", DateTime.UtcNow);

        var result = await resilientSession.UseSessionAsync(session => Task.FromResult(session.SessionId));

        Assert.That(result, Is.EqualTo((ushort)3));
    }

    [Test]
    public void MatterAdministration_FormatInteractionModelStatus_UsesKnownStatusName()
    {
        var formatted = MatterAdministration.FormatInteractionModelStatus((byte)MatterStatusCode.FailsafeRequired);

        Assert.That(formatted, Is.EqualTo("status=FailsafeRequired (0xCA)"));
    }

    [Test]
    public void MatterAdministration_FormatInteractionModelStatus_FallsBackToHexForUnknownStatus()
    {
        var formatted = MatterAdministration.FormatInteractionModelStatus(0xEE);

        Assert.That(formatted, Is.EqualTo("status=0xEE"));
    }

    private static Message CreateCommissionableAnswer()
    {
        var serviceName = new DomainName("_matterc._udp.local");
        var instanceName = new DomainName("ABC123._matterc._udp.local");
        var targetName = new DomainName("abc123-host.local");

        var message = new Message
        {
            QR = true,
            Opcode = MessageOperation.Query
        };

        message.Answers.Add(new PTRRecord
        {
            Name = serviceName,
            DomainName = instanceName
        });

        message.AdditionalRecords.Add(new SRVRecord
        {
            Name = instanceName,
            Target = targetName,
            Port = 5540
        });

        message.AdditionalRecords.Add(new TXTRecord
        {
            Name = instanceName,
            Strings =
            [
                "D=3840",
                "VP=65521+32769",
                "DT=257",
                "DN=Demo Device",
                "CM=1",
                "PH=33",
                "PI=Hold button",
            ]
        });

        message.AdditionalRecords.Add(new AAAARecord
        {
            Name = targetName,
            Address = IPAddress.Parse("fd00::1234")
        });

        return message;
    }

    private static Message CreateOperationalSrvAnswer(ulong fabricId, ulong nodeId, DomainName targetName, ushort port)
    {
        var serviceName = new DomainName("_matter._tcp.local");
        var instanceName = new DomainName($"{fabricId:X16}-{nodeId:X16}._matter._tcp.local");

        var message = new Message
        {
            QR = true,
            Opcode = MessageOperation.Query
        };

        message.Answers.Add(new PTRRecord
        {
            Name = serviceName,
            DomainName = instanceName
        });

        message.AdditionalRecords.Add(new SRVRecord
        {
            Name = instanceName,
            Target = targetName,
            Port = port
        });

        return message;
    }

    private static Message CreateOperationalAddressAnswer(DomainName targetName)
    {
        var message = new Message
        {
            QR = true,
            Opcode = MessageOperation.Query
        };

        message.Answers.Add(new AAAARecord
        {
            Name = targetName,
            Address = IPAddress.Parse("fe80::1234")
        });

        message.Answers.Add(new AAAARecord
        {
            Name = targetName,
            Address = IPAddress.Parse("fd00::1234")
        });

        return message;
    }

    private sealed class StubSession : DotMatter.Core.Sessions.ISession
    {
        private uint _messageCounter;

        public IConnection Connection { get; } = new FakeConnection();

        public ulong SourceNodeId => 1;

        public ulong DestinationNodeId => 2;

        public ushort SessionId => 3;

        public ushort PeerSessionId => 4;

        public bool UseMRP => false;

        public uint MessageCounter => ++_messageCounter;

        public MessageExchange CreateExchange() => throw new NotSupportedException();

        public byte[] Encode(MessageFrame message) => throw new NotSupportedException();

        public MessageFrame Decode(MessageFrameParts messageFrameParts) => throw new NotSupportedException();

        public Task<byte[]> ReadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SendAsync(byte[] payload) => throw new NotSupportedException();

        public void Close() => throw new NotSupportedException();

        public IConnection CreateNewConnection() => throw new NotSupportedException();
    }

    private sealed class FakeMulticastService : IMulticastService
    {
        public event EventHandler<MessageEventArgs>? QueryReceived
        {
            add
            {
            }
            remove
            {
            }
        }

        public event EventHandler<MessageEventArgs>? AnswerReceived;
        public event EventHandler<byte[]>? MalformedMessage
        {
            add
            {
            }
            remove
            {
            }
        }

        public event EventHandler<NetworkInterfaceEventArgs>? NetworkInterfaceDiscovered
        {
            add
            {
            }
            remove
            {
            }
        }

        public bool UseIpv4
        {
            get; set;
        }
        public bool UseIpv6
        {
            get; set;
        }
        public bool IgnoreDuplicateMessages
        {
            get; set;
        }

        public List<Message> SentQueries { get; } = [];

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void SendQuery(DomainName name, DnsClass @class = DnsClass.IN, DnsType type = DnsType.ANY)
            => SendQuery(CreateQuery(name, @class, type));

        public void SendUnicastQuery(DomainName name, DnsClass @class = DnsClass.IN, DnsType type = DnsType.ANY)
            => SendQuery(CreateQuery(name, (DnsClass)((ushort)@class | MulticastService.UNICAST_RESPONSE_BIT), type));

        public void SendQuery(Message msg)
            => SentQueries.Add(msg.Clone<Message>());

        public void SendAnswer(Message answer, bool checkDuplicate = true, IPEndPoint? unicastEndpoint = null)
        {
        }

        public void SendAnswer(Message answer, MessageEventArgs query, bool checkDuplicate = true, IPEndPoint? endPoint = null)
        {
        }

        public void OnDnsMessage(object sender, UdpReceiveResult result)
        {
        }

        public void RaiseAnswer(Message message)
            => AnswerReceived?.Invoke(this, new MessageEventArgs
            {
                Message = message,
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5353)
            });

        public void Dispose()
        {
        }

        private static Message CreateQuery(DomainName name, DnsClass @class, DnsType type)
        {
            var message = new Message
            {
                QR = false,
                Opcode = MessageOperation.Query
            };

            message.Questions.Add(new Question
            {
                Name = name,
                Class = @class,
                Type = type
            });

            return message;
        }
    }

    private static void SetPrivateField<T>(object instance, string name, T value)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        field.SetValue(instance, value);
    }
}
