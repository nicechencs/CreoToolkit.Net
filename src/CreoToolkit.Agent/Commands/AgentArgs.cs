using System.Globalization;
using System.Text.Json;
using CreoToolkit.Agent.Handles;
using CreoToolkit.Sdk.Errors;

namespace CreoToolkit.Agent.Commands;

internal static class AgentArgs
{
    internal static string? GetString(AgentCommand cmd, string key, bool required)
    {
        var value = GetValue(cmd, key, required);
        if (value is null)
            return null;

        try
        {
            if (value is JsonElement json)
            {
                return json.ValueKind switch
                {
                    JsonValueKind.String => json.GetString(),
                    JsonValueKind.Null when !required => null,
                    _ => throw BadInput(key, $"expected string, got JSON {json.ValueKind}")
                };
            }

            return value is string text
                ? text
                : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (IsConversionError(ex))
        {
            throw BadInput(key, ex.Message);
        }
    }

    internal static int GetInt(AgentCommand cmd, string key)
    {
        var value = GetRequiredValue(cmd, key);
        try
        {
            if (value is JsonElement json)
            {
                return json.ValueKind switch
                {
                    JsonValueKind.Number => json.GetInt32(),
                    JsonValueKind.String => Convert.ToInt32(json.GetString(), CultureInfo.InvariantCulture),
                    _ => throw BadInput(key, $"expected int, got JSON {json.ValueKind}")
                };
            }

            return value switch
            {
                int i => i,
                long l when l is <= int.MaxValue and >= int.MinValue => (int)l,
                long => throw BadInput(key, "int overflow"),
                _ => Convert.ToInt32(value, CultureInfo.InvariantCulture)
            };
        }
        catch (Exception ex) when (IsConversionError(ex))
        {
            throw BadInput(key, ex.Message);
        }
    }

    internal static long GetLong(AgentCommand cmd, string key)
    {
        var value = GetRequiredValue(cmd, key);
        try
        {
            if (value is JsonElement json)
            {
                return json.ValueKind switch
                {
                    JsonValueKind.Number => json.GetInt64(),
                    JsonValueKind.String => Convert.ToInt64(json.GetString(), CultureInfo.InvariantCulture),
                    _ => throw BadInput(key, $"expected long, got JSON {json.ValueKind}")
                };
            }

            return value is long l
                ? l
                : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (IsConversionError(ex))
        {
            throw BadInput(key, ex.Message);
        }
    }

    internal static double GetDouble(AgentCommand cmd, string key)
    {
        var value = GetRequiredValue(cmd, key);
        try
        {
            var result = value switch
            {
                JsonElement json => json.ValueKind switch
                {
                    JsonValueKind.Number => json.GetDouble(),
                    JsonValueKind.String => Convert.ToDouble(json.GetString(), CultureInfo.InvariantCulture),
                    _ => throw BadInput(key, $"expected double, got JSON {json.ValueKind}")
                },
                double d => d,
                _ => Convert.ToDouble(value, CultureInfo.InvariantCulture)
            };

            if (double.IsNaN(result) || double.IsInfinity(result))
                throw BadInput(key, "double cannot be NaN or Infinity");

            return result;
        }
        catch (Exception ex) when (IsConversionError(ex))
        {
            throw BadInput(key, ex.Message);
        }
    }

    internal static bool GetBool(AgentCommand cmd, string key)
    {
        var value = GetRequiredValue(cmd, key);
        try
        {
            if (value is JsonElement json)
            {
                return json.ValueKind switch
                {
                    JsonValueKind.True => json.GetBoolean(),
                    JsonValueKind.False => json.GetBoolean(),
                    JsonValueKind.String => ParseBoolString(json.GetString(), key),
                    _ => throw BadInput(key, $"expected bool, got JSON {json.ValueKind}")
                };
            }

            return value switch
            {
                bool b => b,
                string text => ParseBoolString(text, key),
                _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture)
            };
        }
        catch (Exception ex) when (IsConversionError(ex))
        {
            throw BadInput(key, ex.Message);
        }
    }

    internal static JsonElement? GetJsonElement(AgentCommand cmd, string key, bool required)
    {
        var value = GetValue(cmd, key, required);
        if (value is null)
            return null;

        return value is JsonElement json
            ? json
            : throw BadInput(key, $"expected JSON element, got {value.GetType().Name}");
    }

    internal static IReadOnlyList<string>? GetStringArray(AgentCommand cmd, string key, bool required)
    {
        var value = GetValue(cmd, key, required);
        if (value is null)
            return null;

        if (value is IReadOnlyList<string> strings)
            return strings;

        if (value is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Null && !required)
                return null;

            if (json.ValueKind != JsonValueKind.Array)
                throw BadInput(key, $"expected string array, got JSON {json.ValueKind}");

            var result = new List<string>();
            foreach (var item in json.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw BadInput(key, $"expected string array item, got JSON {item.ValueKind}");

                result.Add(item.GetString()!);
            }

            return result;
        }

        throw BadInput(key, $"expected string array, got {value.GetType().Name}");
    }

