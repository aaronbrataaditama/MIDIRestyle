using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MidiRestyle.Mcp;

/// <summary>
/// The one prompt, plus the orientation sent on every initialize. The server cannot hold a
/// conversation; the agent does. What the server can supply is the knowledge (via the tools) and the
/// script: which questions to ask, one at a time, and how each answer maps onto a list_scales filter.
/// The same script is summarised in the instructions because Claude Code surfaces prompts as slash
/// commands and an agent may never invoke one.
/// </summary>
[McpServerPromptType]
public sealed class StylePrompts
{
    /// <remarks>
    /// Budget: 1536 bytes of UTF-8, asserted by test. Trim wording, never tool names - an agent that
    /// has not been told a tool exists will not call it, and the instructions are the only orientation
    /// a client is guaranteed to read.
    /// </remarks>
    public const string ServerInstructions =
        "MIDIRestyle re-maps a MIDI file's scale into one of ~170 world scales (maqamat, gamelan, " +
        "Carnatic melakarta, church modes...). Pitch only: rhythm, articulation and drums never change. " +
        "Microtonal targets are exported with pitch bend on extra channels, exactly as the desktop app does.\n\n" +
        "Typical flow: inspect_midi (tracks, detected key) -> list_scales with filters -> describe_scale " +
        "on two or three candidates -> restyle_midi (or export_musicxml when the scale is notatable and " +
        "the user wants a score). All paths must be absolute. Omitted tonic/source values come from key " +
        "detection; read the 'resolved' block in the report to see what was used.\n\n" +
        "Helping a user choose: ask one question at a time - which musical world (region/tradition, or " +
        "'surprise me'); brighter or darker (third: major/minor/neutral); must it play on ordinary 12-TET " +
        "instruments (fitsTwelveTet=true, or a maxDeviationCents they accept); do they need sheet music " +
        "(notatable=true); how far from the original (degreeCount near the source's 7). Explain a raised " +
        "tolerance or a muted track in the report - they are budget decisions, not faults.";

    [McpServerPrompt(Name = "choose_a_style")]
    [Description("Guide the user to a target scale for a MIDI file with a few questions, then restyle it and explain the result.")]
    public static string ChooseAStyle(
        [Description("Absolute path to the MIDI file, if already known.")] string? midiPath = null)
    {
        // Both branches must name inspect_midi. The cold-start branch - the plain /choose_a_style slash
        // command, which is how most agents will arrive - used to say only "ask for the path", so the
        // agent skipped inspection entirely and question 5 below leaned on a detected key it had never
        // fetched.
        string target = string.IsNullOrWhiteSpace(midiPath) ? "that path" : $"\"{midiPath}\"";
        string opening = string.IsNullOrWhiteSpace(midiPath)
            ? "Ask the user for the absolute path of the MIDI file first, and do not guess it."
            : "You already have the path.";

        return $"""
            You are helping someone restyle a MIDI file with MIDIRestyle. {opening}

            Then call inspect_midi on {target} and tell the user, briefly, what is in it: instruments, how many
            track-channels, the detected key and how confident the detection is (the margin). Question 5 below
            depends on that detected key, so do not skip this.

            Then ask these questions ONE AT A TIME, waiting for each answer:
            1. Which musical world appeals - a region or tradition (Arabic maqam, Javanese gamelan, Carnatic, Persian dastgah,
               Turkish makam, West African, Western modes...) - or "surprise me"? -> list_scales region/tradition/query.
            2. Brighter, darker, or somewhere in between? -> third = major / minor / neutral.
            3. Must it play on ordinary 12-TET instruments (piano, guitar, most VSTs)? -> fitsTwelveTet=true, or ask what
               deviation in cents they would accept and pass maxDeviationCents. Otherwise leave both unset.
            4. Do they need sheet music? -> notatable=true (only scales the staff speller can write).
            5. How far from the original? Close: degreeCount equal to the source's (7 for a major/minor piece). Bold: 5 or 6.

            Call list_scales with those filters. Pick three candidates and call describe_scale on each. Present them with:
            the scale's name and tradition, its source citation, what its deviation from 12-TET will sound like (quarter-tones,
            neutral thirds, equal steps), and how many pitch-bend channels it costs. Let the user choose.

            Then call restyle_midi (or export_musicxml if they asked for a score and the scale is notatable). Explain the report:
            the 'resolved' block (which tonic and source scale were used, and whether key detection chose them), the tally
            (dropped or merged notes), and the channel report - if the tolerance was raised or a track was muted, say that
            the channel budget forced it and offer to exclude a track or accept the raised tolerance. Name the output path.
            """;
    }
}
