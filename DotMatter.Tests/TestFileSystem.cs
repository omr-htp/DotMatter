namespace DotMatter.Tests;

internal static class TestFileSystem
{
    public static TempDirectory CreateTempDirectoryScope()
        => new(CreateTempDirectory());

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DotMatterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public sealed class TempDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
            => DeleteDirectoryIfExists(Path);
    }
}
