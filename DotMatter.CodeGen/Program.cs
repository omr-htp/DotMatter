using DotMatter.CodeGen;

string baseDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..");
string clusterXmlDir = Path.GetFullPath(Path.Combine(baseDir, "Xml"));
string clusterOutDir = Path.GetFullPath(Path.Combine(baseDir, "..", "DotMatter.Core", "Clusters"));

if (args.Length >= 1)
{
    clusterXmlDir = args[0];
}

if (args.Length >= 2)
{
    clusterOutDir = args[1];
}

Console.WriteLine("DotMatter.CodeGen — CHIP v1.5 Code Generator");
Console.WriteLine($"  Cluster XMLs     : {clusterXmlDir}");
Console.WriteLine($"  Output dir       : {clusterOutDir}");
Console.WriteLine();

if (!Directory.Exists(clusterXmlDir))
{
    Console.Error.WriteLine($"ERROR: Cluster XML directory not found: {clusterXmlDir}");
    return 1;
}

Directory.CreateDirectory(clusterOutDir);

foreach (var old in Directory.GetFiles(clusterOutDir, "*.g.cs"))
{
    File.Delete(old);
}

var xmlFiles = Directory.GetFiles(clusterXmlDir, "*.xml");
Console.WriteLine($"Found {xmlFiles.Length} cluster XML files");

int clusterCount = 0;
int skipped = 0;
var mergedClusters = new Dictionary<uint, ClusterModel>();
var clusterSources = new Dictionary<uint, List<string>>();
var clusterOrder = new List<uint>();
var globalTypes = new GlobalTypeRegistry();

foreach (var xmlFile in xmlFiles.OrderBy(f => f))
{
    string xmlFileName = Path.GetFileName(xmlFile);
    string xml = File.ReadAllText(xmlFile);
    var clusters = ZapXmlParser.ParseAll(xml);
    var globalTypesInFile = ZapXmlParser.ParseGlobalTypes(xml);

    CodeGenModelUtilities.MergeGlobalTypes(globalTypes, globalTypesInFile);

    if (clusters.Count == 0 && globalTypesInFile.IsEmpty)
    {
        skipped++;
        continue;
    }

    foreach (var cluster in clusters)
    {
        if (mergedClusters.TryGetValue(cluster.Id, out var existing))
        {
            CodeGenModelUtilities.MergeCluster(existing, cluster);
            clusterSources[cluster.Id].Add(xmlFileName);
        }
        else
        {
            mergedClusters.Add(cluster.Id, cluster);
            clusterSources[cluster.Id] = [xmlFileName];
            clusterOrder.Add(cluster.Id);
        }
    }
}

foreach (var clusterId in clusterOrder)
{
    var cluster = mergedClusters[clusterId];
    CodeGenModelUtilities.AttachReferencedGlobalTypes(cluster, globalTypes);
    string csName = $"{cluster.CSharpName}_0x{cluster.Id:X4}.g.cs";
    string sourceLabel = CodeGenModelUtilities.BuildSourceLabel(clusterSources[clusterId]);
    string code = ClusterCodeEmitter.Emit(cluster, sourceLabel);
    string outPath = Path.Combine(clusterOutDir, csName);
    File.WriteAllText(outPath, code);
    clusterCount++;

    int attrs = cluster.Attributes.Count;
    int cmds = cluster.Commands.Count(c => c.IsClientToServer);
    int evts = cluster.Events.Count;
    Console.WriteLine($"  {csName,-50} {attrs}A {cmds}C {evts}E");
}

Console.WriteLine();
Console.WriteLine($"Generated {clusterCount} cluster files ({skipped} XMLs skipped, no clusters)");

string protocolsDir = Path.GetFullPath(Path.Combine(baseDir, "Protocols"));
string protocolOutDir = Path.GetFullPath(Path.Combine(baseDir, "..", "DotMatter.Core", "Protocol", "Generated"));

if (Directory.Exists(protocolsDir))
{
    Console.WriteLine();
    Console.WriteLine($"  Protocol schemas : {protocolsDir}");
    Directory.CreateDirectory(protocolOutDir);

    foreach (var old in Directory.GetFiles(protocolOutDir, "*.g.cs"))
    {
        File.Delete(old);
    }

    int msgTotal = 0;
    foreach (var jsonFile in Directory.GetFiles(protocolsDir, "*.json").OrderBy(f => f))
    {
        string fileName = Path.GetFileName(jsonFile);
        var messages = MessageCodeEmitter.LoadDefinitions(jsonFile);
        if (messages.Count == 0)
        {
            continue;
        }

        string code = MessageCodeEmitter.EmitFile(messages, fileName);
        string baseName = Path.GetFileNameWithoutExtension(jsonFile);
        string csName = Naming.ToPascalCase(baseName) + ".g.cs";
        File.WriteAllText(Path.Combine(protocolOutDir, csName), code);
        Console.WriteLine($"  {csName,-40} {messages.Count} messages");
        msgTotal += messages.Count;
    }

    Console.WriteLine($"Generated {msgTotal} protocol message types");
}

Console.WriteLine("Done.");
return 0;
