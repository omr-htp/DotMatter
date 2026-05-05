using System.Formats.Asn1;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace DotMatter.Core.Certificates;

internal sealed record MatterDistinguishedNameAttribute(string Oid, string Value);

/// <summary>
/// Signs Matter certificates by building X.509 DER TBS bytes that match
/// what the CHIP SDK's DecodeConvertTBSCert reconstructs from TLV.
/// The CHIP SDK verifies certs by: TLV → reconstruct DER TBS → SHA-256 → verify ECDSA.
/// We must sign the same DER TBS bytes so the signature validates.
/// </summary>
public static class MatterCertSigner
{
    private static readonly DateTime _matterEpoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Sign a root certificate (self-signed). Returns 64-byte r||s for TLV tag 11.
    /// </summary>
    public static byte[] SignRootCert(
        byte[] serialNumber,
        BigInteger rcacId,
        uint notBeforeEpoch,
        uint notAfterEpoch,
        byte[] publicKey,       // 65 bytes uncompressed
        byte[] subjectKeyId,    // 20 bytes SHA-1
        ECDsa signingKey)
    {
        var rcacHex = MatterIdToHex(rcacId);
        var dn = new[] { new MatterDistinguishedNameAttribute("1.3.6.1.4.1.37244.1.4", rcacHex) };

        var notBefore = _matterEpoch.AddSeconds(notBeforeEpoch);
        var notAfter = _matterEpoch.AddSeconds(notAfterEpoch);

        var extDer = BuildRootExtensionsDer(subjectKeyId);
        var derTbs = BuildDerTbs(serialNumber, dn, notBefore, notAfter, dn, publicKey, extDer);

        return signingKey.SignData(derTbs, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>
    /// Sign a NOC (signed by root CA). Returns 64-byte r||s for TLV tag 11.
    /// </summary>
    public static byte[] SignNoc(
        byte[] serialNumber,
        BigInteger rcacId,
        BigInteger nodeId,
        BigInteger fabricId,
        uint notBeforeEpoch,
        uint notAfterEpoch,
        byte[] publicKey,
        byte[] subjectKeyId,
        byte[] authorityKeyId,
        ECDsa signingKey)
    {
        var issuerDn = new[] { new MatterDistinguishedNameAttribute("1.3.6.1.4.1.37244.1.4", MatterIdToHex(rcacId)) };
        var subjectDn = new[] {
            new MatterDistinguishedNameAttribute("1.3.6.1.4.1.37244.1.1", MatterIdToHex(nodeId)),
            new MatterDistinguishedNameAttribute("1.3.6.1.4.1.37244.1.5", MatterIdToHex(fabricId))
        };

        var notBefore = _matterEpoch.AddSeconds(notBeforeEpoch);
        var notAfter = _matterEpoch.AddSeconds(notAfterEpoch);

        var extDer = BuildNocExtensionsDer(subjectKeyId, authorityKeyId);
        var derTbs = BuildDerTbs(serialNumber, issuerDn, notBefore, notAfter, subjectDn, publicKey, extDer);

        return signingKey.SignData(derTbs, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>
    /// Convert BigInteger (from Tomas's Fabric code) to the 16-char uppercase hex string
    /// that the CHIP SDK produces via Uint64ToHex when reading from TLV.
    /// </summary>
    static string MatterIdToHex(BigInteger id)
    {
        var be = id.ToByteArrayUnsigned();
        var padded = new byte[8];
        if (be.Length <= 8)
        {
            Array.Copy(be, 0, padded, 8 - be.Length, be.Length);
        }
        else
        {
            Array.Copy(be, be.Length - 8, padded, 0, 8);
        }
        // TLV stores ToByteArrayUnsigned() bytes directly. CHIP SDK reads as LE uint64.
        // BitConverter.ToUInt64 on LE system interprets bytes as LE → same value CHIP SDK reads.
        ulong val = BitConverter.ToUInt64(padded);
        return val.ToString("X16");
    }

    /// <summary>
    /// Build the X.509 DER TBS structure matching CHIP SDK's DecodeConvertTBSCert output.
    /// </summary>
    internal static byte[] BuildDerTbs(
        byte[] serialNumber,
        MatterDistinguishedNameAttribute[] issuer,
        DateTime notBefore,
        DateTime notAfter,
        MatterDistinguishedNameAttribute[] subject,
        byte[] publicKey,
        byte[] extensionsSequenceDer)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);

        using (w.PushSequence()) // TBSCertificate
        {
            // Version [0] EXPLICIT INTEGER 2 (v3)
            using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
            {
                w.WriteInteger(2);
            }

            // SerialNumber INTEGER — use WriteInteger (signed/two's complement) to match
            // CHIP SDK's PutValue which writes raw bytes without unsigned padding.
            w.WriteInteger(serialNumber);

            // Signature AlgorithmIdentifier: ecdsaWithSHA256
            using (w.PushSequence())
            {
                w.WriteObjectIdentifier("1.2.840.10045.4.3.2");
            }

            // Issuer Name
            WriteName(w, issuer);

            // Validity
            using (w.PushSequence())
            {
                WriteTime(w, notBefore);
                WriteTime(w, notAfter);
            }

            // Subject Name
            WriteName(w, subject);

            // SubjectPublicKeyInfo
            using (w.PushSequence())
            {
                using (w.PushSequence()) // AlgorithmIdentifier
                {
                    w.WriteObjectIdentifier("1.2.840.10045.2.1");   // ecPublicKey
                    w.WriteObjectIdentifier("1.2.840.10045.3.1.7"); // prime256v1
                }
                w.WriteBitString(publicKey); // uncompressed point
            }

            // Extensions [3] EXPLICIT SEQUENCE
            using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 3, true)))
            {
                w.WriteEncodedValue(extensionsSequenceDer);
            }
        }

