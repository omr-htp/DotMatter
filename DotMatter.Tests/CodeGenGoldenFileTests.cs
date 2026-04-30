using DotMatter.CodeGen;

namespace DotMatter.Tests;

[TestFixture]
public class CodeGenGoldenFileTests
{
    [TestCase("actions-cluster.xml", 0x0025u, "Actions_0x0025.g.cs")]
    [TestCase("account-login-cluster.xml", 0x050Eu, "AccountLogin_0x050E.g.cs")]
    public void GeneratedClusterMatchesTrackedOutput(string xmlFileName, uint clusterId, string generatedFileName)
    {
        var xmlPath = FindCodeGenPath("Xml", xmlFileName);
        var generatedPath = FindRepoPath("DotMatter.Core", "Clusters", generatedFileName);

        var xml = File.ReadAllText(xmlPath);
        var cluster = ZapXmlParser.ParseAll(xml).Single(c => c.Id == clusterId);
        var generated = ClusterCodeEmitter.Emit(cluster, xmlFileName);
        var tracked = File.ReadAllText(generatedPath);

        Assert.That(NormalizeLineEndings(generated), Is.EqualTo(NormalizeLineEndings(tracked)));
    }

    [Test]
    public void ArrayAttributeEntryType_GeneratesTypedReader()
    {
        var xmlPath = FindCodeGenPath("Xml", "access-control-cluster.xml");
        var xml = File.ReadAllText(xmlPath);
        var cluster = ZapXmlParser.ParseAll(xml).Single(c => c.Id == 0x001Fu);
        var generated = ClusterCodeEmitter.Emit(cluster, "access-control-cluster.xml");

        Assert.That(generated, Does.Contain("public Task<AccessControlEntryStruct[]?> ReadACLAsync"));
        Assert.That(generated, Does.Contain("=> ReadArrayAttributeAsync(0x0000, ReadAccessControlEntryStruct, ct);"));
        Assert.That(generated, Does.Contain("public Task<WriteResponse> WriteACLAsync"));
        Assert.That(generated, Does.Contain("tlv.AddArray(2); foreach (var item in aCL)"));
        Assert.That(generated, Does.Contain("if (value.DeviceType != null) { tlv.AddUInt32(2, value.DeviceType.Value); } else { tlv.AddNull(2); }"));
        Assert.That(generated, Does.Contain("public byte FabricIndex { get; set; }"));
        Assert.That(generated, Does.Contain("case 254:"));
    }

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n");

    private static string FindCodeGenPath(params string[] parts)
        => FindRepoPath(["DotMatter.CodeGen", .. parts]);

    private static string FindRepoPath(params string[] parts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{Path.Combine(parts)}'.");
    }
}
