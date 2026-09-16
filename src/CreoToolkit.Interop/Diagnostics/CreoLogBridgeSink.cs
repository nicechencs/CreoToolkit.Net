using System.Collections.Generic;
using System.Globalization;
using global::Serilog.Core;
using global::Serilog.Events;
using global::Serilog.Parsing;

namespace CreoToolkit.Interop.Diagnostics;

internal sealed class CreoLogBridgeSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null)
            return;

        CreoLog.WriteRaw(
            MapLevel(logEvent.Level),
            logEvent.MessageTemplate.Render(logEvent.Properties, CultureInfo.InvariantCulture),
            @event: TryGetString(logEvent, "event"),
            module: TryGetString(logEvent, "module"),
            props: ExtractProps(logEvent),
            layer: ParseLayer(TryGetString(logEvent, "layer")),
            err: logEvent.Exception,
            file: string.Empty,
            line: 0,
            loc: TryGetString(logEvent, "loc"),
            corr: TryGetString(logEvent, "corr"));
    }

    private static string? TryGetString(LogEvent ev, string key)
    {
        if (!ev.Properties.TryGetValue(key, out var value))
            return null;

        return value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;
    }

    private static CreoLogLayer ParseLayer(string? name) => name switch
    {
        "L1" => CreoLogLayer.L1,
        "L2" => CreoLogLayer.L2,
        "L3" => CreoLogLayer.L3,
        "L4" => CreoLogLayer.L4,
        "app" or "App" => CreoLogLayer.App,
        _ => CreoLogLayer.L2,
    };

    private static CreoLogLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose or LogEventLevel.Debug => CreoLogLevel.Trace,
        LogEventLevel.Information => CreoLogLevel.Info,
        LogEventLevel.Warning => CreoLogLevel.Warn,
        LogEventLevel.Error or LogEventLevel.Fatal => CreoLogLevel.Error,
        _ => CreoLogLevel.Info,
    };

    private static object? ExtractProps(LogEvent ev)
    {
        Dictionary<string, object?>? result = null;
        if (ev.Properties.TryGetValue("props", out var value)
            && ConvertValue(value) is { } convertedProps)
        {
            if (convertedProps is Dictionary<string, object?> props)
                result = props;
            else
                result = new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = convertedProps };
        }

        var templateProperties = GetTemplatePropertyNames(ev);
        foreach (var property in ev.Properties)
        {
            if (IsReservedProperty(property.Key) || templateProperties.Contains(property.Key))
                continue;

            result ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            result[property.Key] = ConvertValue(property.Value);
        }

        return result;
    }

    private static bool IsReservedProperty(string key)
        => key is "module" or "event" or "props" or "layer" or "loc" or "corr" or "tid";

    private static HashSet<string> GetTemplatePropertyNames(LogEvent ev)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in ev.MessageTemplate.Tokens)
        {
            if (token is PropertyToken property)
                names.Add(property.PropertyName);
        }

        return names;
    }

    private static object? ConvertValue(LogEventPropertyValue value)
    {
        return value switch
        {
            ScalarValue scalar => scalar.Value,
            StructureValue structure => ConvertStructure(structure),
            DictionaryValue dictionary => ConvertDictionary(dictionary),
            SequenceValue sequence => ConvertSequence(sequence),
            _ => value.ToString(),
        };
    }

    private static Dictionary<string, object?> ConvertStructure(StructureValue structure)
    {
        var result = new Dictionary<string, object?>(structure.Properties.Count, StringComparer.Ordinal);
        foreach (var property in structure.Properties)
            result[property.Name] = ConvertValue(property.Value);
        return result;
    }

    private static Dictionary<string, object?> ConvertDictionary(DictionaryValue dictionary)
    {
        var result = new Dictionary<string, object?>(dictionary.Elements.Count, StringComparer.Ordinal);
        foreach (var pair in dictionary.Elements)
        {
            var key = pair.Key.Value?.ToString();
            if (!string.IsNullOrEmpty(key))
                result[key!] = ConvertValue(pair.Value);
        }

        return result;
    }

    private static List<object?> ConvertSequence(SequenceValue sequence)
    {
        var result = new List<object?>(sequence.Elements.Count);
        foreach (var item in sequence.Elements)
            result.Add(ConvertValue(item));
        return result;
    }
}
