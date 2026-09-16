using System.IO;
using System.Runtime.CompilerServices;
using global::Serilog;
using global::Serilog.Events;

namespace CreoToolkit.Interop.Diagnostics;

/// <summary>
/// Product Serilog entrypoint backed by the managed Creo logging configuration.
/// New product code should prefer this over direct CreoLog.Info/Warn/Error calls.
/// </summary>
public static class CreoSerilog
{
    public static ILogger ForContext(string module, CreoLogLayer layer = CreoLogLayer.App)
    {
        if (string.IsNullOrWhiteSpace(module))
            throw new ArgumentException("module is required", nameof(module));

        return CreoLog.CreateSerilogLogger(module, layer);
    }

    public static IDisposable Scope(string corr) => CreoLog.Scope(corr);

    public static void Information(
        ILogger logger,
        string messageTemplate,
        object?[]? propertyValues = null,
        string? @event = null,
        object? props = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => Write(CreoLogLevel.Info, LogEventLevel.Information, logger, exception: null,
            messageTemplate, propertyValues, @event, props, file, line);

    public static void Verbose(
        ILogger logger,
        string messageTemplate,
        object?[]? propertyValues = null,
        string? @event = null,
        object? props = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => Write(CreoLogLevel.Trace, LogEventLevel.Verbose, logger, exception: null,
            messageTemplate, propertyValues, @event, props, file, line);

    public static void Warning(
        ILogger logger,
        string messageTemplate,
        object?[]? propertyValues = null,
        string? @event = null,
        object? props = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => Write(CreoLogLevel.Warn, LogEventLevel.Warning, logger, exception: null,
            messageTemplate, propertyValues, @event, props, file, line);

    public static void Error(
        ILogger logger,
        string messageTemplate,
        object?[]? propertyValues = null,
        string? @event = null,
        object? props = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => Write(CreoLogLevel.Error, LogEventLevel.Error, logger, exception: null,
            messageTemplate, propertyValues, @event, props, file, line);

    public static void Error(
        ILogger logger,
        Exception exception,
        string messageTemplate,
        object?[]? propertyValues = null,
        string? @event = null,
        object? props = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => Write(CreoLogLevel.Error, LogEventLevel.Error, logger, exception,
            messageTemplate, propertyValues, @event, props, file, line);

    private static void Write(
        CreoLogLevel creoLevel,
        LogEventLevel serilogLevel,
        ILogger logger,
        Exception? exception,
        string messageTemplate,
        object?[]? propertyValues,
        string? @event,
        object? props,
        string file,
        int line)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        if (!CreoLog.IsEnabledFor(creoLevel))
            return;

        var enriched = logger.ForContext("event", @event);
        var loc = FormatLoc(file, line);
        if (loc is not null)
            enriched = enriched.ForContext("loc", loc);

        if (props is not null)
            enriched = enriched.ForContext("props", props, destructureObjects: true);

        var values = propertyValues ?? Array.Empty<object?>();
        if (exception is not null)
            enriched.Write(serilogLevel, exception, messageTemplate, values);
        else
            enriched.Write(serilogLevel, messageTemplate, values);
    }

    private static string? FormatLoc(string file, int line)
    {
        return string.IsNullOrEmpty(file)
            ? null
            : $"{Path.GetFileName(file)}:{line}";
    }
}
