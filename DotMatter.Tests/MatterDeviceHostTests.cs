using System.Collections.Concurrent;
using DotMatter.Core;
using DotMatter.Core.Clusters;
using DotMatter.Core.Fabrics;
using DotMatter.Core.InteractionModel;
using DotMatter.Hosting.Thread;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Math;

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

    [Test]
    public void ShouldRefreshSubscription_DetectsStaleEventOnlySubscriptions()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);
        host.RegisterManagedSwitch("switch");
        host.SubscriptionReportTimes["switch"] = DateTime.UtcNow - TimeSpan.FromMinutes(10);
        host.MarkSubscriptionShape("switch", attributePathCount: 0, eventPathCount: 1);
        host.ActiveSubscriptions["switch"] = null!;

        var shouldRefresh = host.IsSubscriptionRefreshDue("switch", DateTime.UtcNow);

        Assert.That(shouldRefresh, Is.True);
    }

    [Test]
    public void ShouldRefreshSubscription_RecoversMissingEventOnlySubscriptions()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);
        host.RegisterManagedSwitch("switch");
        host.SubscriptionReportTimes["switch"] = DateTime.UtcNow - TimeSpan.FromMinutes(10);
        host.MarkSubscriptionShape("switch", attributePathCount: 0, eventPathCount: 1);

        var shouldRefresh = host.IsSubscriptionRefreshDue("switch", DateTime.UtcNow);

        Assert.That(shouldRefresh, Is.True);
    }

    [Test]
    public void ShouldRefreshSubscription_DoesNotReconnectBeforeInitialSubscriptionWindow()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);
        host.RegisterManagedSwitch("switch");

        var shouldRefresh = host.IsSubscriptionRefreshDue("switch", DateTime.UtcNow);

        Assert.That(shouldRefresh, Is.False);
    }

    [Test]
    public void ShouldRefreshSubscription_UsesSubscriptionSpecificThreshold()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);
        host.RegisterManagedSwitch("switch");
        host.SubscriptionReportTimes["switch"] = DateTime.UtcNow - TimeSpan.FromSeconds(20);
        host.SubscriptionStaleThresholds["switch"] = TimeSpan.FromSeconds(12);

        var shouldRefresh = host.IsSubscriptionRefreshDue("switch", DateTime.UtcNow);

        Assert.That(shouldRefresh, Is.True);
    }

    [Test]
    public async Task OnSubscriptionStaleAsync_EventOnlySubscriptionReconnectsWithoutRefresh()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);
        host.MarkSubscriptionShape("switch", attributePathCount: 0, eventPathCount: 1);
        host.ActiveSubscriptions["switch"] = null!;

        await host.TriggerSubscriptionStaleAsync("switch");

        Assert.That(host.ReconnectScheduled, Is.True);
    }

    [Test]
    public async Task OnSubscriptionStaleAsync_AttributeSubscriptionReconnectsWithoutRefresh()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);
        host.MarkSubscriptionShape("switch", attributePathCount: 1, eventPathCount: 1);
        host.ActiveSubscriptions["switch"] = null!;

        await host.TriggerSubscriptionStaleAsync("switch");

        Assert.That(host.ReconnectScheduled, Is.True);
    }

    [Test]
    public void ShouldRecoverOfflineSession_RecoversManagedOfflineDevice()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);
        host.RegisterManagedSwitch("switch");

        Assert.That(host.IsOfflineSessionRecoveryDue("switch"), Is.True);
    }

    [Test]
    public void ShouldRecoverOfflineSession_IgnoresUnsupportedOfflineDevice()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);
        host.Registry.Register(new DeviceInfo
        {
            Id = "unsupported",
            Name = "unsupported",
            NodeId = "1",
            FabricName = "unsupported",
            Endpoints = new Dictionary<ushort, List<uint>>
            {
                [1] = [0x1234]
            }
        });

        Assert.That(host.IsOfflineSessionRecoveryDue("unsupported"), Is.False);
    }

    [Test]
    public void ManagedReconnect_DoesNotOverlapExistingSessionConnectionOperation()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var host = new TestMatterDeviceHost(tempDirectory.Path);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstScheduled = host.ScheduleBlockingSessionConnection("light", release);
        var reconnectScheduled = host.ScheduleBaseManagedReconnect("light");
        release.SetResult();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstScheduled, Is.True);
            Assert.That(reconnectScheduled, Is.False);
        }
    }

    private sealed class TestMatterDeviceHost(string registryPath) : MatterDeviceHost(
        NullLogger.Instance,
        new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, registryPath),
        new MatterRuntimeStatus(),
        new FakeOtbrService(),
        new SessionRecoveryOptions())
    {
        public bool ReconnectScheduled
        {
            get; private set;
        }
        public bool NextSubscriptionMessageResult
        {
            get; set;
        }

        public new DeviceRegistry Registry => base.Registry;
        public ConcurrentDictionary<string, Subscription> ActiveSubscriptions => base.Subscriptions;
        public ConcurrentDictionary<string, DateTime> SubscriptionReportTimes => base.LastSubscriptionReport;
        public new ConcurrentDictionary<string, TimeSpan> SubscriptionStaleThresholds => base.SubscriptionStaleThresholds;

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

        public void MarkSubscriptionShape(string id, int attributePathCount, int eventPathCount)
            => base.TrackSubscriptionShape(id, attributePathCount, eventPathCount);

        public void RegisterManagedSwitch(string id)
            => Registry.Register(new DeviceInfo
            {
                Id = id,
                Name = id,
                NodeId = "1",
                FabricName = id,
                DeviceType = "switch",
                Endpoints = new Dictionary<ushort, List<uint>>
                {
                    [1] = [SwitchCluster.ClusterId]
                }
            });

        public bool IsSubscriptionRefreshDue(string id, DateTime utcNow)
            => base.ShouldRefreshSubscription(id, utcNow);

        public bool IsOfflineSessionRecoveryDue(string id)
            => base.ShouldRecoverOfflineSession(id, new StubDisconnectedResilientSession());

        public bool ScheduleBlockingSessionConnection(string id, TaskCompletionSource release)
            => base.ScheduleOwnedOperation(
                GetSessionConnectionOperationKey(id),
                async ct => await release.Task.WaitAsync(TimeSpan.FromSeconds(2), ct));

        public bool ScheduleBaseManagedReconnect(string id)
            => base.TryScheduleManagedReconnect(id, new StubDisconnectedResilientSession());

        protected override Task<bool> TryProcessSubscriptionMessageAsync(string id, byte[] bytes)
            => Task.FromResult(NextSubscriptionMessageResult);

        protected override bool TryScheduleManagedReconnect(string id, ResilientSession rs)
        {
            ReconnectScheduled = true;
            return true;
        }
    }

    private sealed class StubDisconnectedResilientSession() : ResilientSession(
        new Fabric { CompressedFabricId = "0000000000000001" },
        BigInteger.One,
        "0000000000000001",
        "0000000000000001",
        System.Net.IPAddress.IPv6Loopback);

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

}
