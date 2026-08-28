using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// Covers <see cref="MidiFileExporter"/>. <see cref="RestyleResult"/> fixtures are built directly
/// rather than run through <c>RestyleEngine</c> - the exporter's contract is about what it writes
/// given a result, not about whether the mapper produced it correctly, and hand-built fixtures give
/// exact control over pitches, zero-length notes and track shape.
/// </summary>
public sealed class MidiFileExporterTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private const string Fixture = "Test fixture";

    /// <summary>Exactly the semitone grid, so it never needs pitch bend.</summary>
    private static readonly Scale Chromatic = new(
        "test.chromatic", "Chromatic", "Synthetic", "None",
        [0, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 1100], Fixture);

    /// <summary>Off the semitone grid on two degrees, so it always needs pitch bend.</summary>
    private static readonly Scale Rast = new(
        "test.rast", "Maqam Rast", "Maqam", "Middle East",
        [0, 200, 350, 500, 700, 900, 1050], Fixture);

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Not worth failing a test over.
            }
        }
    }

    // ---- round-trip fidelity ------------------------------------------------------------------

    [Fact]
    public void NotesRoundTripWithPitchTimingAndVelocityIntact()
    {
        Note[] notes =
        [
            new Note(Pitch.FromMidi(60), StartTicks: 0, LengthTicks: 480, Velocity: 100),
            new Note(Pitch.FromMidi(64), StartTicks: 480, LengthTicks: 240, Velocity: 90),
            new Note(Pitch.FromMidi(67), StartTicks: 720, LengthTicks: 60, Velocity: 127),
        ];

        RestyleResult result = BuildResult(
            [Track(trackIndex: 0, channel: 0, notes, program: 40)]);

        MidiProject reloaded = ExportAndReload(result);

        TrackInfo track = reloaded.Tracks.Should().ContainSingle().Subject;
        track.Channel.Should().Be(0);
        track.NoteCount.Should().Be(3);

        track.Notes[0].Pitch.MidiNote.Should().Be(60);
        track.Notes[0].StartTicks.Should().Be(0);
        track.Notes[0].LengthTicks.Should().Be(480);
        track.Notes[0].Velocity.Should().Be(100);

        track.Notes[1].Pitch.MidiNote.Should().Be(64);
        track.Notes[1].StartTicks.Should().Be(480);
        track.Notes[1].LengthTicks.Should().Be(240);
        track.Notes[1].Velocity.Should().Be(90);

        track.Notes[2].Pitch.MidiNote.Should().Be(67);
        track.Notes[2].StartTicks.Should().Be(720);
        track.Notes[2].LengthTicks.Should().Be(60);
        track.Notes[2].Velocity.Should().Be(127);
    }

    [Fact]
    public void ZeroLengthNoteSurvivesTheRoundTrip()
    {
        Note[] notes =
        [
            new Note(Pitch.FromMidi(72), StartTicks: 240, LengthTicks: 0, Velocity: 64),
        ];

        RestyleResult result = BuildResult([Track(trackIndex: 0, channel: 0, notes)]);

        MidiProject reloaded = ExportAndReload(result);

        TrackInfo track = reloaded.Tracks.Should().ContainSingle().Subject;
        track.Notes.Should().ContainSingle();
        track.Notes[0].Pitch.MidiNote.Should().Be(72);
        track.Notes[0].StartTicks.Should().Be(240);
        track.Notes[0].LengthTicks.Should().Be(0);
    }

    // ---- tempo map, time signatures, markers, names -------------------------------------------

    [Fact]
    public void MultiTempoMapSurvivesTheRoundTrip()
    {
        MidiProject source = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [TrackInfoFrom(0, 0, [new Note(Pitch.FromMidi(60), 0, 480, 100)])],
            TempoMap =
            [
                new TempoChange(0, 500_000),
                new TempoChange(480, 250_000),
                new TempoChange(960, 600_000),
            ],
        };

        RestyledTrack restyled = new(0, 0, source.Tracks[0].Notes, WasRestyled: true);
        RestyleResult result = BuildResult(source, [restyled]);

        MidiProject reloaded = ExportAndReload(result);

        reloaded.TempoMap.Should().Equal(
            new TempoChange(0, 500_000),
            new TempoChange(480, 250_000),
            new TempoChange(960, 600_000));
    }

    [Fact]
    public void TimeSignaturesMarkersAndTrackNamesSurviveTheRoundTrip()
    {
        MidiProject source = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [TrackInfoFrom(0, 0, [new Note(Pitch.FromMidi(60), 0, 480, 100)], name: "Lead")],
            TimeSignatures = [new TimeSignatureChange(0, 6, 8), new TimeSignatureChange(960, 4, 4)],
            Markers = [new MarkerInfo(0, "Intro"), new MarkerInfo(960, "Chorus")],
        };

        RestyledTrack restyled = new(0, 0, source.Tracks[0].Notes, WasRestyled: true);
        RestyleResult result = BuildResult(source, [restyled]);

        MidiProject reloaded = ExportAndReload(result);

        reloaded.TimeSignatures.Should().Equal(
            new TimeSignatureChange(0, 6, 8),
            new TimeSignatureChange(960, 4, 4));
        reloaded.Markers.Should().Equal(
            new MarkerInfo(0, "Intro"),
            new MarkerInfo(960, "Chorus"));
        reloaded.Tracks.Should().ContainSingle().Which.Name.Should().Be("Lead");
    }

    // ---- time division -------------------------------------------------------------------------

    [Fact]
    public void NonDefaultPpqnSurvivesTheRoundTrip()
    {
        MidiProject source = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(96),
            Tracks = [TrackInfoFrom(0, 0, [new Note(Pitch.FromMidi(60), 0, 96, 100)])],
        };

        RestyledTrack restyled = new(0, 0, source.Tracks[0].Notes, WasRestyled: true);
        RestyleResult result = BuildResult(source, [restyled]);

        MidiProject reloaded = ExportAndReload(result);

        reloaded.Division.Should().Be(new TicksPerQuarterNote(96));
    }

    // ---- drums and opted-out tracks -------------------------------------------------------------

    [Fact]
    public void DrumAndOptedOutTracksExportUnchangedOnTheirOriginalChannels()
    {
        Note[] drumNotes = [new Note(Pitch.FromMidi(36), 0, 60, 110)];
        Note[] optedOutNotes = [new Note(Pitch.FromMidi(48), 0, 480, 80)];
        Note[] restyledNotes = [new Note(Pitch.FromMidi(61), 0, 480, 100)];

        RestyledTrack drums = new(TrackIndex: 0, Channel: 9, drumNotes, WasRestyled: false);
        RestyledTrack optedOut = new(TrackIndex: 1, Channel: 3, optedOutNotes, WasRestyled: false);
        RestyledTrack restyled = new(TrackIndex: 2, Channel: 0, restyledNotes, WasRestyled: true);

        MidiProject source = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks =
            [
                TrackInfoFrom(0, 9, drumNotes),
                TrackInfoFrom(1, 3, optedOutNotes),
                TrackInfoFrom(2, 0, [new Note(Pitch.FromMidi(60), 0, 480, 100)]),
            ],
        };

        RestyleResult result = BuildResult(source, [drums, optedOut, restyled]);

        MidiProject reloaded = ExportAndReload(result);

        TrackInfo reloadedDrums = reloaded.Tracks.Single(t => t.Channel == 9);
        reloadedDrums.IsDrums.Should().BeTrue();
        reloadedDrums.Notes.Should().Equal(drumNotes);

        TrackInfo reloadedOptedOut = reloaded.Tracks.Single(t => t.Channel == 3);
        reloadedOptedOut.Notes.Should().Equal(optedOutNotes);

        TrackInfo reloadedRestyled = reloaded.Tracks.Single(t => t.Channel == 0);
        reloadedRestyled.Notes[0].Pitch.MidiNote.Should().Be(61);
    }

    // ---- bank select / program change ordering ---------------------------------------------------

    [Fact]
    public void BankSelectPrecedesEveryProgramChangeInTheCorrectOrder()
    {
        Note[] notes = [new Note(Pitch.FromMidi(60), 0, 480, 100)];
        RestyleResult result = BuildResult([Track(0, 0, notes, program: 73)]);

        string path = NewTempPath("bank-select");
        ExportResult export = MidiFileExporter.Export(result, path);
        export.Success.Should().BeTrue();

        // Assert on the actual event sequence, not just presence: read the raw file back rather
        // than going through the loader, which only exposes the resolved program number.
        MidiFile file = MidiFile.Read(path);
        TrackChunk trackChunk = file.Chunks.OfType<TrackChunk>()
            .Single(c => c.Events.OfType<ChannelEvent>().Any());

        List<MidiEvent> channelEvents = [.. trackChunk.Events
            .Where(e => e is ControlChangeEvent or ProgramChangeEvent)];

        channelEvents.Should().HaveCount(3);
        channelEvents[0].Should().BeOfType<ControlChangeEvent>()
            .Which.ControlNumber.Should().Be((SevenBitNumber)0);
        channelEvents[1].Should().BeOfType<ControlChangeEvent>()
            .Which.ControlNumber.Should().Be((SevenBitNumber)32);
        channelEvents[2].Should().BeOfType<ProgramChangeEvent>()
            .Which.ProgramNumber.Should().Be((SevenBitNumber)73);
    }

    [Fact]
    public void NoProgramChangeMeansNoBankSelectEither()
    {
        Note[] notes = [new Note(Pitch.FromMidi(60), 0, 480, 100)];
        RestyleResult result = BuildResult([Track(0, 0, notes, program: null)]);

        string path = NewTempPath("no-program");
        MidiFileExporter.Export(result, path).Success.Should().BeTrue();

        MidiFile file = MidiFile.Read(path);
        bool hasProgramOrBank = file.Chunks.OfType<TrackChunk>()
            .SelectMany(c => c.Events)
            .Any(static e => e is ControlChangeEvent or ProgramChangeEvent);

        hasProgramOrBank.Should().BeFalse();
    }

    // ---- refusals -------------------------------------------------------------------------------

    [Fact]
    public void MicrotonalResultIsRefusedRatherThanExportedDetuned()
    {
        Note[] notes = [new Note(Pitch.FromMidi(60), 0, 480, 100)];
        RestyleResult result = BuildResult(
            [Track(0, 0, notes)],
            targetScale: Rast);

        result.NeedsPitchBend.Should().BeTrue("guard the premise: Rast must actually need bend");

        string path = NewTempPath("microtonal-refused");
        ExportResult export = MidiFileExporter.Export(result, path);

        export.Success.Should().BeFalse();
        export.Reason.Should().Be(ExportFailureReason.NeedsPitchBend);
        export.Message.Should().NotBeNullOrWhiteSpace();
        export.Message.Should().Contain("pitch bend");
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void ANoteCarryingBendIsRefusedEvenWhenTheScaleAsAWholeDoesNotNeedIt()
    {
        // The scale is nominally 12-TET (MaxOffsetCents is 0, well within tolerance), but this
        // particular note carries a bend anyway - the honest, per-note check that
        // RestyleResult.NeedsPitchBend alone would miss.
        Note[] notes = [new Note(new Pitch(6005), 0, 480, 100)];
        RestyleResult result = BuildResult([Track(0, 0, notes)]);

        result.NeedsPitchBend.Should().BeFalse("guard the premise: the scale itself needs no bend");

        ExportResult export = MidiFileExporter.Export(result, new MemoryStream());

        export.Success.Should().BeFalse();
        export.Reason.Should().Be(ExportFailureReason.NeedsPitchBend);
    }

    [Fact]
    public void AnOutOfRangePitchProducesADomainErrorNamingTheNoteInsteadOfThrowing()
    {
        Note[] notes = [new Note(Pitch.FromMidi(140), 0, 480, 100)];
        RestyleResult result = BuildResult([Track(0, 0, notes)]);

        ExportResult export = MidiFileExporter.Export(result, new MemoryStream());

        export.Success.Should().BeFalse();
        export.Reason.Should().Be(ExportFailureReason.NoteOutOfRange);
        export.Message.Should().Contain("tick 0");
        export.Message.Should().Contain("140");
    }

    // ---- no restylable tracks ---------------------------------------------------------------------

    [Fact]
    public void ExportingAProjectWithNoRestylableTracksStillProducesAValidReloadableFile()
    {
        Note[] drumNotes = [new Note(Pitch.FromMidi(38), 0, 60, 100)];
        RestyledTrack drums = new(TrackIndex: 0, Channel: 9, drumNotes, WasRestyled: false);

        MidiProject source = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [TrackInfoFrom(0, 9, drumNotes)],
        };

        RestyleResult result = BuildResult(source, [drums]);

        MidiProject reloaded = ExportAndReload(result);

        reloaded.Tracks.Should().ContainSingle().Which.IsDrums.Should().BeTrue();
        reloaded.RestylableTracks.Should().BeEmpty();
    }

    [Fact]
    public void ExportingAProjectWithNoTracksAtAllStillProducesAValidReloadableFile()
    {
        MidiProject source = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [],
        };

        RestyleResult result = BuildResult(source, []);

        MidiProject reloaded = ExportAndReload(result);

        reloaded.Tracks.Should().BeEmpty();
        reloaded.TotalNoteCount.Should().Be(0);
    }

    // ---- write-to-stream entry point ------------------------------------------------------------

    [Fact]
    public void ExportToStreamRoundTripsThroughLoadFromStream()
    {
        Note[] notes = [new Note(Pitch.FromMidi(65), 0, 240, 77)];
        RestyleResult result = BuildResult([Track(0, 0, notes)]);

        using MemoryStream stream = new();
        MidiFileExporter.Export(result, stream).Success.Should().BeTrue();

        stream.Position = 0;
        MidiProject reloaded = MidiFileLoader.Load(stream);

        reloaded.Tracks.Should().ContainSingle().Which.Notes[0].Pitch.MidiNote.Should().Be(65);
    }

    // ---- IO failure -------------------------------------------------------------------------------

    [Fact]
    public void AnUnwritablePathThrowsADomainExportException()
    {
        Note[] notes = [new Note(Pitch.FromMidi(60), 0, 480, 100)];
        RestyleResult result = BuildResult([Track(0, 0, notes)]);

        // A directory that does not exist is a genuine IO failure, not something the user's musical
        // data caused - it must throw rather than come back as a refused ExportResult.
        string badPath = Path.Combine(
            Path.GetTempPath(), $"midirestyle-missing-dir-{Guid.NewGuid():N}", "out.mid");

        Action export = () => MidiFileExporter.Export(result, badPath);

        export.Should().Throw<MidiFileExportException>()
            .Which.FilePath.Should().Be(badPath);
    }

    // ---- fixture plumbing -------------------------------------------------------------------------

    private static TrackInfo TrackInfoFrom(
        int trackIndex, int channel, IReadOnlyList<Note> notes, string? name = null, int? program = null) =>
        new()
        {
            TrackIndex = trackIndex,
            Channel = channel,
            Name = name,
            ProgramNumber = program,
            Notes = notes,
        };

    /// <summary>
    /// A restyled track paired with the source <see cref="TrackInfo"/> the exporter reads its name
    /// and program number from - the exporter keys that lookup on (track, channel), so the two must
    /// travel together.
    /// </summary>
    private static (TrackInfo Source, RestyledTrack Restyled) Track(
        int trackIndex, int channel, IReadOnlyList<Note> notes, string? name = null, int? program = null)
    {
        TrackInfo source = TrackInfoFrom(trackIndex, channel, notes, name, program);
        RestyledTrack restyled = new(trackIndex, channel, notes, WasRestyled: true);
        return (source, restyled);
    }

    /// <summary>
    /// Builds a one-source-track-per-restyled-track project and result together, so tests that do not
    /// care about the source project's shape can supply just the restyled tracks (with their name and
    /// program, via <see cref="Track"/>).
    /// </summary>
    private static RestyleResult BuildResult(
        IReadOnlyList<(TrackInfo Source, RestyledTrack Restyled)> tracks, Scale? targetScale = null)
    {
        MidiProject source = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [.. tracks.Select(t => t.Source)],
        };

        return BuildResult(source, [.. tracks.Select(t => t.Restyled)], targetScale);
    }

    private static RestyleResult BuildResult(
        MidiProject source, IReadOnlyList<RestyledTrack> tracks, Scale? targetScale = null) => new()
    {
        Source = source,
        Settings = new RestyleSettings
        {
            TargetScale = targetScale ?? Chromatic,
            TargetTonic = Pitch.FromMidi(60),
        },
        Tracks = tracks,
    };

    private MidiProject ExportAndReload(RestyleResult result)
    {
        string path = NewTempPath("export");
        ExportResult export = MidiFileExporter.Export(result, path);
        export.Success.Should().BeTrue(export.Message);
        return MidiFileLoader.Load(path);
    }

    private string NewTempPath(string hint)
    {
        string path = Path.Combine(Path.GetTempPath(), $"midirestyle-{hint}-{Guid.NewGuid():N}.mid");
        _tempFiles.Add(path);
        return path;
    }
}
