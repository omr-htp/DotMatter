using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using System.Security.Cryptography;

namespace DotMatter.Core.Cryptography;

internal static class P256KeyInterop
{
    private const int P256FieldSize = 32;

    private static readonly X9ECParameters _curveParameters =
        ECNamedCurveTable.GetByName("Secp256r1") ?? throw new InvalidOperationException("P-256 curve not available.");

    private static readonly ECDomainParameters _domainParameters = new(_curveParameters);

    public static ECDsa CreateEcdsa()
    {
        return ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }

    public static ECDiffieHellman CreateEcdh()
    {
        return ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    }

    public static byte[] ExportPublicKey(ECDsa key)
    {
        var parameters = key.ExportParameters(false);
        return ExportPublicKey(parameters.Q);
    }

    public static byte[] ExportPublicKey(ECDiffieHellman key)
    {
        var parameters = key.ExportParameters(false);
        return ExportPublicKey(parameters.Q);
    }

    public static byte[] ExportPublicKey(ECPublicKeyParameters key)
    {
        return key.Q.GetEncoded(false);
    }

    public static byte[] ExportPublicKey(ECPoint point)
    {
        if (point.X is null || point.Y is null)
        {
            throw new InvalidOperationException("P-256 public key is missing coordinates.");
        }

        var bytes = new byte[65];
        bytes[0] = 0x04;
        point.X.CopyTo(bytes.AsSpan(1, 32));
        point.Y.CopyTo(bytes.AsSpan(33, 32));
        return bytes;
    }

    public static ECDsa ImportPublicKey(byte[] uncompressedPoint)
    {
        ValidateUncompressedPoint(uncompressedPoint);

        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = uncompressedPoint.AsSpan(1, 32).ToArray(),
                Y = uncompressedPoint.AsSpan(33, 32).ToArray()
            }
        });
    }

    public static ECDsa ImportPublicKey(ECPublicKeyParameters publicKey)
    {
        return ImportPublicKey(ExportPublicKey(publicKey));
    }

    public static ECDsa ImportPrivateKey(ECPrivateKeyParameters privateKey)
    {
        var q = privateKey.Parameters.G.Multiply(privateKey.D).Normalize();
        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = ToFixedLengthUnsigned(privateKey.D.ToByteArrayUnsigned(), P256FieldSize),
            Q = new ECPoint
            {
                X = ToFixedLengthUnsigned(q.AffineXCoord.GetEncoded(), P256FieldSize),
                Y = ToFixedLengthUnsigned(q.AffineYCoord.GetEncoded(), P256FieldSize)
            }
        });
    }

    public static ECPublicKeyParameters ToBouncyCastlePublicKey(ECDsa key)
    {
        var parameters = key.ExportParameters(false);
        return new ECPublicKeyParameters(ToBouncyCastlePoint(parameters.Q), _domainParameters);
    }

    public static ECPrivateKeyParameters ToBouncyCastlePrivateKey(ECDsa key)
    {
        var parameters = key.ExportParameters(true);
        if (parameters.D is null)
        {
            throw new InvalidOperationException("ECDsa key does not contain a private component.");
        }

        return new ECPrivateKeyParameters(new BigInteger(1, parameters.D), _domainParameters);
    }

    public static AsymmetricCipherKeyPair ToBouncyCastleKeyPair(ECDsa key)
    {
        return new AsymmetricCipherKeyPair(ToBouncyCastlePublicKey(key), ToBouncyCastlePrivateKey(key));
    }

    private static Org.BouncyCastle.Math.EC.ECPoint ToBouncyCastlePoint(ECPoint point)
    {
        if (point.X is null || point.Y is null)
        {
            throw new InvalidOperationException("P-256 public key is missing coordinates.");
        }

        return _curveParameters.Curve.CreatePoint(new BigInteger(1, point.X), new BigInteger(1, point.Y));
    }

    private static void ValidateUncompressedPoint(byte[] uncompressedPoint)
    {
        if (uncompressedPoint.Length != 65 || uncompressedPoint[0] != 0x04)
        {
            throw new ArgumentException("Expected an uncompressed P-256 public key.", nameof(uncompressedPoint));
        }
    }

    private static byte[] ToFixedLengthUnsigned(byte[] value, int length)
    {
        if (value.Length == length)
        {
            return value;
        }

        if (value.Length > length)
        {
            throw new InvalidOperationException($"Expected an unsigned value of at most {length} bytes.");
        }

        var padded = new byte[length];
        Buffer.BlockCopy(value, 0, padded, length - value.Length, value.Length);
        return padded;
    }
}
