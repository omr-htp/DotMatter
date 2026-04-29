namespace DotMatter.Core;

/// <summary>
/// Parses Matter manual pairing codes (11/21-digit) and QR code payloads (MT: prefix).
/// </summary>
public static class PairingCodeParser
{
    /// <summary>
    /// Parse an 11-digit or 21-digit manual pairing code.
    /// Returns (discriminator, passcode, isShortDiscriminator) or null if invalid.
    /// Manual codes contain a 4-bit SHORT discriminator (top 4 bits of the 12-bit long discriminator).
    /// </summary>
    public static (int Discriminator, uint Passcode, bool IsShort)? ParseManualCode(string code)
    {
        code = NormalizeDigits(code);
        if (code.Length is not 11 and not 21)
        {
            return null;
        }

        if (!ulong.TryParse(code, out _))
        {
            return null;
        }

        if (!Verhoeff.ValidateVerhoeff(code))
        {
            return null;
        }

        int digit1 = code[0] - '0';
        long chunk2 = long.Parse(code.AsSpan(1, 5));
        long chunk3 = long.Parse(code.AsSpan(6, 4));

        int discHigh = digit1 & 0x03;
        int discLow = (int)(chunk2 >> 14) & 0x03;
        int shortDiscriminator = (discHigh << 2) | discLow;

        uint passcodeLow = (uint)(chunk2 & 0x3FFF);
        uint passcodeHigh = (uint)(chunk3 & 0x1FFF);
        uint passcode = (passcodeHigh << 14) | passcodeLow;

        return (shortDiscriminator, passcode, true);
    }

    /// <summary>
    /// Parse a Matter QR code payload (starts with "MT:").
    /// Returns (discriminator, passcode, isShortDiscriminator) or null if invalid.
    /// QR codes contain the full 12-bit LONG discriminator.
    /// </summary>
    public static (int Discriminator, uint Passcode, bool IsShort)? ParseQrCode(string payload)
    {
        payload = payload.Trim();
        if (!payload.StartsWith("MT:"))
        {
            return null;
        }

        string data = payload[3..];
        var bits = Base38Decode(data);
        if (bits == null || bits.Length < 12)
        {
            return null;
        }

        ulong qrData = 0;
        for (int i = bits.Length - 1; i >= 0; i--)
        {
            qrData = (qrData << 8) | bits[i];
        }

        int discriminator = (int)((qrData >> 45) & 0xFFF);
        uint passcode = (uint)((qrData >> 57) & 0x7FFFFFF);

        return (discriminator, passcode, false);
    }

    private const string Base38Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-.";

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

    private static byte[]? Base38Decode(string data)
    {
        var result = new List<byte>();
        int i = 0;
        while (i < data.Length)
        {
            int chunkLen = Math.Min(5, data.Length - i);
            ulong value = 0;
            ulong multiplier = 1;
            for (int j = 0; j < chunkLen; j++)
            {
                int charIndex = Base38Chars.IndexOf(data[i + j]);
                if (charIndex < 0)
                {
                    return null;
                }

                value += (ulong)charIndex * multiplier;
                multiplier *= 38;
            }

            int byteCount = chunkLen switch
            {
                5 => 3,
                4 => 2,
                3 => 2,
                2 => 1,
                1 => 1,
                _ => 0
            };

            for (int j = 0; j < byteCount; j++)
            {
                result.Add((byte)(value & 0xFF));
                value >>= 8;
            }

            i += chunkLen;
        }

        return [.. result];
    }
}
