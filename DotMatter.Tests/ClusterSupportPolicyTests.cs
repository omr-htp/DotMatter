using DotMatter.CodeGen;
using DotMatter.Core;

namespace DotMatter.Tests;

[TestFixture]
public class ClusterSupportPolicyTests
{
    [Test]
    public void MessagePayload_RoundTripsVendorIdWhenPresent()
    {
        var payload = new MessagePayload(new DotMatter.Core.TLV.MatterTLV())
        {
            ExchangeFlags = ExchangeFlags.VendorPresent,
            ProtocolOpCode = 0x55,
            ExchangeID = 0x1234,
            ProtocolVendorId = 0xFFF1,
            ProtocolId = 0x0001,
        };

        var writer = new MatterMessageWriter();
        payload.Serialize(writer);

        var decoded = new MessagePayload(writer.GetBytes());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decoded.ExchangeFlags, Is.EqualTo(ExchangeFlags.VendorPresent));
            Assert.That(decoded.ProtocolVendorId, Is.EqualTo((ushort)0xFFF1));
            Assert.That(decoded.ProtocolId, Is.EqualTo((ushort)0x0001));
        }
    }

    [Test]
    public void CodeGen_UnsupportedComplexCommand_UsesUnsupportedCommandAsync()
    {
        var cluster = new ClusterModel
        {
            Name = "Demo Cluster",
            Id = 0x1234,
            Commands =
            [
                new CommandModel
                {
                    Name = "Launch",
                    Id = 1,
                    Fields =
                    [
                        new FieldModel
                        {
                            Id = 0,
                            Name = "Application",
                            Type = "ApplicationStruct",
                        }
                    ]
                }
            ],
            Structs =
            [
                new StructModel
                {
                    Name = "ApplicationStruct",
                    Fields =
                    [
                        new FieldModel
                        {
                            Id = 0,
                            Name = "OpaquePayload",
                            Type = "DefinitelyUnsupportedType",
                        }
                    ]
                }
            ]
        };

        var code = ClusterCodeEmitter.Emit(cluster, "demo.xml");

        Assert.That(code, Does.Contain("=> UnsupportedCommandAsync(\"Launch requires serializer support for Application (ApplicationStruct).\")"));
    }

    [Test]
    public void CodeGen_UnsupportedComplexAttribute_UsesRawReadAttributeAsync()
    {
        var cluster = new ClusterModel
        {
            Name = "Demo Cluster",
            Id = 0x1234,
            Attributes =
            [
                new AttributeModel
                {
                    Id = 1,
                    Name = "CurrentApp",
                    Type = "ApplicationStruct"
                }
            ],
            Structs =
            [
                new StructModel
                {
                    Name = "ApplicationStruct"
                }
            ]
        };

        var code = ClusterCodeEmitter.Emit(cluster, "demo.xml");

        Assert.That(code, Does.Contain("public Task<object?> ReadCurrentAppAsync(CancellationToken ct = default)"));
        Assert.That(code, Does.Contain("=> ReadAttributeAsync(0x0001, ct);"));
        Assert.That(code, Does.Not.Contain("UnsupportedAttributeAsync"));
    }

    [Test]
    public void CodeGen_EnumAttribute_RemainsTyped()
    {
        var cluster = new ClusterModel
        {
            Name = "Demo Cluster",
            Id = 0x1234,
            Attributes =
            [
                new AttributeModel
                {
                    Id = 1,
                    Name = "StartUpOnOff",
                    Type = "StartUpOnOffEnum"
                }
            ],
            Enums =
            [
                new EnumModel
                {
                    Name = "StartUpOnOffEnum",
                    BaseType = "enum8",
                    Values =
                    [
                        new EnumValueModel { Name = "Off", Value = 0 },
                        new EnumValueModel { Name = "On", Value = 1 }
                    ]
                }
            ]
        };

        var code = ClusterCodeEmitter.Emit(cluster, "demo.xml");

        Assert.That(code, Does.Contain("public Task<StartUpOnOffEnum> ReadStartUpOnOffAsync"));
        Assert.That(code, Does.Contain("=> ReadAttributeAsync<StartUpOnOffEnum>(0x0001, ct);"));
        Assert.That(code, Does.Not.Contain("UnsupportedAttributeAsync"));
    }

    [Test]
    public void CodeGen_StructArrayCommand_GeneratesSupportedSerializer()
    {
        var cluster = new ClusterModel
        {
            Name = "Content Control",
            Id = 0x050F,
            Commands =
            [
                new CommandModel
                {
                    Name = "AddBlockApplications",
                    Id = 0x000D,
                    Fields =
                    [
                        new FieldModel
                        {
                            Id = 0,
                            Name = "Applications",
                            Type = "AppInfoStruct",
                            IsArray = true,
                            EntryType = "AppInfoStruct",
                        }
                    ]
                }
            ],
            Structs =
            [
                new StructModel
                {
                    Name = "AppInfoStruct",
                    Fields =
                    [
                        new FieldModel
                        {
                            Id = 0,
                            Name = "CatalogVendorID",
                            Type = "vendor-id",
                        },
                        new FieldModel
                        {
                            Id = 1,
                            Name = "ApplicationID",
                            Type = "char_string",
                        }
                    ]
                }
            ]
        };

        var code = ClusterCodeEmitter.Emit(cluster, "content-control.xml");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Does.Contain("public Task<InvokeResponse> AddBlockApplicationsAsync("));
            Assert.That(code, Does.Contain("=> InvokeCommandAsync(0x000D, tlv => { if (applications != null) { tlv.AddArray(0); foreach (var item in applications) { WriteAppInfoStruct(tlv, item); } tlv.EndContainer(); } }, ct);"));
            Assert.That(code, Does.Contain("private static void WriteAppInfoStruct(MatterTLV tlv, AppInfoStruct value)"));
            Assert.That(code, Does.Contain("tlv.AddUInt16(0, value.CatalogVendorID);"));
            Assert.That(code, Does.Contain("tlv.AddUTF8String(1, value.ApplicationID);"));
            Assert.That(code, Does.Not.Contain("UnsupportedCommandAsync(\"AddBlockApplications"));
        }
    }

    [Test]
    public void CodeGen_NestedStructCommand_GeneratesSupportedSerializer()
    {
        var cluster = new ClusterModel
        {
            Name = "Content Control",
            Id = 0x050F,
            Commands =
            [
                new CommandModel
                {
                    Name = "SetBlockContentTimeWindow",
                    Id = 0x000F,
                    Fields =
                    [
                        new FieldModel
                        {
                            Id = 0,
                            Name = "TimeWindow",
                            Type = "TimeWindowStruct",
                        }
                    ]
                }
            ],
            Bitmaps =
            [
                new BitmapModel
                {
                    Name = "DayOfWeekBitmap",
                    BaseType = "bitmap8",
                    Bits =
                    [
                        new BitfieldModel { Name = "Sunday", Bit = 0 },
                        new BitfieldModel { Name = "Monday", Bit = 1 }
                    ]
                }
            ],
            Structs =
            [
                new StructModel
                {
                    Name = "TimePeriodStruct",
                    Fields =
                    [
                        new FieldModel { Id = 0, Name = "StartHour", Type = "uint8" },
                        new FieldModel { Id = 1, Name = "StartMinute", Type = "uint8" },
                        new FieldModel { Id = 2, Name = "EndHour", Type = "uint8" },
                        new FieldModel { Id = 3, Name = "EndMinute", Type = "uint8" }
                    ]
                },
                new StructModel
                {
                    Name = "TimeWindowStruct",
                    Fields =
                    [
                        new FieldModel
                        {
                            Id = 0,
                            Name = "TimeWindowIndex",
                            Type = "uint16",
                            Optional = true,
                        },
                        new FieldModel
                        {
                            Id = 1,
                            Name = "DayOfWeek",
                            Type = "DayOfWeekBitmap",
                        },
                        new FieldModel
                        {
                            Id = 2,
                            Name = "TimePeriod",
                            Type = "TimePeriodStruct",
                            IsArray = true,
                            EntryType = "TimePeriodStruct",
                        }
                    ]
                }
            ]
        };

        var code = ClusterCodeEmitter.Emit(cluster, "content-control.xml");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Does.Contain("public enum DayOfWeekBitmap : uint"));
            Assert.That(code, Does.Contain("public Task<InvokeResponse> SetBlockContentTimeWindowAsync("));
            Assert.That(code, Does.Contain("=> InvokeCommandAsync(0x000F, tlv => { WriteTimeWindowStruct(tlv, 0, timeWindow); }, ct);"));
            Assert.That(code, Does.Contain("if (value.TimeWindowIndex != null) tlv.AddUInt16(0, value.TimeWindowIndex.Value);"));
            Assert.That(code, Does.Contain("tlv.AddUInt8(1, (byte)value.DayOfWeek);"));
            Assert.That(code, Does.Contain("if (value.TimePeriod != null) { tlv.AddArray(2); foreach (var item in value.TimePeriod) { WriteTimePeriodStruct(tlv, item); } tlv.EndContainer(); }"));
            Assert.That(code, Does.Not.Contain("UnsupportedCommandAsync(\"SetBlockContentTimeWindow"));
        }
    }

    [Test]
    public void CodeGen_ComplexArrayAttributes_UseTypedReaders()
    {
        var cluster = new ClusterModel
        {
            Name = "Content Control",
            Id = 0x050F,
            Attributes =
            [
                new AttributeModel { Id = 0x0001, Name = "OnDemandRatings", Type = "array", IsArray = true, EntryType = "RatingNameStruct" },
                new AttributeModel { Id = 0x0008, Name = "BlockChannelList", Type = "array", IsArray = true, EntryType = "BlockChannelStruct" },
                new AttributeModel { Id = 0x0009, Name = "BlockApplicationList", Type = "array", IsArray = true, EntryType = "AppInfoStruct" },
                new AttributeModel { Id = 0x000A, Name = "BlockContentTimeWindow", Type = "array", IsArray = true, EntryType = "TimeWindowStruct" }
            ],
            Structs =
            [
                new StructModel { Name = "RatingNameStruct" },
                new StructModel { Name = "BlockChannelStruct" },
                new StructModel { Name = "AppInfoStruct" },
                new StructModel { Name = "TimeWindowStruct" }
            ]
        };

        var code = ClusterCodeEmitter.Emit(cluster, "content-control.xml");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Does.Contain("public Task<RatingNameStruct[]?> ReadOnDemandRatingsAsync(CancellationToken ct = default)"));
            Assert.That(code, Does.Contain("=> ReadRefAttributeAsync<RatingNameStruct[]>(0x0001, ct);"));
            Assert.That(code, Does.Contain("public Task<BlockChannelStruct[]?> ReadBlockChannelListAsync(CancellationToken ct = default)"));
            Assert.That(code, Does.Contain("=> ReadRefAttributeAsync<BlockChannelStruct[]>(0x0008, ct);"));
            Assert.That(code, Does.Contain("public Task<AppInfoStruct[]?> ReadBlockApplicationListAsync(CancellationToken ct = default)"));
            Assert.That(code, Does.Contain("=> ReadRefAttributeAsync<AppInfoStruct[]>(0x0009, ct);"));
            Assert.That(code, Does.Contain("public Task<TimeWindowStruct[]?> ReadBlockContentTimeWindowAsync(CancellationToken ct = default)"));
            Assert.That(code, Does.Contain("=> ReadRefAttributeAsync<TimeWindowStruct[]>(0x000A, ct);"));
            Assert.That(code, Does.Not.Contain("UnsupportedAttributeAsync"));
        }
    }

    [Test]
    public void CodeGen_ZapXmlStructFields_KeepOriginalFieldIds()
    {
        var xml = File.ReadAllText(FindCodeGenXmlPath("content-control-cluster.xml"));
        var cluster = ZapXmlParser.ParseAll(xml).Single(c => c.Id == 0x050F);

        var code = ClusterCodeEmitter.Emit(cluster, "content-control-cluster.xml");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Does.Contain("public enum DayOfWeekBitmap : uint"));
            Assert.That(code, Does.Contain("private static void WriteAppInfoStructFields(MatterTLV tlv, AppInfoStruct value)"));
            Assert.That(code, Does.Contain("tlv.AddUInt16(0, value.CatalogVendorID);"));
            Assert.That(code, Does.Contain("tlv.AddUTF8String(1, value.ApplicationID);"));
            Assert.That(code, Does.Contain("if (value.BlockChannelIndex != null) { tlv.AddUInt16(0, value.BlockChannelIndex.Value); } else { tlv.AddNull(0); }"));
            Assert.That(code, Does.Contain("tlv.AddUInt16(1, value.MajorNumber);"));
            Assert.That(code, Does.Contain("tlv.AddUInt16(2, value.MinorNumber);"));
            Assert.That(code, Does.Contain("if (value.Identifier != null) tlv.AddUTF8String(3, value.Identifier);"));
            Assert.That(code, Does.Contain("if (value.TimeWindowIndex != null) { tlv.AddUInt16(0, value.TimeWindowIndex.Value); } else { tlv.AddNull(0); }"));
            Assert.That(code, Does.Contain("tlv.AddUInt8(1, (byte)value.DayOfWeek);"));
            Assert.That(code, Does.Contain("if (value.TimePeriod != null) { tlv.AddArray(2); foreach (var item in value.TimePeriod) { WriteTimePeriodStruct(tlv, item); } tlv.EndContainer(); }"));
            Assert.That(code, Does.Contain("=> ReadRefAttributeAsync<RatingNameStruct[]>(0x0001, ct);"));
            Assert.That(code, Does.Contain("=> ReadRefAttributeAsync<BlockChannelStruct[]>(0x0008, ct);"));
            Assert.That(code, Does.Contain("=> ReadRefAttributeAsync<AppInfoStruct[]>(0x0009, ct);"));
            Assert.That(code, Does.Contain("=> ReadRefAttributeAsync<TimeWindowStruct[]>(0x000A, ct);"));
        }
    }

    [Test]
    public void CodeGen_ZapXmlActionsStructSerializer_UsesDistinctTags()
    {
        var xml = File.ReadAllText(FindCodeGenXmlPath("actions-cluster.xml"));
        var cluster = ZapXmlParser.ParseAll(xml).Single(c => c.Id == 0x0025);

        var code = ClusterCodeEmitter.Emit(cluster, "actions-cluster.xml");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Does.Contain("public enum ActionErrorEnum : byte"));
            Assert.That(code, Does.Contain("Unknown = 0x00,"));
            Assert.That(code, Does.Contain("public enum CommandBits : uint"));
            Assert.That(code, Does.Contain("private static void WriteActionStructFields(MatterTLV tlv, ActionStruct value)"));
            Assert.That(code, Does.Contain("tlv.AddUInt16(0, value.ActionID);"));
            Assert.That(code, Does.Contain("tlv.AddUTF8String(1, value.Name);"));
            Assert.That(code, Does.Contain("tlv.AddUInt8(2, (byte)value.Type);"));
            Assert.That(code, Does.Contain("tlv.AddUInt16(3, value.EndpointListID);"));
            Assert.That(code, Does.Contain("tlv.AddUInt16(4, (ushort)value.SupportedCommands);"));
            Assert.That(code, Does.Contain("tlv.AddUInt8(5, (byte)value.State);"));
            Assert.That(code, Does.Contain("if (value.Endpoints != null) { tlv.AddArray(3); foreach (var item in value.Endpoints) { tlv.AddUInt16(item); } tlv.EndContainer(); }"));
        }
    }

    [Test]
    public void CodeGen_ZapXmlClusterExtension_EmitsEnhancedColorControlMembers()
    {
        var xml = File.ReadAllText(FindCodeGenXmlPath("color-control-cluster.xml"));
        var cluster = ZapXmlParser.ParseAll(xml).Single(c => c.Id == 0x0300);

        var code = ClusterCodeEmitter.Emit(cluster, "color-control-cluster.xml");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Does.Contain("public Task<ushort> ReadEnhancedCurrentHueAsync(CancellationToken ct = default)"));
            Assert.That(code, Does.Contain("=> ReadAttributeAsync<ushort>(0x4000, ct);"));
            Assert.That(code, Does.Contain("public Task<EnhancedColorModeEnum> ReadEnhancedColorModeAsync(CancellationToken ct = default)"));
            Assert.That(code, Does.Contain("public Task<ColorCapabilitiesBitmap> ReadColorCapabilitiesAsync(CancellationToken ct = default)"));
            Assert.That(code, Does.Contain("public Task<InvokeResponse> EnhancedMoveToHueAndSaturationAsync("));
            Assert.That(code, Does.Contain("=> InvokeCommandAsync(0x0043"));
            Assert.That(code, Does.Contain("public Task<InvokeResponse> MoveColorTemperatureAsync("));
            Assert.That(code, Does.Contain("=> InvokeCommandAsync(0x004B"));
        }
    }

    [Test]
    public void CodeGen_StandaloneZapClusterExtension_IsParsed()
    {
        var xml = File.ReadAllText(FindCodeGenXmlPath("clusters-extensions.xml"));
        var clusters = ZapXmlParser.ParseAll(xml);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clusters, Has.Count.EqualTo(1));
            Assert.That(clusters[0].Id, Is.EqualTo(0x0028));
            Assert.That(clusters[0].Commands.Any(c => c.Name == "MfgSpecificPing" && c.IsClientToServer), Is.True);
        }
    }

    [Test]
    public void CodeGen_GlobalTypes_AreAttachedWhenClusterReferencesThem()
    {
        var clusterXml = File.ReadAllText(FindCodeGenXmlPath("camera-av-stream-management-cluster.xml"));
        var globalEnumsXml = File.ReadAllText(FindCodeGenXmlPath("global-enums.xml"));
        var globalStructsXml = File.ReadAllText(FindCodeGenXmlPath("global-structs.xml"));

        var cluster = ZapXmlParser.ParseAll(clusterXml).Single(c => c.Id == 0x0551);
        var globalTypes = new GlobalTypeRegistry();
        CodeGenModelUtilities.MergeGlobalTypes(globalTypes, ZapXmlParser.ParseGlobalTypes(globalEnumsXml));
        CodeGenModelUtilities.MergeGlobalTypes(globalTypes, ZapXmlParser.ParseGlobalTypes(globalStructsXml));
        CodeGenModelUtilities.AttachReferencedGlobalTypes(cluster, globalTypes);

        var code = ClusterCodeEmitter.Emit(cluster, "camera-av-stream-management-cluster.xml");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Does.Contain("public enum StreamUsageEnum : byte"));
            Assert.That(code, Does.Contain("public sealed class ViewportStruct"));
            Assert.That(code, Does.Contain("public StreamUsageEnum StreamUsage { get; set; } = default!;"));
            Assert.That(code, Does.Contain("public Task<InvokeResponse> AudioStreamAllocateAsync("));
            Assert.That(code, Does.Contain("StreamUsageEnum streamUsage"));
            Assert.That(code, Does.Not.Contain("Field StreamUsage (object) is not supported"));
        }
    }

    [Test]
    public void CodeGen_GlobalScalarAliases_MapToNumericTypes()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Naming.MapCSharpType("money", false, false, false, null), Is.EqualTo("long"));
            Assert.That(Naming.MapCSharpType("power_mva", false, false, false, null), Is.EqualTo("long"));
            Assert.That(Naming.MapCSharpType("energy_mvah", false, false, false, null), Is.EqualTo("ulong"));
            Assert.That(Naming.MapCSharpType("energy_mvarh", false, false, false, null), Is.EqualTo("ulong"));
        }
    }

    private static string FindCodeGenXmlPath(string fileName)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null)
        {
            var directCandidate = Path.Combine(directory.FullName, "DotMatter.CodeGen", "Xml", fileName);
            if (File.Exists(directCandidate))
            {
                return directCandidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate DotMatter.CodeGen XML file '{fileName}'.");
    }
}
