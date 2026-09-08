using ModelContextProtocol.Server;

namespace MidiRestyle.Mcp;

/// <summary>
/// The server's prompts and its standing instructions to a client. Placeholder: only
/// <see cref="ServerInstructions"/> exists so far, and the prompts themselves land in Task 17.
/// </summary>
[McpServerPromptType]
public sealed class StylePrompts
{
    public const string ServerInstructions =
        "MIDIRestyle re-maps a MIDI file's scale into another (Maqam Rast, Slendro, ...). Placeholder - completed in Task 17.";
}
