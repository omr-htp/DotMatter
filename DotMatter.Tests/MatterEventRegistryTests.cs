using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.TLV;
using DotMatter.Hosting;
using System.Reflection;

namespace DotMatter.Tests;

[TestFixture]
public class MatterEventRegistryTests
{
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

        var coordinatorType = typeof(DeviceInfo).Assembly.GetType("DotMatter.Hosting.SubscriptionCoordinator");
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
                return (EndpointId: endpointId, ClusterId: clusterId);
            })
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(projected, Has.Length.EqualTo(3));
            Assert.That(projected, Does.Contain((0, AccessControlCluster.ClusterId)));
            Assert.That(projected, Does.Contain((0, GeneralDiagnosticsCluster.ClusterId)));
            Assert.That(projected, Does.Contain((1, SwitchCluster.ClusterId)));
            Assert.That(projected, Does.Not.Contain((0, OnOffCluster.ClusterId)));
            Assert.That(projected, Does.Not.Contain((1, OnOffCluster.ClusterId)));
        }
    }
}
