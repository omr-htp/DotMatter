using System.Text;

namespace DotMatter.Core.TLV;

/// <summary>
/// See Appendix A of the Matter Specification for the TLV encoding. 
/// </summary>
public class MatterTLV
{
    private readonly List<byte> _values = [];
    private static MatterTlvException TlvError(string message)
    {
        MatterDiagnostics.TlvParseFailures.Add(1);
        return new MatterTlvException(message);
    }

    /// <summary>MatterTLV.</summary>
    public MatterTLV()
    {
        // Empty constructor
    }

    /// <summary>MatterTLV.</summary>
    public MatterTLV(byte[] payload)
        : this(payload.AsSpan())
    {
    }

    /// <summary>MatterTLV.</summary>
    public MatterTLV(ReadOnlySpan<byte> payload)
    {
        _values = MatterTlvBinary.ToList(payload);
    }

    /// <summary>AddStructure.</summary>
    public MatterTLV AddStructure()
    {
        // Anonymous i.e. has no tag number.
        _values.Add(0x15);
        return this;
    }

    /// <summary>AddStructure.</summary>
    public MatterTLV AddStructure(byte tagNumber)
    {
        // Anonymous i.e. has no tag number.
        _values.Add(0x01 << 5 | 0x15);
        _values.Add(tagNumber);
        return this;
    }

    /// <summary>AddArray.</summary>
    public MatterTLV AddArray(byte tagNumber)
    {
        // This is a Context-Specific Tag (0x01), shifted 5 bits and then OR'd with 0x16
        // to produce a context tag for Array, 1 byte long
        // 00110110
        //
        _values.Add(0x01 << 5 | 0x16);
        _values.Add(tagNumber);
        return this;
    }

    /// <summary>AddArray.</summary>
    public MatterTLV AddArray()
    {
        // This is an anonymous tag, shifted 5 bits and then OR'd with 0x22
        // 00010110
        //
        _values.Add(0x16);
        return this;
    }

    /// <summary>AddList.</summary>
    public MatterTLV AddList(long tagNumber)
    {
        // This is a Context-Specific Tag (0x01), shifted 5 bits and then OR'd with 0x17
        // to produce a context tag for List, one byte long
        // 00110111
        //
        _values.Add(0x01 << 5 | 0x17);
        _values.Add((byte)tagNumber);
        return this;
    }

    /// <summary>AddList.</summary>
    public MatterTLV AddList()
    {
        _values.Add(0x17);
        return this;
    }

    /// <summary>EndContainer.</summary>
    public MatterTLV EndContainer()
    {
        _values.Add(0x18);
        return this;
    }

    /// <summary>AddUTF8String.</summary>
    public MatterTLV AddUTF8String(byte tagNumber, string value)
    {
        var utf8String = Encoding.UTF8.GetBytes(value);
        WriteLengthPrefixedValue(tagNumber, 0x0C, utf8String);
        return this;
    }

    internal MatterTLV AddUTF8String(string value)
    {
        var utf8String = Encoding.UTF8.GetBytes(value);
        WriteLengthPrefixedValue(tagNumber: null, 0x0C, utf8String);
        return this;
    }

    /// <summary>AddOctetString.</summary>
    public MatterTLV AddOctetString(byte tagNumber, byte[] value)
        => AddOctetString(tagNumber, value.AsSpan());

    /// <summary>AddOctetString.</summary>
    public MatterTLV AddOctetString(byte tagNumber, ReadOnlySpan<byte> value)
    {
        WriteLengthPrefixedValue(tagNumber, 0x10, value);
        return this;
    }

    internal MatterTLV AddOctetString(byte[] value)
        => AddOctetString(value.AsSpan());

    internal MatterTLV AddOctetString(ReadOnlySpan<byte> value)
    {
        WriteLengthPrefixedValue(tagNumber: null, 0x10, value);
        return this;
    }

    /// <summary>AddUInt8.</summary>
    public MatterTLV AddUInt8(long tagNumber, byte value)
    {
        _values.Add(0x01 << 5 | 0x4);
        _values.Add((byte)tagNumber);

        // No length required
        //
        _values.Add(value);

        return this;
    }

    /// <summary>AddUInt8.</summary>
    public MatterTLV AddUInt8(byte value)
    {
        _values.Add(0x4);
        _values.Add(value);

        return this;
    }

    /// <summary>AddUInt16.</summary>
    public MatterTLV AddUInt16(long tagNumber, ushort value)
    {
        _values.Add(0x01 << 5 | 0x5);
        _values.Add((byte)tagNumber);

        // No length required.
        //
        MatterTlvBinary.AddUInt16(_values, value);

        return this;
    }

