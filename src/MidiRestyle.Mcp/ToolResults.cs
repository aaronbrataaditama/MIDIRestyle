using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace MidiRestyle.Mcp;

/// <summary>
/// Every tool returns a <see cref="CallToolResult"/> built here, so success and failure have one shape:
/// success is a single text block holding the payload as compact JSON; failure is <c>IsError</c> with a
/// message in our words (an agent fixes what it can read).
/// </summary>
public static class ToolResults
{
    public static CallToolResult Ok(object payload) => new()
    {
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, payload.GetType(), McpJson.Options) }],
    };

    public static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }],
    };

    /// <summary>The single text block's text - the payload JSON or the error message.</summary>
    public static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;
}
