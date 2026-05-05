using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace DotMatter.Benchmarks;

[MemoryDiagnoser]
public class SessionCryptoBenchmarks
{
    private byte[] _sharedSecret = default!;
    private byte[] _operationalIpk = default!;
    private byte[] _sigma2ResponderRandom = default!;
    private byte[] _sigma2ResponderEphPublicKey = default!;
    private byte[] _transcriptHash = default!;

    [GlobalSetup]
    public void Setup()
    {
        _sharedSecret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
        _operationalIpk = [.. Enumerable.Range(0, 16).Select(i => (byte)(0xA0 + i))];
        _sigma2ResponderRandom = [.. Enumerable.Range(0, 32).Select(i => (byte)(0x10 + i))];
        _sigma2ResponderEphPublicKey = [.. Enumerable.Range(0, 65).Select(i => (byte)(0x20 + i))];
        _transcriptHash = SHA256.HashData([.. Enumerable.Range(0, 64).Select(i => (byte)(0x30 + i))]);
    }

    [Benchmark]
    public byte[] DeriveCaseSigma2Key()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        writer.Write(_operationalIpk);
        writer.Write(_sigma2ResponderRandom);
        writer.Write(_sigma2ResponderEphPublicKey);
        writer.Write(_transcriptHash);
        writer.Flush();

        var salt = ms.ToArray();
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, _sharedSecret, 16, salt, Encoding.ASCII.GetBytes("Sigma2"));
    }
}
