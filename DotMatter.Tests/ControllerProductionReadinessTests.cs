using System.Net;
using System.Net.Http.Json;
using DotMatter.Controller;
using DotMatter.Controller.Models;
using DotMatter.Core.Commissioning;
using DotMatter.Hosting.Thread;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotMatter.Tests;

/// <summary>
/// Integration tests using WebApplicationFactory to test the Controller HTTP pipeline.
/// Uses <see cref="ControllerOptions"/> as the entry-point marker type
/// (avoids ambiguity with CodeGen's Program class).
/// </summary>
[TestFixture]
public class ControllerProductionReadinessTests
{
    [Test]
    public async Task ApiRequiresHeaderApiKeyByDefault()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var unauthorized = await client.GetAsync("/api/devices");
        var queryKeyOnly = await client.GetAsync("/api/devices?api_key=test-key");

        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");
        var authorized = await client.GetAsync("/api/devices");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)unauthorized.StatusCode, Is.EqualTo(401));
            Assert.That((int)queryKeyOnly.StatusCode, Is.EqualTo(401));
            Assert.That((int)authorized.StatusCode, Is.EqualTo(200));
        }
    }

    [Test]
    public async Task DiscoveryBrowseRejectsInvalidTimeout()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var response = await client.GetAsync("/api/discovery/commissionable?timeoutMs=0");
        var body = await response.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(400));
            Assert.That(body, Does.Contain("timeoutMs"));
        }
    }

    [Test]
    public async Task DiscoveryBrowseReturnsTypedResponse()
    {
        var sampleDevice = new CommissionableDeviceResponse(
            Transport: "mdns",
            InstanceName: "ABC123",
            FullyQualifiedName: "ABC123._matterc._udp.local.",
            Port: 5540,
            Addresses: ["fd00::1234"],
            PreferredAddress: "fd00::1234",
            BluetoothAddress: null,
            Rssi: null,
            LongDiscriminator: 3840,
            LongDiscriminatorHex: "0xF00",
            ShortDiscriminator: 15,
            ShortDiscriminatorHex: "0xF",
            VendorId: 65521,
            VendorIdHex: "0xFFF1",
            ProductId: 32769,
            ProductIdHex: "0x8001",
            DeviceType: 257,
            DeviceTypeHex: "0x00000101",
            DeviceName: "Demo Device",
            CommissioningMode: 1,
            PairingHint: 33,
            PairingInstruction: "Hold button",
            RotatingIdentifier: "ABCDEF",
            AdvertisementVersion: null,
            ServiceDataHex: null,
            TxtRecords: new Dictionary<string, string> { ["VP"] = "65521+32769" });

        await using var factory = CreateFactory(configureServices: services =>
            services.AddSingleton<ICommissionableDeviceDiscoveryService>(
                new FakeCommissionableDeviceDiscoveryService(
                    new CommissionableDeviceBrowseResponse(500, 1, 1, 1, [sampleDevice]),
                    sampleDevice)));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var response = await client.GetAsync("/api/discovery/commissionable?timeoutMs=500&discriminator=0xF00&includeTxtRecords=true");
        var body = await response.Content.ReadFromJsonAsync(ControllerJsonContext.Default.CommissionableDeviceBrowseResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(body?.MatchedCount, Is.EqualTo(1));
            Assert.That(body?.Devices.Single().InstanceName, Is.EqualTo("ABC123"));
            Assert.That(body?.Devices.Single().TxtRecords?["VP"], Is.EqualTo("65521+32769"));
        }
    }

    [Test]
    public async Task DiscoveryBrowseDefaultsIncludeTxtRecordsToFalse()
    {
        var sampleDevice = new CommissionableDeviceResponse(
            Transport: "mdns",
            InstanceName: "ABC123",
            FullyQualifiedName: "ABC123._matterc._udp.local.",
            Port: 5540,
            Addresses: ["fd00::1234"],
            PreferredAddress: "fd00::1234",
            BluetoothAddress: null,
            Rssi: null,
            LongDiscriminator: 3840,
            LongDiscriminatorHex: "0xF00",
            ShortDiscriminator: 15,
            ShortDiscriminatorHex: "0xF",
            VendorId: 65521,
            VendorIdHex: "0xFFF1",
            ProductId: 32769,
            ProductIdHex: "0x8001",
            DeviceType: 257,
            DeviceTypeHex: "0x00000101",
            DeviceName: "Demo Device",
            CommissioningMode: 1,
            PairingHint: 33,
            PairingInstruction: "Hold button",
            RotatingIdentifier: "ABCDEF",
            AdvertisementVersion: null,
            ServiceDataHex: null,
            TxtRecords: null);

        await using var factory = CreateFactory(configureServices: services =>
            services.AddSingleton<ICommissionableDeviceDiscoveryService>(
                new FakeCommissionableDeviceDiscoveryService(
                    new CommissionableDeviceBrowseResponse(500, 1, 1, 1, [sampleDevice]),
                    sampleDevice)));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var response = await client.GetAsync("/api/discovery/commissionable?timeoutMs=500");
        var body = await response.Content.ReadFromJsonAsync(ControllerJsonContext.Default.CommissionableDeviceBrowseResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(body?.ReturnedCount, Is.EqualTo(1));
            Assert.That(body?.Devices.Single().TxtRecords, Is.Null);
        }
    }

    [Test]
    public async Task DiscoveryResolveRejectsInvalidDiscriminator()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var response = await client.GetAsync("/api/discovery/commissionable/resolve?discriminator=0x1000");
        var body = await response.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(400));
            Assert.That(body, Does.Contain("Discriminator"));
        }
    }

    [Test]
    public async Task UnknownOriginsDoNotReceiveCorsHeadersByDefault()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");
        client.DefaultRequestHeaders.Add("Origin", "https://example.com");

        var response = await client.GetAsync("/api/devices");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(response.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
        }
    }

    [Test]
    public async Task RateLimiterRejectsRequestsAfterConfiguredBudget()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Controller:Api:RateLimitPermitLimit"] = "1",
            ["Controller:Api:RateLimitQueueLimit"] = "0",
            ["Controller:Api:RateLimitWindow"] = "00:10:00",
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var first = await client.GetAsync("/api/devices");
        var second = await client.GetAsync("/api/devices");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)first.StatusCode, Is.EqualTo(200));
            Assert.That((int)second.StatusCode, Is.EqualTo(429));
        }
    }

    [Test]
    public async Task HealthEndpointsRemainAnonymousAndReadyAfterStartup()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)live.StatusCode, Is.EqualTo(200));
            Assert.That((int)ready.StatusCode, Is.EqualTo(200));
        }
    }

    [Test]
    public async Task DevelopmentEnvironmentCanDisableApiKeyThroughAppSettingsOverride()
    {
        await using var factory = CreateFactory(environment: "Development");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/devices");

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task BindingQueryEndpointsValidateInputAndKnownDevices()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var invalidFabricEndpoint = await client.GetAsync("/api/bindings?endpoint=0");
        var unknownDevice = await client.GetAsync("/api/devices/missing/bindings");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)invalidFabricEndpoint.StatusCode, Is.EqualTo(400));
            Assert.That((int)unknownDevice.StatusCode, Is.EqualTo(404));
        }
    }

    [Test]
    public async Task DeviceCapabilitiesEndpointDefaultsRefreshQueryToFalse()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var response = await client.GetAsync("/api/devices/missing/capabilities");

        Assert.That((int)response.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task AclQueryEndpointsValidateKnownDevicesAndMissingFabric()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var missingFabric = await client.GetAsync("/api/acls?fabricName=DotMatter");
        var unknownDevice = await client.GetAsync("/api/devices/missing/acl");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)missingFabric.StatusCode, Is.EqualTo(404));
            Assert.That((int)unknownDevice.StatusCode, Is.EqualTo(404));
        }
    }

    [Test]
    public async Task RuntimeDiagnosticsEndpointRequiresAuthAndDetailedEndpointIsDisabledByDefault()
    {
        await using var factory = CreateFactory();
        var anonymousClient = factory.CreateClient();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var unauthorized = await anonymousClient.GetAsync("/api/system/runtime");
        var runtime = await client.GetFromJsonAsync<RuntimeSnapshotResponse>("/api/system/runtime");
        var detailed = await client.GetAsync("/api/system/diagnostics");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)unauthorized.StatusCode, Is.EqualTo(401));
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime!.Environment, Is.EqualTo("Testing"));
            Assert.That(runtime.Ready, Is.True);
            Assert.That(runtime.Counters.ApiAuthenticationFailures, Is.GreaterThanOrEqualTo(1));
            Assert.That((int)detailed.StatusCode, Is.EqualTo(404));
        }
    }

    [Test]
    public async Task MatterEventEndpointsRequireAuthAndValidateInput()
    {
        await using var factory = CreateFactory();
        var anonymousClient = factory.CreateClient();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var unauthorized = await anonymousClient.GetAsync("/api/matter/events");
        const string unknownDeviceId = "this-device-does-not-exist";
        var missingCluster = await client.GetAsync($"/api/devices/{unknownDeviceId}/matter/events");
        var invalidCluster = await client.GetAsync($"/api/devices/{unknownDeviceId}/matter/events?cluster=nope");
        var invalidEvent = await client.GetAsync($"/api/devices/{unknownDeviceId}/matter/events?cluster=0x003B&eventId=nope");
        var unknownDevice = await client.GetAsync($"/api/devices/{unknownDeviceId}/matter/events?cluster=0x003B");

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)unauthorized.StatusCode, Is.EqualTo(401));
            Assert.That((int)missingCluster.StatusCode, Is.EqualTo(400));
            Assert.That((int)invalidCluster.StatusCode, Is.EqualTo(400));
            Assert.That((int)invalidEvent.StatusCode, Is.EqualTo(400));
            Assert.That((int)unknownDevice.StatusCode, Is.AnyOf(404, 503));
        }
    }

    [Test]
    public async Task MatterEventSseEndpointReturnsEventStreamForAuthorizedClients()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        using var response = await client.GetAsync("/api/matter/events", HttpCompletionOption.ResponseHeadersRead);

        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));
    }

    [Test]
    public async Task DetailedRuntimeDiagnosticsEndpointCanBeEnabledByConfig()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Controller:Diagnostics:EnableDetailedRuntimeEndpoint"] = "true",
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var detailed = await client.GetFromJsonAsync<RuntimeDetailedResponse>("/api/system/diagnostics");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detailed, Is.Not.Null);
            Assert.That(detailed!.Diagnostics.DetailedEndpointEnabled, Is.True);
            Assert.That(detailed.Diagnostics.SensitiveDiagnosticsEnabled, Is.False);
            Assert.That(detailed.Api.RequireApiKey, Is.True);
            Assert.That(detailed.Runtime.Environment, Is.EqualTo("Testing"));
            Assert.That(detailed.Diagnostics.RegulatoryLocation, Is.EqualTo("IndoorOutdoor"));
            Assert.That(detailed.Diagnostics.RegulatoryCountryCode, Is.EqualTo("XX"));
            Assert.That(detailed.Diagnostics.AttestationPolicy, Is.EqualTo("RequireDacChain"));
        }
    }

    [Test]
    public async Task RouteRemovalEndpointsValidateInputAndKnownDevices()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var invalidBindingRemove = await client.PostAsJsonAsync("/api/devices/missing/bindings/remove", new DeviceBindingRemovalRequest());
        var invalidAclRemove = await client.PostAsJsonAsync("/api/devices/missing/acl/remove", new DeviceAclRemovalRequest("Nope", "CASE", ["1"]));
        var unknownRawBindingDevice = await client.PostAsJsonAsync("/api/devices/missing/bindings/remove", new DeviceBindingRemovalRequest(Endpoint: 1, Cluster: 0x0006));
        var unknownRouteDevice = await client.PostAsJsonAsync("/api/devices/missing/bindings/onoff/remove", new SwitchBindingRequest("target"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)invalidBindingRemove.StatusCode, Is.EqualTo(400));
            Assert.That((int)invalidAclRemove.StatusCode, Is.EqualTo(400));
            Assert.That((int)unknownRawBindingDevice.StatusCode, Is.EqualTo(404));
            Assert.That((int)unknownRouteDevice.StatusCode, Is.EqualTo(404));
        }
    }

    [Test]
    public async Task NetworkCommissioningEndpointsValidateInputAndKnownDevices()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var unknownState = await client.GetAsync("/api/devices/missing/network-commissioning");
        var unknownScan = await client.PostAsJsonAsync("/api/devices/missing/network-commissioning/scan", new NetworkCommissioningScanRequest());
        var invalidWifi = await client.PostAsJsonAsync("/api/devices/missing/network-commissioning/wifi", new NetworkCommissioningWiFiRequest(""));
        var invalidThread = await client.PostAsJsonAsync("/api/devices/missing/network-commissioning/thread", new NetworkCommissioningThreadRequest("nope"));
        var invalidConnect = await client.PostAsJsonAsync("/api/devices/missing/network-commissioning/connect", new NetworkCommissioningNetworkIdRequest(""));
        var invalidRemove = await client.PostAsJsonAsync("/api/devices/missing/network-commissioning/remove", new NetworkCommissioningNetworkIdRequest("not-hex"));
        var invalidReorder = await client.PostAsJsonAsync("/api/devices/missing/network-commissioning/reorder", new NetworkCommissioningReorderRequest("", 0));
        var unknownInterface = await client.PostAsJsonAsync("/api/devices/missing/network-commissioning/interface-enabled", new NetworkCommissioningInterfaceEnabledRequest(true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)unknownState.StatusCode, Is.EqualTo(404));
            Assert.That((int)unknownScan.StatusCode, Is.EqualTo(404));
            Assert.That((int)invalidWifi.StatusCode, Is.EqualTo(400));
            Assert.That((int)invalidThread.StatusCode, Is.EqualTo(400));
            Assert.That((int)invalidConnect.StatusCode, Is.EqualTo(400));
            Assert.That((int)invalidRemove.StatusCode, Is.EqualTo(400));
            Assert.That((int)invalidReorder.StatusCode, Is.EqualTo(400));
            Assert.That((int)unknownInterface.StatusCode, Is.EqualTo(404));
        }
    }

    [Test]
    public async Task GroupManagementEndpointsValidateInputAndKnownDevices()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "test-key");

        var invalidGroupsEndpoint = await client.GetAsync("/api/devices/missing/groups?endpoint=0");
        var unknownGroupsEndpoint = await client.GetAsync("/api/devices/missing/groups?endpoint=1");
        var unknownGroupKeys = await client.GetAsync("/api/devices/missing/group-keys");
        var invalidScenesEndpoint = await client.GetAsync("/api/devices/missing/scenes?endpoint=0");
        var unknownScenesEndpoint = await client.GetAsync("/api/devices/missing/scenes?endpoint=1");
        var invalidAddGroup = await client.PostAsJsonAsync("/api/devices/missing/groups/add", new GroupAddRequest(0, 1, "test"));
        var unknownAddGroup = await client.PostAsJsonAsync("/api/devices/missing/groups/add", new GroupAddRequest(1, 1, "test"));
        var invalidGroupMembership = await client.PostAsJsonAsync("/api/devices/missing/groups/membership", new GroupMembershipRequest(0));
        var invalidWriteKeySet = await client.PostAsJsonAsync("/api/devices/missing/group-keys/write", new GroupKeySetWriteRequest(1, "TrustFirst", "XYZ", 1));
        var unknownReadKeySet = await client.PostAsJsonAsync("/api/devices/missing/group-keys/read", new GroupKeySetIdRequest(1));
        var invalidAddScene = await client.PostAsJsonAsync("/api/devices/missing/scenes/add", new SceneAddRequest(
            Endpoint: 1,
            GroupId: 1,
            SceneId: 1,
            TransitionTime: 0,
            SceneName: "Scene",
            ExtensionFieldSets:
            [
                new SceneExtensionFieldSetRequest(
                    ClusterId: 0x0006,
                    AttributeValues:
                    [
                        new SceneAttributeValueRequest(AttributeId: 0x0000, ValueUnsigned8: 1, ValueUnsigned16: 2)
                    ])
            ]));
        var unknownViewScene = await client.PostAsJsonAsync("/api/devices/missing/scenes/view", new SceneCommandRequest(1, 1, 1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)invalidGroupsEndpoint.StatusCode, Is.EqualTo(400));
            Assert.That((int)unknownGroupsEndpoint.StatusCode, Is.EqualTo(404));
            Assert.That((int)unknownGroupKeys.StatusCode, Is.EqualTo(404));
            Assert.That((int)invalidScenesEndpoint.StatusCode, Is.EqualTo(400));
            Assert.That((int)unknownScenesEndpoint.StatusCode, Is.EqualTo(404));
            Assert.That((int)invalidAddGroup.StatusCode, Is.EqualTo(400));
            Assert.That((int)unknownAddGroup.StatusCode, Is.EqualTo(404));
            Assert.That((int)invalidGroupMembership.StatusCode, Is.EqualTo(400));
            Assert.That((int)invalidWriteKeySet.StatusCode, Is.EqualTo(400));
            Assert.That((int)unknownReadKeySet.StatusCode, Is.EqualTo(404));
            Assert.That((int)invalidAddScene.StatusCode, Is.EqualTo(400));
            Assert.That((int)unknownViewScene.StatusCode, Is.EqualTo(404));
        }
    }

    [Test]
    public void BindingQueryDtosUseSourceGeneratedJson()
    {
        var response = new FabricBindingListResponse(
            "DotMatter",
            TotalSources: 1,
            SuccessfulSources: 1,
            FailedSources: 0,
            Sources:
            [
                new DeviceBindingListResponse(
                    "switch",
                    "Switch",
                    "device-switch",
                    Endpoint: 1,
                    Entries:
                    [
                        new DeviceBindingEntry(
                            NodeId: "1234",
                            Group: null,
                            Endpoint: 1,
                            Cluster: 0x0006,
                            ClusterHex: "0x0006",
                            FabricIndex: 1,
                            TargetDeviceId: "light",
                            TargetDeviceName: "Light")
                    ])
            ]);

        var json = System.Text.Json.JsonSerializer.Serialize(
            response,
            ControllerJsonContext.Default.FabricBindingListResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Contain("\"fabricName\":\"DotMatter\""));
            Assert.That(json, Does.Contain("\"targetDeviceId\":\"light\""));
            Assert.That(json, Does.Contain("\"clusterHex\":\"0x0006\""));
        }
    }

    [Test]
    public void RuntimeDiagnosticsDtosUseSourceGeneratedJson()
    {
        var response = new RuntimeDetailedResponse(
            new RuntimeSnapshotResponse(
                "ready",
                "Production",
                StartupCompleted: true,
                Ready: true,
                Stopping: false,
                Uptime: "0.00:10:00",
                StartedAtUtc: DateTime.UtcNow.AddMinutes(-10),
                Devices: new DeviceCounts(2, 2),
                Counters: new RuntimeDiagnosticsCounters(1, 0, 2, 3, 4, 5, 6),
                Timestamp: DateTime.UtcNow),
            new RuntimeApiDiagnostics(
                RequireApiKey: true,
                HeaderName: "X-API-Key",
                AllowedCorsOriginCount: 0,
                RateLimitPermitLimit: 60,
                RateLimitWindow: "00:01:00",
                RateLimitQueueLimit: 5,
                SseClientBufferCapacity: 100,
                CommandTimeout: "00:00:10",
                OpenApiEnabled: false),
            new RuntimeDetailedDiagnostics(
                DetailedEndpointEnabled: true,
                SensitiveDiagnosticsEnabled: false,
                MaxRenderedBytes: 32,
                SharedFabricName: "DotMatter",
                DefaultFabricNamePrefix: "device",
                FollowUpConnectTimeout: "00:00:30",
                RegulatoryLocation: "IndoorOutdoor",
                RegulatoryCountryCode: "XX",
                AttestationPolicy: "RequireDacChain"));

        var json = System.Text.Json.JsonSerializer.Serialize(
            response,
            ControllerJsonContext.Default.RuntimeDetailedResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Contain("\"status\":\"ready\""));
            Assert.That(json, Does.Contain("\"apiAuthenticationFailures\":2"));
            Assert.That(json, Does.Contain("\"rateLimitRejections\":3"));
            Assert.That(json, Does.Contain("\"detailedEndpointEnabled\":true"));
        }
    }

    [Test]
    public void NetworkCommissioningDtosUseSourceGeneratedJson()
    {
        var response = new DeviceNetworkCommissioningScanResponse(
            "light",
            "Light",
            InvokeSucceeded: true,
            Accepted: true,
            InvokeStatus: "Success (0x00)",
            InvokeStatusHex: "0x00",
            NetworkingStatus: "Success",
            DebugText: null,
            WiFiScanResults:
            [
                new DeviceNetworkCommissioningWiFiScanResult(
                    "54455354",
                    "TEST",
                    "001122334455",
                    Channel: 11,
                    WiFiBand: "_2G4",
                    Rssi: -50,
                    Security: ["WPA2PERSONAL"])
            ],
            ThreadScanResults:
            [
                new DeviceNetworkCommissioningThreadScanResult(
                    PanId: 0x1234,
                    ExtendedPanIdHex: "0011223344556677",
                    NetworkName: "thread-net",
                    Channel: 15,
                    Version: 4,
                    ExtendedAddressHex: "AABBCCDDEEFF0011",
                    Rssi: -40,
                    Lqi: 3)
            ]);

        var json = System.Text.Json.JsonSerializer.Serialize(
            response,
            ControllerJsonContext.Default.DeviceNetworkCommissioningScanResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Contain("\"sourceDeviceId\":\"light\""));
            Assert.That(json, Does.Contain("\"networkingStatus\":\"Success\""));
            Assert.That(json, Does.Contain("\"ssidText\":\"TEST\""));
            Assert.That(json, Does.Contain("\"extendedPanIdHex\":\"0011223344556677\""));
        }
    }

    [Test]
    public void DiscoveryDtosUseSourceGeneratedJson()
    {
        var device = new CommissionableDeviceResponse(
            Transport: "mdns",
            InstanceName: "ABC123",
            FullyQualifiedName: "ABC123._matterc._udp.local.",
            Port: 5540,
            Addresses: ["fd00::1234", "192.168.1.10"],
            PreferredAddress: "fd00::1234",
            BluetoothAddress: null,
            Rssi: null,
            LongDiscriminator: 3840,
            LongDiscriminatorHex: "0xF00",
            ShortDiscriminator: 15,
            ShortDiscriminatorHex: "0xF",
            VendorId: 65521,
            VendorIdHex: "0xFFF1",
            ProductId: 32769,
            ProductIdHex: "0x8001",
            DeviceType: 257,
            DeviceTypeHex: "0x00000101",
            DeviceName: "Demo Device",
            CommissioningMode: 1,
            PairingHint: 33,
            PairingInstruction: "Hold button",
            RotatingIdentifier: "ABCDEF",
            AdvertisementVersion: null,
            ServiceDataHex: null,
            TxtRecords: new Dictionary<string, string> { ["VP"] = "65521+32769" });

        var browse = new CommissionableDeviceBrowseResponse(
            BrowseWindowMs: 750,
            TotalDiscovered: 2,
            MatchedCount: 1,
            ReturnedCount: 1,
            Devices: [device]);
        var resolve = new CommissionableDeviceResolveResponse("0xF00", 750, device);

        var browseJson = System.Text.Json.JsonSerializer.Serialize(
            browse,
            ControllerJsonContext.Default.CommissionableDeviceBrowseResponse);
        var resolveJson = System.Text.Json.JsonSerializer.Serialize(
            resolve,
            ControllerJsonContext.Default.CommissionableDeviceResolveResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(browseJson, Does.Contain("\"matchedCount\":1"));
            Assert.That(browseJson, Does.Contain("\"instanceName\":\"ABC123\""));
            Assert.That(browseJson, Does.Contain("\"txtRecords\""));
            Assert.That(resolveJson, Does.Contain("\"discriminator\":\"0xF00\""));
            Assert.That(resolveJson, Does.Contain("\"preferredAddress\":\"fd00::1234\""));
        }
    }

    [Test]
    public void GroupManagementDtosUseSourceGeneratedJson()
    {
        var groups = new DeviceGroupsStateResponse(
            "light",
            "Light",
            new DeviceGroupsState(
                Endpoint: 1,
                NameSupport: ["GroupNames"],
                Groups:
                [
                    new DeviceGroupMembershipEntry(
                        GroupId: 0x0001,
                        GroupIdHex: "0x0001",
                        GroupName: "Kitchen",
                        GroupKeySetId: 0x0042,
                        GroupKeySetIdHex: "0x0042")
                ]));
        var scene = new DeviceSceneViewResponse(
            "light",
            "Light",
            Endpoint: 1,
            InvokeSucceeded: true,
            Accepted: true,
            Status: "Success",
            GroupId: 0x0001,
            GroupIdHex: "0x0001",
            SceneId: 1,
            TransitionTime: 10,
            SceneName: "Evening",
            ExtensionFieldSets:
            [
                new DeviceSceneExtensionFieldSet(
                    ClusterId: 0x0006,
                    ClusterIdHex: "0x0006",
                    AttributeValues:
                    [
                        new DeviceSceneAttributeValue(
                            AttributeId: 0x0000,
                            AttributeIdHex: "0x0000",
                            ValueUnsigned8: 1)
                    ])
            ]);
        var groupKeys = new DeviceGroupKeySetReadResponse(
            "light",
            "Light",
            Endpoint: 0,
            InvokeSucceeded: true,
            Accepted: true,
            GroupKeySet: new DeviceGroupKeySet(
                GroupKeySetId: 0x0042,
                GroupKeySetIdHex: "0x0042",
                GroupKeySecurityPolicy: "TrustFirst",
                EpochKey0Hex: "00112233445566778899AABBCCDDEEFF",
                EpochStartTime0: 1,
                EpochKey1Hex: null,
                EpochStartTime1: null,
                EpochKey2Hex: null,
                EpochStartTime2: null));

        var groupsJson = System.Text.Json.JsonSerializer.Serialize(
            groups,
            ControllerJsonContext.Default.DeviceGroupsStateResponse);
        var sceneJson = System.Text.Json.JsonSerializer.Serialize(
            scene,
            ControllerJsonContext.Default.DeviceSceneViewResponse);
        var groupKeysJson = System.Text.Json.JsonSerializer.Serialize(
            groupKeys,
            ControllerJsonContext.Default.DeviceGroupKeySetReadResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(groupsJson, Does.Contain("\"groupIdHex\":\"0x0001\""));
            Assert.That(groupsJson, Does.Contain("\"groupKeySetIdHex\":\"0x0042\""));
            Assert.That(sceneJson, Does.Contain("\"sceneName\":\"Evening\""));
            Assert.That(sceneJson, Does.Contain("\"clusterIdHex\":\"0x0006\""));
            Assert.That(groupKeysJson, Does.Contain("\"groupKeySecurityPolicy\":\"TrustFirst\""));
            Assert.That(groupKeysJson, Does.Contain("\"epochKey0Hex\":\"00112233445566778899AABBCCDDEEFF\""));
        }
    }

    [Test]
    public void MatterEventDtosUseSourceGeneratedJson()
    {
        var response = new DeviceMatterEventReadResponse(
            "switch",
            "Switch",
            Endpoint: 1,
            Cluster: 0x003B,
            ClusterHex: "0x003B",
            RequestedEventId: 0x0001,
            RequestedEventHex: "0x0001",
            Events:
            [
                new MatterEventResponse(
                    "switch",
                    "Switch",
                    Endpoint: 1,
                    Cluster: 0x003B,
                    ClusterHex: "0x003B",
                    ClusterName: "Switch",
                    EventId: 0x0001,
                    EventHex: "0x0001",
                    EventName: "InitialPress",
                    EventNumber: 42,
                    Priority: 1,
                    EpochTimestamp: 1000,
                    SystemTimestamp: null,
                    DeltaEpochTimestamp: null,
                    DeltaSystemTimestamp: 5,
                    Payload: new MatterEventPayloadResponse(
                        "typed",
                        System.Text.Json.JsonSerializer.SerializeToElement(new { newPosition = 1 })),
                    PayloadTlvHex: "15300102",
                    StatusCode: null,
                    ReceivedAtUtc: new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc))
            ]);

        var json = System.Text.Json.JsonSerializer.Serialize(
            response,
            ControllerJsonContext.Default.DeviceMatterEventReadResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Contain("\"clusterHex\":\"0x003B\""));
            Assert.That(json, Does.Contain("\"clusterName\":\"Switch\""));
            Assert.That(json, Does.Contain("\"requestedEventHex\":\"0x0001\""));
            Assert.That(json, Does.Contain("\"eventName\":\"InitialPress\""));
            Assert.That(json, Does.Contain("\"kind\":\"typed\""));
            Assert.That(json, Does.Contain("\"newPosition\":1"));
            Assert.That(json, Does.Contain("\"payloadTlvHex\":\"15300102\""));
            Assert.That(json, Does.Contain("\"eventNumber\":42"));
        }
    }

    [Test]
    public void RouteRemovalDtosUseSourceGeneratedJson()
    {
        var switchResponse = new SwitchBindingRemovalResponse(
            "switch",
            "Switch",
            "light",
            "Light",
            SourceEndpoint: 1,
            TargetEndpoint: 1,
            Binding: new RemovalStatus("removed", 1, 0),
            Acl: new RemovalStatus("preserved", 0, 1, "Broader or manual ACL state still covers this route"));

        var bindingResponse = new DeviceBindingRemovalResponse(
            "switch",
            "Switch",
            "device-switch",
            Endpoint: 1,
            Result: new RemovalStatus("removed", 1, 0),
            RemovedEntries:
            [
                new DeviceBindingEntry(
                    NodeId: "1234",
                    Group: null,
                    Endpoint: 1,
                    Cluster: 0x0006,
                    ClusterHex: "0x0006",
                    FabricIndex: 1,
                    TargetDeviceId: "light",
                    TargetDeviceName: "Light")
            ]);

        var aclResponse = new DeviceAclRemovalResponse(
            "light",
            "Light",
            "device-light",
            Endpoint: 0,
            Result: new RemovalStatus("removed", 1, 0),
            RemovedEntries:
            [
                new DeviceAclEntry(
                    Privilege: "Operate",
                    AuthMode: "CASE",
                    Subjects:
                    [
                        new DeviceAclSubject(
                            Value: "1234",
                            DeviceId: "switch",
                            DeviceName: "Switch")
                    ],
                    Targets:
                    [
                        new DeviceAclTarget(
                            Cluster: 0x0006,
                            ClusterHex: "0x0006",
                            Endpoint: 1,
                            DeviceType: null,
                            DeviceTypeHex: null)
                    ],
                    AuxiliaryType: null,
                    FabricIndex: 1)
            ]);

        var switchJson = System.Text.Json.JsonSerializer.Serialize(
            switchResponse,
            ControllerJsonContext.Default.SwitchBindingRemovalResponse);
        var bindingJson = System.Text.Json.JsonSerializer.Serialize(
            bindingResponse,
            ControllerJsonContext.Default.DeviceBindingRemovalResponse);
        var aclJson = System.Text.Json.JsonSerializer.Serialize(
            aclResponse,
            ControllerJsonContext.Default.DeviceAclRemovalResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(switchJson, Does.Contain("\"outcome\":\"removed\""));
            Assert.That(switchJson, Does.Contain("\"reason\":\"Broader or manual ACL state still covers this route\""));
            Assert.That(bindingJson, Does.Contain("\"removedEntries\""));
            Assert.That(bindingJson, Does.Contain("\"targetDeviceId\":\"light\""));
            Assert.That(aclJson, Does.Contain("\"privilege\":\"Operate\""));
            Assert.That(aclJson, Does.Contain("\"clusterHex\":\"0x0006\""));
        }
    }

    [Test]
    public void AclQueryDtosUseSourceGeneratedJson()
    {
        var response = new FabricAclListResponse(
            "DotMatter",
            TotalSources: 1,
            SuccessfulSources: 1,
            FailedSources: 0,
            Sources:
            [
                new DeviceAclListResponse(
                    "light",
                    "Light",
                    "device-light",
                    Endpoint: 0,
                    Entries:
                    [
                        new DeviceAclEntry(
                            Privilege: "Operate",
                            AuthMode: "CASE",
                            Subjects:
                            [
                                new DeviceAclSubject(
                                    Value: "1234",
                                    DeviceId: "switch",
                                    DeviceName: "Switch")
                            ],
                            Targets:
                            [
                                new DeviceAclTarget(
                                    Cluster: 0x0006,
                                    ClusterHex: "0x0006",
                                    Endpoint: 1,
                                    DeviceType: null,
                                    DeviceTypeHex: null)
                            ],
                            AuxiliaryType: null,
                            FabricIndex: 1)
                    ])
            ]);

        var json = System.Text.Json.JsonSerializer.Serialize(
            response,
            ControllerJsonContext.Default.FabricAclListResponse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Contain("\"fabricName\":\"DotMatter\""));
            Assert.That(json, Does.Contain("\"privilege\":\"Operate\""));
            Assert.That(json, Does.Contain("\"deviceId\":\"switch\""));
            Assert.That(json, Does.Contain("\"clusterHex\":\"0x0006\""));
        }
    }

    [Test]
    public void BlankApiKeyDoesNotDisableAuthenticationWhenBaseConfigRequiresIt()
    {
        var options = ServiceCollectionExtensions.NormalizeSecurityOptions(new ControllerSecurityOptions
        {
            RequireApiKey = true,
            ApiKey = " "
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.RequireApiKey, Is.True);
            Assert.That(options.ApiKey, Is.Null);
        }
    }

    [Test]
    public async Task CorsPreflightDoesNotRequireApiKey()
    {
        await using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["Controller:Security:AllowedCorsOrigins:0"] = "http://ui.test"
            });
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/devices");
        request.Headers.TryAddWithoutValidation("Origin", "http://ui.test");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "X-API-Key");

        var response = await client.SendAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins), Is.True);
            Assert.That(origins, Does.Contain("http://ui.test"));
        }
    }

    [Test]
    public async Task CommissioningRejectsUnsafeFabricNameBeforeFilesystemAccess()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var registry = new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, tempDirectory.Path);
        var service = new BlockingCommissioningService(registry);

        var result = await service.CommissionDeviceAsync(1234, 20202021, Path.Combine("..", "outside"), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("Fabric name"));
        }
    }

    [Test]
    public void BlankApiKeyFailsStartupWhenApiKeyIsRequired()
    {
        using var factory = CreateFactory(includeApiKey: false);

        var ex = Assert.Catch(() => factory.CreateClient());
        Assert.That(ex!.ToString(), Does.Contain("non-empty API key"));
    }

    [Test]
    public async Task CommissioningServiceRejectsConcurrentAttempts()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var registry = new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, tempDirectory.Path);
        var service = new BlockingCommissioningService(registry);

        var firstTask = service.CommissionDeviceAsync(1234, 20202021, "alpha", CancellationToken.None);
        await service.FirstAttemptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = await service.CommissionDeviceAsync(1234, 20202021, "beta", CancellationToken.None);

        service.AllowFirstAttemptToFinish.SetResult(true);
        var first = await firstTask;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.False);
            Assert.That(second.Error, Does.Contain("already in progress"));
        }
    }

    private static TestControllerFactory CreateFactory(
        Dictionary<string, string?>? overrides = null,
        string environment = "Testing",
        bool includeApiKey = true,
        Action<IServiceCollection>? configureServices = null)
        => new(overrides, environment, includeApiKey, configureServices);

    private sealed class TestControllerFactory : IDisposable, IAsyncDisposable
    {
        private readonly TestFileSystem.TempDirectory _registryDirectory = TestFileSystem.CreateTempDirectoryScope();
        private readonly WebApplicationFactory<ControllerSecurityOptions> _factory;
        private bool _disposed;

        public TestControllerFactory(
            Dictionary<string, string?>? overrides,
            string environment,
            bool includeApiKey,
            Action<IServiceCollection>? configureServices)
        {
            var config = new Dictionary<string, string?>
            {
                ["Controller:Otbr:EnableSrpServerOnStartup"] = "false",
                ["Controller:Registry:BasePath"] = _registryDirectory.Path,
            };

            if (includeApiKey)
            {
                config["Controller:Security:ApiKey"] = "test-key";
            }

            if (overrides != null)
            {
                foreach (var pair in overrides)
                {
                    config[pair.Key] = pair.Value;
                }
            }

            _factory = new WebApplicationFactory<ControllerSecurityOptions>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("environment", environment);
                    builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(config));
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton<IOtbrService, FakeOtbrService>();
                        configureServices?.Invoke(services);
                    });
                });
        }

        public HttpClient CreateClient()
            => _factory.CreateClient();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _factory.Dispose();
            }
            finally
            {
                _registryDirectory.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await _factory.DisposeAsync();
            }
            finally
            {
                _registryDirectory.Dispose();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }

    private sealed class FakeOtbrService : IOtbrService
    {
        Task<string?> IOtbrService.GetActiveDatasetHexAsync(CancellationToken _)
            => Task.FromResult<string?>(null);

        Task<string?> IOtbrService.ResolveSrpServiceAddressAsync(string _, CancellationToken _1)
            => Task.FromResult<string?>(null);

        Task<string?> IOtbrService.DiscoverThreadIpAsync(ILogger _, CancellationToken _1)
            => Task.FromResult<string?>(null);

        Task IOtbrService.EnableSrpServerAsync(CancellationToken _) => Task.CompletedTask;

        Task<string?> IOtbrService.RunOtCtlAsync(string _, CancellationToken _1, bool _2)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeCommissionableDeviceDiscoveryService(
        CommissionableDeviceBrowseResponse browseResponse,
        CommissionableDeviceResponse? resolvedDevice) : ICommissionableDeviceDiscoveryService
    {
        public Task<CommissionableDeviceBrowseResponse> BrowseAsync(CommissionableDeviceDiscoveryRequest request, CancellationToken ct)
            => Task.FromResult(browseResponse);

        public Task<CommissionableDeviceResponse?> ResolveAsync(CommissionableDeviceResolveRequest request, CancellationToken ct)
            => Task.FromResult(resolvedDevice);
    }

    private sealed class BlockingCommissioningService(DeviceRegistry registry) : CommissioningService(
            NullLogger<CommissioningService>.Instance,
            registry,
            new FakeOtbrService(),
            Microsoft.Extensions.Options.Options.Create(new CommissioningOptions()))
    {
        public TaskCompletionSource<bool> FirstAttemptStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> AllowFirstAttemptToFinish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<ControllerCommissioningResult> CommissionCoreAsync(
            int discriminator,
            uint passcode,
            string fabricName,
            bool fetchThreadDataset,
            string? wifiSsid,
            string? wifiPassword,
            string? transport,
            bool isShortDiscriminator,
            Action<CommissioningProgress>? onProgress,
            CancellationToken ct)
        {
            if (fabricName == "alpha")
            {
                FirstAttemptStarted.TrySetResult(true);
                await AllowFirstAttemptToFinish.Task.WaitAsync(TimeSpan.FromSeconds(2), ct);
                return new ControllerCommissioningResult(true, "alpha", "1", "fd00::1", null);
            }

            return await base.CommissionCoreAsync(
                discriminator, passcode, fabricName, fetchThreadDataset,
                wifiSsid, wifiPassword, transport, isShortDiscriminator, onProgress, ct);
        }
    }
}
