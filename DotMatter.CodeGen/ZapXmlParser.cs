using System.Xml.Linq;

namespace DotMatter.CodeGen;

/// <summary>
/// Parses CHIP ZAP-format and CSA-format cluster XML files.
/// ZAP root: &lt;configurator&gt;  |  CSA root: &lt;cluster&gt;
/// </summary>
static class ZapXmlParser
{
    public static List<ClusterModel> ParseAll(string xml)
    {
        var results = new List<ClusterModel>();
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null)
            {
                return results;
            }

            if (root.Name.LocalName == "configurator")
            {
                ParseZapFormat(root, results);
            }
            else if (root.Name.LocalName == "cluster")
            {
                ParseCsaFormat(root, results);
            }
        }
        catch
        {
            // Skip unparseable files silently
        }
        return results;
    }

    public static GlobalTypeRegistry ParseGlobalTypes(string xml)
    {
        var registry = new GlobalTypeRegistry();

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root?.Name.LocalName != "configurator")
            {
                return registry;
            }

            foreach (var enumEl in root.Elements("enum"))
            {
                if (GetClusterCodes(enumEl).Count != 0)
                {
                    continue;
                }

                var model = ParseZapEnum(enumEl);
                if (model != null)
                {
                    registry.Enums.TryAdd(Naming.ToPascalCase(model.Name), model);
                }
            }

            foreach (var bitmapEl in root.Elements("bitmap"))
            {
                if (GetClusterCodes(bitmapEl).Count != 0)
                {
                    continue;
                }

                var model = ParseZapBitmap(bitmapEl);
                if (model != null)
                {
                    registry.Bitmaps.TryAdd(Naming.ToPascalCase(model.Name), model);
                }
            }

            foreach (var structEl in root.Elements("struct"))
            {
                if (GetClusterCodes(structEl).Count != 0)
                {
                    continue;
                }

                var model = ParseZapStruct(structEl);
                if (model != null)
                {
                    registry.Structs.TryAdd(Naming.ToPascalCase(model.Name), model);
                }
            }
        }
        catch
        {
            // Skip unparseable files silently
        }

        return registry;
    }

    // ── ZAP format ──────────────────────────────────────────────

    static void ParseZapFormat(XElement root, List<ClusterModel> results)
    {
        var topEnums = new Dictionary<uint, List<EnumModel>>();
        var topBitmaps = new Dictionary<uint, List<BitmapModel>>();
        var topStructs = new Dictionary<uint, List<StructModel>>();
        var clustersById = new Dictionary<uint, ClusterModel>();
        var clusterOrder = new List<uint>();

        AddTopLevelModels(root.Elements("enum"), topEnums, ParseZapEnum);
        AddTopLevelModels(root.Elements("bitmap"), topBitmaps, ParseZapBitmap);
        AddTopLevelModels(root.Elements("struct"), topStructs, ParseZapStruct);

        foreach (var clusterEl in root.Elements("cluster"))
        {
            var cluster = ParseZapCluster(clusterEl);
            if (cluster == null)
            {
                continue;
            }

            if (topEnums.TryGetValue(cluster.Id, out var enums))
            {
                AddMissingItems(cluster.Enums, enums, static item => item.Name);
            }

            if (topBitmaps.TryGetValue(cluster.Id, out var bitmaps))
            {
                AddMissingItems(cluster.Bitmaps, bitmaps, static item => item.Name);
            }

            if (topStructs.TryGetValue(cluster.Id, out var structs))
            {
                AddMissingItems(cluster.Structs, structs, static item => item.Name);
            }

            clustersById[cluster.Id] = cluster;
            clusterOrder.Add(cluster.Id);
        }

        foreach (var extensionEl in root.Elements("clusterExtension"))
        {
            uint? code = ParseZapClusterCode(extensionEl.Attribute("code")?.Value);
            if (code == null)
            {
                continue;
            }

            if (!clustersById.TryGetValue(code.Value, out var cluster))
            {
                cluster = new ClusterModel
                {
                    Id = code.Value,
                    Name = $"Cluster 0x{code.Value:X4}",
                    Revision = 1,
                };

                if (topEnums.TryGetValue(cluster.Id, out var enums))
                {
                    AddMissingItems(cluster.Enums, enums, static item => item.Name);
                }

                if (topBitmaps.TryGetValue(cluster.Id, out var bitmaps))
                {
                    AddMissingItems(cluster.Bitmaps, bitmaps, static item => item.Name);
                }

                if (topStructs.TryGetValue(cluster.Id, out var structs))
                {
                    AddMissingItems(cluster.Structs, structs, static item => item.Name);
                }

                clustersById.Add(cluster.Id, cluster);
                clusterOrder.Add(cluster.Id);
            }

            ApplyZapClusterContent(cluster, extensionEl);
        }

        foreach (var clusterId in clusterOrder)
        {
            results.Add(clustersById[clusterId]);
        }
    }

    static void AddTopLevelModels<T>(
        IEnumerable<XElement> elements,
        Dictionary<uint, List<T>> modelsByClusterId,
        Func<XElement, T?> parse)
        where T : class
    {
        foreach (var element in elements)
        {
            var codes = GetClusterCodes(element);
            var model = parse(element);
            if (model == null)
            {
                continue;
            }

            foreach (var code in codes)
            {
                if (!modelsByClusterId.TryGetValue(code, out var list))
                {
                    modelsByClusterId[code] = list = [];
                }

                list.Add(model);
            }
        }
    }

    static ClusterModel? ParseZapCluster(XElement el)
    {
        var nameEl = el.Element("name");
        var codeEl = el.Element("code");
        if (nameEl == null || codeEl == null)
        {
            return null;
        }

        var cluster = new ClusterModel
        {
            Name = nameEl.Value.Trim(),
            Id = Naming.ParseHex(codeEl.Value.Trim()),
            Revision = 1,
        };

        ApplyZapClusterMetadata(cluster, el);
        ApplyZapClusterContent(cluster, el);

        return cluster;
    }

    static void ApplyZapClusterMetadata(ClusterModel cluster, XElement el)
    {
        var revAttr = el.Elements("globalAttribute")
            .FirstOrDefault(a => a.Attribute("code")?.Value == "0xFFFD");
        if (revAttr != null && int.TryParse(revAttr.Attribute("value")?.Value, out var rev))
        {
            cluster.Revision = rev;
        }
    }

    static void ApplyZapClusterContent(ClusterModel cluster, XElement el)
    {
        var featuresEl = el.Element("features");
        if (featuresEl != null)
        {
            foreach (var f in featuresEl.Elements("feature"))
            {
                cluster.Features.Add(new FeatureModel
                {
                    Bit = int.TryParse(f.Attribute("bit")?.Value, out var b) ? b : 0,
                    Code = f.Attribute("code")?.Value ?? "",
                    Name = f.Attribute("name")?.Value ?? "",
                    Summary = f.Attribute("summary")?.Value ?? "",
                });
            }
        }

        foreach (var a in el.Elements("attribute"))
        {
            var attr = ParseZapAttribute(a);
            if (attr != null)
            {
                cluster.Attributes.Add(attr);
            }
        }

        foreach (var c in el.Elements("command"))
        {
            var cmd = ParseZapCommand(c);
            if (cmd != null)
            {
                cluster.Commands.Add(cmd);
            }
        }

        foreach (var e in el.Elements("event"))
        {
            var evt = ParseZapEvent(e);
            if (evt != null)
            {
                cluster.Events.Add(evt);
            }
        }

        foreach (var e in el.Elements("enum"))
        {
            var m = ParseZapEnum(e);
            if (m != null)
            {
                cluster.Enums.Add(m);
            }
        }
        foreach (var b in el.Elements("bitmap"))
        {
            var m = ParseZapBitmap(b);
            if (m != null)
            {
                cluster.Bitmaps.Add(m);
            }
        }
        foreach (var s in el.Elements("struct"))
        {
            var m = ParseZapStruct(s);
            if (m != null)
            {
                cluster.Structs.Add(m);
            }
        }
    }

    static AttributeModel? ParseZapAttribute(XElement el)
    {
        string? code = el.Attribute("code")?.Value;
        string? name = el.Attribute("name")?.Value;
        string? type = el.Attribute("type")?.Value;
        if (code == null || name == null)
        {
            return null;
        }

        return new AttributeModel
        {
            Id = Naming.ParseHex(code),
            Name = name,
            Type = type ?? "uint8",
            IsArray = string.Equals(type, "array", StringComparison.OrdinalIgnoreCase)
                || el.Attribute("array")?.Value == "true",
            EntryType = el.Attribute("entryType")?.Value,
            Readable = true,
            Writable = el.Attribute("writable")?.Value == "true",
            Nullable = el.Attribute("isNullable")?.Value == "true",
            Default = el.Attribute("default")?.Value,
        };
    }

    static CommandModel? ParseZapCommand(XElement el)
    {
        string? code = el.Attribute("code")?.Value;
        string? name = el.Attribute("name")?.Value;
        string? source = el.Attribute("source")?.Value;
        if (code == null || name == null)
        {
            return null;
        }

        var cmd = new CommandModel
        {
            Id = Naming.ParseHex(code),
            Name = name,
            IsClientToServer = source == "client",
            ResponseName = el.Attribute("response")?.Value,
        };

        foreach (var arg in el.Elements("arg"))
        {
            string? argName = arg.Attribute("name")?.Value;
            string? argType = arg.Attribute("type")?.Value;
            if (argName == null)
            {
                continue;
            }

            cmd.Fields.Add(new FieldModel
            {
                Id = uint.TryParse(arg.Attribute("id")?.Value, out var argId) ? argId : (uint)cmd.Fields.Count,
                Name = argName,
                Type = argType ?? "uint8",
                Optional = arg.Attribute("optional")?.Value == "true",
                Nullable = arg.Attribute("isNullable")?.Value == "true",
                IsArray = string.Equals(argType, "array", StringComparison.OrdinalIgnoreCase)
                    || arg.Attribute("array")?.Value == "true",
                EntryType = arg.Attribute("entryType")?.Value,
            });
        }

        return cmd;
    }

    static EventModel? ParseZapEvent(XElement el)
    {
        string? code = el.Attribute("code")?.Value;
        string? name = el.Attribute("name")?.Value;
        if (code == null || name == null)
        {
            return null;
        }

        var evt = new EventModel
        {
            Id = Naming.ParseHex(code),
            Name = name,
            Priority = el.Attribute("priority")?.Value ?? "info",
        };

        foreach (var f in el.Elements("field"))
        {
            string? fName = f.Attribute("name")?.Value;
            if (fName == null)
            {
                continue;
            }

            evt.Fields.Add(new FieldModel
            {
                Id = uint.TryParse(f.Attribute("id")?.Value, out var fid) ? fid : 0,
                Name = fName,
                Type = f.Attribute("type")?.Value ?? "uint8",
                IsArray = string.Equals(f.Attribute("type")?.Value, "array", StringComparison.OrdinalIgnoreCase)
                    || f.Attribute("array")?.Value == "true",
                EntryType = f.Attribute("entryType")?.Value,
            });
        }

        return evt;
    }

    static EnumModel? ParseZapEnum(XElement el)
    {
        string? name = el.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var model = new EnumModel { Name = name, BaseType = el.Attribute("type")?.Value };
        foreach (var item in el.Elements("item"))
        {
            string? itemName = item.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(itemName))
            {
                continue;
            }

            model.Values.Add(new EnumValueModel
            {
                Name = itemName,
                Value = Naming.ParseHex(item.Attribute("value")?.Value ?? "0"),
            });
        }
        return model;
    }

    static BitmapModel? ParseZapBitmap(XElement el)
    {
        string? name = el.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var model = new BitmapModel { Name = name, BaseType = el.Attribute("type")?.Value };
        foreach (var field in el.Elements("field"))
        {
            string? fName = field.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(fName))
            {
                continue;
            }

            uint maskVal = Naming.ParseHex(field.Attribute("mask")?.Value ?? "0");
            model.Bits.Add(new BitfieldModel
            {
                Name = fName,
                Bit = MaskToBit(maskVal),
            });
        }
        return model;
    }

    static StructModel? ParseZapStruct(XElement el)
    {
        string? name = el.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var model = new StructModel { Name = name };
        foreach (var item in el.Elements("item"))
        {
            string? iName = item.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(iName))
            {
                continue;
            }

            model.Fields.Add(new FieldModel
            {
                Id = ParseZapFieldId(item, (uint)model.Fields.Count),
                Name = iName,
                Type = item.Attribute("type")?.Value ?? "uint8",
                Optional = item.Attribute("optional")?.Value == "true",
                Nullable = item.Attribute("isNullable")?.Value == "true",
                IsArray = string.Equals(item.Attribute("type")?.Value, "array", StringComparison.OrdinalIgnoreCase)
                    || item.Attribute("array")?.Value == "true",
                EntryType = item.Attribute("entryType")?.Value,
            });
        }
        return model;
    }

    static List<uint> GetClusterCodes(XElement el) =>
        [.. el.Elements("cluster")
          .Select(c => c.Attribute("code")?.Value)
          .Where(v => v != null)
          .Select(v => Naming.ParseHex(v!))];

    static uint? ParseZapClusterCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Naming.ParseHex(value);
    }

    static void AddMissingItems<T>(
        List<T> target,
        IEnumerable<T> source,
        Func<T, string> keySelector)
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

    static int MaskToBit(uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        int bit = 0;
        while ((mask & 1u) == 0) { mask >>= 1; bit++; }
        return bit;
    }

    static uint ParseZapFieldId(XElement element, uint fallback)
    {
        string? value = element.Attribute("fieldId")?.Value ?? element.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (uint.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return Naming.ParseHex(value);
    }

    // ── CSA format ──────────────────────────────────────────────

    static void ParseCsaFormat(XElement root, List<ClusterModel> results)
    {
        string? directId = root.Attribute("id")?.Value;
        string? directName = root.Attribute("name")?.Value;
        int revision = int.TryParse(root.Attribute("revision")?.Value, out var r) ? r : 1;

        var template = ParseCsaClusterContent(root, revision);

        if (!string.IsNullOrEmpty(directId))
        {
            template.Id = Naming.ParseHex(directId!);
            template.Name = directName ?? "Unknown";
            results.Add(template);
        }
        else
        {
            var clusterIdsEl = root.Elements().FirstOrDefault(e => e.Name.LocalName == "clusterIds");
            if (clusterIdsEl != null)
            {
                foreach (var cid in clusterIdsEl.Elements().Where(e => e.Name.LocalName == "clusterId"))
                {
                    string? cidVal = cid.Attribute("id")?.Value;
                    if (string.IsNullOrEmpty(cidVal))
                    {
                        continue;
                    }

                    var clone = CloneModel(template);
                    clone.Id = Naming.ParseHex(cidVal!);
                    clone.Name = cid.Attribute("name")?.Value ?? directName ?? "Unknown";
                    results.Add(clone);
                }
            }
        }
    }

    static ClusterModel ParseCsaClusterContent(XElement root, int revision)
    {
        var model = new ClusterModel { Revision = revision };

        var features = root.Elements().FirstOrDefault(e => e.Name.LocalName == "features");
        if (features != null)
        {
            foreach (var f in features.Elements().Where(e => e.Name.LocalName == "feature"))
            {
                model.Features.Add(new FeatureModel
                {
                    Bit = int.TryParse(f.Attribute("bit")?.Value, out var b) ? b : 0,
                    Code = f.Attribute("code")?.Value ?? "",
                    Name = f.Attribute("name")?.Value ?? "",
                    Summary = f.Attribute("summary")?.Value ?? "",
                });
            }
        }

        var dataTypes = root.Elements().FirstOrDefault(e => e.Name.LocalName == "dataTypes");
        if (dataTypes != null)
        {
            foreach (var e in dataTypes.Elements().Where(el => el.Name.LocalName == "enum"))
            {
                var em = ParseCsaEnum(e);
                if (em != null)
                {
                    model.Enums.Add(em);
                }
            }
            foreach (var b in dataTypes.Elements().Where(el => el.Name.LocalName == "bitmap"))
            {
                var bm = ParseCsaBitmap(b);
                if (bm != null)
                {
                    model.Bitmaps.Add(bm);
                }
            }
            foreach (var s in dataTypes.Elements().Where(el => el.Name.LocalName == "struct"))
            {
                var sm = ParseCsaStruct(s);
                if (sm != null)
                {
                    model.Structs.Add(sm);
                }
            }
        }

        var attributes = root.Elements().FirstOrDefault(e => e.Name.LocalName == "attributes");
        if (attributes != null)
        {
            foreach (var a in attributes.Elements().Where(e => e.Name.LocalName == "attribute"))
            {
                var access = a.Elements().FirstOrDefault(e => e.Name.LocalName == "access");
                var quality = a.Elements().FirstOrDefault(e => e.Name.LocalName == "quality");
                model.Attributes.Add(new AttributeModel
                {
                    Id = Naming.ParseHex(a.Attribute("id")?.Value ?? "0"),
                    Name = a.Attribute("name")?.Value ?? "",
                    Type = a.Attribute("type")?.Value ?? "uint8",
                    IsArray = string.Equals(a.Attribute("type")?.Value, "array", StringComparison.OrdinalIgnoreCase),
                    EntryType = a.Attribute("entryType")?.Value,
                    Readable = access?.Attribute("read")?.Value != "false",
                    Writable = access?.Attribute("write")?.Value == "true",
                    Nullable = quality?.Attribute("nullable")?.Value == "true",
                    Default = a.Attribute("default")?.Value,
                });
            }
        }

        var commands = root.Elements().FirstOrDefault(e => e.Name.LocalName == "commands");
        if (commands != null)
        {
            foreach (var c in commands.Elements().Where(e => e.Name.LocalName == "command"))
            {
                var cmd = new CommandModel
                {
                    Id = Naming.ParseHex(c.Attribute("id")?.Value ?? "0"),
                    Name = c.Attribute("name")?.Value ?? "",
                    IsClientToServer = (c.Attribute("direction")?.Value ?? "commandToServer") == "commandToServer",
                    ResponseName = c.Attribute("response")?.Value,
                };
                foreach (var f in c.Elements().Where(e => e.Name.LocalName == "field"))
                {
                    cmd.Fields.Add(new FieldModel
                    {
                        Id = Naming.ParseHex(f.Attribute("id")?.Value ?? "0"),
                        Name = f.Attribute("name")?.Value ?? "",
                        Type = f.Attribute("type")?.Value ?? "uint8",
                        IsArray = string.Equals(f.Attribute("type")?.Value, "array", StringComparison.OrdinalIgnoreCase),
                        EntryType = f.Attribute("entryType")?.Value,
                        Optional = f.Elements().Any(e => e.Name.LocalName == "optionalConform"),
                        Nullable = f.Elements().FirstOrDefault(e => e.Name.LocalName == "quality")?.Attribute("nullable")?.Value == "true",
                    });
                }
                model.Commands.Add(cmd);
            }
        }

        var events = root.Elements().FirstOrDefault(e => e.Name.LocalName == "events");
        if (events != null)
        {
            foreach (var e in events.Elements().Where(el => el.Name.LocalName == "event"))
            {
                var evt = new EventModel
                {
                    Id = Naming.ParseHex(e.Attribute("id")?.Value ?? "0"),
                    Name = e.Attribute("name")?.Value ?? "",
                    Priority = e.Attribute("priority")?.Value ?? "info",
                };
                foreach (var f in e.Elements().Where(el => el.Name.LocalName == "field"))
                {
                    evt.Fields.Add(new FieldModel
                    {
                        Id = Naming.ParseHex(f.Attribute("id")?.Value ?? "0"),
                        Name = f.Attribute("name")?.Value ?? "",
                        Type = f.Attribute("type")?.Value ?? "uint8",
                        IsArray = string.Equals(f.Attribute("type")?.Value, "array", StringComparison.OrdinalIgnoreCase),
                        EntryType = f.Attribute("entryType")?.Value,
                    });
                }
                model.Events.Add(evt);
            }
        }

        return model;
    }

    static EnumModel? ParseCsaEnum(XElement el)
    {
        string? name = el.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var model = new EnumModel { Name = name, BaseType = el.Attribute("type")?.Value };
        foreach (var item in el.Elements().Where(e => e.Name.LocalName == "item"))
        {
            model.Values.Add(new EnumValueModel
            {
                Value = Naming.ParseHex(item.Attribute("value")?.Value ?? "0"),
                Name = item.Attribute("name")?.Value ?? "",
                Summary = item.Attribute("summary")?.Value ?? "",
            });
        }
        return model;
    }

    static BitmapModel? ParseCsaBitmap(XElement el)
    {
        string? name = el.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var model = new BitmapModel { Name = name };
        foreach (var bf in el.Elements().Where(e => e.Name.LocalName == "bitfield"))
        {
            model.Bits.Add(new BitfieldModel
            {
                Bit = int.TryParse(bf.Attribute("bit")?.Value, out var b) ? b : 0,
                Name = bf.Attribute("name")?.Value ?? "",
                Summary = bf.Attribute("summary")?.Value ?? "",
            });
        }
        return model;
    }

    static StructModel? ParseCsaStruct(XElement el)
    {
        string? name = el.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var model = new StructModel { Name = name };
        foreach (var f in el.Elements().Where(e => e.Name.LocalName == "field"))
        {
            model.Fields.Add(new FieldModel
            {
                Id = Naming.ParseHex(f.Attribute("id")?.Value ?? "0"),
                Name = f.Attribute("name")?.Value ?? "",
                Type = f.Attribute("type")?.Value ?? "uint8",
                IsArray = string.Equals(f.Attribute("type")?.Value, "array", StringComparison.OrdinalIgnoreCase),
                EntryType = f.Attribute("entryType")?.Value,
                Optional = f.Elements().Any(e => e.Name.LocalName == "optionalConform"),
                Nullable = f.Elements().FirstOrDefault(e => e.Name.LocalName == "quality")?.Attribute("nullable")?.Value == "true",
            });
        }
        return model;
    }

    static ClusterModel CloneModel(ClusterModel src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        Revision = src.Revision,
        Features = [.. src.Features],
        Attributes = [.. src.Attributes],
        Commands = [.. src.Commands],
        Events = [.. src.Events],
        Enums = [.. src.Enums],
        Bitmaps = [.. src.Bitmaps],
        Structs = [.. src.Structs],
    };
}
