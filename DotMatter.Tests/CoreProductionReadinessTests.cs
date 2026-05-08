using System.Security.Cryptography;
using System.Text;
using DotMatter.Core;
using DotMatter.Core.Commissioning;
using DotMatter.Core.Cryptography;
using DotMatter.Core.Fabrics;
using DotMatter.Core.TLV;
using DotMatter.Hosting;
using Org.BouncyCastle.Math;

namespace DotMatter.Tests;

[TestFixture]
public class CoreProductionReadinessTests
{
    [Test]
    public void Utf8StringUsesUtf8ByteLength()
    {
        var value = "é";
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddUTF8String(1, value);
        tlv.EndContainer();

        var bytes = tlv.GetBytes();

        Assert.That(bytes[3], Is.EqualTo(Encoding.UTF8.GetByteCount(value)));

        tlv.OpenStructure();
        var decoded = tlv.GetUTF8String(1);

        Assert.That(decoded, Is.EqualTo(value));
    }

    [Test]
    public void MalformedTlvThrowsTypedException()
    {
        var tlv = new MatterTLV([0x15, 0x18]);
        tlv.OpenStructure();

        var ex = Assert.Throws<MatterTlvException>(() => tlv.GetBoolean(1));
        Assert.That(ex!.Message, Does.Contain("Expected Boolean"));
    }

    [Test]
    public async Task FabricStoragePersistsMetadataUpdates()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var storage = new FabricDiskStorage(tempDirectory.Path);
        var manager = new FabricManager(storage);

        var fabric = await manager.GetAsync("prod-ready");
        fabric.AdminVendorId = 0x1234;
        fabric.CompressedFabricId = "AABBCCDDEEFF0011";
        fabric.IPK = [.. Enumerable.Repeat((byte)0xAB, 16)];

        await storage.SaveFabricAsync(fabric);

        var reloaded = await storage.LoadFabricAsync("prod-ready");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.AdminVendorId, Is.EqualTo(0x1234));
            Assert.That(reloaded.CompressedFabricId, Is.EqualTo("AABBCCDDEEFF0011"));
            Assert.That(reloaded.IPK, Is.EqualTo(fabric.IPK));
        }
    }

    [Test]
    public void FabricStorageRejectsUnsafeFabricNames()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var storage = new FabricDiskStorage(tempDirectory.Path);

        Assert.Throws<ArgumentException>(() => storage.DoesFabricExist(Path.Combine("..", "outside")));
        Assert.Throws<ArgumentException>(() => storage.DoesFabricExist(@"bad\fabric"));
    }

    [Test]
    public async Task FabricStorageIgnoresNonNodeSubdirectories()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();

        var storage = new FabricDiskStorage(tempDirectory.Path);
        var manager = new FabricManager(storage);
        await manager.GetAsync("DotMatter");
        Directory.CreateDirectory(Path.Combine(tempDirectory.Path, "DotMatter", "devices"));

        var fabric = await storage.LoadFabricAsync("DotMatter");

        Assert.That(fabric.Nodes, Is.Empty);
    }

    [Test]
    public void ThreadDatasetWithTruncatedExtendedPanIdReturnsNull()
    {
        var extPanId = MatterCommissioner.ExtractExtendedPanId([0x02, 0x08, 0xAA]);

        Assert.That(extPanId, Is.Null);
    }

    [Test]
    public async Task EncodeMatterNoc_AllowsShortUInt64Identifiers()
    {
        using var tempDirectory = TestFileSystem.CreateTempDirectoryScope();
        using var peerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var storage = new FabricDiskStorage(tempDirectory.Path);
        var manager = new FabricManager(storage);
        var fabric = await manager.GetAsync("short-ids");
        fabric.FabricId = new BigInteger("1234", 16);
        var node = new Node { NodeId = new BigInteger("123456", 16) };
        var peerPublicKey = P256KeyInterop.ToBouncyCastlePublicKey(peerKey);
        var peerPublicKeyBytes = P256KeyInterop.ExportPublicKey(peerKey);
        var peerKeyId = SHA1.HashData(peerPublicKeyBytes).AsSpan()[..20].ToArray();
        var generatedNoc = MatterCommissioner.GenerateNoc(node, fabric, peerPublicKey, peerKeyId);

        var encoded = MatterCommissioner.EncodeMatterNoc(node, fabric, generatedNoc.Noc, generatedNoc.Serial, peerPublicKeyBytes, peerKeyId);

        Assert.That(encoded, Is.Not.Empty);
    }

    [Test]
    public void RetryDelayUsesJitteredBackoff()
    {
        var delayWithoutJitter = ResilientSession.ComputeRetryDelay(
            attempt: 2,
            maxDelay: TimeSpan.FromSeconds(30),
            jitterRatio: 0.2,
            jitterSample: 0.5);

        var delayWithPositiveJitter = ResilientSession.ComputeRetryDelay(
            attempt: 2,
            maxDelay: TimeSpan.FromSeconds(30),
            jitterRatio: 0.2,
            jitterSample: 1.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(delayWithoutJitter, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(delayWithPositiveJitter, Is.EqualTo(TimeSpan.FromSeconds(12)));
        }
    }

    [Test]
    public void SensitiveLogsAreRedactedByDefault()
    {
        var previous = MatterLog.Settings;

        try
        {
            MatterLog.Settings = new MatterLogSettings
            {
                EnableSensitiveDiagnostics = false,
                MaxRenderedBytes = 4
            };

            var formatted = MatterLog.FormatSecret([0x01, 0x02, 0x03, 0x04]);

            Assert.That(formatted, Is.EqualTo("<redacted:4-bytes>"));
        }
        finally
        {
            MatterLog.Settings = previous;
        }
    }

    [Test]
    public void OtbrDatasetOutputExtractsHexPayload()
    {
        const string output = """
            0e080000000000010000000300000e4a0300001a35060004001fffe00208ef44b1fbb9c4b9af0708fd91820a791cdc5b05103605ade97578c328837efead1a86943d030f4f70656e5468726561642d3066303801020f080410f54daf28254f25afe2f7bed2cd3f41820c0402a0f7f8
            Done
            """;

        var hex = OtbrCommandHelper.ExtractHexPayload(output);

        Assert.That(hex, Is.EqualTo("0e080000000000010000000300000e4a0300001a35060004001fffe00208ef44b1fbb9c4b9af0708fd91820a791cdc5b05103605ade97578c328837efead1a86943d030f4f70656e5468726561642d3066303801020f080410f54daf28254f25afe2f7bed2cd3f41820c0402a0f7f8"));
    }

    [Test]
    public void OtbrSrpOutputExtractsSpecificServiceAddress()
    {
        const string output = """
            25C291389F53B2EC-769545CD041FDD13._matter._tcp.default.service.arpa.
                deleted: false
                port: 5540
                host: 3E0B3196EBB763EF.default.service.arpa.
                addresses: [fd91:820a:791c:dc5b:b8ea:f677:c97d:8174]
            Done
            """;

        var ip = OtbrCommandHelper.ExtractSrpServiceAddress(output, "25C291389F53B2EC-769545CD041FDD13");

        Assert.That(ip, Is.EqualTo("fd91:820a:791c:dc5b:b8ea:f677:c97d:8174"));
    }
}
