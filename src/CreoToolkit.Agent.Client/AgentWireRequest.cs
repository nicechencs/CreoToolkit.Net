using System.Text.Json.Serialization;

namespace CreoToolkit.Agent.Client;

/// <summary>JSONL wire 请求:一行一条。</summary>
public sealed class AgentWireRequest
{
    [JsonPropertyName("v")]
    public int Version { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("verb")]
    public string Verb { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public Dictionary<string, object?>? Args { get; set; }

    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; set; }
}
