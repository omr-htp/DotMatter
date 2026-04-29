using BenchmarkDotNet.Attributes;
using DotMatter.Core.TLV;

namespace DotMatter.Benchmarks;

[MemoryDiagnoser]
public class TlvBenchmarks
{
    private byte[] _reportPayload = default!;

    [GlobalSetup]
    public void Setup()
    {
        _reportPayload = Convert.FromHexString("15360115350126006B65DE653701240200240328240401182C020E6D61747465722D6E6F64652E6A73181818290424FF0C18");
    }

    [Benchmark]
    public byte[] EncodeCertificateLikePayload()
    {
        var tlv = new MatterTLV();
        tlv.AddStructure();
        tlv.AddOctetString(1, [1, 2, 3, 4, 5, 6, 7, 8]);
        tlv.AddUInt8(2, 1);
        tlv.AddList(3);
        tlv.AddUInt64(20, 0x1122334455667788);
        tlv.EndContainer();
        tlv.AddUInt32(4, 123456);
        tlv.AddUInt32(5, 654321);
        tlv.AddList(6);
        tlv.AddUTF8String(17, "node");
        tlv.AddUTF8String(21, "fabric");
        tlv.EndContainer();
        tlv.EndContainer();
        return tlv.GetBytes();
    }

    [Benchmark]
    public string DecodeReportPayloadToString()
    {
        var tlv = new MatterTLV(_reportPayload);
        return tlv.ToString();
    }
}
