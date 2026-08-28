using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;
using DomainNote = MidiRestyle.Core.Model.Note;
using DomainTimeDivision = MidiRestyle.Core.Model.TimeDivision;
using DwmNote = Melanchall.DryWetMidi.Interaction.Note;
using DwmTimeDivision = Melanchall.DryWetMidi.Core.TimeDivision;

namespace MidiRestyle.Core.Io;

/// <summary>
/// Reads a Standard MIDI File into an immutable <see cref="MidiProject"/>.
/// </summary>
/// <remarks>
/// <para>
/// The one place DryWetMIDI touches the domain on the way in (<c>MidiFileExporter</c> is the way
/// out). Everything downstream sees only the model types, which is what keeps <c>Core</c> testable
/// without files and portable off Windows.
/// </para>
/// <para>
/// <b>Pure.</b> The loader returns a new project or throws; it never mutates anything, so a failed
/// load leaves whatever the app already had loaded exactly as it was.
/// </para>
/// <para>
/// <b>Every track is split by channel.</b> A Format 1 track that plays three channels becomes three
/// <see cref="TrackInfo"/>s sharing one <see cref="TrackInfo.TrackIndex"/>, and Format 0's single
/// track becomes up to sixteen. This is a correctness requirement, not tidiness: the drum rule is
/// per-<em>channel</em> (channel 9 must never be remapped, because a note number selects which drum
/// is struck) while the restyle opt-out in the UI is per-<em>track</em>, so on a Format 0 file one
/// checkbox could not exclude the drums. Splitting here makes <c>(track, channel)</c> the uniform
/// scope key for restyling, channel allocation and export alike.
/// </para>
/// </remarks>
public static class MidiFileLoader
{
    /// <summary>
    /// Reading settings tuned for real-world files: tolerant of the sloppiness DAWs actually emit,
    /// intolerant of structural corruption.
    /// </summary>
    /// <remarks>
    /// The split matters. Unknown chunks, an under-counted header and out-of-range parameter bytes
    /// are all things that ship in files which play correctly everywhere, so refusing them would
    /// reject good input. A truncated chunk, a wrong chunk size or a missing header means the byte
    /// stream cannot be trusted, and reading past it would invent notes - so those abort. In
    /// particular <see cref="NotEnoughBytesPolicy.Abort"/> is deliberate: <c>Ignore</c> would hand
    /// back a half-read project that looks perfectly valid.
    /// </remarks>
    public static ReadingSettings CreateReadingSettings() => new()
    {
        // Tolerated: common, harmless, and present in files that play fine.
        UnknownChunkIdPolicy = UnknownChunkIdPolicy.Skip,
        ExtraTrackChunkPolicy = ExtraTrackChunkPolicy.Read,
        UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
        MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
        InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.SnapToLimits,
        InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,

        // A format field outside 0..2 is still readable; it only means the format is inferred rather
        // than trusted. Aborting would reject a file whose events are intact.
        UnknownFileFormatPolicy = UnknownFileFormatPolicy.Ignore,

        // Required by the MIDI spec and load-bearing for note pairing: a Note On with velocity 0 is
        // a Note Off.
        SilentNoteOnPolicy = SilentNoteOnPolicy.NoteOff,

        // Refused: past any of these the byte stream cannot be trusted.
        NotEnoughBytesPolicy = NotEnoughBytesPolicy.Abort,
        InvalidChunkSizePolicy = InvalidChunkSizePolicy.Abort,
        NoHeaderChunkPolicy = NoHeaderChunkPolicy.Abort,
    };

    /// <summary>Loads the file at <paramref name="filePath"/>.</summary>
    /// <exception cref="MidiFileLoadException">The file is missing, unreadable or malformed.</exception>
    public static MidiProject Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        MidiFile file;
        try
        {
            file = MidiFile.Read(filePath, CreateReadingSettings());
        }
        catch (MidiException ex)
        {
            throw Translate(ex, filePath);
        }
        catch (IOException ex)
        {
            throw new MidiFileLoadException(
                $"Could not open '{Describe(filePath)}': {ex.Message}",
                filePath,
                ex.GetType().Name,
                innerException: ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new MidiFileLoadException(
                $"Access to '{Describe(filePath)}' was denied. Check the file's permissions.",
                filePath,
                ex.GetType().Name,
                innerException: ex);
        }

        return Build(file, filePath);
    }

