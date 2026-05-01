using DotMatter.Controller;
using DotMatter.Core.Commissioning;
using DotMatter.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