    internal static int? GetIntOrNull(AgentCommand cmd, string key)
    {
        var value = GetValue(cmd, key, required: false);
        return value is null ? null : GetInt(cmd, key);
    }

    internal static HandleId? GetHandleId(AgentCommand cmd, string key, bool required)
    {
        var value = GetValue(cmd, key, required);
        if (value is null)
            return null;

        if (value is HandleId handleId)
            return handleId;

        try
        {
            if (value is JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.Null && !required)
                    return null;

                if (json.ValueKind != JsonValueKind.Object)
                    throw BadInput(key, $"expected handle object, got JSON {json.ValueKind}");

                var id = GetRequiredProperty(json, key, "id").GetInt64();
                var kind = GetRequiredProperty(json, key, "kind").GetString()
                    ?? throw BadInput(key, "field 'kind' cannot be null");
                var sessionEpoch = GetRequiredProperty(json, key, "sessionEpoch").GetInt32();
                var registryId = GetRequiredProperty(json, key, "registryId").GetGuid();
                var version = GetRequiredProperty(json, key, "version").GetInt32();
                var mac = GetOptionalString(json, key, "mac");
                var ttlExpiresAtUnixMs = GetOptionalInt64(json, key, "ttlExpiresAtUnixMs");

                return new HandleId(id, kind, sessionEpoch, registryId, version, mac, ttlExpiresAtUnixMs);
            }
        }
        catch (Exception ex) when (IsConversionError(ex))
        {
            throw BadInput(key, ex.Message);
        }

        throw BadInput(key, $"expected handle object, got {value.GetType().Name}");
    }

    private static object? GetRequiredValue(AgentCommand cmd, string key)
        => GetValue(cmd, key, required: true);

    private static object? GetValue(AgentCommand cmd, string key, bool required)
    {
        ThrowUtil.IfNull(cmd);
        ThrowUtil.IfNullOrWhiteSpace(key);

        if (!cmd.Args.TryGetValue(key, out var value) || value is null)
        {
            if (required)
                throw BadInput(key, "missing required argument");

            return null;
        }

        return value;
    }

    private static bool ParseBoolString(string? text, string key)
        => text switch
        {
            "true" => true,
            "false" => false,
            _ => throw BadInput(key, "expected exact 'true' or 'false'")
        };

    private static JsonElement GetRequiredProperty(JsonElement json, string key, string field)
    {
        if (!json.TryGetProperty(field, out var property))
            throw BadInput(key, $"missing field '{field}'");

        return property;
    }

    private static string? GetOptionalString(JsonElement json, string key, string field)
    {
        if (!json.TryGetProperty(field, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : throw BadInput(key, $"field '{field}' expected string or null");
    }

    private static long? GetOptionalInt64(JsonElement json, string key, string field)
    {
        if (!json.TryGetProperty(field, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;

        return property.ValueKind == JsonValueKind.Number
            ? property.GetInt64()
            : throw BadInput(key, $"field '{field}' expected number or null");
    }

    private static CreoException BadInput(string key, string detail)
        => new("Agent.Args", ProError.BadInputs, $"argument '{key}' missing or invalid: {detail}");

    private static bool IsConversionError(Exception ex)
        => ex is FormatException
            or InvalidCastException
            or InvalidOperationException
            or OverflowException;
}