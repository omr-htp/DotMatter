using Microsoft.Extensions.Logging;

namespace DotMatter.Core;

/// <summary>
/// Centralized logging for Matter.Core.
/// Initialize with MatterLog.Init(loggerFactory) at startup.
/// </summary>
public static class MatterLog
{
    private static ILoggerFactory? _factory;
    private static ILogger? _default;
    private static MatterLogSettings _settings = new();

    /// <summary>
    /// Gets or sets the global log formatting settings for DotMatter.Core.
    /// </summary>
    public static MatterLogSettings Settings
    {
        get => _settings;
        set => _settings = value ?? new MatterLogSettings();
    }

    /// <summary>Initializes the global logger factory.</summary>
    /// <param name="factory">The logger factory to use.</param>
    public static void Init(ILoggerFactory factory)
    {
        _factory = factory;
        _default = CreateLogger(() => factory.CreateLogger("Matter"));
    }

    /// <summary>Creates a logger for the specified type.</summary>
    /// <typeparam name="T">The type to create a logger for.</typeparam>
    public static ILogger For<T>() => CreateLogger(() => _factory?.CreateLogger<T>());
    /// <summary>Creates a logger for the specified category.</summary>
    /// <param name="category">The log category name.</param>
    public static ILogger For(string category) => CreateLogger(() => _factory?.CreateLogger(category));

    /// <summary>Logs a debug message.</summary>
    public static void Debug(string msg, params object?[] args) => LogSafely(log => log.LogDebug(msg, args));
    /// <summary>Logs an information message.</summary>
    public static void Info(string msg, params object?[] args) => LogSafely(log => log.LogInformation(msg, args));
    /// <summary>Logs a warning message.</summary>
    public static void Warn(string msg, params object?[] args) => LogSafely(log => log.LogWarning(msg, args));
    /// <summary>Logs a warning with exception details.</summary>
    public static void Warn(Exception ex, string msg, params object?[] args) => LogSafely(log => log.LogWarning(ex, msg, args));
    /// <summary>Logs an error message.</summary>
    public static void Error(string msg, params object?[] args) => LogSafely(log => log.LogError(msg, args));
    /// <summary>Logs an error with exception.</summary>
    public static void Error(Exception ex, string msg, params object?[] args) => LogSafely(log => log.LogError(ex, msg, args));

    /// <summary>
    /// Formats non-sensitive bytes for logs, truncating long payloads by default.
    /// </summary>
    public static string FormatBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return "<empty>";
        }

        var maxBytes = Math.Max(1, _settings.MaxRenderedBytes);
        if (bytes.Length <= maxBytes)
        {
            return Convert.ToHexString(bytes);
        }

        return $"{Convert.ToHexString(bytes[..maxBytes])}...(+{bytes.Length - maxBytes} bytes)";
    }

    /// <summary>
    /// Formats sensitive bytes for logs and redacts them unless sensitive diagnostics are explicitly enabled.
    /// </summary>
    public static string FormatSecret(ReadOnlySpan<byte> bytes)
    {
        if (!_settings.EnableSensitiveDiagnostics)
        {
            return $"<redacted:{bytes.Length}-bytes>";
        }

        return FormatBytes(bytes);
    }

    private static ILogger CreateLogger(Func<ILogger?> factory)
    {
        try
        {
            return factory() ?? NullLogger.Instance;
        }
        catch (ObjectDisposedException)
        {
            return NullLogger.Instance;
        }
    }

    private static void LogSafely(Action<ILogger> writeLog)
    {
        var logger = _default;
        if (logger is null)
        {
            return;
        }

        try
        {
            writeLog(logger);
        }
        catch (ObjectDisposedException)
        {
            _default = NullLogger.Instance;
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is ObjectDisposedException))
        {
            _default = NullLogger.Instance;
        }
    }

    private sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
