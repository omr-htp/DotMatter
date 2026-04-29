using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace DotMatter.Core.TLV;

internal static class MatterTlvBinary
{
    public static ReadOnlySpan<byte> AsSpan(List<byte> values) => CollectionsMarshal.AsSpan(values);

    public static List<byte> ToList(ReadOnlySpan<byte> values)
    {
        var list = new List<byte>(values.Length);
        CollectionsMarshal.SetCount(list, values.Length);
        values.CopyTo(CollectionsMarshal.AsSpan(list));
        return list;
    }

    public static void AddBytes(List<byte> values, ReadOnlySpan<byte> bytes)
    {
        var offset = values.Count;
        values.EnsureCapacity(offset + bytes.Length);
        CollectionsMarshal.SetCount(values, offset + bytes.Length);
        bytes.CopyTo(CollectionsMarshal.AsSpan(values)[offset..]);
    }

    public static byte[] CopyAll(List<byte> values) => AsSpan(values).ToArray();

    public static byte[] SliceToArray(List<byte> values, int offset, int length)
        => AsSpan(values).Slice(offset, length).ToArray();

    public static ReadOnlySpan<byte> Slice(List<byte> values, int offset, int length)
        => AsSpan(values).Slice(offset, length);

    public static void AddUInt16(List<byte> values, ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        AddBytes(values, buffer);
    }

    public static void AddUInt32(List<byte> values, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        AddBytes(values, buffer);
    }

    public static void AddUInt64(List<byte> values, ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        AddBytes(values, buffer);
    }

    public static void AddInt16(List<byte> values, short value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        AddBytes(values, buffer);
    }

    public static void AddInt32(List<byte> values, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        AddBytes(values, buffer);
    }

    public static void AddInt64(List<byte> values, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        AddBytes(values, buffer);
    }

    public static void AddSingle(List<byte> values, float value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        BitConverter.TryWriteBytes(buffer, value);
        AddBytes(values, buffer);
    }

    public static void AddDouble(List<byte> values, double value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        BitConverter.TryWriteBytes(buffer, value);
        AddBytes(values, buffer);
    }

    public static ushort ReadUInt16(List<byte> values, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(AsSpan(values).Slice(offset, sizeof(ushort)));

    public static uint ReadUInt32(List<byte> values, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(AsSpan(values).Slice(offset, sizeof(uint)));

    public static ulong ReadUInt64(List<byte> values, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(AsSpan(values).Slice(offset, sizeof(ulong)));

    public static short ReadInt16(List<byte> values, int offset)
        => BinaryPrimitives.ReadInt16LittleEndian(AsSpan(values).Slice(offset, sizeof(short)));

    public static int ReadInt32(List<byte> values, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(AsSpan(values).Slice(offset, sizeof(int)));

    public static long ReadInt64(List<byte> values, int offset)
        => BinaryPrimitives.ReadInt64LittleEndian(AsSpan(values).Slice(offset, sizeof(long)));

    public static float ReadSingle(List<byte> values, int offset)
        => BitConverter.ToSingle(AsSpan(values).Slice(offset, sizeof(float)));

    public static double ReadDouble(List<byte> values, int offset)
        => BitConverter.ToDouble(AsSpan(values).Slice(offset, sizeof(double)));
}
