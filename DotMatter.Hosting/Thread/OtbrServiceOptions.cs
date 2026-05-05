namespace DotMatter.Hosting.Thread;

/// <summary>
/// Options for OTBR and <c>ot-ctl</c> integration.
/// </summary>
public sealed class OtbrServiceOptions
{
    /// <summary>Gets or sets a value indicating whether the SRP server should be enabled during startup.</summary>
    public bool EnableSrpServerOnStartup { get; set; } = true;
    /// <summary>Gets or sets the path to the <c>ot-ctl</c> command.</summary>
    public string CommandPath { get; set; } = "ot-ctl";
    /// <summary>Gets or sets the command used for privileged <c>ot-ctl</c> operations.</summary>
    public string? SudoCommand { get; set; } = "sudo";
    /// <summary>Gets or sets the maximum number of Thread IP discovery attempts.</summary>
    public int ThreadIpDiscoveryMaxAttempts { get; set; } = 10;
    /// <summary>Gets or sets the delay between Thread IP discovery attempts.</summary>
    public TimeSpan ThreadIpDiscoveryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
