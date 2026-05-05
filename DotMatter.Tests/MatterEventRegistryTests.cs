using System.Reflection;
using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.TLV;

namespace DotMatter.Tests;

[TestFixture]
public class MatterEventRegistryTests
{
    private readonly record struct AttributePathProjection(ushort? EndpointId, uint? ClusterId, uint? AttributeId);

    private readonly record struct EventPathProjection(ushort? EndpointId, uint? ClusterId);

    [Test]
    public void ClusterEventRegistry_MapsTypedSwitchEventAndPayloadJson()
    {
        var report = new MatterEventReport(
            endpointId: 1,
            clusterId: SwitchCluster.ClusterId,
            eventId: SwitchCluster.Events.InitialPress,
            eventNumber: 42,
            priority: 1,
            epochTimestamp: null,
            systemTimestamp: 100,
            deltaEpochTimestamp: null,
            deltaSystemTimestamp: null,
            data: null,
            rawData: new MatterTLV(Convert.FromHexString("350724000118")));

        var mapped = ClusterEventRegistry.MapEventReport(report);
        var payloadJson = ClusterEventRegistry.BuildPayloadJson(mapped!);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapped, Is.Not.Null);
            Assert.That(mapped!.ClusterName, Is.EqualTo("Switch"));
            Assert.That(mapped.EventName, Is.EqualTo("InitialPress"));
            Assert.That(mapped.TypedPayload, Is.Not.Null);
            Assert.That(payloadJson, Is.Not.Null);
            Assert.That(payloadJson!["newPosition"]?.GetValue<byte>(), Is.EqualTo(1));
        }
    }

    [Test]
    public void SubscriptionCoordinator_BuildAttributePaths_IncludesSwitchCurrentPosition()
    {
        var device = new DeviceInfo
        {
            Endpoints = new Dictionary<ushort, List<uint>>
            {
                [0] = [AccessControlCluster.ClusterId],
                [1] = [SwitchCluster.ClusterId],
            }
        };

        var coordinatorType = typeof(DeviceInfo).Assembly.GetType("DotMatter.Hosting.Runtime.SubscriptionCoordinator");
        Assert.That(coordinatorType, Is.Not.Null);

        var buildAttributePaths = coordinatorType!.GetMethod("BuildAttributePaths", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(buildAttributePaths, Is.Not.Null);

        var paths = ((System.Collections.IEnumerable?)buildAttributePaths!.Invoke(null, [device]))?.Cast<object>().ToArray();
        Assert.That(paths, Is.Not.Null);

        var projected = paths!
            .Select(path =>
            {
                var type = path.GetType();
                var endpointId = (ushort?)type.GetProperty("EndpointId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(path);
                var clusterId = (uint?)type.GetProperty("ClusterId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(path);
                var attributeId = (uint?)type.GetProperty("AttributeId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(path);
                return new AttributePathProjection(endpointId, clusterId, attributeId);
            })
            .ToArray();

        Assert.That(projected, Is.EqualTo([new AttributePathProjection(1, SwitchCluster.ClusterId, SwitchCluster.Attributes.CurrentPosition)]));
    }

    [Test]
    public void SubscriptionCoordinator_BuildEventPaths_UsesDiscoveredEventClusters()
    {
        var device = new DeviceInfo
        {
            Endpoints = new Dictionary<ushort, List<uint>>
            {
                [0] = [AccessControlCluster.ClusterId, GeneralDiagnosticsCluster.ClusterId, OnOffCluster.ClusterId],
                [1] = [SwitchCluster.ClusterId, OnOffCluster.ClusterId],
            }
        };

        var coordinatorType = typeof(DeviceInfo).Assembly.GetType("DotMatter.Hosting.Runtime.SubscriptionCoordinator");
        Assert.That(coordinatorType, Is.Not.Null);

        var buildEventPaths = coordinatorType!.GetMethod("BuildEventPaths", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(buildEventPaths, Is.Not.Null);

        var paths = ((System.Collections.IEnumerable?)buildEventPaths!.Invoke(null, [device]))?.Cast<object>().ToArray();
        Assert.That(paths, Is.Not.Null);

        var projected = paths!
            .Select(path =>
            {
                var type = path.GetType();
                var endpointId = (ushort?)type.GetProperty("EndpointId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(path);
                var clusterId = (uint?)type.GetProperty("ClusterId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(path);
                return new EventPathProjection(endpointId, clusterId);
            })
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(projected, Has.Length.EqualTo(3));
            Assert.That(projected, Does.Contain(new EventPathProjection(0, AccessControlCluster.ClusterId)));
            Assert.That(projected, Does.Contain(new EventPathProjection(0, GeneralDiagnosticsCluster.ClusterId)));
            Assert.That(projected, Does.Contain(new EventPathProjection(1, SwitchCluster.ClusterId)));
            Assert.That(projected, Does.Not.Contain(new EventPathProjection(0, OnOffCluster.ClusterId)));
            Assert.That(projected, Does.Not.Contain(new EventPathProjection(1, OnOffCluster.ClusterId)));
        }
    }

    [Test]
    public void SubscriptionCoordinator_EventOnlySubscriptions_UseShorterMaxInterval()
    {
        var coordinatorType = typeof(DeviceInfo).Assembly.GetType("DotMatter.Hosting.Runtime.SubscriptionCoordinator");
        Assert.That(coordinatorType, Is.Not.Null);

        var getMaxInterval = coordinatorType!.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "GetSubscriptionMaxIntervalSeconds" && method.GetParameters().Length == 2);

        var maxInterval = (ushort?)getMaxInterval!.Invoke(null, [0, 1]);

        Assert.That(maxInterval, Is.EqualTo((ushort)5));
    }

    [Test]
    public void SubscriptionCoordinator_MixedSubscriptions_KeepLongerMaxInterval()
    {
        var coordinatorType = typeof(DeviceInfo).Assembly.GetType("DotMatter.Hosting.Runtime.SubscriptionCoordinator");
        Assert.That(coordinatorType, Is.Not.Null);

        var getMaxInterval = coordinatorType!.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "GetSubscriptionMaxIntervalSeconds" && method.GetParameters().Length == 2);

        var maxInterval = (ushort?)getMaxInterval!.Invoke(null, [1, 1]);

        Assert.That(maxInterval, Is.EqualTo((ushort)30));
    }

    [Test]
    public void SubscriptionCoordinator_SwitchSubscriptions_UseShorterMaxInterval()
    {
        var coordinatorType = typeof(DeviceInfo).Assembly.GetType("DotMatter.Hosting.Runtime.SubscriptionCoordinator");
        Assert.That(coordinatorType, Is.Not.Null);

        var getMaxInterval = coordinatorType!.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "GetSubscriptionMaxIntervalSeconds" && method.GetParameters().Length == 3);

        var maxInterval = (ushort?)getMaxInterval.Invoke(null, [1, 1, true]);

        Assert.That(maxInterval, Is.EqualTo((ushort)5));
    }

    [Test]
    public void SubscriptionCoordinator_StaleThreshold_FollowsSubscriptionMaxInterval()
    {
        var coordinatorType = typeof(DeviceInfo).Assembly.GetType("DotMatter.Hosting.Runtime.SubscriptionCoordinator");
        Assert.That(coordinatorType, Is.Not.Null);

        var getStaleThreshold = coordinatorType!.GetMethod("GetSubscriptionStaleThreshold", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(getStaleThreshold, Is.Not.Null);

        var staleThreshold = (TimeSpan?)getStaleThreshold!.Invoke(null, [(ushort)5]);

        Assert.That(staleThreshold, Is.EqualTo(TimeSpan.FromSeconds(12)));
    }
}
