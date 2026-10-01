using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrafficEvents.Core;

/// <summary>One JSON configuration for HTTP bodies and message payloads.</summary>
public static class TrafficJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
