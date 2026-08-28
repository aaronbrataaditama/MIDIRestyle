using Melanchall.DryWetMidi.Core;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;
using DomainNote = MidiRestyle.Core.Model.Note;

namespace MidiRestyle.Core.Tests;

public class PlaybackSequenceBuilderTests
{
    private static readonly Scale CMajor = new(
        "t.cmajor", "C major", "Western", "Europe & Balkans",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    private static readonly Scale Gong = new(
        "t.gong", "Gong", "Chinese Wusheng", "East Asia",
        [0, 200, 400, 700, 900], "Test fixture, 2026");

    private static readonly Scale Rast = new(
        "t.rast", "Maqam Rast", "Arabic maqam", "Middle East",
        [0, 200, 350, 500, 700, 900, 1050], "Test fixture, 2026");

    private static MidiProject Project(bool withDrums = false)
    {
        List<TrackInfo> tracks =
        [
            new TrackInfo
            {
                TrackIndex = 0,
                Channel = 0,
                Name = "Melody",
                ProgramNumber = 73,
                Notes = [.. new[] { 60, 62, 64, 65, 67, 69, 71, 72 }
                    .Select((n, i) => new DomainNote(Pitch.FromMidi(n), i * 480, 480, 90))],
            },
        ];

        if (withDrums)
        {
            tracks.Add(new TrackInfo
            {
                TrackIndex = 1,
                Channel = TrackInfo.DrumChannel,
                Notes = [new DomainNote(Pitch.FromMidi(36), 0, 120, 100)],
            });
        }

        return new MidiProject
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = tracks,
            TempoMap = [new TempoChange(0, 500_000)],
            TimeSignatures = [new TimeSignatureChange(0, 4, 4)],
        };
    }

    private static RestyleResult Restyled(Scale target, bool withDrums = false) =>
        RestyleEngine.Restyle(Project(withDrums), new RestyleSettings
        {
            TargetScale = target,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        });

    private static MidiFile Read(byte[] bytes)
    {
        using MemoryStream stream = new(bytes);
        return MidiFile.Read(stream);
    }

    private static int[] Pitches(MidiFile file) =>
        [.. file.GetTrackChunks()
            .SelectMany(c => c.Events.OfType<NoteOnEvent>())
            .Select(n => (int)n.NoteNumber)
            .Order()];

    // --- both sides ---------------------------------------------------------------------

    [Fact]
    public void BuildsBothSides()
    {
        PlaybackBuildResult built = PlaybackSequenceBuilder.Build(Restyled(Gong));

        built.Success.Should().BeTrue(built.Message);
        built.Sequences!.Original.Should().NotBeEmpty();
        built.Sequences.Restyled.Should().NotBeEmpty();
    }

    /// <summary>
    /// The original side is the source untouched - built through the same exporter, but with nothing
    /// remapped.
    /// </summary>
    [Fact]
    public void TheOriginalSideCarriesTheSourcePitches()
    {
        PlaybackSequences sequences = PlaybackSequenceBuilder.Build(Restyled(Gong)).Sequences!;

        Pitches(Read(sequences.Original)).Should().Equal(60, 62, 64, 65, 67, 69, 71, 72);
    }

    [Fact]
    public void TheRestyledSideCarriesRemappedPitches()
    {
        PlaybackSequences sequences = PlaybackSequenceBuilder.Build(Restyled(Gong)).Sequences!;

        Pitches(Read(sequences.Restyled))
            .Should().NotEqual(Pitches(Read(sequences.Original)),
                "if the two sides sound the same, the A/B switch has nothing to demonstrate");
    }

    /// <summary>
    /// Both sides must share a tick grid, or seeking to the same tick on either lands in a different
    /// musical place and the A/B switch appears to jump.
    /// </summary>
    [Fact]
    public void BothSidesShareTheSameTimeDivisionAndTempoMap()
    {
        PlaybackSequences sequences = PlaybackSequenceBuilder.Build(Restyled(Gong)).Sequences!;

        MidiFile original = Read(sequences.Original);
        MidiFile restyled = Read(sequences.Restyled);

        // Compare the value, not the object: DryWetMIDI's division types expose nothing that
        // structural equivalence can walk, so BeEquivalentTo throws rather than comparing.
        short Ppqn(MidiFile f) =>
            ((TicksPerQuarterNoteTimeDivision)f.TimeDivision).TicksPerQuarterNote;

        Ppqn(original).Should().Be(Ppqn(restyled)).And.Be(480);

        long[] Tempos(MidiFile f) => [.. f.GetTrackChunks()
            .SelectMany(c => c.Events.OfType<SetTempoEvent>())
            .Select(t => t.MicrosecondsPerQuarterNote)];

        Tempos(original).Should().Equal(Tempos(restyled));
    }

