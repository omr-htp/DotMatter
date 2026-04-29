using DotMatter.Core.Certificates;
using DotMatter.Core.Cryptography;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;
using System.Security.Cryptography;

namespace DotMatter.Core.Fabrics;

/// <summary>Fabric class.</summary>
public class Fabric
{
    /// <summary>RootCAKeyPair.</summary>
    /// <summary>Gets or sets the RootCAKeyPair value.</summary>
    public ECDsa RootCAKeyPair { get; set; } = default!;

    /// <summary>RootCAECDiffieHellman.</summary>
    /// <summary>Gets or sets the RootCAECDiffieHellman value.</summary>
    public ECDiffieHellman RootCAECDiffieHellman { get; set; } = default!;

    /// <summary>RootCACertificateId.</summary>
    /// <summary>Gets or sets the RootCACertificateId value.</summary>
    public BigInteger RootCACertificateId { get; set; } = default!;

    /// <summary>RootCACertificate.</summary>
    /// <summary>Gets or sets the RootCACertificate value.</summary>
    public X509Certificate RootCACertificate { get; set; } = default!;

    /// <summary>IPK.</summary>
    /// <summary>Gets or sets the IPK value.</summary>
    public byte[] IPK { get; set; } = default!;

    /// <summary>OperationalIPK.</summary>
    /// <summary>Gets or sets the OperationalIPK value.</summary>
    public byte[] OperationalIPK { get; set; } = default!;

    /// <summary>RootNodeId.</summary>
    /// <summary>Gets or sets the RootNodeId value.</summary>
    public BigInteger RootNodeId { get; set; } = default!;

    /// <summary>AdminVendorId.</summary>
    /// <summary>Gets or sets the AdminVendorId value.</summary>
    public ushort AdminVendorId { get; set; }

    /// <summary>RootKeyIdentifier.</summary>
    /// <summary>Gets or sets the RootKeyIdentifier value.</summary>
    public byte[] RootKeyIdentifier { get; set; } = default!;

    /// <summary>FabricId.</summary>
    /// <summary>Gets or sets the FabricId value.</summary>
    public BigInteger FabricId { get; set; } = default!;

    /// <summary>FabricName.</summary>
    /// <summary>Gets or sets the FabricName value.</summary>
    public string FabricName { get; set; } = default!;

    /// <summary>OperationalCertificate.</summary>
    /// <summary>Gets or sets the OperationalCertificate value.</summary>
    public X509Certificate OperationalCertificate { get; set; } = default!;

    /// <summary>OperationalCertificateKeyPair.</summary>
    /// <summary>Gets or sets the OperationalCertificateKeyPair value.</summary>
    public ECDsa OperationalCertificateKeyPair { get; set; } = default!;

    /// <summary>OperationalECDiffieHellman.</summary>
    /// <summary>Gets or sets the OperationalECDiffieHellman value.</summary>
    public ECDiffieHellman OperationalECDiffieHellman { get; set; } = default!;

    /// <summary>Nodes.</summary>
    /// <summary>Gets or sets the Nodes value.</summary>
    public List<Node> Nodes { get; } = [];

    /// <summary>CompressedFabricId.</summary>
    /// <summary>Gets or sets the CompressedFabricId value.</summary>
    public string CompressedFabricId { get; set; } = default!;

    /// <summary>RootPublicKeyBytes.</summary>
    public byte[] RootPublicKeyBytes
    {
        get
        {
            return P256KeyInterop.ExportPublicKey(RootCAKeyPair);
        }
    }

    /// <summary>NodeAddedToFabric delegate.</summary>
    public delegate void NodeAddedToFabric(object sender, NodeAddedToFabricEventArgs args);
    /// <summary>NodeAdded delegate.</summary>
    public event NodeAddedToFabric NodeAdded = delegate { };

