using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreoToolkit.Agent.Client;

/// <summary>JSONL wire 编解码:序列化/反序列化 + 防御性校验。</summary>
public static class AgentWireCodec
{
    /// <summary>单行最大字节数(1 MB)。</summary>
    public const int MaxLineBytes = 1 * 1024 * 1024;

    /// <summary>当前协议版本。</summary>
    public const int ProtocolVersion = 1;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        // 深嵌套防御:wire 结构最深仅 args 内 handle 对象一层,8 层富余且防栈耗尽
        MaxDepth = 8,
    };

    /// <summary>解析一行 JSONL 为请求;返回 null 表示解析失败(调用方应回错误响应)。
    /// 失败原因通过 <paramref name="error"/> 输出。</summary>
    public static AgentWireRequest? ParseRequest(string line, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "空行";
            return null;
        }

        AgentWireRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<AgentWireRequest>(line, DeserializeOptions);
        }
        catch (JsonException ex)
        {
            error = $"JSON 解析失败: {ex.Message}";
            return null;
        }

        if (req is null)
        {
            error = "反序列化结果为 null";
            return null;
        }

        if (string.IsNullOrWhiteSpace(req.Id))
        {
            error = "缺少 id 字段";
            return null;
        }

        if (string.IsNullOrWhiteSpace(req.Verb))
        {
            error = "缺少 verb 字段";
            return null;
        }

        return req;
    }

    /// <summary>序列化响应为单行 JSON。</summary>
    public static string Serialize(AgentWireResponse response)
    {
        ThrowUtil.IfNull(response);
        return JsonSerializer.Serialize(response, SerializeOptions);
    }

    /// <summary>序列化请求为单行 JSON(客户端侧)。</summary>
    public static string Serialize(AgentWireRequest request)
    {
        ThrowUtil.IfNull(request);
        return JsonSerializer.Serialize(request, SerializeOptions);
    }

    /// <summary>解析一行 JSONL 为响应(客户端侧)。</summary>
    public static AgentWireResponse? ParseResponse(string line, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "空行";
            return null;
        }

        AgentWireResponse? resp;
        try
        {
            resp = JsonSerializer.Deserialize<AgentWireResponse>(line, DeserializeOptions);
        }
        catch (JsonException ex)
        {
            error = $"JSON 解析失败: {ex.Message}";
            return null;
        }

        if (resp is null)
        {
            error = "反序列化结果为 null";
            return null;
        }

        return resp;
    }

    /// <summary>将 wire args 中的 JsonElement 值转为 CLR 基元以兼容 AgentArgs 取值。</summary>
    public static Dictionary<string, object?> NormalizeArgs(Dictionary<string, object?>? wireArgs)
    {
        if (wireArgs is null || wireArgs.Count == 0)
            return new Dictionary<string, object?>(0, StringComparer.Ordinal);

        var result = new Dictionary<string, object?>(wireArgs.Count, StringComparer.Ordinal);
        foreach (var pair in wireArgs)
        {
            result[pair.Key] = pair.Value is JsonElement je ? ConvertJsonElement(je) : pair.Value;
        }
        return result;
    }

    /// <summary>JsonElement 转 CLR 基元;保留复杂结构为 JsonElement 供 AgentArgs 直接消费。</summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var l) && l is >= int.MinValue and <= int.MaxValue
                => (int)l,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            // 数组/对象保留为 JsonElement,AgentArgs 已支持
            _ => element,
        };
    }

    /// <summary>构建协议版本不匹配错误响应。</summary>
    public static AgentWireResponse VersionMismatchResponse(string id, int receivedVersion)
        => new()
        {
            Version = ProtocolVersion,
            Id = id,
            Ok = false,
            Error = "protocol.version-mismatch",
            Reason = $"服务端协议版本 {ProtocolVersion},收到版本 {receivedVersion}",
        };

    /// <summary>构建错误响应。</summary>
    public static AgentWireResponse ErrorResponse(string id, string error, string reason)
        => new()
        {
            Version = ProtocolVersion,
            Id = id,
            Ok = false,
            Error = error,
            Reason = reason,
        };

    /// <summary>构建成功响应。</summary>
    public static AgentWireResponse OkResponse(string id, Dictionary<string, object?>? data)
        => new()
        {
            Version = ProtocolVersion,
            Id = id,
            Ok = true,
            Data = data,
        };
}
