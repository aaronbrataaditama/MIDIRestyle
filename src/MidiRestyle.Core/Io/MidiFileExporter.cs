using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Tuning;
using DomainChannelEvent = MidiRestyle.Core.Output.ChannelEvent;
using DomainTimeDivision = MidiRestyle.Core.Model.TimeDivision;
using DwmTimeDivision = Melanchall.DryWetMidi.Core.TimeDivision;

namespace MidiRestyle.Core.Io;

/// <summary>Why <see cref="MidiFileExporter"/> refused to write a <c>RestyleResult</c>.</summary>
public enum ExportFailureReason
{
    /// <summary>
    /// The target tuning needs pitch bend, which this exporter cannot emit. Microtonal export needs
    /// <c>ChannelAllocator</c> - phase 8 - so refusing here beats silently writing a detuned file.
    /// </summary>
    NeedsPitchBend,

    /// <summary>
    /// A mapped note's pitch fell outside MIDI 0..127. <c>RangeEnforcer</c> should have caught this
    /// upstream; surfacing it here as a named domain error beats a raw
    /// <see cref="ArgumentOutOfRangeException"/> thrown from inside DryWetMIDI.
    /// </summary>
    NoteOutOfRange,
}

/// <summary>The outcome of an export attempt.</summary>
/// <remarks>
/// A result type, not an exception, for anything a user's own data can cause - a microtonal target
/// scale or a note that slipped out of range. Exceptions are reserved for genuine IO failure; see
/// <see cref="MidiFileExportException"/>.
/// </remarks>
public readonly record struct ExportResult
{
    private ExportResult(bool success, ExportFailureReason? reason, string? message)
    {
        Success = success;
        Reason = reason;
        Message = message;
    }

    /// <summary>Whether the file was written.</summary>
    public bool Success { get; }

    /// <summary>Why the export was refused. Null on success.</summary>
    public ExportFailureReason? Reason { get; }

    /// <summary>A user-facing explanation. Null on success.</summary>
    public string? Message { get; }

    public static ExportResult Ok() => new(success: true, reason: null, message: null);

    public static ExportResult Fail(ExportFailureReason reason, string message) =>
        new(success: false, reason, message);
}

/// <summary>
/// Writes a <see cref="RestyleResult"/> out as a Standard MIDI File, 12-TET only.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="MidiFileLoader"/>, and the only other place in <c>Core</c> that
/// touches DryWetMIDI. Everything above this type sees only the domain model.
/// </para>
/// <para>
/// <b>12-TET only.</b> A note carrying pitch bend needs a pitch-bend channel, which is
/// <c>ChannelAllocator</c>'s job - phase 8, not yet built. Rather than silently drop the bend and
/// ship a detuned file, this type refuses outright: see <see cref="ExportFailureReason.NeedsPitchBend"/>.
/// </para>
/// <para>
/// <b>Always Format 1.</b> Every <see cref="RestyledTrack"/> becomes its own <c>TrackChunk</c> on its
/// own channel, including drum and opted-out tracks - the result is what will be exported, not a
/// diff against the source. A leading conductor chunk carries the tempo map, time signatures and
/// markers, none of which restyling ever touches.
/// </para>
/// </remarks>
public static class MidiFileExporter
{
    /// <summary>Writes <paramref name="result"/> to the file at <paramref name="filePath"/>.</summary>
    /// <exception cref="MidiFileExportException">The file could not be written.</exception>
    public static ExportResult Export(RestyleResult result, string filePath)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        ExportResult validation = Validate(result);
        if (!validation.Success)
        {
            return validation;
        }

        MidiFile file = Build(result);

        try
        {
            file.Write(filePath, overwriteFile: true, MidiFileFormat.MultiTrack);
        }
        catch (IOException ex)
        {
            throw new MidiFileExportException(
                $"Could not write '{Describe(filePath)}': {ex.Message}", filePath, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new MidiFileExportException(
                $"Access to '{Describe(filePath)}' was denied. Check the file's permissions.",
                filePath,
                ex);
        }

        return ExportResult.Ok();
    }

