using System.ComponentModel;
using System.Text;
using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MidiRestyle.Mcp;

/// <summary>
/// The file tools. One instance per process; holds the immutable library, the path probe and a
/// resolver, nothing per request. Every call loads its own file.
/// </summary>
/// <remarks>
/// Everything an agent sends is untrusted input, so no path reaches the filesystem before
/// <see cref="OutputPathPolicy.ValidateInputPath"/> has passed it, no output path is written before
/// <see cref="OutputPathPolicy.ValidateOutputPath"/> has passed it - in that order, so a refused
/// destination is never touched at all - and every failure, a refused path, a missing file, a file
/// that is not MIDI at all, leaves as a <see cref="ToolResults.Error"/> in our own words. Nothing
/// throws out of a tool method: an unhandled exception would take down the JSON-RPC session for every
/// later call, not just the one that caused it.
/// </remarks>
[McpServerToolType]
public sealed class MidiTools(ScaleLibrary library, PathProbe probe)
{
    // Both are per-process, immutable and shared by concurrent calls. inspect_midi needs neither -
    // it reads a file and reports it - but the write tools resolve an agent's request against the
    // library and check every output path against these locations, and the resolution is deliberately
    // built once rather than per call. The probe is read lazily because ResolveWritableRoot touches
    // the filesystem, which server construction should not.
    private readonly RestyleRequestResolver _resolver = new(library);
    private readonly Lazy<ProtectedLocations> _protected = new(() => ProtectedLocations.FromProcess(probe));

