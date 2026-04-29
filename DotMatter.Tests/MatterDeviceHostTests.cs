using DotMatter.Core;
using DotMatter.Core.Sessions;
using DotMatter.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotMatter.Tests;

[TestFixture]
public class MatterDeviceHostTests
{
    [Test]
    public async Task OnSubscriptionStaleAsync_SchedulesReconnectWhenRefreshFails()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);

        await host.TriggerSubscriptionStaleAsync("lamp");

        Assert.That(host.ReconnectScheduled, Is.True);
    }

    private sealed class TestMatterDeviceHost(string registryPath) : MatterDeviceHost(
        NullLogger.Instance,
        new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, registryPath),
        new MatterRuntimeStatus(),
        new FakeOtbrService(),
        new SessionRecoveryOptions())
    {
        public bool ReconnectScheduled { get; private set; }

        public Task TriggerSubscriptionStaleAsync(string id, CancellationToken ct = default)
            => base.OnSubscriptionStaleAsync(id, null!, null!, ct);

        protected override Task<bool> TryRefreshSubscriptionAsync(string id, ISession session, CancellationToken ct)
            => Task.FromResult(false);

        protected override bool TryScheduleManagedReconnect(string id, ResilientSession rs)
        {
            ReconnectScheduled = true;
            return true;
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
}
