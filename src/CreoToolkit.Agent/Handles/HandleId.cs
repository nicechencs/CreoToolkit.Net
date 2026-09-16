using System.Text.Json.Serialization;

namespace CreoToolkit.Agent.Handles;

public readonly record struct HandleId(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("sessionEpoch")] int SessionEpoch,
    [property: JsonPropertyName("registryId")] Guid RegistryId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("mac")] string? Mac,
    [property: JsonPropertyName("ttlExpiresAtUnixMs")] long? TtlExpiresAtUnixMs);