        return w.Encode();
    }

    /// <summary>
    /// Write a Name (SEQUENCE of RDN SETs). Each RDN = SET { SEQUENCE { OID, UTF8String } }.
    /// Matches CHIP SDK's ChipDN::EncodeToASN1 which uses Uint64ToHex for Matter IDs.
    /// </summary>
    internal static void WriteName(AsnWriter w, MatterDistinguishedNameAttribute[] attrs)
    {
        using (w.PushSequence()) // Name = SEQUENCE
        {
            foreach (var attr in attrs)
            {
                using (w.PushSetOf()) // RDN = SET
                using (w.PushSequence()) // AttributeTypeAndValue
                {
                    w.WriteObjectIdentifier(attr.Oid);
                    w.WriteCharacterString(UniversalTagNumber.UTF8String, attr.Value);
                }
            }
        }
    }

    /// <summary>
    /// Write ASN.1 time matching CHIP SDK's ChipEpochToASN1Time.
    /// Years before 2050: UTCTime. Years >= 2050: GeneralizedTime.
    /// </summary>
    internal static void WriteTime(AsnWriter w, DateTime dt)
    {
        if (dt.Year < 2050)
        {
            w.WriteUtcTime(dt);
        }
        else
        {
            w.WriteGeneralizedTime(dt, omitFractionalSeconds: true);
        }
    }

    /// <summary>
    /// Build root cert extensions SEQUENCE DER.
    /// Order: BasicConstraints, KeyUsage, SubjectKeyId, AuthorityKeyId.
    /// </summary>
    static byte[] BuildRootExtensionsDer(byte[] keyId)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence()) // Extensions SEQUENCE
        {
            // 1. BasicConstraints (critical): SEQUENCE { BOOLEAN TRUE }
            WriteExtension(w, "2.5.29.19", critical: true, BuildBasicConstraintsDer(isCA: true));

            // 2. KeyUsage (critical): keyCertSign | cRLSign → bits 5,6 → byte 0x06
            WriteExtension(w, "2.5.29.15", critical: true, BuildKeyUsageDer(0x06, unusedBits: 1));

            // 3. SubjectKeyIdentifier (not critical)
            WriteExtension(w, "2.5.29.14", critical: false, BuildSubjectKeyIdDer(keyId));

            // 4. AuthorityKeyIdentifier (not critical)
            WriteExtension(w, "2.5.29.35", critical: false, BuildAuthorityKeyIdDer(keyId));
        }
        return w.Encode();
    }

    /// <summary>
    /// Build NOC extensions SEQUENCE DER.
    /// Order: BasicConstraints, KeyUsage, ExtKeyUsage, SubjectKeyId, AuthorityKeyId.
    /// </summary>
    static byte[] BuildNocExtensionsDer(byte[] subjectKeyId, byte[] authorityKeyId)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            // 1. BasicConstraints (critical): SEQUENCE {} (CA=false → empty per DER)
            WriteExtension(w, "2.5.29.19", critical: true, BuildBasicConstraintsDer(isCA: false));

            // 2. KeyUsage (critical): digitalSignature → bit 0 → byte 0x80
            WriteExtension(w, "2.5.29.15", critical: true, BuildKeyUsageDer(0x80, unusedBits: 7));

            // 3. ExtendedKeyUsage (critical): clientAuth + serverAuth
            WriteExtension(w, "2.5.29.37", critical: true, BuildExtKeyUsageDer());

            // 4. SubjectKeyIdentifier
            WriteExtension(w, "2.5.29.14", critical: false, BuildSubjectKeyIdDer(subjectKeyId));

            // 5. AuthorityKeyIdentifier
            WriteExtension(w, "2.5.29.35", critical: false, BuildAuthorityKeyIdDer(authorityKeyId));
        }
        return w.Encode();
    }

    /// <summary>Write a single X.509 extension.</summary>
    internal static void WriteExtension(AsnWriter w, string oid, bool critical, byte[] valueDer)
    {
        using (w.PushSequence())
        {
            w.WriteObjectIdentifier(oid);
            if (critical)
            {
                w.WriteBoolean(true);
            }

            w.WriteOctetString(valueDer);
        }
    }

    /// <summary>BasicConstraints extension value DER.</summary>
    internal static byte[] BuildBasicConstraintsDer(bool isCA)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            if (isCA)
            {
                w.WriteBoolean(true);
            }
            // If not CA, CHIP SDK writes empty SEQUENCE (DER default omission)
        }
        return w.Encode();
    }

    /// <summary>KeyUsage extension value DER (BIT STRING).</summary>
    internal static byte[] BuildKeyUsageDer(byte bits, int unusedBits)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        w.WriteBitString([bits], unusedBits);
        return w.Encode();
    }

    /// <summary>ExtendedKeyUsage extension value DER: clientAuth + serverAuth.</summary>
    static byte[] BuildExtKeyUsageDer()
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            w.WriteObjectIdentifier("1.3.6.1.5.5.7.3.2"); // clientAuth
            w.WriteObjectIdentifier("1.3.6.1.5.5.7.3.1"); // serverAuth
        }
        return w.Encode();
    }

    /// <summary>SubjectKeyIdentifier extension value DER: OCTET STRING { keyId }.</summary>
    internal static byte[] BuildSubjectKeyIdDer(byte[] keyId)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        w.WriteOctetString(keyId);
        return w.Encode();
    }

    /// <summary>
    /// AuthorityKeyIdentifier extension value DER:
    /// SEQUENCE { [0] IMPLICIT OCTET STRING keyId }.
    /// </summary>
    internal static byte[] BuildAuthorityKeyIdDer(byte[] keyId)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            // [0] IMPLICIT = context-specific, primitive, tag 0
            w.WriteOctetString(keyId, new Asn1Tag(TagClass.ContextSpecific, 0));
        }
        return w.Encode();
    }

    /// <summary>
    /// Sign DER TBS bytes with ECDSA-SHA256, extract r||s as 64 bytes (32+32, zero-padded).
    /// </summary>
    static byte[] SignAndExtractRS(byte[] derTbs, ECPrivateKeyParameters key)
    {
        var signer = SignerUtilities.GetSigner("SHA-256withECDSA");
        signer.Init(true, key);
        signer.BlockUpdate(derTbs, 0, derTbs.Length);
        var derSig = signer.GenerateSignature();

        // Parse DER: SEQUENCE { INTEGER r, INTEGER s }
        AsnDecoder.ReadSequence(derSig, AsnEncodingRules.DER, out var ofs, out var len, out _);
        var inner = derSig.AsSpan().Slice(ofs, len).ToArray();
        var r = AsnDecoder.ReadInteger(inner, AsnEncodingRules.DER, out var bc);
        var s = AsnDecoder.ReadInteger(inner.AsSpan()[bc..], AsnEncodingRules.DER, out _);

        // Pad r,s to exactly 32 bytes each (P-256)
        var rBytes = r.ToByteArray(isUnsigned: true, isBigEndian: true);
        var sBytes = s.ToByteArray(isUnsigned: true, isBigEndian: true);

        var result = new byte[64];
        Array.Copy(rBytes, 0, result, 32 - rBytes.Length, rBytes.Length);
        Array.Copy(sBytes, 0, result, 64 - sBytes.Length, sBytes.Length);

        return result;
    }
}
