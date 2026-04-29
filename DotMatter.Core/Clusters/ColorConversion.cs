namespace DotMatter.Core.Clusters;

/// <summary>
/// CIE XY ↔ HSV ↔ sRGB color conversions for Matter color control.
/// All conversions use the standard sRGB↔XYZ D65 matrices.
/// </summary>
public static class ColorConversion
{
    /// <summary>
    /// Convert color temperature in mireds to CIE XY.
    /// Uses Tanner Helland approximation for Kelvin → sRGB.
    /// </summary>
    public static (ushort x, ushort y) MiredsToXY(ushort mireds)
    {
        double kelvin = 1_000_000.0 / Math.Max((int)mireds, 1);
        double temp = kelvin / 100.0;
        double r, g, b;

        if (temp <= 66)
        {
            r = 1.0;
            g = Math.Clamp((99.4708025861 * Math.Log(temp) - 161.1195681661) / 255.0, 0, 1);
            b = temp <= 19 ? 0 : Math.Clamp((138.5177312231 * Math.Log(temp - 10) - 305.0447927307) / 255.0, 0, 1);
        }
        else
        {
            r = Math.Clamp(329.698727446 * Math.Pow(temp - 60, -0.1332047592) / 255.0, 0, 1);
            g = Math.Clamp(288.1221695283 * Math.Pow(temp - 60, -0.0755148492) / 255.0, 0, 1);
            b = 1.0;
        }

        return SrgbToMatterXY(r, g, b);
    }

    /// <summary>
    /// Convert Matter hue (0-254) and saturation (0-254) to CIE xy coordinates
    /// encoded as Matter uint16 (0-65535). Uses sRGB→XYZ matrix.
    /// </summary>
    public static (ushort x, ushort y) HueSatToXY(byte hue, byte saturation)
    {
        double h = hue / 254.0 * 360.0;
        double s = saturation / 254.0;
        double v = 1.0;

        double c = v * s;
        double hp = h / 60.0;
        double x2 = c * (1 - Math.Abs(hp % 2 - 1));
        double m = v - c;
        double r, g, b;

        if (hp < 1) { r = c; g = x2; b = 0; }
        else if (hp < 2) { r = x2; g = c; b = 0; }
        else if (hp < 3) { r = 0; g = c; b = x2; }
        else if (hp < 4) { r = 0; g = x2; b = c; }
        else if (hp < 5) { r = x2; g = 0; b = c; }
        else { r = c; g = 0; b = x2; }

        r += m; g += m; b += m;

        return SrgbToMatterXY(r, g, b);
    }

    /// <summary>
    /// Convert Matter CIE xy (uint16, 0-65535) to approximate hue (0-254) and saturation (0-254).
    /// Uses XYZ→sRGB reverse matrix.
    /// </summary>
    public static (byte hue, byte saturation) XYToHueSat(ushort matterX, ushort matterY)
    {
        double cx = matterX / 65535.0;
        double cy = matterY / 65535.0;

        if (cy < 1e-6)
        {
            return (0, 0);
        }

        double bigY = 1.0;
        double bigX = (bigY / cy) * cx;
        double bigZ = (bigY / cy) * (1 - cx - cy);

        double rLin = bigX * 3.2404542 + bigY * -1.5371385 + bigZ * -0.4985314;
        double gLin = bigX * -0.9692660 + bigY * 1.8760108 + bigZ * 0.0415560;
        double bLin = bigX * 0.0556434 + bigY * -0.2040259 + bigZ * 1.0572252;

        rLin = Math.Max(rLin, 0); gLin = Math.Max(gLin, 0); bLin = Math.Max(bLin, 0);

        double r = rLin > 0.0031308 ? 1.055 * Math.Pow(rLin, 1 / 2.4) - 0.055 : 12.92 * rLin;
        double g = gLin > 0.0031308 ? 1.055 * Math.Pow(gLin, 1 / 2.4) - 0.055 : 12.92 * gLin;
        double b = bLin > 0.0031308 ? 1.055 * Math.Pow(bLin, 1 / 2.4) - 0.055 : 12.92 * bLin;

        r = Math.Clamp(r, 0, 1); g = Math.Clamp(g, 0, 1); b = Math.Clamp(b, 0, 1);

        // RGB → HSV
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h2 = 0;
        if (delta > 1e-6)
        {
            if (max == r)
            {
                h2 = ((g - b) / delta + 6) % 6;
            }
            else if (max == g)
            {
                h2 = (b - r) / delta + 2;
            }
            else
            {
                h2 = (r - g) / delta + 4;
            }

            h2 *= 60;
        }
        double sat = max < 1e-6 ? 0 : delta / max;

        byte matterHue = (byte)Math.Clamp(h2 / 360.0 * 254, 0, 254);
        byte matterSat = (byte)Math.Clamp(sat * 254, 0, 254);
        return (matterHue, matterSat);
    }

    private static (ushort x, ushort y) SrgbToMatterXY(double r, double g, double b)
    {
        double rLin = r > 0.04045 ? Math.Pow((r + 0.055) / 1.055, 2.4) : r / 12.92;
        double gLin = g > 0.04045 ? Math.Pow((g + 0.055) / 1.055, 2.4) : g / 12.92;
        double bLin = b > 0.04045 ? Math.Pow((b + 0.055) / 1.055, 2.4) : b / 12.92;

        double X = rLin * 0.4124564 + gLin * 0.3575761 + bLin * 0.1804375;
        double Y = rLin * 0.2126729 + gLin * 0.7151522 + bLin * 0.0721750;
        double Z = rLin * 0.0193339 + gLin * 0.1191920 + bLin * 0.9503041;

        double sum = X + Y + Z;
        if (sum < 1e-6)
        {
            return (0, 0);
        }

        double cx = X / sum;
        double cy = Y / sum;

        return ((ushort)Math.Clamp(cx * 65535, 0, 65535), (ushort)Math.Clamp(cy * 65535, 0, 65535));
    }
}