    internal MatterTLV AddUInt16(ushort value)
    {
        _values.Add(0x05);
        MatterTlvBinary.AddUInt16(_values, value);

        return this;
    }

    /// <summary>AddUInt32.</summary>
    public MatterTLV AddUInt32(long tagNumber, uint value)
    {
        _values.Add(0x01 << 5 | 0x6);
        _values.Add((byte)tagNumber);

        // No length required.
        //
        MatterTlvBinary.AddUInt32(_values, value);

        return this;
    }

    internal MatterTLV AddUInt32(uint value)
    {
        _values.Add(0x06);
        MatterTlvBinary.AddUInt32(_values, value);

        return this;
    }

    /// <summary>AddUInt64.</summary>
    public MatterTLV AddUInt64(long tagNumber, ulong value)
    {
        _values.Add(0x01 << 5 | 0x7);
        _values.Add((byte)tagNumber);

        // No length required.
        //
        MatterTlvBinary.AddUInt64(_values, value);

        return this;
    }

    /// <summary>Add anonymous UInt64 element (for array entries).</summary>
    public MatterTLV AddUInt64(ulong value)
    {
        _values.Add(0x07); // anonymous tag, unsigned int 8-byte
        MatterTlvBinary.AddUInt64(_values, value);
        return this;
    }

    /// <summary>AddUInt64.</summary>
    public MatterTLV AddUInt64(long tagNumber, byte[] value)
    {
        if (value.Length != 8)
        {
            throw TlvError("Value must be 8 bytes long");
        }
        _values.Add(0x01 << 5 | 0x7);
        _values.Add((byte)tagNumber);
        _values.AddRange(value);

        return this;
    }

    /// <summary>AddBool.</summary>
    public MatterTLV AddBool(int tagNumber, bool value)
    {
        if (value)
        {
            _values.Add(0x01 << 5 | 0x09); // Boolean TRUE
        }
        else
        {
            _values.Add(0x01 << 5 | 0x08); // Boolean FALSE
        }

        _values.Add((byte)tagNumber);

        return this;
    }

    internal MatterTLV AddBool(bool value)
    {
        _values.Add(value ? (byte)0x09 : (byte)0x08);
        return this;
    }

    /// <summary>AddInt8.</summary>
    public MatterTLV AddInt8(long tagNumber, sbyte value)
    {
        _values.Add(0x01 << 5 | 0x00);
        _values.Add((byte)tagNumber);
        _values.Add(unchecked((byte)value));
        return this;
    }

    internal MatterTLV AddInt8(sbyte value)
    {
        _values.Add(0x00);
        _values.Add(unchecked((byte)value));
        return this;
    }

    /// <summary>AddInt16.</summary>
    public MatterTLV AddInt16(long tagNumber, short value)
    {
        _values.Add(0x01 << 5 | 0x01);
        _values.Add((byte)tagNumber);
        MatterTlvBinary.AddInt16(_values, value);
        return this;
    }

    internal MatterTLV AddInt16(short value)
    {
        _values.Add(0x01);
        MatterTlvBinary.AddInt16(_values, value);
        return this;
    }

    /// <summary>AddInt32.</summary>
    public MatterTLV AddInt32(long tagNumber, int value)
    {
        _values.Add(0x01 << 5 | 0x02);
        _values.Add((byte)tagNumber);
        MatterTlvBinary.AddInt32(_values, value);
        return this;
    }

    internal MatterTLV AddInt32(int value)
    {
        _values.Add(0x02);
        MatterTlvBinary.AddInt32(_values, value);
        return this;
    }

    /// <summary>AddInt64.</summary>
    public MatterTLV AddInt64(long tagNumber, long value)
    {
        _values.Add(0x01 << 5 | 0x03);
        _values.Add((byte)tagNumber);
        MatterTlvBinary.AddInt64(_values, value);
        return this;
    }

    internal MatterTLV AddInt64(long value)
    {
        _values.Add(0x03);
        MatterTlvBinary.AddInt64(_values, value);
        return this;
    }

    /// <summary>AddFloat.</summary>
    public MatterTLV AddFloat(byte tagNumber, float value)
    {
        _values.Add(0x01 << 5 | 0x0A); // Float, 4-byte IEEE 754
        _values.Add(tagNumber);
        MatterTlvBinary.AddSingle(_values, value);
        return this;
    }

    internal MatterTLV AddFloat(float value)
    {
        _values.Add(0x0A);
        MatterTlvBinary.AddSingle(_values, value);
        return this;
    }

    /// <summary>AddDouble.</summary>
    public MatterTLV AddDouble(byte tagNumber, double value)
    {
        _values.Add(0x01 << 5 | 0x0B); // Double, 8-byte IEEE 754
        _values.Add(tagNumber);
        MatterTlvBinary.AddDouble(_values, value);
        return this;
    }

