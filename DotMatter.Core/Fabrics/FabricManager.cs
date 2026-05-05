using System.Security.Cryptography;
using System.Text;
using DotMatter.Core.Certificates;
using DotMatter.Core.Cryptography;
using Org.BouncyCastle.Math;

namespace DotMatter.Core.Fabrics;

/// <summary>
/// Loads, creates, and persists fabric metadata.
/// </summary>
public class FabricManager(IFabricStorageProvider storageProvider)
{
    private readonly IFabricStorageProvider _storageProvider = storageProvider;
    private readonly Lock _saveSync = new();
    private Task _pendingSave = Task.CompletedTask;

    /// <summary>
    /// Gets an existing fabric or creates a new one when no persisted fabric exists.
    /// </summary>
    /// <param name="fabricName">The fabric name used by the storage provider.</param>
    public async Task<Fabric> GetAsync(string fabricName)
    {
        Fabric? fabric = null;

        if (_storageProvider.DoesFabricExist(fabricName))
        {
            fabric = await _storageProvider.LoadFabricAsync(fabricName);
        }
        else
        {
            var fabricIdBytes = RandomNumberGenerator.GetBytes(8);
            var fabricId = new BigInteger(1, fabricIdBytes);

            var rootCertificateIdBytes = RandomNumberGenerator.GetBytes(8);
            var rootCertificateId = new BigInteger(1, rootCertificateIdBytes);
            var rootNodeId = new BigInteger(1, rootCertificateIdBytes);

            var rootKeyPair = CertificateAuthority.GenerateECDsa();
            var rootCert = CertificateAuthority.GenerateRootCertificate(rootCertificateId, rootKeyPair);

            var rootPubKeyBytes = P256KeyInterop.ExportPublicKey(rootKeyPair);
            var rootKeyIdentifier = SHA1.HashData(rootPubKeyBytes).AsSpan()[..20].ToArray();

            var ipk = RandomNumberGenerator.GetBytes(16);

            byte[] compressedFabricInfo = Encoding.ASCII.GetBytes("CompressedFabric");

            // Generate the CompressedFabricIdentifier using HKDF.
            // CHIP SDK (CHIPCryptoPAL.cpp GenerateCompressedFabricId):
            //   IKM  = root public key X||Y (64 bytes, skip 04 prefix)
            //   Salt = FabricID in big-endian (8 bytes)
            // Because our TLV UInt64 stores BigInteger bytes as-is (big-endian),
            // the device reads them as LE and gets a byte-swapped value.
            // The device then uses BigEndian(swapped_value) as salt, which equals
            // our fabricIdBytes reversed. We must match that.
            //
            var keyBytes = rootPubKeyBytes.AsSpan().Slice(1, 64).ToArray(); // X||Y = 64 bytes
            var fabricIdSalt = (byte[])fabricIdBytes.Clone();
            Array.Reverse(fabricIdSalt);

            var compressedFabricIdentifier = HKDF.DeriveKey(HashAlgorithmName.SHA256, keyBytes, 8, fabricIdSalt, compressedFabricInfo);

            // Generate the OperationalGroupKey(OperationalIPK) using HKDF.
            //
            byte[] groupKey = Encoding.ASCII.GetBytes("GroupKey v1.0");
            var operationalIPK = HKDF.DeriveKey(HashAlgorithmName.SHA256, ipk, 16, compressedFabricIdentifier, groupKey);

            MatterLog.Info("Fabric ID: {0}", fabricId);
            MatterLog.Debug("Fabric ID bytes (orig BE): {0}", MatterLog.FormatBytes(fabricIdBytes));
            MatterLog.Debug("Fabric ID salt (reversed): {0}", MatterLog.FormatBytes(fabricIdSalt));
            MatterLog.Debug("Root PubKey X||Y ({0} bytes): {1}", keyBytes.Length, MatterLog.FormatBytes(keyBytes));
            MatterLog.Debug("IPK: {0}", MatterLog.FormatSecret(ipk));

            var compressedFabricIdentifierText = Convert.ToHexString(compressedFabricIdentifier);

            MatterLog.Info("CompressedFabricIdentifier: {0}", compressedFabricIdentifierText);
            MatterLog.Debug("OperationalIPK: {0}", MatterLog.FormatSecret(operationalIPK));

            var operationalCertificate = Fabric.GenerateNOC(rootKeyPair, rootKeyIdentifier, fabricId, rootCertificateId);

            fabric = new Fabric()
            {
                FabricId = fabricId,
                FabricName = fabricName,
                RootNodeId = rootNodeId,
                AdminVendorId = 0xFFF1, // Default value from Matter specification 
                RootCAKeyPair = rootKeyPair,
                RootCAECDiffieHellman = ECDiffieHellman.Create(rootKeyPair.ExportParameters(true)),
                RootCACertificateId = rootCertificateId,
                RootCACertificate = rootCert,
                RootKeyIdentifier = rootKeyIdentifier,
                IPK = ipk,
                OperationalIPK = operationalIPK,
                OperationalCertificate = operationalCertificate.Certificate,
                OperationalCertificateKeyPair = operationalCertificate.KeyPair,
                OperationalECDiffieHellman = ECDiffieHellman.Create(operationalCertificate.KeyPair.ExportParameters(true)),
                CompressedFabricId = compressedFabricIdentifierText,
            };

            await _storageProvider.SaveFabricAsync(fabric);
        }

        fabric.NodeAdded += Fabric_NodeAdded;

        return fabric!;
    }

    private void Fabric_NodeAdded(object sender, NodeAddedToFabricEventArgs args)
    {
        if (sender is Fabric fabric)
        {
            QueueFabricSave(fabric);
        }
    }

    private void QueueFabricSave(Fabric fabric)
    {
        lock (_saveSync)
        {
            _pendingSave = _pendingSave.ContinueWith(
                _ => SaveFabricSafeAsync(fabric),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    private async Task SaveFabricSafeAsync(Fabric fabric)
    {
        try
        {
            await _storageProvider.SaveFabricAsync(fabric);
        }
        catch (Exception ex)
        {
            MatterLog.Warn("SaveFabric error: {0}", ex.Message);
        }
    }
}
