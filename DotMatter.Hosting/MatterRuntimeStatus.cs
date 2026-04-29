namespace DotMatter.Hosting;

/// <summary>
/// Shared runtime state used by health/readiness endpoints and operational diagnostics.
/// </summary>
public sealed class MatterRuntimeStatus
{
    private readonly Lock _lock = new();

    /// <summary>Gets a value indicating whether startup has completed.</summary>
    public bool StartupCompleted { get; private set; }
    /// <summary>Gets a value indicating whether the runtime is ready to serve traffic.</summary>
    public bool IsReady { get; private set; }
    /// <summary>Gets a value indicating whether the runtime is stopping.</summary>
    public bool IsStopping { get; private set; }
    /// <summary>Gets the time the current startup attempt began.</summary>
    public DateTime StartedAtUtc { get; private set; } = DateTime.UtcNow;
    /// <summary>Gets the most recent startup failure message, if any.</summary>
    public string? LastStartupError { get; private set; }

    /// <summary>Marks the runtime as starting.</summary>
    public void MarkStarting()
    {
        lock (_lock)
        {
            StartupCompleted = false;
            IsReady = false;
            IsStopping = false;
            LastStartupError = null;
            StartedAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Marks the runtime as ready.</summary>
    public void MarkReady()
    {
        lock (_lock)
        {
            StartupCompleted = true;
            IsReady = true;
            LastStartupError = null;
        }
    }

    /// <summary>Marks startup as failed.</summary>
    public void MarkStartupFailed(string message)
    {
        lock (_lock)
        {
            StartupCompleted = true;
            IsReady = false;
            LastStartupError = message;
        }
    }

    /// <summary>Marks the runtime as stopping.</summary>
    public void MarkStopping()
    {
        lock (_lock)
        {
            IsStopping = true;
            IsReady = false;
        }
    }
}
