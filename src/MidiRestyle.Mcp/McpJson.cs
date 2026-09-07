using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MidiRestyle.Mcp;

/// <summary>
/// The one serializer configuration for everything the server sends or binds: tool arguments, result
/// payloads and the contract golden test. camelCase throughout, because the SDK binds arguments by C#
/// parameter name and applies no naming policy to them - so result JSON follows the same convention
/// rather than mixing two. Enums are strings; nulls are omitted.
/// </summary>
public static class McpJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
