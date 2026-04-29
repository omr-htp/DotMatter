namespace DotMatter.Core;

/// <summary>Message flags for Matter protocol frames.</summary>
[Flags]
public enum MessageFlags : byte
{
    /// <summary>None.</summary>
    None = 0x00,
    /// <summary>S.</summary>
    S = 0x04,
}

/// <summary>MessageFlagsExtensions class.</summary>
public static class MessageFlagsExtensions
{
    private const byte DsizMask = 0x03;

    /// <summary>Extract the 2-bit DSIZ field (0-3).</summary>
    public static byte GetDsiz(this MessageFlags flags) =>
        (byte)((byte)flags & DsizMask);

    /// <summary>Set the 2-bit DSIZ field value.</summary>
    public static MessageFlags WithDsiz(this MessageFlags flags, byte dsiz) =>
        (MessageFlags)(((byte)flags & ~DsizMask) | (dsiz & DsizMask));
}
