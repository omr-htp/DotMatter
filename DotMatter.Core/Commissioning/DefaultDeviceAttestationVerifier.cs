using DotMatter.Core.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.X509;

namespace DotMatter.Core.Commissioning;

/// <summary>
/// Default device attestation verifier.
/// Verifies DAC → PAI certificate chain and attestation signature.
/// Does not verify PAI → PAA (requires CSA trust store download).
/// </summary>
/// <param name="allowTestDevices">
/// If true, attestation failures are logged as warnings instead of throwing.
/// Use for development/test devices that may lack valid attestation certs.
/// </param>
public sealed class DefaultDeviceAttestationVerifier(bool allowTestDevices = false) : IDeviceAttestationVerifier
{
    private readonly bool _allowTestDevices = allowTestDevices;

    /// <summary>Verify.</summary>
    public byte[] Verify(byte[] dacCert, byte[] paiCert, byte[] attestationElements,
        byte[] attestationSignature, byte[] attestationNonce)
    {
        try
        {
            var dacX509 = new X509CertificateParser().ReadCertificate(dacCert);
            var paiX509 = new X509CertificateParser().ReadCertificate(paiCert);

            // Verify DAC is signed by PAI
            dacX509.Verify(paiX509.GetPublicKey());

            // Extract DAC public key
            var dacPubKeyParameters = (ECPublicKeyParameters)dacX509.GetPublicKey();
            var dacPubKeyBytes = P256KeyInterop.ExportPublicKey(dacPubKeyParameters);
            using var dacECDsa = P256KeyInterop.ImportPublicKey(dacPubKeyParameters);

            // Verify attestation signature: ECDSA-SHA256 over (attestationElements || attestationNonce)
            var tbs = new byte[attestationElements.Length + attestationNonce.Length];
            Buffer.BlockCopy(attestationElements, 0, tbs, 0, attestationElements.Length);
            Buffer.BlockCopy(attestationNonce, 0, tbs, attestationElements.Length, attestationNonce.Length);

            if (!dacECDsa.VerifyData(tbs, attestationSignature, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                throw new MatterCertificateException("Attestation signature verification failed");
            }

            // Validate DAC subject key usage: digitalSignature must be set
            var keyUsage = dacX509.GetKeyUsage();
            if (keyUsage != null && !keyUsage[0]) // bit 0 = digitalSignature
            {
                throw new MatterCertificateException("DAC missing digitalSignature key usage");
            }

            MatterLog.Info("Device attestation verified: DAC signed by PAI, attestation signature valid");
            return dacPubKeyBytes;
        }
        catch (Exception ex) when (_allowTestDevices && ex is not OperationCanceledException)
        {
            MatterLog.Warn("Device attestation failed (test device allowed): {0}", ex.Message);
            // Return empty — caller should still proceed but may want to log
            // Try to extract DAC public key even if verification failed
            try
            {
                var dacX509 = new X509CertificateParser().ReadCertificate(dacCert);
                var dacPubKey = (ECPublicKeyParameters)dacX509.GetPublicKey();
                return P256KeyInterop.ExportPublicKey(dacPubKey);
            }
            catch
            {
                return [];
            }
        }
    }
}
