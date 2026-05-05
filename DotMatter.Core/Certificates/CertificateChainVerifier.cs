using System.Formats.Asn1;
using System.Security.Cryptography;
using DotMatter.Core.Cryptography;
using DotMatter.Core.TLV;

namespace DotMatter.Core.Certificates;

/// <summary>
/// Verifies Matter certificate chains received during CASE Sigma2.
/// Parses Matter TLV certificates, reconstructs DER TBS, and verifies ECDSA signatures.
/// </summary>
public static class CertificateChainVerifier
{
    private static readonly DateTime _matterEpoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Matter TLV cert DN attribute tag → X.509 OID
    private static readonly Dictionary<int, string> _dnTagToOid = new()
    {
        [17] = "1.3.6.1.4.1.37244.1.1", // matter-node-id
        [18] = "1.3.6.1.4.1.37244.1.2", // matter-firmware-signing-id
        [19] = "1.3.6.1.4.1.37244.1.3", // matter-icac-id
        [20] = "1.3.6.1.4.1.37244.1.4", // matter-rcac-id
        [21] = "1.3.6.1.4.1.37244.1.5", // matter-fabric-id
        [22] = "1.3.6.1.4.1.37244.1.6", // matter-noc-cat
    };

    // TLV EKU value → X.509 OID
    private static readonly Dictionary<int, string> _ekuValueToOid = new()
    {
        [1] = "1.3.6.1.5.5.7.3.1", // serverAuth
        [2] = "1.3.6.1.5.5.7.3.2", // clientAuth
    };

    private sealed record TlvSubjectAttribute(int Tag, ulong Value);

    private readonly record struct DerKeyUsage(byte DerByte, int UnusedBits);

    sealed class ParsedCert
    {
        public byte[] SerialNumber = default!;
        public MatterDistinguishedNameAttribute[] IssuerDn = default!;
        public DateTime NotBefore, NotAfter;
        public MatterDistinguishedNameAttribute[] SubjectDn = default!;
        public byte[] PublicKey = default!;
        public bool IsCA;
        public ushort KeyUsage;
        public int[]? ExtKeyUsage;
        public byte[] SubjectKeyId = default!;
        public byte[] AuthorityKeyId = default!;
        public byte[] Signature = default!;
        public List<TlvSubjectAttribute> SubjectAttrs = [];
    }

    /// <summary>
    /// Verify the responder's certificate chain from Sigma2 TBE data.
    /// Returns the responder's NOC public key (65 bytes, uncompressed P-256).
    /// </summary>
    public static byte[] VerifySigma2CertChain(
        byte[] nocTlvBytes,
        byte[]? icacTlvBytes,
        ECDsa rootCaPublicKey,
        Org.BouncyCastle.Math.BigInteger expectedFabricId,
        Org.BouncyCastle.Math.BigInteger? expectedNodeId = null)
    {
        ECDsa nocIssuerKey;

        if (icacTlvBytes != null)
        {
            var icac = ParseTlvCert(icacTlvBytes);
            VerifyCertSignature(icac, rootCaPublicKey);
            nocIssuerKey = DecodeP256PublicKey(icac.PublicKey);
        }
        else
        {
            nocIssuerKey = rootCaPublicKey;
        }

        var noc = ParseTlvCert(nocTlvBytes);
        VerifyCertSignature(noc, nocIssuerKey);

        var fabricId = GetRequiredSubjectAttribute(noc, 21, "fabric ID");
        var expectedFabricValue = ToTlvSubjectUlong(expectedFabricId);
        if (fabricId != expectedFabricValue)
        {
            throw new MatterCertificateException(
                $"NOC fabric ID 0x{fabricId:X16} != expected 0x{expectedFabricValue:X16}");
        }

        if (expectedNodeId != null)
        {
            var nodeId = GetRequiredSubjectAttribute(noc, 17, "node ID");
            var expectedNodeValue = ToTlvSubjectUlong(expectedNodeId);
            if (nodeId != expectedNodeValue)
            {
                throw new MatterCertificateException(
                    $"NOC node ID 0x{nodeId:X16} != expected 0x{expectedNodeValue:X16}");
            }
        }

        return noc.PublicKey;
    }

