namespace DotMatter.Core;

/// <summary>Exchange flags for Matter protocol messages.</summary>
[Flags]
public enum ExchangeFlags : byte
{
    /// <summary>Initiator.</summary>
    Initiator = 0x1,
    /// <summary>Acknowledgement.</summary>
    Acknowledgement = 0x2,
    /// <summary>Reliability.</summary>
    Reliability = 0x4,
    /// <summary>SecuredExtensions.</summary>
    SecuredExtensions = 0x8,
    /// <summary>VendorPresent.</summary>
    VendorPresent = 0x10,
}