    internal static (X509Certificate, ECDsa) GenerateNOC(
        ECDsa rootKeyPair,
        byte[] rootKeyIdentifier,
        BigInteger fabricId,
        BigInteger rootCertificateId,
        BigInteger? nodeId = null)
    {
        var keyPair = CertificateAuthority.GenerateECDsa();
        var nocPublicKeyBytes = P256KeyInterop.ExportPublicKey(keyPair);

        var nocKeyIdentifier = SHA1.HashData(nocPublicKeyBytes).AsSpan()[..20].ToArray();

        var certGenerator = new X509V3CertificateGenerator();
        var randomGenerator = new CryptoApiRandomGenerator();
        var random = new SecureRandom(randomGenerator);
        var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);

        certGenerator.SetSerialNumber(serialNumber);

        BigInteger nocNodeId;
        if (nodeId != null)
        {
            nocNodeId = nodeId;
        }
        else
        {
            var idBytes = RandomNumberGenerator.GetBytes(8);
            nocNodeId = new BigInteger(1, idBytes);
        }

        var subjectOids = new List<DerObjectIdentifier>();
        var subjectValues = new List<string>();

        subjectOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.1")); // NodeId
        subjectOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.5")); // FabricId
        subjectValues.Add(nocNodeId.ToString(16).ToUpperInvariant().PadLeft(16, '0'));
        subjectValues.Add(fabricId.ToString(16).ToUpperInvariant().PadLeft(16, '0'));

        X509Name subjectDN = new(subjectOids, subjectValues);

        certGenerator.SetSubjectDN(subjectDN);

        var rcacIdHex = rootCertificateId.ToString(16).ToUpperInvariant().PadLeft(16, '0');

        var issuerOids = new List<DerObjectIdentifier>();
        var issuerValues = new List<string>();

        issuerOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.4"));
        issuerValues.Add(rcacIdHex);

        X509Name issuerDN = new(issuerOids, issuerValues);

        certGenerator.SetIssuerDN(issuerDN); // The root certificate is the issuer.

        certGenerator.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        certGenerator.SetNotAfter(DateTime.UtcNow.AddYears(10));

        certGenerator.SetPublicKey(P256KeyInterop.ToBouncyCastlePublicKey(keyPair));

        // Add the BasicConstraints and SubjectKeyIdentifier extensions
        //
        certGenerator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
        certGenerator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature));
        certGenerator.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(KeyPurposeID.id_kp_clientAuth, KeyPurposeID.id_kp_serverAuth));
        certGenerator.AddExtension(X509Extensions.SubjectKeyIdentifier, false, new SubjectKeyIdentifier(nocKeyIdentifier));
        certGenerator.AddExtension(X509Extensions.AuthorityKeyIdentifier, false, new AuthorityKeyIdentifier(rootKeyIdentifier));

        // Create a signature factory for the specified algorithm. Sign the cert with the RootCertificate PrivateyKey
        //
        var bcRootPrivKey = P256KeyInterop.ToBouncyCastlePrivateKey(rootKeyPair);
        ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA256WITHECDSA", bcRootPrivKey);
        var noc = certGenerator.Generate(signatureFactory);

        return (noc, keyPair);
    }

    /// <summary>AddCommissionedNodeAsync.</summary>
    public async Task AddCommissionedNodeAsync(BigInteger peerNodeId, System.Net.IPAddress address, ushort port)
    {
        Nodes.Add(new Node()
        {
            NodeId = peerNodeId,
            LastKnownIpAddress = address,
            LastKnownPort = port,
        });

        NodeAdded?.Invoke(this, new NodeAddedToFabricEventArgs()
        {
            NodeId = peerNodeId,
        });
    }

    /// <summary>CreateNode.</summary>
    public static Node CreateNode()
    {
        var nodeIdBytes = RandomNumberGenerator.GetBytes(8);
        var nodeId = new BigInteger(1, nodeIdBytes);

        return new Node()
        {
            NodeId = nodeId
        };
    }

    /// <summary>AddNode.</summary>
    public void AddNode(Node node)
    {
        node.Fabric = this;
        Nodes.Add(node);
    }

    /// <summary>GetFullNodeName.</summary>
    public string GetFullNodeName(Node node)
    {
        // Specification 1.4 - 4.3.2.1.Operational Instance Name
        //
        return $"{CompressedFabricId}-{node.NodeId}";
    }
}
