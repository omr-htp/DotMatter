namespace DotMatter.Core;

/// <summary>Security flags for Matter protocol frames.</summary>
[Flags]
public enum SecurityFlags : byte
{
    /// <summary>None.</summary>
    None = 0x00,
    /// <summary>MessageExtensions.</summary>
    MessageExtensions = 0x20,
    /// <summary>ControlMessage.</summary>
    ControlMessage = 0x40,
    /// <summary>Privacy.</summary>
    Privacy = 0x80,
}

/// <summary>SecurityFlagsExtensions class.</summary>
public static class SecurityFlagsExtensions
{
    private const byte SessionTypeMask = 0x03;
    /// <summary>SessionType_Unicast.</summary>
    /// <summary>The SessionType_Unicast value.</summary>
    public const byte SessionType_Unicast = 0x00;
    /// <summary>SessionType_Group.</summary>
    /// <summary>The SessionType_Group value.</summary>
    public const byte SessionType_Group = 0x01;

    /// <summary>Extract the 2-bit SessionType field.</summary>
    public static byte GetSessionType(this SecurityFlags flags) => (byte)((byte)flags & SessionTypeMask);
}