    /// <summary>Writes <paramref name="result"/> to an open, writable stream.</summary>
    /// <exception cref="MidiFileExportException">The stream could not be written to.</exception>
    public static ExportResult Export(RestyleResult result, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(stream);

        ExportResult validation = Validate(result);
        if (!validation.Success)
        {
            return validation;
        }

        MidiFile file = Build(result);

        try
        {
            file.Write(stream, MidiFileFormat.MultiTrack);
        }
        catch (IOException ex)
        {
            throw new MidiFileExportException($"Could not write to the stream: {ex.Message}", null, ex);
        }

        return ExportResult.Ok();
    }

    private static string Describe(string? filePath) =>
        string.IsNullOrEmpty(filePath) ? "the MIDI data" : Path.GetFileName(filePath);

    // ---- validation --------------------------------------------------------------------------

    /// <summary>
    /// Refuses anything this exporter cannot honestly write, before a single byte is produced.
    /// </summary>
    /// <remarks>
    /// Two checks, deliberately both run. <see cref="RestyleResult.NeedsPitchBend"/> is the cheap
    /// one - a single comparison against the scale's worst-case offset - and rejects the common case
    /// fast. It is stated against the project's tolerance, though, so a scale that is nominally
    /// within tolerance can still leave an individual note with a non-zero <c>BendCents</c>; the
    /// per-note scan is the honest check that actually guarantees nothing detunes silently.
    /// </remarks>
    private static ExportResult Validate(RestyleResult result)
    {
        if (result.NeedsPitchBend)
        {
            return ExportResult.Fail(
                ExportFailureReason.NeedsPitchBend,
                $"'{result.Settings.TargetScale.Name}' needs pitch bend (up to " +
                $"{result.Settings.TargetScale.MaxOffsetCents:0.#} cents of offset) to sound correctly, " +
                "and microtonal export is not supported yet. Refusing to export a detuned 12-TET file.");
        }

        foreach (RestyledTrack track in result.Tracks)
        {
            IReadOnlyList<Note> notes = track.Notes;
            for (int i = 0; i < notes.Count; i++)
            {
                Note note = notes[i];

                if (!note.Pitch.IsTwelveTet)
                {
                    return ExportResult.Fail(
                        ExportFailureReason.NeedsPitchBend,
                        $"Track {track.TrackIndex} channel {track.Channel + 1}: the note at tick " +
                        $"{note.StartTicks} needs a {note.Pitch.BendCents:+0.##;-0.##} cent pitch bend, " +
                        "and microtonal export is not supported yet. Refusing to export a detuned " +
                        "12-TET file.");
                }

                if (!note.Pitch.IsInMidiRange)
                {
                    return ExportResult.Fail(
                        ExportFailureReason.NoteOutOfRange,
                        $"Track {track.TrackIndex} channel {track.Channel + 1}: the note at tick " +
                        $"{note.StartTicks} maps to MIDI {note.Pitch.MidiNote}, outside the " +
                        $"representable range {Pitch.MinMidiNote}..{Pitch.MaxMidiNote}.");
                }
            }
        }

        return ExportResult.Ok();
    }

    // ---- model construction -----------------------------------------------------------------

    private static MidiFile Build(RestyleResult result)
    {
        MidiProject source = result.Source;

        Dictionary<(int TrackIndex, int Channel), TrackInfo> sourceByKey = source.Tracks
            .ToDictionary(t => (t.TrackIndex, t.Channel));

        List<TrackChunk> chunks = [BuildConductorChunk(source)];

        foreach (RestyledTrack track in result.Tracks)
        {
            sourceByKey.TryGetValue((track.TrackIndex, track.Channel), out TrackInfo? sourceTrack);
            chunks.Add(BuildTrackChunk(track, sourceTrack));
        }

        return new MidiFile(chunks) { TimeDivision = ToDwmDivision(source.Division) };
    }

