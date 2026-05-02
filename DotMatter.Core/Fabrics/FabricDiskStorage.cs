using DotMatter.Core.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.X509;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotMatter.Core.Fabrics;

[JsonSerializable(typeof(FabricDiskStorage.FabricDetails))]
[JsonSerializable(typeof(FabricDiskStorage.NodeDetails))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class FabricStorageJsonContext : JsonSerializerContext;

/// <summary>FabricDiskStorage class.</summary>
public class FabricDiskStorage : IFabricStorageProvider
{
    private static readonly UTF8Encoding _utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly string _rootDirectory;

    /// <summary>FabricDiskStorage.</summary>
    public FabricDiskStorage(string rootDirectory)
    {
        _rootDirectory = rootDirectory;

        if (!Directory.Exists(_rootDirectory))
        {
            Directory.CreateDirectory(_rootDirectory);
        }
    }

    internal class FabricDetails
    {
        public byte[] FabricId { get; set; } = default!;

        public byte[] RootCertificateId { get; set; } = default!;

        public byte[] RootNodeId { get; set; } = default!;

        public ushort AdminVendorId { get; set; }

        public byte[] IPK { get; set; } = default!;

        public byte[] OperationalIPK { get; set; } = default!;

        public byte[] RootKeyIdentifier { get; set; } = default!;

        public string CompressedFabricId { get; set; } = default!;
    }

    internal class NodeDetails
    {
        public byte[] NodeId { get; set; } = default!;

        public string LastKnownIPAddress { get; set; } = default!;

        public ushort LastKnownPort { get; set; }
    }

    /// <summary>DoesFabricExist.</summary>
    public bool DoesFabricExist(string fabricName)
    {
        return Directory.Exists(GetFullPath(fabricName));
    }

    /// <summary>LoadFabricAsync.</summary>
    public async Task<Fabric> LoadFabricAsync(string fabricName)
    {
        var allFiles = Directory.GetFiles(GetFullPath(fabricName));

        var fabric = new Fabric()
        {
            FabricName = fabricName,
        };

        foreach (var file in allFiles)
        {
            if (file.EndsWith("fabric.json"))
            {
                var fileBytes = ReadJsonFileBytes(await File.ReadAllBytesAsync(file));
                var details = JsonSerializer.Deserialize(fileBytes, FabricStorageJsonContext.Default.FabricDetails)!;

                fabric.FabricId = new BigInteger(details.FabricId);
                fabric.RootCACertificateId = new BigInteger(details.RootCertificateId);
                fabric.RootNodeId = new BigInteger(details.RootNodeId);
                fabric.AdminVendorId = details.AdminVendorId;
                fabric.IPK = details.IPK;
                fabric.OperationalIPK = details.OperationalIPK;
                fabric.RootKeyIdentifier = details.RootKeyIdentifier;
                fabric.CompressedFabricId = details.CompressedFabricId;
            }
            else if (file.EndsWith("rootCertificate.pem"))
            {
                using var reader = new StreamReader(file);
                var pemReader = new PemReader(reader);
                fabric.RootCACertificate = (X509Certificate)pemReader.ReadObject();
            }
            else if (file.EndsWith("rootKeyPair.pem"))
            {
                using var reader = new StreamReader(file);
                var pemReader = new PemReader(reader);
                var keyPair = (AsymmetricCipherKeyPair)pemReader.ReadObject();
                fabric.RootCAKeyPair = P256KeyInterop.ImportPrivateKey(keyPair.Private as ECPrivateKeyParameters ?? throw new InvalidOperationException("Failed to read root key pair"));
                fabric.RootCAECDiffieHellman = ECDiffieHellman.Create(fabric.RootCAKeyPair.ExportParameters(true));
            }
            else if (file.EndsWith("operationalCertificate.pem"))
            {
                using var reader = new StreamReader(file);
                var pemReader = new PemReader(reader);
                fabric.OperationalCertificate = (X509Certificate)pemReader.ReadObject();
            }
            else if (file.EndsWith("operationalKeyPair.pem"))
            {
                using var reader = new StreamReader(file);
                var pemReader = new PemReader(reader);
                var keyPair = (AsymmetricCipherKeyPair)pemReader.ReadObject();
                fabric.OperationalCertificateKeyPair = P256KeyInterop.ImportPrivateKey(keyPair.Private as ECPrivateKeyParameters ?? throw new InvalidOperationException("Failed to read operational key pair"));
                fabric.OperationalECDiffieHellman = ECDiffieHellman.Create(fabric.OperationalCertificateKeyPair.ExportParameters(true));
            }
        }

        var allDirectories = Directory.GetDirectories(GetFullPath(fabricName));

        foreach (var directory in allDirectories)
        {
            var nodeId = new BigInteger(Path.GetFileName(directory));

            var nodeFiles = Directory.GetFiles(directory);

            foreach (var file in nodeFiles)
            {
                if (file.EndsWith("node.json"))
                {
                    var fileBytes = ReadJsonFileBytes(await File.ReadAllBytesAsync(file));
                    var details = JsonSerializer.Deserialize(fileBytes, FabricStorageJsonContext.Default.NodeDetails)!;

                    var node = new Node
                    {
                        NodeId = nodeId,
                        LastKnownIpAddress = string.IsNullOrEmpty(details.LastKnownIPAddress) ? null : IPAddress.Parse(details.LastKnownIPAddress),
                        LastKnownPort = details.LastKnownPort
                    };

                    fabric.AddNode(node);
                }
            }
        }

        return fabric;
    }

    /// <summary>SaveFabricAsync.</summary>
    public async Task SaveFabricAsync(Fabric fabric)
    {
        var fabricDir = GetFullPath(fabric.FabricName);
        Directory.CreateDirectory(fabricDir);

        var details = new FabricDetails
        {
            FabricId = fabric.FabricId.ToByteArray(),
            RootCertificateId = fabric.RootCACertificateId.ToByteArray(),
            RootNodeId = fabric.RootNodeId.ToByteArray(),
            AdminVendorId = fabric.AdminVendorId,
            IPK = fabric.IPK,
            OperationalIPK = fabric.OperationalIPK,
            RootKeyIdentifier = fabric.RootKeyIdentifier,
            CompressedFabricId = fabric.CompressedFabricId,
        };

        var json = JsonSerializer.Serialize(details, FabricStorageJsonContext.Default.FabricDetails);

        var fabricJsonPath = Path.Combine(fabricDir, "fabric.json");
        await WriteAtomicAsync(fabricJsonPath, json);
        FabricStorageSecurity.TryApplyOwnerOnlyFilePermissions(fabricJsonPath);
        WritePemAtomic(Path.Combine(fabricDir, "rootCertificate.pem"), fabric.RootCACertificate);
        var rootKeyPairPath = Path.Combine(fabricDir, "rootKeyPair.pem");
        WritePemAtomic(rootKeyPairPath, P256KeyInterop.ToBouncyCastleKeyPair(fabric.RootCAKeyPair));
        FabricStorageSecurity.TryApplyOwnerOnlyFilePermissions(rootKeyPairPath);
        WritePemAtomic(Path.Combine(fabricDir, "operationalCertificate.pem"), fabric.OperationalCertificate);
        var operationalKeyPairPath = Path.Combine(fabricDir, "operationalKeyPair.pem");
        WritePemAtomic(operationalKeyPairPath, P256KeyInterop.ToBouncyCastleKeyPair(fabric.OperationalCertificateKeyPair));
        FabricStorageSecurity.TryApplyOwnerOnlyFilePermissions(operationalKeyPairPath);

        foreach (var node in fabric.Nodes)
        {
            var nodeDirectoryPath = GetFullPath(fabric.FabricName, node.NodeId);
            Directory.CreateDirectory(nodeDirectoryPath);

            var nodeDetails = new NodeDetails
            {
                NodeId = node.NodeId.ToByteArray(),
                LastKnownIPAddress = node.LastKnownIpAddress?.ToString() ?? "",
                LastKnownPort = node.LastKnownPort ?? 0,
            };

            var nodeJson = JsonSerializer.Serialize(nodeDetails, FabricStorageJsonContext.Default.NodeDetails);
            await WriteAtomicAsync(Path.Combine(nodeDirectoryPath, "node.json"), nodeJson);
        }
    }

    /// <summary>Write text to a temp file, then atomically rename to target.</summary>
    private static async Task WriteAtomicAsync(string path, string content)
    {
        var tempPath = CreateTempPath(path);
        try
        {
            await File.WriteAllTextAsync(tempPath, content, _utf8WithoutBom);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    /// <summary>Write a PEM object atomically.</summary>
    private static void WritePemAtomic(string path, object pemObject)
    {
        var tempPath = CreateTempPath(path);
        try
        {
            using (var writer = new StreamWriter(tempPath, append: false, _utf8WithoutBom))
            {
                var pemWriter = new PemWriter(writer);
                pemWriter.WriteObject(pemObject);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static string CreateTempPath(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"No directory for '{path}'.");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static ReadOnlySpan<byte> ReadJsonFileBytes(byte[] bytes)
        => bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? bytes.AsSpan(3) : bytes.AsSpan();

    private string GetFullPath(string fabricName)
    {
        var path = Path.Combine(_rootDirectory, fabricName);
        return path;
    }

    private string GetFullPath(string fabricName, BigInteger nodeId)
    {
        var path = Path.Combine(_rootDirectory, fabricName, nodeId.ToString());
        return path;
    }
}