    static ulong GetRequiredSubjectAttribute(ParsedCert cert, int tag, string name)
    {
        var attr = cert.SubjectAttrs.Find(a => a.Tag == tag);
        return attr is null ? throw new MatterCertificateException($"NOC missing {name} in subject") : attr.Value;
    }

    static ulong ToTlvSubjectUlong(Org.BouncyCastle.Math.BigInteger value)
    {
        var bytes = value.ToByteArrayUnsigned();
        var padded = new byte[8];
        if (bytes.Length <= 8)
        {
            Array.Copy(bytes, 0, padded, 8 - bytes.Length, bytes.Length);
        }
        else
        {
            Array.Copy(bytes, bytes.Length - 8, padded, 0, 8);
        }

        return BitConverter.ToUInt64(padded);
    }

    /// <summary>
    /// Verify TBSData2 signature — proves responder holds the NOC private key.
    /// </summary>
    public static void VerifyTbs2Signature(
        byte[] nocTlvBytes,
        byte[]? icacTlvBytes,
        byte[] responderEphPubKey,
        byte[] initiatorEphPubKey,
        byte[] tbs2Signature,
        byte[] nocPublicKey)
    {
        // Construct TBSData2 (Matter spec §4.14.2.6)
        var tbs2 = new MatterTLV();
        tbs2.AddStructure();
        tbs2.AddOctetString(1, nocTlvBytes);
        if (icacTlvBytes != null)
        {
            tbs2.AddOctetString(2, icacTlvBytes);
        }

        tbs2.AddOctetString(3, responderEphPubKey);
        tbs2.AddOctetString(4, initiatorEphPubKey);
        tbs2.EndContainer();

        using var ecPubKey = DecodeP256PublicKey(nocPublicKey);
        VerifyRawSignature(tbs2.AsSpan(), tbs2Signature, ecPubKey);
    }

    static ParsedCert ParseTlvCert(byte[] tlvBytes)
    {
        var cert = new ParsedCert();
        var tlv = new MatterTLV(tlvBytes);
        tlv.OpenStructure();

        cert.SerialNumber = tlv.GetOctetString(1);
        _ = (byte)tlv.GetUnsignedInt(2); // signature algorithm (always 1)

        // Issuer DN (List, tag 3)
        tlv.OpenList(3);
        var issuerAttrs = new List<TlvSubjectAttribute>();
        while (!tlv.IsEndContainerNext())
        {
            int tag = tlv.PeekTag();
            ulong val = (ulong)tlv.GetUnsignedInt(tag);
            issuerAttrs.Add(new TlvSubjectAttribute(tag, val));
        }
        tlv.CloseContainer();
        cert.IssuerDn = [.. issuerAttrs.Select(a => new MatterDistinguishedNameAttribute(_dnTagToOid[a.Tag], a.Value.ToString("X16")))];

        cert.NotBefore = _matterEpoch.AddSeconds((uint)tlv.GetUnsignedInt(4));
        cert.NotAfter = _matterEpoch.AddSeconds((uint)tlv.GetUnsignedInt(5));

        // Subject DN (List, tag 6)
        tlv.OpenList(6);
        while (!tlv.IsEndContainerNext())
        {
            int tag = tlv.PeekTag();
            ulong val = (ulong)tlv.GetUnsignedInt(tag);
            cert.SubjectAttrs.Add(new TlvSubjectAttribute(tag, val));
        }
        tlv.CloseContainer();
        cert.SubjectDn = [.. cert.SubjectAttrs.Select(a => new MatterDistinguishedNameAttribute(_dnTagToOid[a.Tag], a.Value.ToString("X16")))];

        _ = (byte)tlv.GetUnsignedInt(7); // public-key-algorithm (always 1)
        _ = (byte)tlv.GetUnsignedInt(8); // elliptic-curve-id (always 1)
        cert.PublicKey = tlv.GetOctetString(9);

        // Extensions (List, tag 10)
        tlv.OpenList(10);
        ParseExtensions(tlv, cert);
        tlv.CloseContainer();

        cert.Signature = tlv.GetOctetString(11);
        return cert;
    }