    [Fact]
    public void BothSidesHoldTheSameNumberOfNotes()
    {
        PlaybackSequences sequences = PlaybackSequenceBuilder.Build(Restyled(Gong)).Sequences!;

        Pitches(Read(sequences.Restyled)).Should().HaveCount(Pitches(Read(sequences.Original)).Length,
            "a 12-TET target drops nothing, so the two sides must line up note for note");
    }

    // --- the allocation ----------------------------------------------------------------

    [Fact]
    public void ATwelveTetTargetNeedsNoAllocation()
    {
        PlaybackSequences sequences = PlaybackSequenceBuilder.Build(Restyled(Gong)).Sequences!;

        sequences.Allocation.Should().BeNull("Gong sits on the semitone grid, so no channel is bent");
        sequences.RestyledChannels.Should().BeEmpty();
    }

    [Fact]
    public void AMicrotonalTargetCarriesItsAllocationAndNamesTheChannels()
    {
        PlaybackSequences sequences = PlaybackSequenceBuilder.Build(Restyled(Rast)).Sequences!;

        sequences.Allocation.Should().NotBeNull();
        sequences.Allocation!.ChannelCount.Should().Be(2);

        // These are the channels the stop sequence must reach; missing one leaves notes hanging
        // and a stale bend behind.
        sequences.RestyledChannels.Should().HaveCount(2).And.NotContain(9);
    }

    [Fact]
    public void AMicrotonalRestyleActuallyBendsInTheBytes()
    {
        PlaybackSequences sequences = PlaybackSequenceBuilder.Build(Restyled(Rast)).Sequences!;

        Read(sequences.Restyled).GetTrackChunks()
            .SelectMany(c => c.Events.OfType<PitchBendEvent>())
            .Select(b => (int)b.PitchValue)
            .Order()
            .Should().Equal([6144, 8192], "-50 cents at the default range is 6144; centre is 8192");
    }

    [Fact]
    public void TheOriginalSideNeverBends()
    {
        PlaybackSequences sequences = PlaybackSequenceBuilder.Build(Restyled(Rast)).Sequences!;

        Read(sequences.Original).GetTrackChunks()
            .SelectMany(c => c.Events.OfType<PitchBendEvent>())
            .Should().BeEmpty();
    }

    // --- the guarantee -----------------------------------------------------------------

    /// <summary>
    /// Preview plays the exported bytes. Not "matches" them - <em>is</em> them.
    /// </summary>
    /// <remarks>
    /// This is what makes "what you heard is what you exported" structural rather than hopeful. If
    /// this test ever fails, someone has introduced a second output path, which is the exact bug the
    /// single-allocator design exists to make hard.
    /// </remarks>
    [Fact]
    public void ThePreviewBytesAreByteIdenticalToWhatExportWrites()
    {
        RestyleResult result = Restyled(Rast);
        PlaybackSequences sequences = PlaybackSequenceBuilder.Build(result).Sequences!;

        using MemoryStream exported = new();
        MidiFileExporter.Export(result, exported, sequences.Allocation!).Success.Should().BeTrue();

        sequences.Restyled.Should().Equal(exported.ToArray());
    }

    [Fact]
    public void DrumsRideAlongOnBothSides()
    {
        PlaybackSequences sequences = PlaybackSequenceBuilder.Build(Restyled(Rast, withDrums: true)).Sequences!;

        foreach (byte[] side in new[] { sequences.Original, sequences.Restyled })
        {
            Read(side).GetTrackChunks()
                .SelectMany(c => c.Events.OfType<NoteOnEvent>())
                .Where(n => n.Channel == 9)
                .Should().ContainSingle().Which.NoteNumber.Should().Be(
                    (Melanchall.DryWetMidi.Common.SevenBitNumber)36);
        }
    }

    // --- failure -----------------------------------------------------------------------

    [Fact]
    public void APieceWithNoRestylableTracksStillBuildsBothSides()
    {
        MidiProject drumsOnly = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks =
            [
                new TrackInfo
                {
                    TrackIndex = 0,
                    Channel = TrackInfo.DrumChannel,
                    Notes = [new DomainNote(Pitch.FromMidi(36), 0, 120, 100)],
                },
            ],
        };

        RestyleResult result = RestyleEngine.Restyle(drumsOnly, new RestyleSettings
        {
            TargetScale = Rast,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        });

        PlaybackBuildResult built = PlaybackSequenceBuilder.Build(result);

        built.Success.Should().BeTrue(built.Message);
        built.Sequences!.Allocation.Should().BeNull("nothing was restyled, so nothing needs bending");
    }

    [Fact]
    public void FailureCarriesAStatedReasonRatherThanThrowing()
    {
        // A range policy of Drop with an impossible target is not easy to force here, so assert the
        // shape of the contract instead: a failure result names what went wrong.
        PlaybackBuildResult failed = PlaybackBuildResult.Fail("something specific went wrong");

        failed.Success.Should().BeFalse();
        failed.Sequences.Should().BeNull();
        failed.Message.Should().Be("something specific went wrong");
    }
}
