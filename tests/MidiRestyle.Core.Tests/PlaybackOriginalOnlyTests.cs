using Melanchall.DryWetMidi.Core;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;
using DomainNote = MidiRestyle.Core.Model.Note;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// Covers playing a file before any target scale has been chosen.
/// </summary>
public class PlaybackOriginalOnlyTests
{
    private static MidiProject Project(params int[] notes) => new()
    {
        FilePath = @"C:\music\test.mid",
        Format = MidiFileFormatKind.MultiTrack,
        Division = new TicksPerQuarterNote(96),
        Tracks =
        [
            new TrackInfo
            {
                TrackIndex = 0,
                Channel = 0,
                Name = "Piano",
                ProgramNumber = 0,
                Notes = [.. notes.Select((n, i) => new DomainNote(Pitch.FromMidi(n), i * 96, 96, 88))],
            },
            new TrackInfo
            {
                TrackIndex = 1,
                Channel = TrackInfo.DrumChannel,
                Notes = [new DomainNote(Pitch.FromMidi(36), 0, 48, 100)],
            },
        ],
        TempoMap = [new TempoChange(0, 400_000), new TempoChange(384, 500_000)],
        TimeSignatures = [new TimeSignatureChange(0, 3, 4)],
    };

    private static MidiFile Read(byte[] bytes)
    {
        using MemoryStream stream = new(bytes);
        return MidiFile.Read(stream);
    }

    [Fact]
    public void BuildsWithoutATargetScaleHavingBeenChosen()
    {
        PlaybackBuildResult built = PlaybackSequenceBuilder.BuildOriginalOnly(Project(60, 62, 64));

        built.Success.Should().BeTrue(built.Message);
        built.Sequences!.Allocation.Should().BeNull("nothing is transformed, so nothing is bent");
        built.Sequences.RestyledChannels.Should().BeEmpty();
    }

    [Fact]
    public void EveryPitchSurvivesUntouched()
    {
        PlaybackSequences sequences =
            PlaybackSequenceBuilder.BuildOriginalOnly(Project(60, 62, 64, 65, 67)).Sequences!;

        int[] pitches = [.. Read(sequences.Original)
            .GetTrackChunks()
            .SelectMany(c => c.Events.OfType<NoteOnEvent>())
            .Select(n => (int)n.NoteNumber)
            .Order()];

        // Array form, not params: Equal(params T[]) would swallow a because-string as an expected
        // element and then complain the collection is one item short.
        pitches.Should().Equal([36, 60, 62, 64, 65, 67],
            "the drum note rides along unchanged too");
    }

    [Fact]
    public void BothSidesAreTheSameBytesSoAStrayToggleIsHarmless()
    {
        PlaybackSequences sequences =
            PlaybackSequenceBuilder.BuildOriginalOnly(Project(60, 62)).Sequences!;

        sequences.Restyled.Should().Equal(sequences.Original);
    }

    [Fact]
    public void NothingBends()
    {
        PlaybackSequences sequences =
            PlaybackSequenceBuilder.BuildOriginalOnly(Project(60, 62, 64)).Sequences!;

        Read(sequences.Original).GetTrackChunks()
            .SelectMany(c => c.Events.OfType<PitchBendEvent>())
            .Should().BeEmpty();
    }

    [Fact]
    public void TimingMetadataSurvives()
    {
        PlaybackSequences sequences =
            PlaybackSequenceBuilder.BuildOriginalOnly(Project(60, 62)).Sequences!;

        MidiFile file = Read(sequences.Original);

        ((TicksPerQuarterNoteTimeDivision)file.TimeDivision).TicksPerQuarterNote.Should().Be(96);
        file.GetTrackChunks().SelectMany(c => c.Events.OfType<SetTempoEvent>())
            .Select(t => t.MicrosecondsPerQuarterNote)
            .Should().Equal([400_000, 500_000], "a multi-tempo map must survive intact");
        file.GetTrackChunks().SelectMany(c => c.Events.OfType<TimeSignatureEvent>())
            .Should().ContainSingle();
    }

    /// <summary>
    /// The identity transform maps nothing, so the engine must not demand a mapper it will never use.
    /// </summary>
    /// <remarks>
    /// This is a regression test. <c>RestyleEngine</c> built its mapper eagerly, and
    /// <c>ScaleDegreeMapper</c> throws without a source scale - so a transform that touches no notes
    /// failed for want of an input it would never have read. Nothing in the suite had previously asked
    /// the engine to transform nothing.
    /// </remarks>
    [Fact]
    public void RestylingNothingNeedsNoSourceScale()
    {
        Scale target = new(
            "t.slendro", "Slendro", "Gamelan", "Southeast Asia",
            [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

        MidiProject project = Project(60, 62, 64);

        RestyleSettings excludeEverything = new()
        {
            TargetScale = target,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = null,
            Excluded = new HashSet<(int Track, int Channel)>(
                project.Tracks.Select(t => (t.TrackIndex, t.Channel))),
        };

        Action act = () => RestyleEngine.Restyle(project, excludeEverything);

        act.Should().NotThrow("a run that maps nothing must not require a mapper's inputs");
    }

    [Fact]
    public void RestylingSomethingStillRequiresASourceScaleForDegreeMapping()
    {
        Scale target = new(
            "t.gong", "Gong", "Chinese Wusheng", "East Asia",
            [0, 200, 400, 700, 900], "Test fixture, 2026");

        Action act = () => RestyleEngine.Restyle(Project(60, 62), new RestyleSettings
        {
            TargetScale = target,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = null,
        });

        act.Should().Throw<ArgumentException>(
            "degree mapping genuinely cannot work without one - the laziness must not hide that");
    }
}
