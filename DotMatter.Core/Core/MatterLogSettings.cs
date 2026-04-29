#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace DotMatter.Core;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Global logging settings for DotMatter.Core diagnostics.
/// </summary>
public sealed class MatterLogSettings
{
    /// <summary>
    /// When true, logs may include sensitive byte material intended only for controlled debugging.
    /// Defaults to false.
    /// </summary>
    public bool EnableSensitiveDiagnostics { get; set; }

    /// <summary>
    /// Maximum number of bytes rendered in normal diagnostic output.
    /// </summary>
    public int MaxRenderedBytes { get; set; } = 32;
}
