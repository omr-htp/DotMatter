using DotMatter.Core;
using DotMatter.Core.Sessions;
using DotMatter.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

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

    [Test]
    public async Task TryProcessSubscriptionMessageAsync_ResultControlsSubscriptionFreshness()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);
        host.Registry.Register(new DeviceInfo
        {
            Id = "lamp",
            Name = "Lamp",
            NodeId = "1",
            FabricName = "lamp",
        });

        host.NextSubscriptionMessageResult = false;
        await host.HandleUnroutedMessageAsync("lamp", []);
        Assert.That(host.SubscriptionReportTimes.ContainsKey("lamp"), Is.False);

        host.NextSubscriptionMessageResult = true;
        await host.HandleUnroutedMessageAsync("lamp", []);
        Assert.That(host.SubscriptionReportTimes.ContainsKey("lamp"), Is.True);
    }

    private sealed class TestMatterDeviceHost(string registryPath) : MatterDeviceHost(
        NullLogger.Instance,
        new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, registryPath),
        new MatterRuntimeStatus(),
        new FakeOtbrService(),
        new SessionRecoveryOptions())
    {
        public bool ReconnectScheduled { get; private set; }
        public bool NextSubscriptionMessageResult { get; set; }

        public new DeviceRegistry Registry => base.Registry;
        public ConcurrentDictionary<string, DateTime> SubscriptionReportTimes => base.LastSubscriptionReport;

        public Task TriggerSubscriptionStaleAsync(string id, CancellationToken ct = default)
            => base.OnSubscriptionStaleAsync(id, null!, null!, ct);

        public async Task HandleUnroutedMessageAsync(string id, byte[] bytes)
        {
            if (await TryProcessSubscriptionMessageAsync(id, bytes))
            {
                SubscriptionReportTimes[id] = DateTime.UtcNow;
            }

            Registry.Update(id, d => d.LastSeen = DateTime.UtcNow);
        }

        protected override Task<bool> TryRefreshSubscriptionAsync(string id, ISession session, CancellationToken ct)
            => Task.FromResult(false);

        protected override Task<bool> TryProcessSubscriptionMessageAsync(string id, byte[] bytes)
            => Task.FromResult(NextSubscriptionMessageResult);

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
