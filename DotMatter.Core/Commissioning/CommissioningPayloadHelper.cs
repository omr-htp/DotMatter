namespace DotMatter.Core.Commissioning;

/// <summary>CommissioningPayloadHelper class.</summary>
public class CommissioningPayloadHelper
{
    /// <summary>ParseManualSetupCode.</summary>
    public static CommissioningPayload ParseManualSetupCode(string manualSetupCode)
    {
        manualSetupCode = NormalizeDigits(manualSetupCode);

        if (manualSetupCode.Length != 11)
        {
            throw new ArgumentException("Manual setup code must be 11 digits long.");
        }

        var isValid = Verhoeff.ValidateVerhoeff(manualSetupCode);

        if (!isValid)
        {
            throw new ArgumentException("Manual setup code failed checksum.");
        }

        byte byte1 = byte.Parse(manualSetupCode[..1]);

        ushort discriminator = (ushort)(byte1 << 10);

        ushort byte2to6 = ushort.Parse(manualSetupCode.AsSpan(1, 5));

        discriminator |= (ushort)((byte2to6 & 0xC000) >> 6);

        uint passcode = (uint)(byte2to6 & 0x3FFF);

        ushort byte7to10 = ushort.Parse(manualSetupCode.AsSpan(6, 4));

        passcode |= (uint)(byte7to10 << 14);

        return new CommissioningPayload()
        {
            Discriminator = discriminator,
            Passcode = passcode
        };
    }

    private static string NormalizeDigits(string value)
    {
        var source = value.AsSpan().Trim();
        Span<char> buffer = stackalloc char[source.Length];
        var length = 0;

        foreach (var character in source)
        {
            if (character is '-' or ' ')
            {
                continue;
            }

            buffer[length++] = character;
        }

        return new string(buffer[..length]);
    }

    /// <summary>ParseQRCode.</summary>
    public static CommissioningPayload ParseQRCode(string qrCode)
    {
        var parsed = PairingCodeParser.ParseQrCode(qrCode);
        if (parsed is null)
        {
            throw new ArgumentException("QR code payload is invalid.", nameof(qrCode));
        }

        return new CommissioningPayload
        {
            Discriminator = (ushort)parsed.Value.Discriminator,
            Passcode = parsed.Value.Passcode
        };
    }
}
