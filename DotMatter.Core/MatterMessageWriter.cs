using System.Buffers;
using System.Buffers.Binary;

namespace DotMatter.Core;

class MatterMessageWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    public ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;

    internal void Write(byte @byte)
        => _buffer.Write([@byte]);

    internal void Write(ReadOnlySpan<byte> bytes)
        => _buffer.Write(bytes);

    internal void Write(ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        _buffer.Write(buffer);
    }

    internal void Write(ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        _buffer.Write(buffer);
    }

    internal void Write(uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _buffer.Write(buffer);
    }

    internal byte[] GetBytes()
        => _buffer.WrittenSpan.ToArray();
}
