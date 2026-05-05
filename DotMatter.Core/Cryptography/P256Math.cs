using System.Numerics;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Math.EC;

namespace DotMatter.Core.Cryptography;

/// <summary>
/// Thin P-256 (secp256r1) point-math wrapper used for Spake2+ (PASE).
/// System.Security.Cryptography does not expose low-level point addition and arbitrary-point
/// scalar multiplication, so we keep the public API in System.Numerics while delegating the
/// group operations to the curve implementation that already exists in the project.
/// </summary>
public static class P256Math
{
    private static readonly X9ECParameters _curveParameters =
        ECNamedCurveTable.GetByName("Secp256r1") ?? throw new InvalidOperationException("P-256 curve not available.");

    private static readonly ECCurve _curve = _curveParameters.Curve;

    // NIST P-256 (secp256r1) Constants
    /// <summary>P.</summary>
    public static readonly BigInteger P = FromBouncyCastle(_curveParameters.Curve.Field.Characteristic);
    /// <summary>N.</summary>
    public static readonly BigInteger N = FromBouncyCastle(_curveParameters.N);
    /// <summary>B.</summary>
    public static readonly BigInteger B = FromBouncyCastle(_curveParameters.Curve.B.ToBigInteger());
    /// <summary>A.</summary>
    /// <summary>The A value.</summary>
    public static readonly BigInteger A = P - 3;

    // Generator G
    /// <summary>G.</summary>
    public static readonly ECPoint G = FromBouncyCastle(_curveParameters.G);

    // Matter-specific constant points M and N
    /// <summary>M.</summary>
    public static readonly ECPoint M = DecodePoint(Convert.FromHexString("02886e2f97ace46e55ba9dd7242579f2993b64e16ef3dcab95afd497333d8fa12f"));
    /// <summary>N_Point.</summary>
    public static readonly ECPoint N_Point = DecodePoint(Convert.FromHexString("03d8bbd6c639c62937b04d997f38c3770719c629d7014d49a24b4f98baa1292b49"));

    /// <summary>ECPoint struct.</summary>
    /// <summary>ECPoint struct.</summary>
    /// <summary>X struct.</summary>
    /// <summary>ECPoint struct.</summary>
    /// <summary>Y struct.</summary>
    /// <summary>IsInfinity struct.</summary>
    /// <summary>X struct.</summary>
    /// <summary>Y struct.</summary>
    /// <summary>IsInfinity struct.</summary>
    public record struct ECPoint(BigInteger X, BigInteger Y, bool IsInfinity = false)
    {
        /// <summary>Infinity.</summary>
        public static readonly ECPoint Infinity = new(0, 0, true);
    }

    /// <summary>ECPoint).</summary>
    public static ECPoint Add(ECPoint p1, ECPoint p2)
    {
        return FromBouncyCastle(ToBouncyCastle(p1).Add(ToBouncyCastle(p2)));
    }

    /// <summary>ECPoint).</summary>
    public static ECPoint Double(ECPoint p)
    {
        return FromBouncyCastle(ToBouncyCastle(p).Twice());
    }

    /// <summary>ECPoint, BigInteger).</summary>
    public static ECPoint Multiply(ECPoint p, BigInteger k)
    {
        if (p.IsInfinity)
        {
            return ECPoint.Infinity;
        }

        k %= N;
        if (k.Sign < 0)
        {
            k += N;
        }

        if (k.IsZero)
        {
            return ECPoint.Infinity;
        }

        if (k.IsOne)
        {
            return p;
        }

        return FromBouncyCastle(ToBouncyCastle(p).Multiply(ToBouncyCastle(k)));
    }

    /// <summary>ECPoint).</summary>
    public static ECPoint Negate(ECPoint p)
    {
        return FromBouncyCastle(ToBouncyCastle(p).Negate());
    }

    /// <summary>ModInverse.</summary>
    public static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        if (a == 0)
        {
            throw new DivideByZeroException();
        }

        if (a < 0)
        {
            a = (a % m) + m;
        }

        return BigInteger.ModPow(a, m - 2, m);
    }

    /// <summary>DecodePoint.</summary>
    public static ECPoint DecodePoint(byte[] encoded)
    {
        if (encoded.Length == 0)
        {
            return ECPoint.Infinity;
        }

        if (encoded.Length == 1 && encoded[0] == 0x00)
        {
            return ECPoint.Infinity;
        }

        return FromBouncyCastle(_curve.DecodePoint(encoded));
    }

    /// <summary>ECPoint, bool).</summary>
    public static byte[] GetEncoded(this ECPoint p, bool compressed)
    {
        if (p.IsInfinity)
        {
            return [0x00];
        }

        byte[] x = p.X.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] xPadded = new byte[32];
        x.CopyTo(xPadded.AsSpan(32 - x.Length));

        if (compressed)
        {
            byte[] result = new byte[33];
            result[0] = (byte)(p.Y.IsEven ? 0x02 : 0x03);
            xPadded.CopyTo(result.AsSpan(1));
            return result;
        }
        else
        {
            byte[] y = p.Y.ToByteArray(isUnsigned: true, isBigEndian: true);
            byte[] yPadded = new byte[32];
            y.CopyTo(yPadded.AsSpan(32 - y.Length));

            byte[] result = new byte[65];
            result[0] = 0x04;
            xPadded.CopyTo(result.AsSpan(1));
            yPadded.CopyTo(result.AsSpan(33));
            return result;
        }
    }

    private static ECPoint FromBouncyCastle(Org.BouncyCastle.Math.EC.ECPoint point)
    {
        var normalized = point.Normalize();
        if (normalized.IsInfinity)
        {
            return ECPoint.Infinity;
        }

        return new ECPoint(
            FromBouncyCastle(normalized.AffineXCoord.ToBigInteger()),
            FromBouncyCastle(normalized.AffineYCoord.ToBigInteger()));
    }

    private static Org.BouncyCastle.Math.EC.ECPoint ToBouncyCastle(ECPoint point)
    {
        if (point.IsInfinity)
        {
            return _curve.Infinity;
        }

        return _curve.CreatePoint(ToBouncyCastle(point.X), ToBouncyCastle(point.Y));
    }

    private static BigInteger FromBouncyCastle(Org.BouncyCastle.Math.BigInteger value)
    {
        return new BigInteger(value.ToByteArrayUnsigned(), isUnsigned: true, isBigEndian: true);
    }

    private static Org.BouncyCastle.Math.BigInteger ToBouncyCastle(BigInteger value)
    {
        return new Org.BouncyCastle.Math.BigInteger(1, value.ToByteArray(isUnsigned: true, isBigEndian: true));
    }
}
