using System.Globalization;
using System.Text;

namespace DotMatter.CodeGen;

/// <summary>Shared naming/type-mapping utilities.</summary>
static class Naming
{
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "Unknown";
        }

        var sb = new StringBuilder();
        bool nextUpper = true;
        foreach (char c in name)
        {
            if (c is ' ' or '-' or '_' or '/' or '.')
            {
                nextUpper = true;
                continue;
            }
            if (!char.IsLetterOrDigit(c))
            {
                continue;
            }

            sb.Append(nextUpper ? char.ToUpperInvariant(c) : c);
            nextUpper = false;
        }

        string result = sb.ToString();
        if (result.Length == 0)
        {
            return "Unknown";
        }

        if (char.IsDigit(result[0]))
        {
            result = "_" + result;
        }

        if (IsCSharpKeyword(result))
        {
            result = "@" + result;
        }

        return result;
    }

    public static string ToCamelCase(string name)
    {
        var pascal = ToPascalCase(name);
        if (pascal.Length == 0)
        {
            return pascal;
        }

        if (pascal[0] == '@')
        {
            return "@" + char.ToLowerInvariant(pascal[1]) + pascal[2..];
        }

        string result = char.ToLowerInvariant(pascal[0]) + pascal[1..];
        if (IsCSharpKeyword(result))
        {
            result = "@" + result;
        }

        return result;
    }

    public static bool IsCSharpKeyword(string s) =>
        s is "string" or "bool" or "int" or "long" or "byte" or "short" or "float"
        or "double" or "object" or "default" or "class" or "struct" or "enum"
        or "event" or "switch" or "case" or "true" or "false" or "null"
        or "namespace" or "static" or "new" or "return" or "void" or "if"
        or "else" or "for" or "foreach" or "while" or "do" or "break"
        or "continue" or "try" or "catch" or "finally" or "throw" or "using"
        or "lock" or "this" or "base" or "is" or "as" or "in" or "out"
        or "ref" or "params" or "delegate" or "interface" or "abstract"
        or "virtual" or "override" or "sealed" or "readonly" or "const"
        or "volatile" or "extern" or "internal" or "protected" or "private"
        or "public" or "sizeof" or "typeof" or "checked" or "unchecked"
        or "fixed" or "unsafe" or "stackalloc" or "implicit" or "explicit"
        or "operator" or "where" or "yield" or "partial" or "var" or "dynamic"
        or "async" or "await" or "nameof" or "when" or "managed" or "unmanaged";

    public static string MapCSharpType(string matterType, bool nullable, bool optional,
        bool isArray, string? entryType, HashSet<string>? localTypes = null)
    {
        if (isArray && entryType != null)
        {
            string inner = MapCSharpType(entryType, false, false, false, null, localTypes);
            string arrayType = inner + "[]";
            return (nullable || optional) ? arrayType + "?" : arrayType;
        }

        string baseType = matterType.ToLowerInvariant() switch
        {
            "bool" or "boolean" => "bool",
            "int8u" or "uint8" or "enum8" or "bitmap8" or "percent" or "action-id" or "action_id"
                or "fabric-idx" or "fabric_idx" or "status" => "byte",
            "int8s" or "int8" => "sbyte",
            "int16u" or "uint16" or "enum16" or "bitmap16" or "percent100ths" or "vendor-id"
                or "vendor_id" or "endpoint-no" or "endpoint_no" or "group-id"
                or "group_id" or "entry-idx" or "entry_idx" => "ushort",
            "int16s" or "int16" or "temperature" => "short",
            "int24u" or "uint24" => "uint",
            "int24s" or "int24" => "int",
            "int32u" or "uint32" or "bitmap32" or "cluster-id" or "cluster_id" or "attrib-id"
                or "attrib_id" or "attribute-id" or "devtype-id" or "devtype_id"
                or "epoch-s" or "epoch_s" or "elapsed-s" or "elapsed_s"
                or "data-ver" or "data_ver" or "tod" or "date" or "command_id" => "uint",
            "int32s" or "int32" => "int",
            "int64u" or "uint64" or "bitmap64" or "node-id" or "node_id" or "fabric-id"
                or "fabric_id" or "epoch-us" or "epoch_us" or "posix-ms"
                or "systime_ms" or "event-no" or "event_no" or "subject-id"
                or "subject_id" or "trans_id" or "systime_us" or "amperage_ma"
                or "voltage_mv" or "power_mw" or "energy_mwh"
                or "energy_mvah" or "energy_mvarh" => "ulong",
            "int64s" or "int64" or "money" or "power_mva" or "power_mvar" => "long",
            "single" or "float" => "float",
            "double" => "double",
            "char_string" or "long_char_string" => "string",
            "octet_string" or "long_octet_string" or "hwadr" or "ipv4adr"
                or "ipv6adr" or "ipadr" or "ipv6pre" => "byte[]",
            _ => ResolveCustomType(matterType, localTypes),
        };

        // When IsArray but no entryType, the base type IS the element type
        if (isArray)
        {
            string arrayType = baseType + "[]";
            return (nullable || optional) ? arrayType + "?" : arrayType;
        }

        if ((nullable || optional) && IsValueType(baseType))
        {
            return baseType + "?";
        }

        if (optional && !IsValueType(baseType))
        {
            return baseType + "?";
        }

        return baseType;
    }

    public static bool IsValueType(string csType) =>
        csType is "bool" or "byte" or "sbyte" or "short" or "ushort"
        or "int" or "uint" or "long" or "ulong" or "float" or "double";

    public static bool NeedsDefaultInit(string csType)
    {
        if (csType.EndsWith('?'))
        {
            return false;
        }

        if (csType.EndsWith("[]"))
        {
            return true;
        }

        if (IsValueType(csType))
        {
            return false;
        }

        return true;
    }

    public static string EscapeXml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public static uint ParseHex(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.Parse(value[2..], NumberStyles.HexNumber);
        }

        return uint.TryParse(value, out var v) ? v : 0;
    }

    private static string ResolveCustomType(string matterType, HashSet<string>? localTypes)
    {
        string sanitized = ToPascalCase(matterType);
        if (localTypes != null && localTypes.Contains(sanitized))
        {
            return sanitized;
        }

        return "object";
    }
}
