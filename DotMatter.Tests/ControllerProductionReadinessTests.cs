using DotMatter.Controller;
using DotMatter.Core.Commissioning;
using DotMatter.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

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
                FollowUpConnectTimeout: "00:00:30"));

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
    public void StartupFailsWhenAuthIsEnabledWithoutAnApiKey()
    {
        using var factory = CreateFactory(includeApiKey: false);

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.That(exception!.Message, Does.Contain("non-empty API key"));
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
        bool includeApiKey = true)
        => new(overrides, environment, includeApiKey);

    private sealed class TestControllerFactory : IDisposable, IAsyncDisposable
    {
        private readonly TestFileSystem.TempDirectory _registryDirectory = TestFileSystem.CreateTempDirectoryScope();
        private readonly WebApplicationFactory<ControllerSecurityOptions> _factory;
        private bool _disposed;

        public TestControllerFactory(
            Dictionary<string, string?>? overrides,
            string environment,
            bool includeApiKey)
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
                        services.AddSingleton<IOtbrService, FakeOtbrService>());
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
        public Task<string?> GetActiveDatasetHexAsync(CancellationToken ct)
            => Task.FromResult<string?>(null);

        public Task<string?> ResolveSrpServiceAddressAsync(string serviceName, CancellationToken ct)
            => Task.FromResult<string?>(null);

        public Task<string?> DiscoverThreadIpAsync(ILogger log, CancellationToken ct)
            => Task.FromResult<string?>(null);

        public Task EnableSrpServerAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<string?> RunOtCtlAsync(string command, CancellationToken ct, bool firstLineOnly = true)
            => Task.FromResult<string?>(null);
    }

    private sealed class BlockingCommissioningService(DeviceRegistry registry) : CommissioningService(
            NullLogger<CommissioningService>.Instance,
            registry,
            new FakeOtbrService(),
            Microsoft.Extensions.Options.Options.Create(new CommissioningOptions()))
    {
        public TaskCompletionSource<bool> FirstAttemptStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> AllowFirstAttemptToFinish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<(bool Success, string? DeviceId, string? NodeId, string? Ip, string? Error)> CommissionCoreAsync(
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
                return (true, "alpha", "1", "fd00::1", null);
            }

            return await base.CommissionCoreAsync(
                discriminator, passcode, fabricName, fetchThreadDataset,
                wifiSsid, wifiPassword, transport, isShortDiscriminator, onProgress, ct);
        }
    }
}
