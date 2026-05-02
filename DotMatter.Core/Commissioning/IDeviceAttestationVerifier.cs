namespace DotMatter.Core.Commissioning;

/// <summary>
/// Verifies device attestation during commissioning (DAC → PAI chain, attestation signature).
/// </summary>
public interface IDeviceAttestationVerifier
{
    /// <summary>
    /// Verify device attestation using DAC and PAI certificates.
    /// Returns the DAC public key bytes on success, throws on failure.
    /// </summary>
    /// <param name="dacCert">DER-encoded DAC (Device Attestation Certificate).</param>
    /// <param name="paiCert">DER-encoded PAI (Product Attestation Intermediate).</param>
    /// <param name="attestationElements">Raw attestation elements TLV from AttestationResponse.</param>
    /// <param name="attestationSignature">ECDSA signature over attestation elements + attestation challenge.</param>
    /// <param name="attestationChallenge">The attestation challenge derived from the secure session.</param>
    byte[] Verify(byte[] dacCert, byte[] paiCert, byte[] attestationElements,
        byte[] attestationSignature, byte[] attestationChallenge);
}