    internal MatterTLV AddDouble(double value)
    {
        _values.Add(0x0B);
        MatterTlvBinary.AddDouble(_values, value);
        return this;
    }

    /// <summary>AddNull.</summary>
    public MatterTLV AddNull(byte tagNumber)
    {
        _values.Add(0x01 << 5 | 0x14); // Null
        _values.Add(tagNumber);
        return this;
    }

    internal MatterTLV AddNull()
    {
        _values.Add(0x14);
        return this;
    }

    internal void Serialize(MatterMessageWriter writer)
    {
        writer.Write(AsSpan());
    }

    private int _pointer;

    /// <summary>Current read position in the TLV byte buffer.</summary>
    public int Position => _pointer;

    private void EnsureAvailable(int count, string context)
    {
        if (count < 0 || _pointer > _values.Count - count)
        {
            throw TlvError($"{context}: TLV buffer ended unexpectedly");
        }
    }

    private byte PeekByte(string context)
    {
        EnsureAvailable(1, context);
        return _values[_pointer];
    }

    private byte ReadByte(string context)
    {
        EnsureAvailable(1, context);
        return _values[_pointer++];
    }

    private void SkipBytes(int count, string context)
    {
        EnsureAvailable(count, context);
        _pointer += count;
    }

    private static int TagControlLength(int tagControl)
        => tagControl switch
        {
            0 => 0,
            1 => 1,
            2 or 4 => 2,
            3 or 5 => 4,
            6 => 6,
            7 => 8,
            _ => 0,
        };

    private void ReadExpectedContextTag(int tag)
    {
        if (ReadByte("context tag") != (byte)tag)
        {
            throw TlvError($"Expected tag number {tag} not found");
        }
    }

    private ushort ReadUInt16Checked(string context)
    {
        EnsureAvailable(2, context);
        var value = MatterTlvBinary.ReadUInt16(_values, _pointer);
        _pointer += 2;
        return value;
    }

    private uint ReadUInt32Checked(string context)
    {
        EnsureAvailable(4, context);
        var value = MatterTlvBinary.ReadUInt32(_values, _pointer);
        _pointer += 4;
        return value;
    }

    private ulong ReadUInt64Checked(string context)
    {
        EnsureAvailable(8, context);
        var value = MatterTlvBinary.ReadUInt64(_values, _pointer);
        _pointer += 8;
        return value;
    }

    private short ReadInt16Checked(string context)
    {
        EnsureAvailable(2, context);
        var value = MatterTlvBinary.ReadInt16(_values, _pointer);
        _pointer += 2;
        return value;
    }

    private int ReadInt32Checked(string context)
    {
        EnsureAvailable(4, context);
        var value = MatterTlvBinary.ReadInt32(_values, _pointer);
        _pointer += 4;
        return value;
    }

    private long ReadInt64Checked(string context)
    {
        EnsureAvailable(8, context);
        var value = MatterTlvBinary.ReadInt64(_values, _pointer);
        _pointer += 8;
        return value;
    }

    private float ReadSingleChecked(string context)
    {
        EnsureAvailable(4, context);
        var value = MatterTlvBinary.ReadSingle(_values, _pointer);
        _pointer += 4;
        return value;
    }

    private double ReadDoubleChecked(string context)
    {
        EnsureAvailable(8, context);
        var value = MatterTlvBinary.ReadDouble(_values, _pointer);
        _pointer += 8;
        return value;
    }

    private int ReadLength(int lengthBytes, string context)
    {
        ulong valueLength = lengthBytes switch
        {
            1 => ReadByte(context),
            2 => ReadUInt16Checked(context),
            4 => ReadUInt32Checked(context),
            8 => ReadUInt64Checked(context),
            _ => throw TlvError($"{context}: unsupported length width")
        };

        if (valueLength > int.MaxValue || valueLength > (ulong)(_values.Count - _pointer))
        {
            throw TlvError($"{context}: length extends beyond TLV buffer");
        }

        return (int)valueLength;
    }

    private ReadOnlySpan<byte> ReadSpan(int length, string context)
    {
        EnsureAvailable(length, context);
        var bytes = MatterTlvBinary.Slice(_values, _pointer, length);
        _pointer += length;
        return bytes;
    }

    /// <summary>IsNextTag.</summary>
    public bool IsNextTag(int tagNumber)
    {
        if (_pointer + 1 >= _values.Count)
        {
            return false;
        }

        if (_values[_pointer] == 0x18) // end-of-container — no more tags
        {
            return false;
        }

        return _values[_pointer + 1] == (byte)tagNumber;
    }

