namespace DotMatter.Hosting;

/// <summary>
/// Options for the on-disk device registry and fabric metadata store.
/// </summary>
public sealed class RegistryOptions
{
    /// <summary>Gets or sets the base directory used to store fabric metadata.</summary>
    public string? BasePath { get; set; }
}