    /// <summary>
    /// MusicXML's own declaration already says UTF-8, and some older readers choke on a BOM - the same
    /// call <c>MusicXmlExporter</c> makes for the file it writes itself. Stated explicitly rather than
    /// relied on: <see cref="Encoding.GetBytes(string)"/> emits no preamble whatever the flag says, so
    /// the guarantee lives in the fact that nothing prepends <c>GetPreamble</c> to these bytes.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [McpServerTool(Name = "inspect_midi", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Read a MIDI file without changing it: format, tempo, time signatures, one row per track-channel " +
                 "(the unit restyle_midi's exclude parameter addresses), and key detection with the source scale it implies. " +
                 "Call this before restyle_midi to choose a tonic and decide what to exclude.")]
    public CallToolResult InspectMidi(
        [Description("Absolute path to the .mid file.")] string inputPath)
    {
        if (OutputPathPolicy.ValidateInputPath(inputPath) is { } pathError)
        {
            return ToolResults.Error(pathError);
        }

        MidiProject? project;
        string? loadError;
        try
        {
            if (!MidiFileLoader.TryLoad(inputPath, out project, out loadError) || project is null)
            {
                return ToolResults.Error($"Could not load '{inputPath}': {loadError}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // TryLoad translates every failure the loader anticipates; this is the backstop for the
            // ones it does not - a path the OS rejects only on open, a file whose handle is revoked
            // mid-read. An agent's bad argument must never become an unhandled exception.
            return ToolResults.Error($"Could not load '{inputPath}': {ex.Message}");
        }

        return ToolResults.Ok(Inspect(inputPath, project));
    }

    [McpServerTool(Name = "restyle_midi", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("Re-map a MIDI file's pitches from its source scale into a target scale and write a new .mid. " +
                 "Microtonal targets use pitch bend on extra channels, exactly as the desktop app exports. " +
                 "Overwrites an existing output only when overwrite is true. Rhythm, articulation and drums are never changed. " +
                 "Omitted tonic/source values are taken from key detection; the report's 'resolved' block says what was used.")]
    public CallToolResult RestyleMidi(
        [Description("Absolute path to the source .mid file.")] string inputPath,
        [Description("Target scale id from list_scales.")] string targetScaleId,
        [Description("Target tonic: note name with optional accidental and octave (D, Eb4, F#3) or MIDI number as a string. Default: detected key's tonic at octave 4.")] string? targetTonic = null,
        [Description("Source scale id. Default: ionian or aeolian per the detected key. Unused under strategy nearestPitch.")] string? sourceScaleId = null,
        [Description("Source tonic, same forms as targetTonic. Default: detected key's tonic.")] string? sourceTonic = null,
        [Description("Track-channels to leave untouched, as {track, channel} pairs from inspect_midi. Drums (channel 9) are always left untouched.")] IReadOnlyList<TrackChannelRef>? exclude = null,
        [Description("scaleDegree (default) maps degree to degree; nearestPitch snaps each note to the nearest target pitch.")] string? strategy = null,
        [Description("What to do with notes outside the source scale: snapToNearestSourceDegree (default), passThrough, drop.")] string? nonScaleNotes = null,
        [Description("When two notes map to one pitch: merge (default) or displaceOctave.")] string? collisions = null,
        [Description("When a mapped note leaves MIDI range: shiftIntoRange (default), foldOctave, drop.")] string? range = null,
        [Description("Pitch-bend clustering tolerance in cents, 0.5..50 (default 5). Raised automatically if the channel budget does not fit; the report says so.")] double? toleranceCents = null,
        [Description("Absolute output path ending in .mid or .midi. Default: beside the input as <name>.<scaleId>.mid.")] string? outputPath = null,
        [Description("Replace an existing output file (default false).")] bool overwrite = false)
    {
        var request = new RestyleRequest(inputPath, targetScaleId, targetTonic, sourceScaleId, sourceTonic, exclude,
            strategy, nonScaleNotes, collisions, range, toleranceCents, outputPath, overwrite);

        // The resolver reports every field problem in one message; it is passed through whole rather
        // than truncated to the first, so a wrong request costs the agent one round trip, not four.
        if (!_resolver.TryResolve(request, out Resolution? resolution, out string? error))
        {
            return ToolResults.Error(error);
        }

        string requested = outputPath ?? OutputPathPolicy.DefaultOutputPath(inputPath, resolution.Resolved.TargetScaleId, ".mid");

        // Order matters and is load-bearing: the path is judged before a single byte is written, so a
        // refused destination is never touched at all - not even by the atomic write's temp file. The
        // canonical form it hands back is what is written and what is reported, so the guard, the
        // write and the agent all name one destination.
        if (OutputPathPolicy.ValidateOutputPath(requested, inputPath, overwrite, OutputPathPolicy.MidiExtensions, _protected.Value, out string output) is { } outputError)
        {
            return ToolResults.Error(outputError);
        }

        RestyleResult result;
        ChannelAllocation allocation;
        byte[] bytes;
        try
        {
            result = RestyleEngine.Restyle(resolution.Project, resolution.Settings);

            // The default ceiling, which is the one playback passes too: preview and file are the same
            // plan by construction. A second ceiling here would be the divergence the design forbids.
            allocation = ChannelAllocator.Allocate(result);

            using var stream = new MemoryStream();
            ExportResult export = MidiFileExporter.Export(result, stream, allocation);
            if (!export.Success)
            {
                return ToolResults.Error($"Export refused ({EnumNames.Echo(export.Reason!.Value)}): {export.Message}");
            }

            bytes = stream.ToArray();
        }
        catch (Exception ex) when (ex is MidiFileExportException or InvalidOperationException or IOException or NotSupportedException)
        {
            // Refusals an agent's own data can cause - a microtonal target, a note out of range - come
            // back as an ExportResult above, not from here. This is the backstop for the rest.
            // Reachable: ChannelAllocator raises InvalidOperationException if the budget and the
            // allocator ever disagree about how many channels were planned. Defensive:
            // MidiFileExportException is declared across the exporter's surface but its
            // stream-plus-allocation overload does not currently raise it, and a MemoryStream does not
            // fail the way a file does. All four are here because a bug in our own pipeline must reach
            // the agent as one failed call, not as a dropped JSON-RPC session.
            return ToolResults.Error($"Export failed: {ex.Message}");
        }

        // Rendered in full before anything is written, so a failure mid-render cannot leave a partial
        // file; WriteAtomically then makes the replacement itself all-or-nothing.
        if (OutputPathPolicy.WriteAtomically(output, bytes, overwrite) is { } writeError)
        {
            return ToolResults.Error(writeError);
        }

        var warnings = new List<string>(resolution.Warnings);
        if (allocation.Describe() is { } budget) { warnings.Add(budget); }
        if (result.Tally.Describe() is { } tally) { warnings.Add(tally); }

        FidelityReport fidelity = TuningFidelity.Assess(resolution.Settings.TargetScale);
        return ToolResults.Ok(new RestyleReport(
            output,
            resolution.Resolved,
            result.RestyledTracks.Sum(t => t.Notes.Count),
            new TallyReport(result.Tally.DroppedOutOfRange, result.Tally.DroppedNotInScale, result.Tally.Merged, result.Tally.Displaced),
            new ChannelReport(
                allocation.ChannelCount,
                ScaleDescriptors.Round(allocation.Budget.EffectiveToleranceCents),
                allocation.Budget.ToleranceWasRaised,
                ScaleDescriptors.Round(allocation.Budget.WorstErrorCents),
                [.. allocation.Muted.Select(m => new MutedTrack(m.TrackIndex, m.Channel, m.NoteCount))]),
            new FidelityInfo(EnumNames.Echo(fidelity.Badge), ScaleDescriptors.RoundOrNull(fidelity.MaxDeviationCents), fidelity.WorstDegreeIndex),
            warnings));
    }

    [McpServerTool(Name = "export_musicxml", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("Restyle a MIDI file and write the result as a MusicXML score (.musicxml or .xml) that notation software can open. " +
                 "Only for target scales that can be written on a staff - check list_scales' notatable flag; a scale that cannot be " +
                 "spelled is refused, and restyle_midi produces a playable .mid for it instead. Same parameters as restyle_midi, plus " +
                 "detectTuplets. Overwrites an existing output only when overwrite is true.")]
    public CallToolResult ExportMusicXml(
        [Description("Absolute path to the source .mid file.")] string inputPath,
        [Description("Target scale id from list_scales; must be one list_scales reports as notatable.")] string targetScaleId,
        [Description("Target tonic: note name with optional accidental and octave (D, Eb4, F#3) or MIDI number as a string. Default: detected key's tonic at octave 4.")] string? targetTonic = null,
        [Description("Source scale id. Default: ionian or aeolian per the detected key. Unused under strategy nearestPitch.")] string? sourceScaleId = null,
        [Description("Source tonic, same forms as targetTonic. Default: detected key's tonic.")] string? sourceTonic = null,
        [Description("Track-channels to leave untouched, as {track, channel} pairs from inspect_midi. Drums (channel 9) are never notated.")] IReadOnlyList<TrackChannelRef>? exclude = null,
        [Description("scaleDegree (default) maps degree to degree; nearestPitch snaps each note to the nearest target pitch.")] string? strategy = null,
        [Description("What to do with notes outside the source scale: snapToNearestSourceDegree (default), passThrough, drop.")] string? nonScaleNotes = null,
        [Description("When two notes map to one pitch: merge (default) or displaceOctave.")] string? collisions = null,
        [Description("When a mapped note leaves MIDI range: shiftIntoRange (default), foldOctave, drop.")] string? range = null,
        [Description("Pitch-bend clustering tolerance in cents, 0.5..50 (default 5). Affects the restyle, not the engraving.")] double? toleranceCents = null,
        [Description("Absolute output path ending in .musicxml or .xml. Default: beside the input as <name>.<scaleId>.musicxml.")] string? outputPath = null,
        [Description("Replace an existing output file (default false).")] bool overwrite = false,
        [Description("Detect triplets and sextuplets when quantising rhythm (default true). Off, triplet material is spelled as the nearest straight value.")] bool detectTuplets = true)
    {
        var request = new RestyleRequest(inputPath, targetScaleId, targetTonic, sourceScaleId, sourceTonic, exclude,
            strategy, nonScaleNotes, collisions, range, toleranceCents, outputPath, overwrite);

        if (!_resolver.TryResolve(request, out Resolution? resolution, out string? error))
        {
            return ToolResults.Error(error);
        }

        // CLAUDE.md: the authored flag gates the staff, but the speller decides - several dastgahs and
        // makams are flagged notatable and still run to eight or nine degrees, which no seven-letter
        // spelling reaches. Asking the flag alone would write a score whose noteheads are a guess.
        if (!ScaleDescriptors.IsNotatable(resolution.Settings.TargetScale, out string? why))
        {
            return ToolResults.Error(
                $"'{resolution.Settings.TargetScale.Id}' cannot be written on a staff: "
                + $"{why ?? "it is authored as not notatable, because no staff spelling of it would be honest"}. "
                + "Use restyle_midi to produce a playable .mid instead, or describe_scale to see the degrees.");
        }

        string requested = outputPath ?? OutputPathPolicy.DefaultOutputPath(inputPath, resolution.Resolved.TargetScaleId, ".musicxml");

        // Same order, and for the same reason, as restyle_midi: judged before a byte is written.
        if (OutputPathPolicy.ValidateOutputPath(requested, inputPath, overwrite, OutputPathPolicy.MusicXmlExtensions, _protected.Value, out string output) is { } outputError)
        {
            return ToolResults.Error(outputError);
        }

        NotationScore score;
        byte[] bytes;
        string? tally;
        try
        {
            RestyleResult result = RestyleEngine.Restyle(resolution.Project, resolution.Settings);

            // The single source of measures, ties, rests and voices - the same builder the staff view
            // and the degree view read. A second path here would eventually disagree with the screen.
            score = NotationBuilder.Build(resolution.Project, result.Tracks, resolution.Settings,
                QuantiseOptions.Default with { DetectTuplets = detectTuplets });

            bytes = Utf8NoBom.GetBytes(MusicXmlExporter.ToXml(score));
            tally = result.Tally.Describe();
        }
        catch (Exception ex) when (ex is MusicXmlExportException or InvalidOperationException or NotSupportedException)
        {
            // MusicXmlExportException is genuinely reachable here, unlike on restyle_midi's path: a
            // file whose only notes are drums notates to no parts at all, and MusicXML has no way to
            // say that. The other two are the backstop for a bug in our own pipeline, which must
            // reach the agent as one failed call rather than as a dropped JSON-RPC session.
            return ToolResults.Error($"MusicXML export failed: {ex.Message}");
        }

        // Rendered in full before anything is written, so a failure mid-render cannot leave a partial
        // file; WriteAtomically then makes the replacement itself all-or-nothing.
        if (OutputPathPolicy.WriteAtomically(output, bytes, overwrite) is { } writeError)
        {
            return ToolResults.Error(writeError);
        }

        var warnings = new List<string>(resolution.Warnings);
        if (tally is not null) { warnings.Add(tally); }

        // No channel report: pitch bend and the channel budget belong to playback and to .mid export.
        // A staff has neither, so reporting one here would describe a plan this call never made.
        return ToolResults.Ok(new MusicXmlReport(
            output,
            resolution.Resolved,
            score.MeasureCount,
            [.. score.Parts.Select(p => p.Name)],
            score.Diagnostics,
            warnings));
    }

    private static MidiInspection Inspect(string path, MidiProject project)
    {
        KeyDetectionResult detection = KeyDetector.Detect(project);
        KeyEstimate? chosen = RestyleDefaults.ChosenCandidate(detection);

        return new MidiInspection(
            path,
            EnumNames.Echo(project.Format),
            project.Division.Describe(),
            project.Division is TicksPerQuarterNote tpqn ? tpqn.Ticks : null,
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
