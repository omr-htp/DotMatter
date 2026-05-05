using System.Text;

namespace DotMatter.Hosting.Storage;

/// <summary>
/// Writes files through a temporary file and atomic replacement.
/// </summary>
public static class AtomicFilePersistence
{
    private static readonly UTF8Encoding _utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes text to a file asynchronously using atomic replacement.
    /// </summary>
    public static async Task WriteTextAsync(string path, string content, CancellationToken ct = default)
    {
        var directory = GetRequiredDirectory(path);
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, content, _utf8WithoutBom, ct);
        ReplaceFile(tempPath, path);
    }

    /// <summary>
    /// Writes text to a file using atomic replacement.
    /// </summary>
    public static void WriteText(string path, string content)
    {
        var directory = GetRequiredDirectory(path);
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, content, _utf8WithoutBom);
        ReplaceFile(tempPath, path);
    }

    private static void ReplaceFile(string tempPath, string destinationPath)
    {
        try
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            DeleteTempFileIfExists(tempPath);
            throw;
        }
    }

    private static string GetRequiredDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Cannot determine directory for '{path}'.");
        }

        return directory;
    }

    private static void DeleteTempFileIfExists(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch
        {
        }
    }
}
