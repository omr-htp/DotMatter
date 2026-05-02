namespace DotMatter.Core.Fabrics;

/// <summary>
/// Applies platform-appropriate permissions to persisted fabric secrets.
/// </summary>
public static class FabricStorageSecurity
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Returns whether the given persisted fabric file contains secret material that should be owner-readable only.
    /// </summary>
    public static bool IsSensitiveFabricFile(string fileName)
        => fileName.Equals("fabric.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("rootKeyPair.pem", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("operationalKeyPair.pem", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies owner-only permissions to a persisted secret file on Unix-like systems.
    /// </summary>
    public static void TryApplyOwnerOnlyFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, OwnerReadWrite);
    }
}
