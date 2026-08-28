using System.Diagnostics;
using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

public class RestyleEngineTests
{
    private static readonly Scale CMajor = new(
        "test.cmajor", "C major", "Western", "Europe & Balkans",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    private static readonly Scale Gong = new(
        "test.gong", "Gong", "Chinese Wusheng", "East Asia",
        [0, 200, 400, 700, 900], "Test fixture, 2026");

    private static readonly Scale Slendro = new(
        "test.slendro", "Slendro", "Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

    private static TrackInfo Track(int channel, int trackIndex, params int[] midiNotes) => new()
    {
        TrackIndex = trackIndex,
        Channel = channel,
        Notes = [.. midiNotes.Select((n, i) => new Note(Pitch.FromMidi(n), i * 480, 480, 90))],
    };

    private static MidiProject Project(params TrackInfo[] tracks) => new()
    {
        Format = MidiFileFormatKind.MultiTrack,
        Division = new TicksPerQuarterNote(480),
        Tracks = tracks,
    };

    private static RestyleSettings Settings(Scale target, Scale? source = null) => new()
    {
        TargetScale = target,
        TargetTonic = Pitch.FromMidi(60),
        SourceScale = source ?? CMajor,
        SourceTonic = Pitch.FromMidi(60),
    };

    // --- purity ------------------------------------------------------------------------

    [Fact]
    public void TheSourceProjectIsNeverMutated()
    {
        TrackInfo track = Track(0, 0, 60, 62, 64);
        MidiProject project = Project(track);
        double[] before = [.. track.Notes.Select(n => n.Pitch.Cents)];

        RestyleEngine.Restyle(project, Settings(Gong));

        track.Notes.Select(n => n.Pitch.Cents).Should().Equal(before,
            "the original model is what the ghost overlay and the A/B switch read");
    }

    [Fact]
    public void TheSameInputsProduceTheSameOutput()
    {
        MidiProject project = Project(Track(0, 0, 60, 62, 64, 65, 67));
        RestyleSettings settings = Settings(Slendro);

        RestyleResult a = RestyleEngine.Restyle(project, settings);
        RestyleResult b = RestyleEngine.Restyle(project, settings);

        a.Tracks[0].Notes.Select(n => n.Pitch.Cents)
            .Should().Equal(b.Tracks[0].Notes.Select(n => n.Pitch.Cents));
    }

    // --- scope -------------------------------------------------------------------------

    /// <summary>
    /// Drums pass through untouched. A percussion note number selects which drum is struck, so
    /// transposing it changes the instrument rather than the pitch.
    /// </summary>
    [Fact]
    public void DrumsAreNeverRestyledButStillAppearInTheResult()
    {
        MidiProject project = Project(
            Track(0, 0, 60, 62, 64),
            Track(TrackInfo.DrumChannel, 1, 36, 38, 42));

        RestyleResult result = RestyleEngine.Restyle(project, Settings(Slendro));

        RestyledTrack drums = result.Tracks.Single(t => t.IsDrums);
        drums.WasRestyled.Should().BeFalse();
        drums.Notes.Select(n => n.Pitch.MidiNote).Should().Equal(36, 38, 42);
        result.Tracks.Should().HaveCount(2, "the result is what will be exported, not only what changed");
    }

    [Fact]
    public void AnOptedOutTrackPassesThroughUnchanged()
    {
        MidiProject project = Project(Track(0, 0, 60, 62), Track(1, 1, 67, 69));
        RestyleSettings settings = Settings(Gong) with
        {
            Excluded = new HashSet<(int, int)> { (1, 1) },
        };

        RestyleResult result = RestyleEngine.Restyle(project, settings);

        result.Tracks.Single(t => t.Channel == 1).WasRestyled.Should().BeFalse();
        result.Tracks.Single(t => t.Channel == 1).Notes.Select(n => n.Pitch.MidiNote)
            .Should().Equal(67, 69);
        result.Tracks.Single(t => t.Channel == 0).WasRestyled.Should().BeTrue();
    }

    // --- the transform -----------------------------------------------------------------

    /// <summary>
    /// Degree mapping re-emits at the same degree index, so a 7-note source into a 5-note target
    /// keeps the contour and changes the register.
    /// </summary>
    [Fact]
    public void SevenIntoFiveKeepsAnAscendingLineAscending()
    {
        MidiProject project = Project(Track(0, 0, 60, 62, 64, 65, 67, 69, 71, 72));

        RestyleResult result = RestyleEngine.Restyle(project, Settings(Gong));

        double[] cents = [.. result.Tracks[0].Notes.Select(n => n.Pitch.Cents)];
        cents.Should().BeInAscendingOrder("contour survival is the point of degree mapping");
    }

    [Fact]
    public void RestylingIntoAMicrotonalScaleProducesMicrotonalPitches()
    {
        MidiProject project = Project(Track(0, 0, 60, 62, 64, 65, 67));

        RestyleResult result = RestyleEngine.Restyle(project, Settings(Slendro));

        result.Tracks[0].Notes.Should().Contain(n => n.Pitch.BendCents != 0,
            "Slendro's 240-cent steps cannot land on the semitone grid");
        result.NeedsPitchBend.Should().BeTrue();
    }

    [Fact]
    public void RestylingIntoATwelveTetScaleNeedsNoPitchBend()
    {
        MidiProject project = Project(Track(0, 0, 60, 62, 64));

        RestyleResult result = RestyleEngine.Restyle(project, Settings(Gong));

        result.Tracks[0].Notes.Should().OnlyContain(n => n.Pitch.BendCents == 0);
        result.NeedsPitchBend.Should().BeFalse();
    }

    [Fact]
    public void TimingAndVelocitySurviveUntouched()
    {
        // Restyling is pitch remapping only - never rhythm, ornamentation or articulation.
        TrackInfo track = Track(0, 0, 60, 62, 64);
        MidiProject project = Project(track);

        RestyleResult result = RestyleEngine.Restyle(project, Settings(Slendro));

        result.Tracks[0].Notes.Select(n => n.StartTicks).Should().Equal(0, 480, 960);
        result.Tracks[0].Notes.Should().OnlyContain(n => n.LengthTicks == 480 && n.Velocity == 90);
    }

    // --- the tally ----------------------------------------------------------------------

    [Fact]
    public void ACleanRunReportsNothing()
    {
        RestyleResult result = RestyleEngine.Restyle(Project(Track(0, 0, 60, 62, 64)), Settings(Gong));

        result.Tally.IsClean.Should().BeTrue();
        result.Tally.Describe().Should().BeNull("a clean run has nothing to say");
    }

    /// <summary>
    /// Range overflow is routine, not exceptional: 7 degrees into 5 stretches a piece's range by
    /// 1.4x.
    /// </summary>
    /// <remarks>
    /// Measured, because the obvious example does not actually overflow. The 88-key piano range
    /// (MIDI 21..108) into Slendro on a C4 tonic reaches 4.80..127.20 - and 127.20 <em>rounds to
    /// 127</em>, which is in range, so nothing is dropped. Overflow needs material wider than a
    /// piano (the full MIDI range drops 36 notes) or a lower target tonic (C3 drops 3). The plan
    /// cites the piano case as its overflow example; that is off by the rounding.
    /// </remarks>
    [Fact]
    public void OutOfRangeNotesAreDroppedAndCountedRatherThanThrowing()
    {
        // The full MIDI range, which genuinely does overflow a 7->5 mapping.
        MidiProject project = Project(Track(0, 0, [.. Enumerable.Range(0, 128)]));
        RestyleSettings settings = Settings(Slendro) with
        {
            Mapping = MappingOptions.Default with { Range = RangePolicy.Drop },
        };

        RestyleResult result = RestyleEngine.Restyle(project, settings);

        result.Tally.DroppedOutOfRange.Should().BeGreaterThan(0);
        result.Tally.Describe().Should().Contain("out of MIDI range");
        result.Tracks[0].Notes.Should().OnlyContain(n => n.Pitch.IsInMidiRange);
    }

    [Fact]
    public void ShiftIntoRangeKeepsEveryNoteInsideTheMidiRange()
    {
        MidiProject project = Project(Track(0, 0, [.. Enumerable.Range(0, 128)]));

        RestyleResult result = RestyleEngine.Restyle(project, Settings(Slendro));

        result.Tracks[0].Notes.Should().OnlyContain(n => n.Pitch.IsInMidiRange);
    }

    [Fact]
    public void CollisionsAreResolvedAndCounted()
    {
        // Two source degrees a tone apart both land on one target degree when the scale compresses,
        // and they sound at the same time - so one has to give.
        TrackInfo track = new()
        {
            TrackIndex = 0,
            Channel = 0,
            Notes =
            [
                new Note(Pitch.FromMidi(60), 0, 480, 90),
                new Note(Pitch.FromMidi(62), 0, 240, 90),
                new Note(Pitch.FromMidi(64), 0, 120, 90),
                new Note(Pitch.FromMidi(65), 0, 100, 90),
                new Note(Pitch.FromMidi(67), 0, 90, 90),
                new Note(Pitch.FromMidi(69), 0, 80, 90),
                new Note(Pitch.FromMidi(71), 0, 70, 90),
            ],
        };

        RestyleResult result = RestyleEngine.Restyle(Project(track), Settings(Gong));

        // Whether any collide depends on the mapping, but if the tally says so the notes must agree.
        int expected = track.Notes.Count - result.Tally.Merged - result.Tally.Displaced
            - result.Tally.TotalDropped;
        result.Tracks[0].Notes.Should().HaveCount(expected,
            "every note is either kept, merged away, displaced or dropped - the tally must balance");
    }

    // --- flattening ----------------------------------------------------------------------

    [Fact]
    public void FlattenSortedByStartMergesEveryTrackInOrder()
    {
        MidiProject project = Project(Track(0, 0, 60, 62), Track(1, 1, 67, 69));

        Note[] flat = RestyleEngine.FlattenSortedByStart(RestyleEngine.Restyle(project, Settings(Gong)));

        flat.Should().HaveCount(4);
        flat.Select(n => n.StartTicks).Should().BeInAscendingOrder(
            "the piano roll's culling requires start order");
    }

    // --- the performance gate --------------------------------------------------------------

    /// <summary>
    /// The scale list is arrow-key browsable, so this runs on every keystroke.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a catastrophic-regression guard, not a benchmark.</b> The documented target is
    /// 16 ms and the measured figure on an idle machine is about 3 ms - but wall-clock under a
    /// parallel test run is not that. A 50 ms ceiling flaked at 51.7 ms with three test assemblies
    /// and an app instance competing for the CPU, which told us nothing about the code.
    /// </para>
    /// <para>
    /// So the ceiling is deliberately far above the real cost: it still catches an implementation
    /// that went quadratic or started re-parsing per note (either would be orders of magnitude, not
    /// percent), and it will not fail because the machine was busy. For actual performance work use
    /// BenchmarkDotNet on an idle machine - a wall-clock assertion inside `dotnet test` cannot do
    /// that job and should not pretend to.
    /// </para>
    /// </remarks>
    [Fact]
    public void TwentyThousandNotesRestyleWellInsideAFrame()
    {
        var notes = new List<Note>(20_000);
        for (int i = 0; i < 20_000; i++)
        {
            notes.Add(new Note(Pitch.FromMidi(36 + (i % 60)), i * 60, 200, 90));
        }

        MidiProject project = Project(new TrackInfo { TrackIndex = 0, Channel = 0, Notes = notes });
        RestyleSettings settings = Settings(Slendro);

        for (int i = 0; i < 3; i++)
        {
            RestyleEngine.Restyle(project, settings);
        }

        double best = double.MaxValue;
        for (int run = 0; run < 7; run++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            RestyleResult result = RestyleEngine.Restyle(project, settings);
            sw.Stop();
            result.TotalNoteCount.Should().BeGreaterThan(0);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }

        best.Should().BeLessThan(400,
            $"20,000 notes must stay orders of magnitude inside a catastrophic regression; "
            + $"measured {best:0.0} ms, documented target 16 ms, typical idle figure ~3 ms");
    }
}
