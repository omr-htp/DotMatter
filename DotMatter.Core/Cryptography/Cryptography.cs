using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace DotMatter.Core.Cryptography;

/// <summary>CryptographyMethods class.</summary>
public class CryptographyMethods
{
    /// <summary>Crypto_PAKEValues_Initiator.</summary>
    public static (BigInteger w0, BigInteger w1, BigInteger x, P256Math.ECPoint X) Crypto_PAKEValues_Initiator(uint passcode, ushort iterations, byte[] salt)
    {
        // https://datatracker.ietf.org/doc/rfc9383/
        //
        var GROUP_SIZE_BYTES = 32;
        var CRYPTO_W_SIZE_BYTES = GROUP_SIZE_BYTES + 8;

        var passcodeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(passcodeBytes, passcode);

        var pbkdf = Rfc2898DeriveBytes.Pbkdf2(passcodeBytes, salt, iterations, HashAlgorithmName.SHA256, 2 * CRYPTO_W_SIZE_BYTES);

        var w0s = new BigInteger(pbkdf.AsSpan()[..CRYPTO_W_SIZE_BYTES], isUnsigned: true, isBigEndian: true);
        var w1s = new BigInteger(pbkdf.AsSpan().Slice(CRYPTO_W_SIZE_BYTES, CRYPTO_W_SIZE_BYTES), isUnsigned: true, isBigEndian: true);

        var w0 = w0s % P256Math.N;
        var w1 = w1s % P256Math.N;

        BigInteger x = new(RandomNumberGenerator.GetBytes(GROUP_SIZE_BYTES), isUnsigned: true, isBigEndian: true);

        // X = G*x + M*w0
        var X = P256Math.Add(P256Math.Multiply(P256Math.G, x), P256Math.Multiply(P256Math.M, w0));

        return (w0, w1, x, X);
    }

    /// <summary>ECPoint, byte[]).</summary>
    public static (byte[] Ke, byte[] hAY, byte[] hBX) Crypto_P2(byte[] contextHash, BigInteger w0, BigInteger w1, BigInteger x, P256Math.ECPoint X, byte[] Y)
    {
        var YPoint = P256Math.DecodePoint(Y);

        // yNwo = Y + [N * w0].Negate() = Y - N*w0
        var yNwo = P256Math.Add(YPoint, P256Math.Negate(P256Math.Multiply(P256Math.N_Point, w0)));
        var Z = P256Math.Multiply(yNwo, x);
        var V = P256Math.Multiply(yNwo, w1);

        return ComputeSecretAndVerifiers(contextHash, w0, X, Y, Z, V);
    }

    private static (byte[] Ke, byte[] hAY, byte[] hBX) ComputeSecretAndVerifiers(byte[] contextHash, BigInteger w0, P256Math.ECPoint X, byte[] Y, P256Math.ECPoint Z, P256Math.ECPoint V)
    {
        var TT_HASH = ComputeTranscriptHash(contextHash, w0, X, Y, Z, V);

        var Ka = TT_HASH.AsSpan()[..16].ToArray();
        var Ke = TT_HASH.AsSpan().Slice(16, 16).ToArray();

        byte[] salt = []; // Empty salt (Uint8Array(0))
        byte[] info = Encoding.ASCII.GetBytes("ConfirmationKeys");

        var KcAB = HKDF.DeriveKey(HashAlgorithmName.SHA256, Ka, 32, salt, info);

        var KcA = KcAB.AsSpan()[..16].ToArray();
        var KcB = KcAB.AsSpan().Slice(16, 16).ToArray();

        var hmac = new HMACSHA256(KcA);
        byte[] hAY = hmac.ComputeHash(Y);

        hmac = new HMACSHA256(KcB);
        byte[] hBX = hmac.ComputeHash(X.GetEncoded(false));

        return (Ke, hAY, hBX);
    }

    private static byte[] ComputeTranscriptHash(byte[] contextHash, BigInteger w0, P256Math.ECPoint X, byte[] Y, P256Math.ECPoint Z, P256Math.ECPoint V)
    {
        var memoryStream = new MemoryStream();
        var TTwriter = new BinaryWriter(memoryStream);

        AddToContext(TTwriter, contextHash);
        AddToContext(TTwriter, BitConverter.GetBytes((ulong)0), BitConverter.GetBytes((ulong)0));
        AddToContext(TTwriter, P256Math.M.GetEncoded(false));
        AddToContext(TTwriter, P256Math.N_Point.GetEncoded(false));
        AddToContext(TTwriter, X.GetEncoded(false));
        AddToContext(TTwriter, Y);
        AddToContext(TTwriter, Z.GetEncoded(false));
        AddToContext(TTwriter, V.GetEncoded(false));
        AddToContext(TTwriter, w0.ToByteArray(isUnsigned: true, isBigEndian: true));

        TTwriter.Flush();

        var bytes = memoryStream.ToArray();

        return SHA256.HashData(bytes);
    }

    private static void AddToContext(BinaryWriter TTwriter, byte[] data)
    {
        var lengthBytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(lengthBytes, (ulong)data.Length);

        TTwriter.Write(lengthBytes);
        TTwriter.Write(data);
    }

    private static void AddToContext(BinaryWriter TTwriter, byte[] length, byte[] data)
    {
        TTwriter.Write(length);
        TTwriter.Write(data);
    }
}