    /// <summary>IsEndContainerNext.</summary>
    public bool IsEndContainerNext()
    {
        return _pointer < _values.Count && _values[_pointer] == 0x18;
    }

    /// <summary>OpenStructure.</summary>
    public void OpenStructure()
    {
        if (ReadByte("structure control") != 0x15) // Tag Anonymous Structure
        {
            throw TlvError("Expected Structure not found");
        }
    }

    /// <summary>OpenStructure.</summary>
    public void OpenStructure(int? tag)
    {
        int tagControl = PeekByte("structure control") >> 5;

        if ((0x1F & ReadByte("structure control")) != 0x15) // Structure
        {
            throw TlvError("Expected Structure not found");
        }

        if (tag is null)
        {
            if (tagControl == 0x01)
            {
                SkipBytes(1, "structure tag"); // Skip the tag byte. We can't compare since we don't know the tag.
            }
        }
        else
        {
            ReadExpectedContextTag(tag.Value);
        }
    }

    /// <summary>OpenArray.</summary>
    public void OpenArray(int? tag)
    {
        int tagControl = PeekByte("array control") >> 5;

        if ((0x1F & ReadByte("array control")) != 0x16) // Array
        {
            throw TlvError("Expected Array not found");
        }

        if (tag is null)
        {
            if (tagControl == 0x01)
            {
                SkipBytes(1, "array tag"); // Skip the tag byte. We can't compare since we don't know the tag.
            }
        }
        else
        {
            ReadExpectedContextTag(tag.Value);
        }
    }

    /// <summary>OpenList.</summary>
    public void OpenList(int? tag)
    {
        int tagControl = PeekByte("list control") >> 5;

        if ((0x1F & ReadByte("list control")) != 0x17) // List
        {
            throw TlvError("Expected List not found");
        }

        if (tag is null)
        {
            if (tagControl == 0x01)
            {
                SkipBytes(1, "list tag"); // Skip the tag byte. We can't compare since we don't know the tag.
            }
        }
        else
        {
            ReadExpectedContextTag(tag.Value);
        }
    }

    /// <summary>GetBoolean.</summary>
    public bool GetBoolean(int tag)
    {
        var selectedByte = ReadByte("boolean control");

        if (selectedByte != 0x28 && selectedByte != 0x29) // Context Boolean (false)
        {
            throw TlvError("Expected Boolean not found");
        }

        bool value = selectedByte == 0x29; // True

        ReadExpectedContextTag(tag);

        return value;
    }

    /// <summary>GetOctetString.</summary>
    public byte[] GetOctetString(int tag)
        => GetOctetStringSpan(tag).ToArray();

    /// <summary>GetOctetStringSpan.</summary>
    public ReadOnlySpan<byte> GetOctetStringSpan(int tag)
    {
        // Check the Control Octet.
        //
        int length;

        var elementType = 0x1F & PeekByte("octet string control");
        if (elementType == 0x13)
        {
            //Octet String, 2 - octet length
            length = 8;
        }
        else if (elementType == 0x12)
        {
            //Octet String, 2 - octet length
            length = 4;
        }
        else if (elementType == 0x11)
        {
            //Octet String, 2 - octet length
            length = 2;
        }
        else if (elementType == 0x10) // Context Octet String, 1 - octet length
        {
            length = 1;
        }
        else
        {
            throw TlvError("Expected Octet String not found");
        }

        SkipBytes(1, "octet string control");

        ReadExpectedContextTag(tag);

        return ReadSpan(ReadLength(length, "octet string length"), "octet string payload");
    }

    /// <summary>GetUTF8String.</summary>
    public string GetUTF8String(int tag)
    {
        // Check the Control Octet.
        //
        int length;

        var elementType = 0x1F & PeekByte("UTF-8 string control");
        if (elementType == 0x0C)
        {
            //Octet String, 1 - octet length
            length = 1;
        }
        else if (elementType == 0x0D)
        {
            //Octet String, 2 - octet length
            length = 2;
        }
        else if (elementType == 0x0E)
        {
            //Octet String, 4 - octet length
            length = 4;
        }
        else if (elementType == 0x0F)
        {
            //Octet String, 8 - octet length
            length = 8;
        }
        else
        {
            throw TlvError("Expected UTF-8 String not found");
        }

        SkipBytes(1, "UTF-8 string control");

        ReadExpectedContextTag(tag);
        var bytes = ReadSpan(ReadLength(length, "UTF-8 string length"), "UTF-8 string payload");
        return Encoding.UTF8.GetString(bytes);
    }

