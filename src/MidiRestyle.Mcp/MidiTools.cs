using System.ComponentModel;
using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MidiRestyle.Mcp;

/// <summary>
/// The file tools. One instance per process; holds the immutable library, the path probe and a
/// resolver, nothing per request. Every call loads its own file.
/// </summary>
/// <remarks>
/// Everything an agent sends is untrusted input, so no path reaches the filesystem before
/// <see cref="OutputPathPolicy.ValidateInputPath"/> has passed it, and every failure - a refused path,
/// a missing file, a file that is not MIDI at all - leaves as a <see cref="ToolResults.Error"/> in our
/// own words. Nothing throws out of a tool method: an unhandled exception would take down the JSON-RPC
/// session for every later call, not just the one that caused it.
/// </remarks>
[McpServerToolType]
public sealed class MidiTools(ScaleLibrary library, PathProbe probe)
{
    // Both are per-process, immutable and shared by concurrent calls. inspect_midi needs neither -
    // it reads a file and reports it - but the write tools that join this type next resolve an
    // agent's request against the library and check every output path against these locations, and
    // the resolution is deliberately built once rather than per call. The probe is read lazily
    // because ResolveWritableRoot touches the filesystem, which server construction should not.
    private readonly RestyleRequestResolver _resolver = new(library);
    private readonly Lazy<ProtectedLocations> _protected = new(() => ProtectedLocations.FromProcess(probe));

    [McpServerTool(Name = "inspect_midi", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Read a MIDI file without changing it: format, tempo, time signatures, one row per track-channel " +
                 "(the unit restyle_midi's exclude parameter addresses), and key detection with the source scale it implies. " +
                 "Call this before restyle_midi to choose a tonic and decide what to exclude.")]
    public CallToolResult InspectMidi(
        [Description("Absolute path to the .mid file.")] string path)
    {
        if (OutputPathPolicy.ValidateInputPath(path) is { } pathError)
        {
            return ToolResults.Error(pathError);
        }

        MidiProject? project;
        string? loadError;
        try
        {
            if (!MidiFileLoader.TryLoad(path, out project, out loadError) || project is null)
            {
                return ToolResults.Error($"Could not load '{path}': {loadError}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // TryLoad translates every failure the loader anticipates; this is the backstop for the
            // ones it does not - a path the OS rejects only on open, a file whose handle is revoked
            // mid-read. An agent's bad argument must never become an unhandled exception.
            return ToolResults.Error($"Could not load '{path}': {ex.Message}");
        }

        return ToolResults.Ok(Inspect(path, project));
    }

    private static MidiInspection Inspect(string path, MidiProject project)
    {
        KeyDetectionResult detection = KeyDetector.Detect(project);
        KeyEstimate? chosen = RestyleDefaults.ChosenCandidate(detection);

        return new MidiInspection(
            path,
            EnumNames.Echo(project.Format),
            project.Division.Describe(),
            project.DurationTicks,
            project.DurationSeconds is { } seconds ? ScaleDescriptors.Round(seconds) : null,
            project.Title,
            InitialTempoBpm(project),
            [.. project.TimeSignatures.Select(t => new TimeSignatureInfo(t.Ticks, t.Numerator, t.Denominator))],
            [.. project.Tracks.Select(Summarise)],
            project.TotalNoteCount,
            project.RestylableTracks.Sum(t => t.NoteCount),
            new KeyReport(
                EnumNames.Echo(detection.Outcome),
                [.. detection.Candidates.Select(c => new KeyCandidate(
                    c.TonicName,
                    c.IsMinor ? "minor" : "major",
                    Math.Round(c.R, 4, MidpointRounding.AwayFromZero),
                    Math.Round(c.Margin, 4, MidpointRounding.AwayFromZero)))],
                detection.IsAmbiguous,
                chosen is null ? null : RestyleDefaults.SourceScaleIdFor(chosen)));
    }

    private static TrackSummary Summarise(TrackInfo track) => new(
        track.TrackIndex, track.Channel, track.Name,
        track.InstrumentName ?? (track.ProgramNumber is { } program ? GeneralMidi.NameFor(program, track.Channel) : null),
        track.NoteCount, track.IsDrums, track.IsRestylable, track.HasExistingPitchBend,
        track.LowestPitch?.MidiNote, track.HighestPitch?.MidiNote);

    /// <summary>
    /// The tempo in force at the start: the earliest change by tick, which is <c>TempoMap[0]</c>
    /// because <c>MidiFileLoader</c> sorts the map by tick after reading every chunk - the events
    /// themselves arrive in chunk order, so a Format 1 file can carry its opening tempo in its second
    /// track. Null for a file with no tempo event at all, rather than asserting the MIDI default of
    /// 120: an agent that needs to know the file said nothing can then see that it said nothing.
    /// </summary>
    private static double? InitialTempoBpm(MidiProject project) =>
        project.TempoMap.Count == 0 ? null : ScaleDescriptors.Round(project.TempoMap[0].BeatsPerMinute);
}