    /// <summary>
    /// The tempo map, time signatures and markers, none of which belong to any one track-channel.
    /// Restyling never touches any of these - pitch remapping only - so they are copied verbatim.
    /// </summary>
    private static TrackChunk BuildConductorChunk(MidiProject source)
    {
        List<(long Time, MidiEvent Event)> events = [];

        if (!string.IsNullOrWhiteSpace(source.Title))
        {
            events.Add((0, new SequenceTrackNameEvent(source.Title)));
        }

        foreach (TempoChange tempo in source.TempoMap)
        {
            events.Add((tempo.Ticks, new SetTempoEvent(tempo.MicrosecondsPerQuarterNote)));
        }

        foreach (TimeSignatureChange signature in source.TimeSignatures)
        {
            events.Add((signature.Ticks, new TimeSignatureEvent(
                (byte)signature.Numerator, (byte)signature.Denominator)));
        }

        foreach (MarkerInfo marker in source.Markers)
        {
            events.Add((marker.Ticks, new MarkerEvent(marker.Text)));
        }

        return ToChunk(events);
    }

    /// <summary>
    /// One track-channel: its name and program (with the bank select that must precede it), then its
    /// notes as paired Note On / Note Off events.
    /// </summary>
    private static TrackChunk BuildTrackChunk(RestyledTrack track, TrackInfo? sourceTrack)
    {
        List<(long Time, MidiEvent Event)> events = [];
        FourBitNumber channel = (FourBitNumber)track.Channel;

        if (!string.IsNullOrWhiteSpace(sourceTrack?.Name))
        {
            events.Add((0, new SequenceTrackNameEvent(sourceTrack.Name)));
        }

        if (sourceTrack?.ProgramNumber is int program)
        {
            // Bank Select (CC0 then CC32) immediately before the Program Change. Without it, a
            // Program Change alone selects a different instrument on any GS/XG device - the source
            // track carries only the program number, so the bank it addresses is GM's default, 0/0.
            events.Add((0, new ControlChangeEvent(
                (SevenBitNumber)ControlName.BankSelect, SevenBitNumber.MinValue) { Channel = channel }));
            events.Add((0, new ControlChangeEvent(
                (SevenBitNumber)ControlName.BankSelectLsb, SevenBitNumber.MinValue) { Channel = channel }));
            events.Add((0, new ProgramChangeEvent((SevenBitNumber)program) { Channel = channel }));
        }

        IReadOnlyList<Note> notes = track.Notes;
        for (int i = 0; i < notes.Count; i++)
        {
            Note note = notes[i];
            SevenBitNumber pitch = (SevenBitNumber)note.Pitch.MidiNote;
            SevenBitNumber velocity = (SevenBitNumber)note.Velocity;

            events.Add((note.StartTicks, new NoteOnEvent(pitch, velocity) { Channel = channel }));
            events.Add((note.EndTicks, new NoteOffEvent(pitch, SevenBitNumber.MinValue) { Channel = channel }));
        }

        return ToChunk(events);
    }

    // ---- microtonal export ------------------------------------------------------------------