    internal long GetSignedInt(int? tag)
    {
        int tagControl = PeekByte("signed integer control") >> 5;
        var elementType = (0x1F & ReadByte("signed integer control"));

        if (tag is null)
        {
            if (tagControl > 0)
            {
                SkipBytes(TagControlLength(tagControl), "signed integer tag");
            }
        }
        else
        {
            ReadExpectedContextTag(tag.Value);
        }

        long value = elementType switch
        {
            0x00 => (sbyte)ReadByte("signed integer value"),
            0x01 => ReadInt16Checked("signed integer value"),
            0x02 => ReadInt32Checked("signed integer value"),
            0x03 => ReadInt64Checked("signed integer value"),
            _ => throw TlvError($"Unexpected element type {elementType}"),
        };

        return value;
    }

    internal ulong GetUnsignedInt(int? tag)
    {
        int tagControl = PeekByte("unsigned integer control") >> 5;
        var elementType = (0x1F & ReadByte("unsigned integer control"));

        if (tag is null)
        {
            if (tagControl > 0)
            {
                SkipBytes(TagControlLength(tagControl), "unsigned integer tag");
            }
        }
        else
        {
            ReadExpectedContextTag(tag.Value);
        }

        ulong value = elementType switch
        {
            0x04 => ReadByte("unsigned integer value"),
            0x05 => ReadUInt16Checked("unsigned integer value"),
            0x06 => ReadUInt32Checked("unsigned integer value"),
            0x07 => ReadUInt64Checked("unsigned integer value"),
            _ => throw TlvError($"Unexpected element type {elementType}"),
        };

        return value;
    }

    /// <summary>GetUnsignedInt8.</summary>
    public byte GetUnsignedInt8(int tag)
    {
        if ((0x1F & ReadByte("uint8 control")) != 0x04)
        {
            throw TlvError("Expected Unsigned Integer, 1-octet value not found");
        }

        ReadExpectedContextTag(tag);

        byte value = ReadByte("uint8 value");

        return value;
    }

    /// <summary>GetUnsignedInt16.</summary>
    public ushort GetUnsignedInt16(int tag)
    {
        if ((0x1F & ReadByte("uint16 control")) != 0x05)
        {
            throw TlvError("Expected Unsigned Integer, 2-octet value");
        }

        ReadExpectedContextTag(tag);

        var value = ReadUInt16Checked("uint16 value");

        return value;
    }

    /// <summary>GetUnsignedInt32.</summary>
    public uint GetUnsignedInt32(int tag)
    {
        if ((0x1F & ReadByte("uint32 control")) != 0x06)
        {
            throw TlvError("Expected Unsigned Integer, 4-octet value");
        }

        ReadExpectedContextTag(tag);

        var value = ReadUInt32Checked("uint32 value");

        return value;
    }

    /// <summary>GetUnsignedInt64.</summary>
    public ulong GetUnsignedInt64(int tag)
    {
        if ((0x1F & ReadByte("uint64 control")) != 0x07)
        {
            throw TlvError("Expected Unsigned Integer, 8-octet value");
        }

        ReadExpectedContextTag(tag);

        var value = ReadUInt64Checked("uint64 value");

        return value;
    }

    /// <summary>
    /// Reads an unsigned integer of any width (UInt8/16/32/64) and returns it as uint.
    /// CHIP SDK's TLV writer encodes integers in the smallest type that fits.
    /// </summary>
    public uint GetUnsignedIntAny(int tag)
    {
        byte elementType = (byte)(0x1F & ReadByte("unsigned integer control"));

        ReadExpectedContextTag(tag);

        switch (elementType)
        {
            case 0x04: // UInt8
                return ReadByte("uint8 value");
            case 0x05: // UInt16
                return ReadUInt16Checked("uint16 value");
            case 0x06: // UInt32
                return ReadUInt32Checked("uint32 value");
            case 0x07: // UInt64
                var v64 = ReadUInt64Checked("uint64 value");
                return (uint)v64;
            default:
                throw TlvError($"Expected unsigned integer at tag {tag}, got element type 0x{elementType:X2}");
        }
    }

    /// <summary>CloseContainer.</summary>
    public void CloseContainer()
    {
        // Skip any remaining elements in the container (like CHIP SDK ExitContainer)
        while (_pointer < _values.Count && _values[_pointer] != 0x18)
        {
            SkipElement();
        }

        if (_pointer >= _values.Count || _values[_pointer] != 0x18)
        {
            throw TlvError("Expected EndContainer not found");
        }

        _pointer++; // consume the 0x18
    }

    /// <summary>Returns the context tag number of the next element, or -1 if anonymous/end-of-container.</summary>
    public int PeekTag()
    {
        if (_pointer >= _values.Count)
        {
            return -1;
        }

        byte control = _values[_pointer];
        if (control == 0x18)
        {
            return -1; // End container
        }

        int tagControl = control >> 5;
        if (tagControl == 0x01 && _pointer + 1 < _values.Count)
        {
            return _values[_pointer + 1];
        }

        return -1; // Anonymous or non-context tag
    }

