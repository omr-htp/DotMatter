namespace DotMatter.CodeGen;

/// <summary>Parsed cluster definition from ZAP or CSA XML.</summary>
sealed class ClusterModel
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public int Revision { get; set; }
    public List<FeatureModel> Features { get; set; } = [];
    public List<AttributeModel> Attributes { get; set; } = [];
    public List<CommandModel> Commands { get; set; } = [];
    public List<EventModel> Events { get; set; } = [];
    public List<EnumModel> Enums { get; set; } = [];
    public List<BitmapModel> Bitmaps { get; set; } = [];
    public List<StructModel> Structs { get; set; } = [];

    public string CSharpName => Naming.ToPascalCase(Name.Replace(" Cluster", "").Replace("/", ""));
}

sealed class FeatureModel
{
    public int Bit { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
}

sealed class AttributeModel
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsArray { get; set; }
    public string? EntryType { get; set; }
    public bool Readable { get; set; } = true;
    public bool Writable { get; set; }
    public bool Nullable { get; set; }
    public string? Default { get; set; }
}

sealed class CommandModel
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsClientToServer { get; set; } = true;
    public string? ResponseName { get; set; }
    public List<FieldModel> Fields { get; set; } = [];
}

sealed class EventModel
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public string Priority { get; set; } = "info";
    public List<FieldModel> Fields { get; set; } = [];
}

sealed class FieldModel
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Optional { get; set; }
    public bool Nullable { get; set; }
    public bool IsArray { get; set; }
    public string? EntryType { get; set; }
}

sealed class EnumModel
{
    public string Name { get; set; } = "";
    public string? BaseType { get; set; }
    public List<EnumValueModel> Values { get; set; } = [];
}

sealed class EnumValueModel
{
    public uint Value { get; set; }
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
}

sealed class BitmapModel
{
    public string Name { get; set; } = "";
    public string? BaseType { get; set; }
    public List<BitfieldModel> Bits { get; set; } = [];
}

sealed class BitfieldModel
{
    public int Bit { get; set; }
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
}

sealed class StructModel
{
    public string Name { get; set; } = "";
    public List<FieldModel> Fields { get; set; } = [];
}