    static void ParseExtensions(MatterTLV tlv, ParsedCert cert)
    {
        while (!tlv.IsEndContainerNext())
        {
            int tag = tlv.PeekTag();
            switch (tag)
            {
                case 1: // BasicConstraints
                    tlv.OpenStructure(1);
                    cert.IsCA = !tlv.IsEndContainerNext() && tlv.GetBoolean(1);
                    tlv.CloseContainer();
                    break;
                case 2: // KeyUsage
                    cert.KeyUsage = (ushort)tlv.GetUnsignedInt(2);
                    break;
                case 3: // ExtendedKeyUsage
                    tlv.OpenArray(3);
                    var ekuList = new List<int>();
                    while (!tlv.IsEndContainerNext())
                    {
                        ekuList.Add((int)tlv.GetUnsignedInt(null));
                    }

                    tlv.CloseContainer();
                    cert.ExtKeyUsage = [.. ekuList];
                    break;
                case 4: // SubjectKeyIdentifier
                    cert.SubjectKeyId = tlv.GetOctetString(4);
                    break;
                case 5: // AuthorityKeyIdentifier
                    cert.AuthorityKeyId = tlv.GetOctetString(5);
                    break;
                default:
                    tlv.SkipElement();
                    break;
            }
        }
    }

    static void VerifyCertSignature(ParsedCert cert, ECDsa issuerKey)
    {
        var extDer = BuildExtensionsDer(cert);
        var derTbs = MatterCertSigner.BuildDerTbs(
            cert.SerialNumber, cert.IssuerDn,
            cert.NotBefore, cert.NotAfter,
            cert.SubjectDn, cert.PublicKey, extDer);

        VerifyRawSignature(derTbs, cert.Signature, issuerKey);
    }

    static byte[] BuildExtensionsDer(ParsedCert cert)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            MatterCertSigner.WriteExtension(w, "2.5.29.19", true,
                MatterCertSigner.BuildBasicConstraintsDer(cert.IsCA));

            var keyUsage = TlvKeyUsageToDer(cert.KeyUsage);
            MatterCertSigner.WriteExtension(w, "2.5.29.15", true,
                MatterCertSigner.BuildKeyUsageDer(keyUsage.DerByte, keyUsage.UnusedBits));

            if (cert.ExtKeyUsage != null)
            {
                var ekuDer = BuildExtKeyUsageDer(cert.ExtKeyUsage);
                MatterCertSigner.WriteExtension(w, "2.5.29.37", true, ekuDer);
            }

            MatterCertSigner.WriteExtension(w, "2.5.29.14", false,
                MatterCertSigner.BuildSubjectKeyIdDer(cert.SubjectKeyId));

            MatterCertSigner.WriteExtension(w, "2.5.29.35", false,
                MatterCertSigner.BuildAuthorityKeyIdDer(cert.AuthorityKeyId));
        }
        return w.Encode();
    }

    static byte[] BuildExtKeyUsageDer(int[] ekuValues)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            foreach (var val in ekuValues)
            {
                if (_ekuValueToOid.TryGetValue(val, out var oid))
                {
                    w.WriteObjectIdentifier(oid);
                }
            }
        }
        return w.Encode();
    }

    static DerKeyUsage TlvKeyUsageToDer(ushort tlvFlags)
    {
        byte der = 0;
        int highestBit = -1;
        for (int i = 0; i < 9; i++)
        {
            if ((tlvFlags & (1 << i)) != 0)
            {
                der |= (byte)(0x80 >> i);
                highestBit = i;
            }
        }
        int unused = highestBit >= 0 ? 7 - highestBit : 0;
        return new DerKeyUsage(der, unused);
    }

    /// <summary>
    /// Verify ECDSA-SHA256 signature in raw r||s format (64 bytes).
    /// </summary>
    static void VerifyRawSignature(ReadOnlySpan<byte> data, byte[] rawSignature, ECDsa publicKey)
    {
        if (!publicKey.VerifyData(data, rawSignature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            throw new MatterCertificateException("Certificate signature verification failed");
        }
    }

    static ECDsa DecodeP256PublicKey(byte[] uncompressedPoint)
    {
        return P256KeyInterop.ImportPublicKey(uncompressedPoint);
    }
}