    /// <summary>Returns the element type (lower 5 bits of control byte) of the next element.</summary>
    public int PeekElementType()
    {
        if (_pointer >= _values.Count)
        {
            return -1;
        }

        return _values[_pointer] & 0x1F;
    }

    /// <summary>Skip elements until finding the given context tag, or hitting end-of-container.</summary>
    /// <returns>True if the tag was found (pointer is at that element). False if end-of-container was reached.</returns>
    public bool SkipToTag(int tagNumber)
    {
        while (!IsEndContainerNext())
        {
            if (PeekTag() == tagNumber)
            {
                return true;
            }

            SkipElement();
        }
        return false;
    }

    /// <summary>Returns true if the next element is a Null type.</summary>
    public bool IsNextNull()
    {
        return _pointer < _values.Count && (_values[_pointer] & 0x1F) == 0x14;
    }

    /// <summary>Read a Null element with the given context tag.</summary>
    public void GetNull(int tag)
    {
        if ((ReadByte("null control") & 0x1F) != 0x14)
        {
            throw TlvError("Expected Null not found");
        }

        ReadExpectedContextTag(tag);
    }

    /// <summary>Read a 32-bit IEEE 754 float with a context tag.</summary>
    public float GetFloat(int tag)
    {
        if ((ReadByte("float control") & 0x1F) != 0x0A)
        {
            throw TlvError("Expected Float not found");
        }

        ReadExpectedContextTag(tag);

        return ReadSingleChecked("float value");
    }

    /// <summary>Read a 64-bit IEEE 754 double with a context tag.</summary>
    public double GetDouble(int tag)
    {
        if ((ReadByte("double control") & 0x1F) != 0x0B)
        {
            throw TlvError("Expected Double not found");
        }

        ReadExpectedContextTag(tag);

        return ReadDoubleChecked("double value");
    }

    /// <summary>Returns true if there is more data to read.</summary>
    public bool HasMore => _pointer < _values.Count;

    /// <summary>GetBytes.</summary>
    public byte[] GetBytes()
        => AsSpan().ToArray();

    /// <summary>AsSpan.</summary>
    public ReadOnlySpan<byte> AsSpan()
        => MatterTlvBinary.AsSpan(_values);

    private void WriteLengthPrefixedValue(byte? tagNumber, byte baseType, ReadOnlySpan<byte> value)
    {
        var valueLength = value.Length;
        if (tagNumber.HasValue)
        {
            _values.Add((byte)((0x01 << 5) | GetLengthEncodedType(baseType, valueLength)));
            _values.Add(tagNumber.Value);
        }
        else
        {
            _values.Add(GetLengthEncodedType(baseType, valueLength));
        }

        WriteLength(valueLength);
        MatterTlvBinary.AddBytes(_values, value);
    }

    private static byte GetLengthEncodedType(byte baseType, int valueLength)
        => valueLength switch
        {
            <= byte.MaxValue => baseType,
            <= ushort.MaxValue => (byte)(baseType + 1),
            _ => (byte)(baseType + 2),
        };

    private void WriteLength(int valueLength)
    {
        if (valueLength <= byte.MaxValue)
        {
            _values.Add((byte)valueLength);
            return;
        }

        if (valueLength <= ushort.MaxValue)
        {
            MatterTlvBinary.AddUInt16(_values, (ushort)valueLength);
            return;
        }

        MatterTlvBinary.AddUInt32(_values, (uint)valueLength);
    }

    /// <summary>ToString.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append('\n');

        var indentation = 0;

        void indent(StringBuilder sb)
        {
            for (int x = 0; x < indentation; x++)
            {
                sb.Append(' ');
            }
        }

        int renderTag(byte[] bytes, int index)
        {
            int tagControl = bytes[index] >> 5;
            int elementType = bytes[index] >> 0 & 0x1F;

            int length = 1; // We have read the tagControl/elementType byte

            sb.Append('|');
            indent(sb);

            try
            {
                if (elementType == 0x15) // Structure
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    sb.AppendLine("Structure {");
                    indentation += 2;
                }

                else if (elementType == 0x16)
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    sb.AppendLine("Array [");
                    indentation += 2;
                }