    /// <summary>Loads from an open stream. <paramref name="filePath"/> is recorded for display only.</summary>
    /// <exception cref="MidiFileLoadException">The stream does not hold a readable MIDI file.</exception>
    public static MidiProject Load(Stream stream, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        MidiFile file;
        try
        {
            file = MidiFile.Read(stream, CreateReadingSettings());
        }
        catch (MidiException ex)
        {
            throw Translate(ex, filePath);
        }

        return Build(file, filePath);
    }

    /// <summary>
    /// Loads without throwing, yielding a user-facing message on failure. The form the UI uses, so a
    /// bad file becomes a status message rather than an exception dialog - and, because the loader is
    /// pure, a failure leaves any already-loaded project untouched.
    /// </summary>
    public static bool TryLoad(string filePath, out MidiProject? project, out string? errorMessage)
    {
        try
        {
            project = Load(filePath);
            errorMessage = null;
            return true;
        }
        catch (MidiFileLoadException ex)
        {
            project = null;
            errorMessage = ex.Message;
            return false;
        }
    }

    // ---- translation of read failures -------------------------------------------------------

    /// <summary>
    /// Turns a DryWetMIDI read failure into a domain failure, reporting the exception type plus
    /// whatever chunk id and sizes the specific exception genuinely carries - and nothing it does not.
    /// </summary>
    private static MidiFileLoadException Translate(MidiException ex, string? filePath)
    {
        string what = Describe(filePath);
        string type = ex.GetType().Name;

        switch (ex)
        {
            case NotEnoughBytesException notEnough:
            {
                // ExpectedCount/ActualCount exist but are both 0 for a truncated file, so they are
                // only worth printing when they actually say something.
                string sizes = notEnough.ExpectedCount > 0 || notEnough.ActualCount > 0
                    ? $" Expected {notEnough.ExpectedCount} byte(s), found {notEnough.ActualCount}."
                    : string.Empty;

                return new MidiFileLoadException(
                    $"'{what}' ends earlier than its own contents declare ({type}).{sizes} " +
                    "The file is most likely truncated or incompletely copied - try re-exporting it.",
                    filePath,
                    type,
                    expectedSize: notEnough.ExpectedCount,
                    actualSize: notEnough.ActualCount,
                    innerException: ex);
            }

            case InvalidChunkSizeException invalidSize:
                return new MidiFileLoadException(
                    $"'{what}' has a corrupt '{invalidSize.ChunkId}' chunk ({type}): it declares " +
                    $"{invalidSize.ExpectedSize} byte(s) but holds {invalidSize.ActualSize}. " +
                    "Re-export the file from whatever wrote it.",
                    filePath,
                    type,
                    invalidSize.ChunkId,
                    invalidSize.ExpectedSize,
                    invalidSize.ActualSize,
                    ex);

            case UnknownChannelEventException unknownChannel:
                return new MidiFileLoadException(
                    $"'{what}' contains an unrecognised MIDI message ({type}): status nibble 0x" +
                    $"{(byte)unknownChannel.StatusByte:X1} on channel {unknownChannel.Channel + 1}. " +
                    "The file is not a valid Standard MIDI File.",
                    filePath,
                    type,
                    innerException: ex);

            case UnknownFileFormatException unknownFormat:
                return new MidiFileLoadException(
                    $"'{what}' declares an unknown format {unknownFormat.FileFormat} ({type}); " +
                    "only formats 0, 1 and 2 exist.",
                    filePath,
                    type,
                    innerException: ex);

            default:
                return new MidiFileLoadException(
                    $"'{what}' could not be read as a MIDI file ({type}): {ex.Message}",
                    filePath,
                    type,
                    innerException: ex);
        }
    }

    private static string Describe(string? filePath) =>
        string.IsNullOrEmpty(filePath) ? "the MIDI data" : Path.GetFileName(filePath);