    /// <summary>
    /// Writes a microtonal restyle, using <paramref name="allocation"/> to place notes on
    /// pitch-bent channels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The allocation must be the one playback used.</b> <c>ChannelAllocator</c> is the single
    /// path shared by preview and export precisely so the two cannot diverge; passing a
    /// differently-planned allocation here would reintroduce the divergence the design exists to
    /// prevent. Both callers pass the same ceiling and so get the same plan.
    /// </para>
    /// <para>
    /// Muted track-channels are omitted, exactly as in preview, and are named in
    /// <see cref="ChannelAllocation.Muted"/> so the caller can report them.
    /// </para>
    /// </remarks>
    public static ExportResult Export(RestyleResult result, string filePath, ChannelAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        ExportResult validation = ValidateRangeOnly(result);
        if (!validation.Success)
        {
            return validation;
        }

        MidiFile file = BuildMicrotonal(result, allocation);

        try
        {
            file.Write(filePath, overwriteFile: true, MidiFileFormat.MultiTrack);
        }
        catch (IOException ex)
        {
            throw new MidiFileExportException(
                $"Could not write '{Describe(filePath)}': {ex.Message}", filePath, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new MidiFileExportException(
                $"Access to '{Describe(filePath)}' was denied. Check the file's permissions.",
                filePath,
                ex);
        }

        return ExportResult.Ok();
    }

    /// <summary>Writes a microtonal restyle to an open, writable stream.</summary>
    public static ExportResult Export(RestyleResult result, Stream stream, ChannelAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(stream);

        ExportResult validation = ValidateRangeOnly(result);
        if (!validation.Success)
        {
            return validation;
        }

        BuildMicrotonal(result, allocation).Write(stream, MidiFileFormat.MultiTrack);
        return ExportResult.Ok();
    }

    /// <summary>
    /// Range is still enforced for microtonal output; bend is not, since bend is the whole point.
    /// </summary>
    private static ExportResult ValidateRangeOnly(RestyleResult result)
    {
        foreach (RestyledTrack track in result.Tracks)
        {
            foreach (Note note in track.Notes)
            {
                if (!note.Pitch.IsInMidiRange)
                {
                    return ExportResult.Fail(
                        ExportFailureReason.NoteOutOfRange,
                        $"Track {track.TrackIndex} channel {track.Channel + 1} has a note at tick "
                        + $"{note.StartTicks} mapping to MIDI {note.Pitch.MidiNote}, outside 0-127. "
                        + "Choose a different range policy or a nearer target tonic.");
                }
            }
        }

        return ExportResult.Ok();
    }

    private static MidiFile BuildMicrotonal(RestyleResult result, ChannelAllocation allocation)
    {
        MidiProject source = result.Source;
        List<TrackChunk> chunks = [BuildConductorChunk(source)];

        foreach (RestyledTrack track in result.Tracks)
        {
            TrackInfo? sourceTrack = source.Tracks.FirstOrDefault(
                t => t.TrackIndex == track.TrackIndex && t.Channel == track.Channel);

            // Untouched tracks - drums and opt-outs - keep their own channel and need no bend, so
            // they go out exactly as the 12-TET path writes them.
            if (!track.WasRestyled)
            {
                chunks.Add(BuildTrackChunk(track, sourceTrack));
                continue;
            }

            if (allocation.IsMuted(track.TrackIndex, track.Channel))
            {
                continue;
            }

            List<AllocatedChannel> mine = [.. allocation.Channels.Where(
                c => c.SourceTrackIndex == track.TrackIndex && c.SourceChannel == track.Channel)];

            if (mine.Count == 0)
            {
                // Neither allocated nor muted: emit unbent rather than silently dropping the part.
                chunks.Add(BuildTrackChunk(track, sourceTrack));
                continue;
            }

            foreach (AllocatedChannel allocated in mine)
            {
                chunks.Add(BuildBentChunk(track, sourceTrack, allocated));
            }
        }

        return new MidiFile(chunks) { TimeDivision = ToDwmDivision(source.Division) };
    }

    /// <summary>One allocated channel: its setup sequence, then only the notes whose offset it carries.</summary>
    private static TrackChunk BuildBentChunk(
        RestyledTrack track,
        TrackInfo? sourceTrack,
        AllocatedChannel allocated)
    {
        List<(long Time, MidiEvent Event)> events = [];
        FourBitNumber channel = (FourBitNumber)allocated.OutputChannel;

        if (!string.IsNullOrWhiteSpace(sourceTrack?.Name))
        {
            events.Add((0, new SequenceTrackNameEvent(
                $"{sourceTrack.Name} ({allocated.BendCents:+0.#;-0.#;0}c)")));
        }

        // The setup sequence comes from PitchBendEncoder, not from this file, so export and playback
        // emit identical channel state. Translating it is all that happens here.
        SourceChannelState state = new(
            Program: sourceTrack?.ProgramNumber ?? 0,
            BankMsb: 0,
            BankLsb: 0,
            ControllerValues: sourceTrack?.ControllerValues ?? new Dictionary<int, int>(),
            ChannelPressure: sourceTrack?.ChannelPressure);

        foreach (DomainChannelEvent setup in PitchBendEncoder.SetupSequence(
            allocated.OutputChannel, allocated.BendCents, state))
        {
            events.Add((0, Translate(setup)));
        }

        IReadOnlyList<Note> notes = track.Notes;
        for (int i = 0; i < notes.Count; i++)
        {
            Note note = notes[i];

            // Each note belongs to exactly one allocated channel: the one carrying its offset.
            if (!allocated.Carries(ChannelAllocator.OffsetFor(note.Pitch)))
            {
                continue;
            }

            SevenBitNumber pitch = (SevenBitNumber)note.Pitch.MidiNote;
            events.Add((note.StartTicks,
                new NoteOnEvent(pitch, (SevenBitNumber)note.Velocity) { Channel = channel }));
            events.Add((note.EndTicks,
                new NoteOffEvent(pitch, SevenBitNumber.MinValue) { Channel = channel }));
        }

        return ToChunk(events);
    }

    /// <summary>Translates one domain channel event into DryWetMIDI's representation.</summary>
    private static MidiEvent Translate(DomainChannelEvent e)
    {
        FourBitNumber channel = (FourBitNumber)e.Channel;

        return e.Kind switch
        {
            ChannelEventKind.ControlChange => new ControlChangeEvent(
                (SevenBitNumber)e.Data1, (SevenBitNumber)e.Data2) { Channel = channel },
            ChannelEventKind.ProgramChange => new ProgramChangeEvent(
                (SevenBitNumber)e.Data1) { Channel = channel },
            ChannelEventKind.PitchBend => new PitchBendEvent(
                (ushort)((e.Data2 * 128) + e.Data1)) { Channel = channel },
            ChannelEventKind.ChannelPressure => new ChannelAftertouchEvent(
                (SevenBitNumber)e.Data1) { Channel = channel },
            _ => throw new NotSupportedException($"Unhandled channel event kind {e.Kind}."),
        };
    }

    /// <summary>
    /// Standard MIDI control numbers this exporter emits. Not <see cref="ControlChangeEvent"/>'s
    /// own type, which DryWetMIDI does not expose as a public enum.
    /// </summary>
    private static class ControlName
    {
        public const byte BankSelect = 0;
        public const byte BankSelectLsb = 32;
    }

    /// <summary>
    /// Builds a chunk from absolute-time events, converting to the delta times a Standard MIDI File
    /// actually stores. Stable ordering keeps same-tick events - notably Bank Select, then Program
    /// Change - in the order they were added, and DryWetMIDI appends the end-of-track marker itself.
    /// </summary>
    private static TrackChunk ToChunk(List<(long Time, MidiEvent Event)> events)
    {
        TrackChunk chunk = new();
        long previous = 0;

        foreach ((long time, MidiEvent midiEvent) in events.OrderBy(e => e.Time))
        {
            midiEvent.DeltaTime = time - previous;
            previous = time;
            chunk.Events.Add(midiEvent);
        }

        return chunk;
    }

    /// <summary>
    /// The inverse of <c>MidiFileLoader.ReadDivision</c>: the domain's closed time-division
    /// hierarchy back onto DryWetMIDI's type.
    /// </summary>
    private static DwmTimeDivision ToDwmDivision(DomainTimeDivision division) => division switch
    {
        TicksPerQuarterNote ppqn => new TicksPerQuarterNoteTimeDivision(ppqn.Ticks),
        SmpteDivision smpte => new SmpteTimeDivision(ToSmpteFormat(smpte.FramesPerSecond), (byte)smpte.TicksPerFrame),
        _ => new TicksPerQuarterNoteTimeDivision(TicksPerQuarterNoteTimeDivision.DefaultTicksPerQuarterNote),
    };

    /// <summary>The inverse of <c>MidiFileLoader.FramesPerSecond</c>.</summary>
    private static SmpteFormat ToSmpteFormat(int framesPerSecond) => framesPerSecond switch
    {
        24 => SmpteFormat.TwentyFour,
        25 => SmpteFormat.TwentyFive,
        29 => SmpteFormat.ThirtyDrop,
        _ => SmpteFormat.Thirty,
    };
}