                else if (elementType == 0x17)
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    sb.AppendLine("List [");
                    indentation += 2;
                }

                else if (elementType == 0x07) // Unsigned Integer 64bit 
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    var value = BitConverter.ToUInt64(bytes, index + length);

                    sb.AppendLine($"Unsigned Int (64bit) ({value}|0x{value:X2})");

                    length += 8;
                }

                else if (elementType == 0x06) // Unsigned Integer 32bit 
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    var value = BitConverter.ToUInt32(bytes, index + length);

                    sb.AppendLine($"Unsigned Int (32bit) ({value}|0x{value:X2})");

                    length += 4;
                }

                else if (elementType == 0x05) // Unsigned Integer 16bit 
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    var value = BitConverter.ToUInt16(bytes, index + length);

                    sb.AppendLine($"Unsigned Int (16bit) ({value}|0x{value:X2})");

                    length += 2;
                }

                else if (elementType == 0x04) // Unsigned Integer 8bit 
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    var value = bytes[index + length];

                    sb.AppendLine($"Unsigned Int (8bit) ({value}|0x{value:X2})");

                    length += 1;
                }

                else if (elementType == 0x00) // Signed Integer 8bit 
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    var value = bytes[index + length];

                    sb.AppendLine($"Signed Int (8bit) ({value}|0x{value:X2})");

                    length += 1;
                }

                else if (elementType == 0x01) // Signed Integer 16bit 
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    var value = BitConverter.ToInt16(bytes, index + length);

                    sb.AppendLine($"Signed Int (16bit) ({value}|0x{value:X2})");

                    length += 2;
                }

                else if (elementType == 0x0C) // UTF-8 String, 1-octet length
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    // One octet length
                    var stringLength = bytes[index + length];

                    length++;

                    var value = Encoding.UTF8.GetString(bytes.AsSpan().Slice(index + length, stringLength));

                    sb.AppendLine($"UTF-8 String, 1-octet length ({stringLength}) ({value})");

                    length += stringLength;
                }

                else if (elementType == 0x0D) // UTF-8 String, 2-octet length
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    // Two octet length
                    var stringLength = BitConverter.ToUInt16(bytes, index + length);

                    length += 2;

                    var value = Encoding.UTF8.GetString(bytes.AsSpan().Slice(index + length, stringLength));

                    sb.AppendLine($"UTF-8 String, 1-octet length ({stringLength}) ({value})");

                    length += stringLength;
                }

                else if (elementType == 0x0E) // UTF-8 String, 4-octet length
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");

                        length++;
                    }
                    else if (tagControl == 0x02) // Common Profile Tag Form, 2 octets
                    {
                        var tag = BitConverter.ToInt16(bytes, index + length);
                        sb.Append($"{tag} => ");

                        length += 2;
                    }
                    else if (tagControl == 0x03) // Common Profile Tag Form, 4 octets
                    {
                        var tag = BitConverter.ToInt32(bytes, index + length);
                        sb.Append($"{tag} => ");

                        length += 4;
                    }

                    // Four octet length
                    var stringLength = BitConverter.ToUInt32(bytes, index + length);

                    length += 4;

                    var value = Encoding.UTF8.GetString(bytes.AsSpan().Slice(index + length, (int)stringLength));

                    sb.AppendLine($"UTF-8 String, 4-octet length ({value})");

                    length += (int)stringLength;
                }

                else if (elementType == 0x10) // Octet String, 1-octet length
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    // One octet length
                    var stringLength = bytes[index + length];

                    length++;

                    var value = bytes.AsSpan().Slice(index + length, stringLength).ToArray();

                    sb.AppendLine($"Octet String, 1-octet length ({stringLength}) ({BitConverter.ToString(value)})");

                    length += stringLength;
                }

                else if (elementType == 0x11) // Octet String, 2-octet length
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    // Two octet length
                    var stringLength = BitConverter.ToUInt16(bytes, index + length);

                    length += 2;

                    var value = bytes.AsSpan().Slice(index + length, stringLength).ToArray();

                    sb.AppendLine($"Octet String, 2-octet length ({BitConverter.ToString(value)})");

                    length += stringLength;
                }

                else if (elementType == 0x08 || elementType == 0x09) // Boolean
                {
                    if (tagControl == 0x01) // Context {
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }

                    sb.AppendLine($"Boolean ({elementType == 0x09})");
                }

                else if (elementType == 0x0A) // Float 32-bit
                {
                    if (tagControl == 0x01)
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }
                    var fval = BitConverter.ToSingle(bytes, index + length);
                    sb.AppendLine($"Float ({fval})");
                    length += 4;
                }

                else if (elementType == 0x0B) // Double 64-bit
                {
                    if (tagControl == 0x01)
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }
                    var dval = BitConverter.ToDouble(bytes, index + length);
                    sb.AppendLine($"Double ({dval})");
                    length += 8;
                }

                else if (elementType == 0x14) // Null
                {
                    if (tagControl == 0x01)
                    {
                        sb.Append($"{bytes[index + 1].ToString()} => ");
                        length++;
                    }
                    sb.AppendLine("Null");
                }

                else if (elementType == 0x18)
                {
                    indentation -= 2;
                    sb.AppendLine("}"); // Should be a ] if we opened with an array or list.
                }

                else
                {
                    sb.AppendLine($"Unhandled Tag ({tagControl:X2}|{elementType:X2})");
                }
            }
            catch
            {

            }

            return length;
        }

        // Move through each
        //
        var bytes = GetBytes();

        sb.AppendLine("TLV Payload");
        sb.AppendLine();
        sb.AppendLine(Convert.ToHexString(bytes));
        sb.AppendLine();

        var i = 0;

        while (i < bytes.Length)
        {
            i += renderTag(bytes, i);
        }

        return sb.ToString();
    }


    /// <summary>GetData.</summary>
    public object? GetData(int? tag)
    {
        int tagControl = PeekByte("TLV element control") >> 5;
        int elementType = PeekByte("TLV element control") & 0x1F;

        switch (elementType)
        {
            case 0x00:
            case 0x01:
            case 0x02:
            case 0x03:
                return GetSignedInt(tag);

            case 0x04: //
            case 0x05:
            case 0x06:
            case 0x07:
                return GetUnsignedInt(tag);

            case 0x08: // Boolean False
            case 0x09: // Boolean True
                {
                    SkipBytes(1, "boolean control");
                    SkipBytes(TagControlLength(tagControl), "boolean tag");

                    return elementType == 0x09;
                }

            case 0x0A: // Float (4-byte IEEE 754)
                {
                    SkipBytes(1, "float control");
                    SkipBytes(TagControlLength(tagControl), "float tag");
                    return ReadSingleChecked("float value");
                }

            case 0x0B: // Double (8-byte IEEE 754)
                {
                    SkipBytes(1, "double control");
                    SkipBytes(TagControlLength(tagControl), "double tag");
                    return ReadDoubleChecked("double value");
                }

            case 0x0C: // UTF-8 String, 1-octet length
            case 0x0D: // UTF-8 String, 2-octet length
            case 0x0E: // UTF-8 String, 4-octet length
            case 0x0F: // UTF-8 String, 8-octet length
                {
                    SkipBytes(1, "UTF-8 string control");
                    SkipBytes(TagControlLength(tagControl), "UTF-8 string tag");

                    int slen = elementType switch
                    {
                        0x0C => ReadLength(1, "UTF-8 string length"),
                        0x0D => ReadLength(2, "UTF-8 string length"),
                        0x0E => ReadLength(4, "UTF-8 string length"),
                        0x0F => ReadLength(8, "UTF-8 string length"),
                        _ => throw TlvError("Unsupported string length"),
                    };
                    var str = Encoding.UTF8.GetString(ReadSpan(slen, "UTF-8 string payload"));
                    return str;
                }

            case 0x10: // Octet String, 1-octet length
            case 0x11: // Octet String, 2-octet length
            case 0x12: // Octet String, 4-octet length
            case 0x13: // Octet String, 8-octet length
                {
                    SkipBytes(1, "octet string control");
                    SkipBytes(TagControlLength(tagControl), "octet string tag");

                    int olen = elementType switch
                    {
                        0x10 => ReadLength(1, "octet string length"),
                        0x11 => ReadLength(2, "octet string length"),
                        0x12 => ReadLength(4, "octet string length"),
                        0x13 => ReadLength(8, "octet string length"),
                        _ => throw TlvError("Unsupported octet string length"),
                    };
                    var octets = ReadSpan(olen, "octet string payload").ToArray();
                    return octets;
                }

            case 0x15: // Structure

                List<object> structure = [];

                OpenStructure(tag);

                while (!IsEndContainerNext())
                {
                    structure.Add(GetData(null)!);
                }

                CloseContainer();

                return structure;

            case 0x16:

                List<object> array = [];

                OpenArray(tag);

                while (!IsEndContainerNext())
                {
                    array.Add(GetData(null)!);
                }

                CloseContainer();

                return array;
            case 0x17:

                List<object> list = [new List<object>()];

                OpenList(tag);

                while (!IsEndContainerNext())
                {
                    list.Add(GetData(null)!);
                }

                CloseContainer();

                return list;

            case 0x14: // Null
                {
                    SkipBytes(1, "null control");
                    SkipBytes(TagControlLength(tagControl), "null tag");

                    return null;
                }

            default:
                throw TlvError($"Cannot process elementType {elementType:X2}");
        }
    }

    /// <summary>Skip the current TLV element (advances the read pointer past it).</summary>
    public void SkipElement() => GetData(null);
}
