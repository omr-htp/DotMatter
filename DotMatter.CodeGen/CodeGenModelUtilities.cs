namespace DotMatter.CodeGen;

sealed class GlobalTypeRegistry
{
    public Dictionary<string, EnumModel> Enums { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, BitmapModel> Bitmaps { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, StructModel> Structs { get; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Enums.Count == 0 && Bitmaps.Count == 0 && Structs.Count == 0;
}

static class CodeGenModelUtilities
{
    public static string BuildSourceLabel(List<string> sources)
    {
        if (sources.Count == 0)
        {
            return "unknown";
        }

        if (sources.Count == 1)
        {
            return sources[0];
        }

        return $"{sources[0]} (+{sources.Count - 1} merged XML file(s))";
    }

    public static void MergeCluster(ClusterModel target, ClusterModel source)
    {
        if (string.IsNullOrWhiteSpace(target.Name) || target.Name.StartsWith("Cluster 0x", StringComparison.Ordinal))
        {
            target.Name = source.Name;
        }

        target.Revision = Math.Max(target.Revision, source.Revision);

        MergeUnique(target.Features, source.Features, static item => $"{item.Bit}:{item.Code}:{item.Name}");
        MergeUnique(target.Attributes, source.Attributes, static item => item.Id.ToString());
        MergeUnique(target.Commands, source.Commands, static item => $"{item.Id}:{item.IsClientToServer}");
        MergeUnique(target.Events, source.Events, static item => item.Id.ToString());
        MergeUnique(target.Enums, source.Enums, static item => item.Name);
        MergeUnique(target.Bitmaps, source.Bitmaps, static item => item.Name);
        MergeUnique(target.Structs, source.Structs, static item => item.Name);
    }

    public static void MergeGlobalTypes(GlobalTypeRegistry target, GlobalTypeRegistry source)
    {
        MergeNamedTypes(target.Enums, source.Enums);
        MergeNamedTypes(target.Bitmaps, source.Bitmaps);
        MergeNamedTypes(target.Structs, source.Structs);
    }

    public static void AttachReferencedGlobalTypes(ClusterModel cluster, GlobalTypeRegistry globalTypes)
    {
        if (globalTypes.IsEmpty)
        {
            return;
        }

        bool foundNewReferences;
        do
        {
            foundNewReferences = false;
            var existingTypeNames = GetLocalTypeNames(cluster);
            var referencedTypes = EnumerateReferencedCustomTypeNames(cluster).ToArray();

            foreach (var typeName in referencedTypes)
            {
                if (existingTypeNames.Contains(typeName))
                {
                    continue;
                }

                if (globalTypes.Enums.TryGetValue(typeName, out var enumModel))
                {
                    cluster.Enums.Add(enumModel);
                    existingTypeNames.Add(typeName);
                    foundNewReferences = true;
                    continue;
                }

                if (globalTypes.Bitmaps.TryGetValue(typeName, out var bitmapModel))
                {
                    cluster.Bitmaps.Add(bitmapModel);
                    existingTypeNames.Add(typeName);
                    foundNewReferences = true;
                    continue;
                }

                if (globalTypes.Structs.TryGetValue(typeName, out var structModel))
                {
                    cluster.Structs.Add(structModel);
                    existingTypeNames.Add(typeName);
                    foundNewReferences = true;
                }
            }
        }
        while (foundNewReferences);
    }

    static HashSet<string> GetLocalTypeNames(ClusterModel cluster)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cluster.Enums)
        {
            names.Add(Naming.ToPascalCase(item.Name));
        }

        foreach (var item in cluster.Bitmaps)
        {
            names.Add(Naming.ToPascalCase(item.Name));
        }

        foreach (var item in cluster.Structs)
        {
            names.Add(Naming.ToPascalCase(item.Name));
        }

        return names;
    }

    static IEnumerable<string> EnumerateReferencedCustomTypeNames(ClusterModel cluster)
    {
        foreach (var attr in cluster.Attributes)
        {
            string? typeName = GetReferencedCustomTypeName(attr.Type, attr.EntryType, attr.IsArray);
            if (typeName != null)
            {
                yield return typeName;
            }
        }

        foreach (var command in cluster.Commands)
        {
            foreach (var field in command.Fields)
            {
                string? typeName = GetReferencedCustomTypeName(field.Type, field.EntryType, field.IsArray);
                if (typeName != null)
                {
                    yield return typeName;
                }
            }
        }

        foreach (var evt in cluster.Events)
        {
            foreach (var field in evt.Fields)
            {
                string? typeName = GetReferencedCustomTypeName(field.Type, field.EntryType, field.IsArray);
                if (typeName != null)
                {
                    yield return typeName;
                }
            }
        }

        foreach (var structModel in cluster.Structs)
        {
            foreach (var field in structModel.Fields)
            {
                string? typeName = GetReferencedCustomTypeName(field.Type, field.EntryType, field.IsArray);
                if (typeName != null)
                {
                    yield return typeName;
                }
            }
        }
    }

    static string? GetReferencedCustomTypeName(string matterType, string? entryType, bool isArray)
    {
        string? candidate = isArray && !string.IsNullOrWhiteSpace(entryType) ? entryType : matterType;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (candidate.Equals("array", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string mapped = Naming.MapCSharpType(candidate, false, false, false, null, localTypes: null);
        if (mapped != "object")
        {
            return null;
        }

        return Naming.ToPascalCase(candidate);
    }

    static void MergeNamedTypes<T>(Dictionary<string, T> target, Dictionary<string, T> source)
    {
        foreach (var (key, value) in source)
        {
            target.TryAdd(key, value);
        }
    }

    static void MergeUnique<T>(List<T> target, IEnumerable<T> source, Func<T, string> keySelector)
    {
        var seen = new HashSet<string>(target.Select(keySelector), StringComparer.Ordinal);
        foreach (var item in source)
        {
            if (seen.Add(keySelector(item)))
            {
                target.Add(item);
            }
        }
    }
}
