using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jaravi.Core;

/// <summary>
/// The single JSON dialect of the ecosystem — server, WebSocket telemetry and
/// Dashboard all serialize with these options so the wire contract never drifts.
/// </summary>
public static class JaraviJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
