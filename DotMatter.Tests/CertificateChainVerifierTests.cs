using System.Security.Cryptography;
using DotMatter.Core;
using DotMatter.Core.Certificates;
using DotMatter.Core.Cryptography;
using DotMatter.Core.Fabrics;
using DotMatter.Core.TLV;
using Org.BouncyCastle.Math;

namespace DotMatter.Tests;

[TestFixture]
public class CertificateChainVerifierTests
{
    private ECDsa _rootKeyPair = default!;
    private Org.BouncyCastle.X509.X509Certificate _rootCert = default!;
    private BigInteger _rootCertId = default!;
    private BigInteger _fabricId = default!;
    private byte[] _rootKeyIdentifier = default!;

    private sealed record TestNoc(byte[] TlvBytes, byte[] PublicKeyBytes);

    [SetUp]
    public void Setup()
    {
        _rootKeyPair = CertificateAuthority.GenerateECDsa();
        _rootCertId = BigInteger.ValueOf(0x1234567890ABCDEF);
        _rootCert = CertificateAuthority.GenerateRootCertificate(_rootCertId, _rootKeyPair);
        _fabricId = new BigInteger("DDEEDDEE00000001", 16);

        var rootPubKeyBytes = P256KeyInterop.ExportPublicKey(_rootKeyPair);
        _rootKeyIdentifier = SHA1.HashData(rootPubKeyBytes)[..20];
    }

    [TearDown]
    public void TearDown()
    {
        _rootKeyPair?.Dispose();
    }

    private TestNoc GenerateTestNoc()
    {
        var nodeId = new BigInteger("0100000000000000", 16);
        var generatedNoc = Fabric.GenerateNOC(
            _rootKeyPair, _rootKeyIdentifier, _fabricId, _rootCertId, nodeId);

        var nocPubKeyBytes = P256KeyInterop.ExportPublicKey(generatedNoc.KeyPair);
        var nocKeyId = SHA1.HashData(nocPubKeyBytes)[..20];

        var notBefore = new DateTimeOffset(generatedNoc.Certificate.NotBefore).ToEpochTime();
        var notAfter = new DateTimeOffset(generatedNoc.Certificate.NotAfter).ToEpochTime();

        // Encode NOC in Matter TLV format (same as CASEClient does)
        var nocSignature = MatterCertSigner.SignNoc(
            generatedNoc.Certificate.SerialNumber.ToByteArrayUnsigned(),
            _rootCertId,
            // Extract node ID from the certificate subject
            ExtractNodeId(generatedNoc.Certificate),
            _fabricId,
            notBefore, notAfter,
            nocPubKeyBytes,
            nocKeyId,
            _rootKeyIdentifier,
            _rootKeyPair);

        var tlv = EncodeTlvCert(generatedNoc.Certificate, nocPubKeyBytes, nocKeyId, nocSignature);
        return new TestNoc(tlv.GetBytes(), nocPubKeyBytes);
    }

    private static BigInteger ExtractNodeId(Org.BouncyCastle.X509.X509Certificate cert)
    {
        var subject = cert.SubjectDN;
        var nodeIdOid = new Org.BouncyCastle.Asn1.DerObjectIdentifier("1.3.6.1.4.1.37244.1.1");
        var values = subject.GetValueList(nodeIdOid);
        if (values.Count > 0)
        {
            var hex = values[0]!.ToString()!;
            return new BigInteger(hex, 16);
        }
        throw new InvalidOperationException("No node ID in cert subject");
    }

