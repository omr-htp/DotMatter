namespace DotMatter.Hosting.Storage;

/// <summary>
/// Shared helpers for fabric directory names used by hosted Matter consumers.
/// </summary>
public static class MatterFabricNames
{
    /// <summary>Returns a normalized fabric name that is safe to use as a single directory name.</summary>
    public static string Normalize(string fabricName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fabricName);

        var trimmed = fabricName.Trim();
        if (trimmed is "." or ".."
            || Path.IsPathFullyQualified(trimmed)
            || trimmed.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "Fabric name must be a single file-system-safe directory name.",
                nameof(fabricName));
        }

        return trimmed;
    }

    /// <summary>Attempts to normalize a fabric name without throwing for invalid input.</summary>
    public static bool TryNormalize(string? fabricName, out string normalized)
    {
        try
        {
            normalized = Normalize(fabricName!);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    /// <summary>Returns a filesystem-safe fabric name generated from a display name.</summary>
    public static string SanitizeDeviceFabricName(string name)
    {
        var sanitized = new string([.. name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')]).Trim('-', '_');
        return Normalize(string.IsNullOrEmpty(sanitized) ? $"matter-{DateTime.UtcNow:yyyyMMddHHmmss}" : sanitized);
    }

    /// <summary>Combines a base fabric directory with a normalized fabric name.</summary>
    public static string GetFabricPath(string basePath, string fabricName)
        => Path.Combine(basePath, Normalize(fabricName));
}
