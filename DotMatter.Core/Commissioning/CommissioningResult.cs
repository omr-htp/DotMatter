namespace DotMatter.Core.Commissioning;

/// <summary>Result of a Matter commissioning operation.</summary>
/// <param name="Success">Whether commissioning succeeded.</param>
/// <param name="NodeId">The assigned node identifier.</param>
/// <param name="ThreadIp">The Thread network IP address.</param>
/// <param name="Error">Error message if commissioning failed.</param>
public record CommissioningResult(
    bool Success,
    string? NodeId,
    string? ThreadIp,
    string? Error
);

/// <summary>Progress update during Matter commissioning.</summary>
/// <param name="Step">The current commissioning step.</param>
/// <param name="Percent">Completion percentage (0-100).</param>
/// <param name="Message">Human-readable progress message.</param>
public record CommissioningProgress(
    string Step,
    int Percent,
    string Message);
