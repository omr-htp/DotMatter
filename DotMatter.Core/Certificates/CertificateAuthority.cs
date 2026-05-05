using System.Security.Cryptography;
using DotMatter.Core.Cryptography;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace DotMatter.Core.Certificates;

/// <summary>CertificateAuthority class.</summary>
public class CertificateAuthority
{
    /// <summary>GenerateRootCertificate.</summary>
    public static X509Certificate GenerateRootCertificate(BigInteger rootCertificateId, ECDsa keyPair)
    {
        return GenerateRootCertificate(rootCertificateId, P256KeyInterop.ToBouncyCastleKeyPair(keyPair));
    }

    /// <summary>GenerateRootCertificate.</summary>
    public static X509Certificate GenerateRootCertificate(BigInteger rootCertificateId, AsymmetricCipherKeyPair keyPair)
    {
        var privateKey = keyPair.Private as ECPrivateKeyParameters;
        var publicKey = (ECPublicKeyParameters)keyPair.Public;


        var rootKeyIdentifier = SHA1.HashData(publicKey.Q.GetEncoded(false)).AsSpan()[..20].ToArray();

        var rcacIdHex = rootCertificateId.ToString(16).ToUpperInvariant().PadLeft(16, '0');

        var subjectOids = new List<DerObjectIdentifier>();
        var subjectValues = new List<string>();

        subjectOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.4"));
        subjectValues.Add(rcacIdHex);

        X509Name subjectDN = new(subjectOids, subjectValues);

        var issuerOids = new List<DerObjectIdentifier>();
        var issuerValues = new List<string>();

        issuerOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.4"));
        issuerValues.Add(rcacIdHex);

        X509Name issuerDN = new(issuerOids, issuerValues);

        var certificateGenerator = new X509V3CertificateGenerator();
        certificateGenerator.SetSerialNumber(rootCertificateId);
        certificateGenerator.SetPublicKey(publicKey);
        certificateGenerator.SetSubjectDN(subjectDN);
        certificateGenerator.SetIssuerDN(issuerDN);
        certificateGenerator.SetNotBefore(DateTime.UtcNow.AddYears(-1));
        certificateGenerator.SetNotAfter(DateTime.UtcNow.AddYears(10));
        certificateGenerator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(true));
        certificateGenerator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign));
        certificateGenerator.AddExtension(X509Extensions.SubjectKeyIdentifier, false, new SubjectKeyIdentifier(rootKeyIdentifier));
        certificateGenerator.AddExtension(X509Extensions.AuthorityKeyIdentifier, false, new AuthorityKeyIdentifier(rootKeyIdentifier));

        ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA256WITHECDSA", privateKey);

        var rootCertificate = certificateGenerator.Generate(signatureFactory);

        return rootCertificate;
    }

    /// <summary>GenerateECDsa.</summary>
    public static ECDsa GenerateECDsa()
    {
        return P256KeyInterop.CreateEcdsa();
    }

    /// <summary>ToECDsa.</summary>
    public static ECDsa ToECDsa(ECPrivateKeyParameters privKey)
    {
        return P256KeyInterop.ImportPrivateKey(privKey);
    }

    /// <summary>ToECPublicKeyParameters.</summary>
    public static ECPublicKeyParameters ToECPublicKeyParameters(ECDsa ecdsa)
    {
        return P256KeyInterop.ToBouncyCastlePublicKey(ecdsa);
    }

    /// <summary>ToAsymmetricCipherKeyPair.</summary>
    public static AsymmetricCipherKeyPair ToAsymmetricCipherKeyPair(ECDsa ecdsa)
    {
        return P256KeyInterop.ToBouncyCastleKeyPair(ecdsa);
    }

    /// <summary>ToECDsa.</summary>
    public static ECDsa ToECDsa(ECPublicKeyParameters pubKey)
    {
        return P256KeyInterop.ImportPublicKey(pubKey);
    }

    /// <summary>GenerateECDiffieHellman.</summary>
    public static ECDiffieHellman GenerateECDiffieHellman()
    {
        return P256KeyInterop.CreateEcdh();
    }

    /// <summary>GenerateKeyPair.</summary>
    public static AsymmetricCipherKeyPair GenerateKeyPair()
    {
        //var curve = ECNamedCurveTable.GetByName("P-256");

        // Include the curve name in the key parameters (prime256v1)
        //
        var ecParam = new DerObjectIdentifier("1.2.840.10045.3.1.7");

        var secureRandom = new SecureRandom();

        var keyParams = new ECKeyGenerationParameters(ecParam, secureRandom);

        var generator = new ECKeyPairGenerator("ECDSA");
        generator.Init(keyParams);
        var keyPair = generator.GenerateKeyPair();

        return keyPair;
    }
}
