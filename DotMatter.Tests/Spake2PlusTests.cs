using DotMatter.Core.Cryptography;
using System.Numerics;
using System.Security.Cryptography;

namespace DotMatter.Tests;

[TestFixture]
public class Spake2PlusTests
{
    [Test]
    public void Test_P256Math_Add_G_Double()
    {
        var g2 = P256Math.Double(P256Math.G);
        var gAdd = P256Math.Add(P256Math.G, P256Math.G);

        Assert.That(gAdd, Is.EqualTo(g2));
    }

    [Test]
    public void Test_P256Math_Multiply_Generator()
    {
        var res = P256Math.Multiply(P256Math.G, BigInteger.One);
        Assert.That(res, Is.EqualTo(P256Math.G));
    }

    [Test]
    public void TestVectors()
    {
        // This test uses some of the Test Vectors from the Spake2+ RFC (https://datatracker.ietf.org/doc/html/draft-bar-cfrg-spake2plus-02#appendix-B)
        //
        BigInteger w0 = new(Convert.FromHexString("e6887cf9bdfb7579c69bf47928a84514b5e355ac034863f7ffaf4390e67d798c"), isUnsigned: true, isBigEndian: true);
        BigInteger w1 = new(Convert.FromHexString("24b5ae4abda868ec9336ffc3b78ee31c5755bef1759227ef5372ca139b94e512"), isUnsigned: true, isBigEndian: true);

        //x < - [0, p)
        BigInteger x = new(Convert.FromHexString("8b0f3f383905cf3a3bb955ef8fb62e24849dd349a05ca79aafb18041d30cbdb6"), isUnsigned: true, isBigEndian: true);

        // G is generator (P)
        // X = x*P + w0*M
        //
        var X = P256Math.Add(P256Math.Multiply(P256Math.G, x), P256Math.Multiply(P256Math.M, w0));

        var Xs = Convert.ToHexString(X.GetEncoded(false));

        // Check that X is generated as expected!
        //
        Assert.That(Xs, Is.EqualTo("04AF09987A593D3BAC8694B123839422C3CC87E37D6B41C1D630F000DD64980E537AE704BCEDE04EA3BEC9B7475B32FA2CA3B684BE14D11645E38EA6609EB39E7E"));

        //y < - [0, p)
        BigInteger y = new(Convert.FromHexString("2e0895b0e763d6d5a9564433e64ac3cac74ff897f6c3445247ba1bab40082a91"), isUnsigned: true, isBigEndian: true);

        //Y = y * P + w0 * N
        var Y = P256Math.Add(P256Math.Multiply(P256Math.G, y), P256Math.Multiply(P256Math.N_Point, w0));

        var Ys = Convert.ToHexString(Y.GetEncoded(false));

        Assert.That(Ys, Is.EqualTo("04417592620AEBF9FD203616BBB9F121B730C258B286F890C5F19FEA833A9C900CBE9057BC549A3E19975BE9927F0E7614F08D1F0A108EEDE5FD7EB5624584A4F4"));

        // yNwo = Y - N*w0
        var yNwo = P256Math.Add(Y, P256Math.Negate(P256Math.Multiply(P256Math.N_Point, w0)));

        //Z = h * x * (Y - w0 * N)
        var Z = P256Math.Multiply(yNwo, x);

        //V = h * w1 * (Y - w0 * N)
        var V = P256Math.Multiply(yNwo, w1);

        var Zs = Convert.ToHexString(Z.GetEncoded(false));
        Assert.That(Zs, Is.EqualTo("0471A35282D2026F36BF3CEB38FCF87E3112A4452F46E9F7B47FD769CFB570145B62589C76B7AA1EB6080A832E5332C36898426912E29C40EF9E9C742EEE82BF30"));

        var Vs = Convert.ToHexString(V.GetEncoded(false));
        Assert.That(Vs, Is.EqualTo("046718981BF15BC4DB538FC1F1C1D058CB0EECECF1DBE1B1EA08A4E25275D382E82B348C8131D8ED669D169C2E03A858DB7CF6CA2853A4071251A39FBE8CFC39BC"));
    }

    [Test]
    public void Test_Spake2Plus_FullHandshake()
    {
        uint passcode = 20202021;
        ushort iterations = 1000;
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        byte[] contextHash = SHA256.HashData("TestContext"u8.ToArray());

        // Initiator (Commissioner)
        var (w0, w1, x, X) = CryptographyMethods.Crypto_PAKEValues_Initiator(passcode, iterations, salt);
        _ = X.GetEncoded(false);

        // Responder (Device)
        BigInteger y = new(RandomNumberGenerator.GetBytes(32), isUnsigned: true, isBigEndian: true);
        var Y_point = P256Math.Add(P256Math.Multiply(P256Math.G, y), P256Math.Multiply(P256Math.N_Point, w0));
        byte[] Y_bytes = Y_point.GetEncoded(false);

        // Initiator processes Sigma2 (Y)
        var (Ke, hAY, hBX) = CryptographyMethods.Crypto_P2(contextHash, w0, w1, x, X, Y_bytes);

        Assert.That(Ke, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Ke, Has.Length.EqualTo(16));
            Assert.That(hAY, Has.Length.EqualTo(32));
            Assert.That(hBX, Has.Length.EqualTo(32));
        }
    }
}
