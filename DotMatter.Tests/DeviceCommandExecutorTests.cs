using DotMatter.Controller.Models;
using DotMatter.Core;
using DotMatter.Core.InteractionModel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotMatter.Tests;

[TestFixture]
public class DeviceCommandExecutorTests
{
    [Test]
    public async Task ExecuteAsync_UpdatesRegistryAndPublishesEventOnSuccess()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var registry = new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, tempDirectory.Path);
        registry.Register(new DeviceInfo { Id = "lamp", Name = "Lamp", NodeId = "1", FabricName = "lamp" });

        var executor = new DeviceCommandExecutor(
            NullLogger.Instance,
            registry,
            Options.Create(new ControllerApiOptions()));

        string? published = null;
        var result = await executor.ExecuteAsync(
            "lamp",
            "SetLevel",
            _ => Task.FromResult(new InvokeResponse(true, 0x00, new MessageFrame(new MessagePayload()), null)),
            device =>
            {
                device.Level = 42;
                device.LastSeen = DateTime.UtcNow;
            },
            "level:42",
            value => published = value,
            "Device lamp: Level -> 42");

        var device = registry.Get("lamp");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(device?.Level, Is.EqualTo(42));
            Assert.That(device?.LastSeen, Is.Not.Null);
            Assert.That(published, Is.EqualTo("level:42"));
        }
    }

    [Test]
    public async Task ExecuteAsync_TriggersRecoveryCallbackOnTransportFailure()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var registry = new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, tempDirectory.Path);
        var executor = new DeviceCommandExecutor(
            NullLogger.Instance,
            registry,
            Options.Create(new ControllerApiOptions()));

        var recoveryCalled = false;
        var result = await executor.ExecuteAsync(
            "lamp",
            "SetColor",
            _ => throw new InvalidOperationException("transport exploded"),
            _ => { },
            "color:h1s2",
            _ => { },
            "ignored",
            () =>
            {
                recoveryCalled = true;
                return Task.CompletedTask;
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure, Is.EqualTo(DeviceOperationFailure.TransportError));
            Assert.That(result.Error, Does.Contain("transport exploded"));
            Assert.That(recoveryCalled, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_TriggersRecoveryCallbackOnTimeout()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var registry = new DeviceRegistry(NullLogger<DeviceRegistry>.Instance, tempDirectory.Path);
        var executor = new DeviceCommandExecutor(
            NullLogger.Instance,
            registry,
            Options.Create(new ControllerApiOptions
            {
                CommandTimeout = TimeSpan.FromMilliseconds(20)
            }));

        var recoveryCalled = false;
        var result = await executor.ExecuteAsync(
            "lamp",
            "Toggle",
            async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                throw new InvalidOperationException("unreachable");
            },
            _ => { },
            "toggle",
            _ => { },
            "ignored",
            () =>
            {
                recoveryCalled = true;
                return Task.CompletedTask;
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure, Is.EqualTo(DeviceOperationFailure.Timeout));
            Assert.That(result.Error, Does.Contain("timed out"));
            Assert.That(recoveryCalled, Is.True);
        }
    }
}