    // ---- model construction -----------------------------------------------------------------

    private static MidiProject Build(MidiFile file, string? filePath)
    {
        List<TrackChunk> chunks = [.. file.Chunks.OfType<TrackChunk>()];

        List<TempoChange> tempoMap = [];
        List<TimeSignatureChange> timeSignatures = [];
        List<MarkerInfo> markers = [];
        List<TrackInfo> tracks = [];
        string? title = null;

        for (int trackIndex = 0; trackIndex < chunks.Count; trackIndex++)
        {
            string? trackName = ReadTrack(
                chunks[trackIndex], trackIndex, tracks, tempoMap, timeSignatures, markers);

            // FF 03 in the first track is the sequence name under formats 0 and 1; later tracks name
            // only themselves. Taking the first one found gives the file a title either way.
            title ??= trackName;
        }

        tempoMap.Sort(static (a, b) => a.Ticks.CompareTo(b.Ticks));
        timeSignatures.Sort(static (a, b) => a.Ticks.CompareTo(b.Ticks));
        markers.Sort(static (a, b) => a.Ticks.CompareTo(b.Ticks));

        return new MidiProject
        {
            FilePath = filePath,
            Format = ReadFormat(file, chunks.Count),
            Division = ReadDivision(file.TimeDivision),
            Tracks = tracks,
            TempoMap = tempoMap,
            TimeSignatures = timeSignatures,
            Markers = markers,
            SequenceCount = ReadSequenceCount(file, chunks.Count),
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
        };
    }

    /// <summary>
    /// Reads one source track chunk, appending one <see cref="TrackInfo"/> per channel it uses and
    /// folding its meta events into the project-wide maps. Returns the track's name, if it has one.
    /// </summary>
    private static string? ReadTrack(
        TrackChunk chunk,
        int trackIndex,
        List<TrackInfo> tracks,
        List<TempoChange> tempoMap,
        List<TimeSignatureChange> timeSignatures,
        List<MarkerInfo> markers)
    {
        string? trackName = null;

        // Keyed by channel. A channel earns an entry from any channel event at all, not only from a
        // note: a program change on a channel that happens to be silent in this track is still part
        // of the arrangement, and dropping it would lose the instrument name.
        Dictionary<int, ChannelAccumulator> byChannel = [];

        foreach (TimedEvent timedEvent in chunk.GetTimedEvents())
        {
            switch (timedEvent.Event)
            {
                case SequenceTrackNameEvent name:
                    trackName ??= name.Text;
                    break;

                case SetTempoEvent tempo:
                    tempoMap.Add(new TempoChange(
                        timedEvent.Time, ToMicrosecondsPerQuarter(tempo.MicrosecondsPerQuarterNote)));
                    break;

                case TimeSignatureEvent signature:
                    timeSignatures.Add(new TimeSignatureChange(
                        timedEvent.Time, signature.Numerator, signature.Denominator));
                    break;

                case MarkerEvent marker:
                    markers.Add(new MarkerInfo(timedEvent.Time, marker.Text ?? string.Empty));
                    break;

                case CuePointEvent cue:
                    markers.Add(new MarkerInfo(timedEvent.Time, cue.Text ?? string.Empty));
                    break;

                case ChannelEvent channelEvent:
                {
                    ChannelAccumulator accumulator = Accumulator(byChannel, channelEvent.Channel);
                    switch (channelEvent)
                    {
                        case ProgramChangeEvent program:
                            // First program change wins: it is what the channel sounds like when
                            // playback starts, which is what a track-list label should say.
                            accumulator.ProgramNumber ??= program.ProgramNumber;
                            break;

                        case PitchBendEvent:
                            accumulator.HasExistingPitchBend = true;
                            break;

                        case ControlChangeEvent cc:
                            // Recorded with its time; which of these actually count as "before the
                            // first note" is resolved once all notes are known, below.
                            accumulator.ControllerEvents.Add(
                                (timedEvent.Time, cc.ControlNumber, cc.ControlValue));
                            break;

                        case ChannelAftertouchEvent pressure:
                            accumulator.PressureEvents.Add((timedEvent.Time, pressure.AftertouchValue));
                            break;
                    }

                    break;
                }
            }
        }

        // GetNotes pairs Note On with Note Off - including a zero-velocity Note On, which the spec
        // makes an off. Zero-length notes are legal MIDI and are preserved, never filtered.
        foreach (DwmNote note in chunk.GetNotes())
        {
            Accumulator(byChannel, note.Channel).Notes.Add(new DomainNote(
                Pitch.FromMidi(note.NoteNumber),
                note.Time,
                note.Length,
                note.Velocity));
        }

        foreach (int channel in byChannel.Keys.Order())
        {
            ChannelAccumulator accumulator = byChannel[channel];

            // Deterministic order, so golden tests and the piano roll never depend on hash order.
            accumulator.Notes.Sort(static (a, b) =>
                a.StartTicks != b.StartTicks
                    ? a.StartTicks.CompareTo(b.StartTicks)
                    : a.Pitch.Cents.CompareTo(b.Pitch.Cents));

            // "Before the first note" - notes are already sorted by StartTicks, so [0] is the
            // earliest. A channel with no notes at all has nothing to be "before", so every
            // controller event seen on it counts.
            long? firstNoteTime = accumulator.Notes.Count > 0 ? accumulator.Notes[0].StartTicks : null;

            tracks.Add(new TrackInfo
            {
                TrackIndex = trackIndex,
                Channel = channel,
                Name = string.IsNullOrWhiteSpace(trackName) ? null : trackName,
                ProgramNumber = accumulator.ProgramNumber,
                InstrumentName = accumulator.ProgramNumber is int program
                    ? GeneralMidi.NameFor(program, channel)
                    : null,
                HasExistingPitchBend = accumulator.HasExistingPitchBend,
                ControllerValues = ResolveControllerValues(accumulator.ControllerEvents, firstNoteTime),
                ChannelPressure = ResolveLastValue(accumulator.PressureEvents, firstNoteTime),
                Notes = accumulator.Notes,
            });
        }

        return trackName;
    }