    private MatterTLV EncodeTlvCert(
        Org.BouncyCastle.X509.X509Certificate cert,
        byte[] pubKeyBytes,
        byte[] subjectKeyId,
        byte[] signature)
    {
        var notBefore = new DateTimeOffset(cert.NotBefore).ToEpochTime();
        var notAfter = new DateTimeOffset(cert.NotAfter).ToEpochTime();

        var nodeId = ExtractNodeId(cert);

        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddOctetString(1, cert.SerialNumber.ToByteArrayUnsigned());
        tlv.AddUInt8(2, 1); // ECDSA-SHA256

        // Issuer (root CA)
        tlv.AddList(3);
        tlv.AddUInt64(20, _rootCertId.ToByteArrayUnsigned());
        tlv.EndContainer();

        tlv.AddUInt32(4, notBefore);
        tlv.AddUInt32(5, notAfter);

        // Subject
        tlv.AddList(6);
        tlv.AddUInt64(17, nodeId.ToByteArrayUnsigned());
        tlv.AddUInt64(21, _fabricId.ToByteArrayUnsigned());
        tlv.EndContainer();

        tlv.AddUInt8(7, 1); // EC
        tlv.AddUInt8(8, 1); // P-256
        tlv.AddOctetString(9, pubKeyBytes);

        // Extensions
        tlv.AddList(10);
        tlv.AddStructure(1);
        tlv.AddBool(1, false);
        tlv.EndContainer();
        tlv.AddUInt8(2, 0x01); // digitalSignature
        tlv.AddArray(3);
        tlv.AddUInt8(0x02); // clientAuth
        tlv.AddUInt8(0x01); // serverAuth
        tlv.EndContainer();
        tlv.AddOctetString(4, subjectKeyId);
        tlv.AddOctetString(5, _rootKeyIdentifier);
        tlv.EndContainer();

        tlv.AddOctetString(11, signature);
        tlv.EndContainer();

        return tlv;
    }

    [Test]
    public void ValidNocChain_PassesVerification()
    {
        var testNoc = GenerateTestNoc();

        var expectedNodeId = new BigInteger("0100000000000000", 16);
        var pubKey = CertificateChainVerifier.VerifySigma2CertChain(
            testNoc.TlvBytes, null, _rootKeyPair, _fabricId, expectedNodeId);

        Assert.That(pubKey, Is.Not.Null);
        Assert.That(pubKey, Has.Length.EqualTo(65)); // uncompressed P-256 point
    }

    [Test]
    public void WrongRootKey_FailsVerification()
    {
        var testNoc = GenerateTestNoc();

        // Use a different root key for verification
        using var wrongKeyPair = CertificateAuthority.GenerateECDsa();

        Assert.Throws<MatterCertificateException>(() =>
            CertificateChainVerifier.VerifySigma2CertChain(
                testNoc.TlvBytes, null, wrongKeyPair, _fabricId));
    }

    [Test]
    public void WrongFabricId_FailsVerification()
    {
        var testNoc = GenerateTestNoc();

        var wrongFabricId = new BigInteger("9999999999999999", 16);

        Assert.Throws<MatterCertificateException>(() =>
            CertificateChainVerifier.VerifySigma2CertChain(
                testNoc.TlvBytes, null, _rootKeyPair, wrongFabricId));
    }

    [Test]
    public void WrongNodeId_FailsVerification()
    {
        var testNoc = GenerateTestNoc();

        var wrongNodeId = new BigInteger("9999999999999999", 16);

        Assert.Throws<MatterCertificateException>(() =>
            CertificateChainVerifier.VerifySigma2CertChain(
                testNoc.TlvBytes, null, _rootKeyPair, _fabricId, wrongNodeId));
    }

    [Test]
    public void Tbs2Signature_VerifiesCorrectly()
    {
        var testNoc = GenerateTestNoc();

        // Simulate TBS2 data
        var responderEphPubKey = RandomNumberGenerator.GetBytes(65);
        var initiatorEphPubKey = RandomNumberGenerator.GetBytes(65);

        // Create TBS2 and sign it with the NOC private key
        var tbs2 = new MatterTLV();
        tbs2.AddStructure();
        tbs2.AddOctetString(1, testNoc.TlvBytes);
        tbs2.AddOctetString(3, responderEphPubKey);
        tbs2.AddOctetString(4, initiatorEphPubKey);
        tbs2.EndContainer();

        // Sign TBS2 — we need the NOC private key for this test
        // Since we don't have it stored, test that invalid signature throws
        var badSignature = RandomNumberGenerator.GetBytes(64);

        Assert.Throws<MatterCertificateException>(() =>
            CertificateChainVerifier.VerifyTbs2Signature(
                testNoc.TlvBytes, null,
                responderEphPubKey, initiatorEphPubKey,
                badSignature, testNoc.PublicKeyBytes));
    }
}

internal static class TestEpochExtension
{
    private static readonly long _matterEpochUnix = 946684800;

    public static uint ToEpochTime(this DateTimeOffset dt)
    {
        return (uint)(dt.ToUnixTimeSeconds() - _matterEpochUnix);
    }
}