    /// <summary>
    /// Reduces every Control Change seen on a channel down to the last value per controller number
    /// that was in effect before the channel's first note - the initial state a derived pitch-bend
    /// channel must be set up with. Bank Select (CC0/CC32) is carried separately via
    /// <see cref="TrackInfo.ProgramNumber"/>'s counterpart in the exporter, and CC121/CC123 are
    /// commands rather than state (handled by <c>PitchBendEncoder</c> itself), so all four are
    /// excluded here regardless of whether the source file set them.
    /// </summary>
    private static IReadOnlyDictionary<int, int> ResolveControllerValues(
        List<(long Time, int Controller, int Value)> events, long? firstNoteTime)
    {
        if (events.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        Dictionary<int, int> result = [];
        foreach ((long time, int controller, int value) in events.OrderBy(e => e.Time))
        {
            if (controller is CcBankSelectMsb or CcBankSelectLsb or CcResetAllControllers or CcAllNotesOff)
            {
                continue;
            }

            if (firstNoteTime is long limit && time > limit)
            {
                continue;
            }

            // Later (but still-eligible) events overwrite earlier ones - last value before the
            // first note wins, exactly as it would sound on the source channel.
            result[controller] = value;
        }

        return result;
    }

    /// <summary>Same before-first-note reduction as <see cref="ResolveControllerValues"/>, for a single value stream (channel pressure).</summary>
    private static int? ResolveLastValue(List<(long Time, int Value)> events, long? firstNoteTime)
    {
        int? last = null;
        foreach ((long time, int value) in events.OrderBy(e => e.Time))
        {
            if (firstNoteTime is long limit && time > limit)
            {
                continue;
            }

            last = value;
        }

        return last;
    }

    // Standard MIDI control numbers excluded from TrackInfo.ControllerValues - see its doc comment.
    private const int CcBankSelectMsb = 0;
    private const int CcBankSelectLsb = 32;
    private const int CcResetAllControllers = 121;
    private const int CcAllNotesOff = 123;

    private static ChannelAccumulator Accumulator(
        Dictionary<int, ChannelAccumulator> byChannel, FourBitNumber channel)
    {
        if (!byChannel.TryGetValue(channel, out ChannelAccumulator? accumulator))
        {
            accumulator = new ChannelAccumulator();
            byChannel[channel] = accumulator;
        }

        return accumulator;
    }

    /// <summary>
    /// The header's format field, falling back to the chunk count when the field is not 0, 1 or 2.
    /// </summary>
    /// <remarks>
    /// <c>OriginalFormat</c> is a property that <em>throws</em> for an unknown format field, and
    /// <see cref="CreateReadingSettings"/> deliberately reads such files anyway - so the throw has to
    /// be caught here rather than avoided.
    /// </remarks>
    private static MidiFileFormatKind ReadFormat(MidiFile file, int trackChunkCount)
    {
        try
        {
            return file.OriginalFormat switch
            {
                MidiFileFormat.SingleTrack => MidiFileFormatKind.SingleTrack,
                MidiFileFormat.MultiSequence => MidiFileFormatKind.MultiSequence,
                _ => MidiFileFormatKind.MultiTrack,
            };
        }
        catch (UnknownFileFormatException)
        {
            return trackChunkCount <= 1
                ? MidiFileFormatKind.SingleTrack
                : MidiFileFormatKind.MultiTrack;
        }
    }

    /// <summary>
    /// How many independent sequences the file holds: one, except under Format 2 where every track
    /// chunk is a sequence in its own right.
    /// </summary>
    private static int ReadSequenceCount(MidiFile file, int trackChunkCount)
    {
        try
        {
            return file.OriginalFormat == MidiFileFormat.MultiSequence
                ? Math.Max(1, trackChunkCount)
                : 1;
        }
        catch (UnknownFileFormatException)
        {
            return 1;
        }
    }

    /// <summary>
    /// Maps the file's timebase onto the closed <see cref="DomainTimeDivision"/> hierarchy. SMPTE
    /// files genuinely have no PPQN, so none is fabricated.
    /// </summary>
    private static DomainTimeDivision ReadDivision(DwmTimeDivision division) => division switch
    {
        TicksPerQuarterNoteTimeDivision ppqn => new TicksPerQuarterNote(ppqn.TicksPerQuarterNote),
        SmpteTimeDivision smpte => new SmpteDivision(FramesPerSecond(smpte.Format), smpte.Resolution),
        _ => new TicksPerQuarterNote(TicksPerQuarterNoteTimeDivision.DefaultTicksPerQuarterNote),
    };

    /// <summary>
    /// SMPTE frame rates. 29 is the drop-frame rate, nominally 29.97 fps; the integer is what the
    /// file stores and what the metadata header shows.
    /// </summary>
    private static int FramesPerSecond(SmpteFormat format) => format switch
    {
        SmpteFormat.TwentyFour => 24,
        SmpteFormat.TwentyFive => 25,
        SmpteFormat.ThirtyDrop => 29,
        _ => 30,
    };

    /// <summary>
    /// Narrows a tempo to the model's <see cref="int"/>. The <c>FF 51</c> payload is three bytes, so
    /// every legal value fits; the clamp only guards against a library that let something else past.
    /// </summary>
    private static int ToMicrosecondsPerQuarter(long value) => (int)Math.Clamp(value, 1, int.MaxValue);

    /// <summary>Mutable per-channel scratch space, discarded once the immutable model is built.</summary>
    private sealed class ChannelAccumulator
    {
        public List<DomainNote> Notes { get; } = [];

        public int? ProgramNumber { get; set; }

        public bool HasExistingPitchBend { get; set; }

        /// <summary>Every Control Change seen on this channel, with its time - unfiltered until the first note's time is known.</summary>
        public List<(long Time, int Controller, int Value)> ControllerEvents { get; } = [];

        /// <summary>Every Channel Pressure (aftertouch) event seen on this channel, with its time.</summary>
        public List<(long Time, int Value)> PressureEvents { get; } = [];
    }
}
