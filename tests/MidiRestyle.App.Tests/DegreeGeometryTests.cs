using MidiRestyle.App.Controls;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The scale wheel's arithmetic.
/// </summary>
/// <remarks>
/// The numeral-layout tests this file used to hold are gone with the design they described: the
/// degree view is a circular wheel now, not a row of cipher numerals with octave dots and duration
/// underlines. What replaces them is the geometry that makes the wheel worth having - a degree sits
/// at its <em>true</em> cents angle, so the tuning itself is visible - which is why the Slendro and
/// Rast cases below assert actual angles rather than merely that five markers exist.
/// </remarks>
public class DegreeGeometryTests
{
    private static readonly WheelLayout Layout = new(100, 100, 100);

    /// <summary>Gamelan Slendro: five near-even steps, none of them a 12-TET interval.</summary>
    private static readonly double[] Slendro = [0, 240, 480, 720, 960];

    /// <summary>Maqam Rast: visibly uneven, with neutral degrees between the Western positions.</summary>
    private static readonly double[] Rast = [0, 200, 350, 500, 700, 900, 1050];

    // --- cents to angle --------------------------------------------------------------------------

    [Fact]
    public void TheTonicSitsAtTwelveOClock() =>
        DegreeGeometry.AngleDegreesAt(0).Should().Be(0);

    [Fact]
    public void ATritoneSitsAtTheBottomOfTheWheel() =>
        DegreeGeometry.AngleDegreesAt(600).Should().Be(180);

    [Fact]
    public void AQuarterOfAnOctaveIsAQuarterTurn() =>
        DegreeGeometry.AngleDegreesAt(300).Should().Be(90);

    [Fact]
    public void AWholeOctaveWrapsBackToTheTop() =>
        DegreeGeometry.AngleDegreesAt(1200).Should().Be(0);

    [Fact]
    public void APitchAboveTheOctaveWrapsIntoIt() =>
        DegreeGeometry.AngleDegreesAt(1500).Should().BeApproximately(90, 1e-9);

    [Fact]
    public void APitchBelowTheTonicWrapsUpwardRatherThanGoingNegative()
    {
        // C# keeps the sign of the dividend, so a naive `%` puts a bass note anticlockwise of the
        // top instead of just below it. Any bass line hits this on its first note.
        DegreeGeometry.AngleDegreesAt(-100).Should().BeApproximately(330, 1e-9);
        DegreeGeometry.AngleDegreesAt(-1200).Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void AnglesIncreaseClockwise()
    {
        WheelPoint quarter = DegreeGeometry.PointAtCents(Layout, 300, 50);

        // A quarter turn clockwise from the top is due right, which in screen coordinates is
        // +X at the centre's own Y.
        quarter.X.Should().BeApproximately(150, 1e-9);
        quarter.Y.Should().BeApproximately(100, 1e-9);
    }

    [Fact]
    public void TheTopOfTheWheelIsDirectlyAboveTheCentre()
    {
        WheelPoint top = DegreeGeometry.PointAtCents(Layout, 0, 60);

        top.X.Should().BeApproximately(100, 1e-9);
        top.Y.Should().BeApproximately(40, 1e-9);
    }

    [Fact]
    public void EveryPointSitsExactlyItsRadiusFromTheCentre()
    {
        foreach (double cents in Rast)
        {
            WheelPoint at = DegreeGeometry.PointAtCents(Layout, cents, 73);
            double dx = at.X - Layout.CenterX;
            double dy = at.Y - Layout.CenterY;

            Math.Sqrt((dx * dx) + (dy * dy)).Should().BeApproximately(73, 1e-9);
        }
    }

    // --- the whole point: true angles, not even slots ----------------------------------------------

    [Fact]
    public void SlendroComesOutNearlyButNotExactlyEvenlySpaced()
    {
        double[] angles = [.. Slendro.Select(DegreeGeometry.AngleDegreesAt)];

        angles.Should().Equal([0, 72, 144, 216, 288], "240 cents is exactly a fifth of an octave");

        // Five even slots would be 72 degrees apart, and Slendro's steps genuinely are - what makes
        // it microtonal is that none of those angles lands on a 12-TET tick (every 30 degrees)
        // except the tonic.
        double[] offGrid = [.. angles.Skip(1).Select(a => Math.Abs((a % 30) - 15))];
        offGrid.Should().AllSatisfy(o => o.Should().BeLessThan(15));
    }

    [Fact]
    public void RastComesOutVisiblyUneven()
    {
        double[] angles = [.. Rast.Select(DegreeGeometry.AngleDegreesAt)];

        angles.Should().Equal([0, 60, 105, 150, 210, 270, 315], "each degree sits at its own cents angle");

        double[] gaps = [.. angles.Zip(angles.Skip(1), (a, b) => b - a)];
        gaps.Should().Equal([60, 45, 45, 60, 60, 45], "Rast's steps are not all the same size");

        // Seven even slots would be 51.43 degrees apart. Rast's widest gap is a third again its
        // narrowest, which is exactly the unevenness the wheel exists to show.
        gaps.Max().Should().BeGreaterThan(gaps.Min() * 1.3);
    }

    [Fact]
    public void ARastNeutralDegreeFallsBetweenTwoTwelveTetTicks()
    {
        // 350 cents is the half-flat third: dead between the 12-TET tick at 300 and the one at 400,
        // so it can sit on neither. This is the contrast the reference ticks are drawn for.
        double neutral = DegreeGeometry.AngleDegreesAt(350);

        neutral.Should().BeApproximately(105, 1e-9);
        (neutral % 30).Should().BeApproximately(15, 1e-9);
    }

    [Fact]
    public void APlainMajorScaleLandsExactlyOnTheTwelveTetTicks()
    {
        double[] major = [0, 200, 400, 500, 700, 900, 1100];

        foreach (double cents in major)
        {
            (DegreeGeometry.AngleDegreesAt(cents) % 30).Should().BeApproximately(0, 1e-9);
        }
    }

    [Fact]
    public void ADegreesDeviationWhiskerRunsBackToItsNearestSemitone()
    {
        DegreeGeometry.NearestTwelveTetCents(350).Should().Be(400, "ties round away from zero");
        DegreeGeometry.NearestTwelveTetCents(240).Should().Be(200);
        DegreeGeometry.NearestTwelveTetCents(700).Should().Be(700);
    }

    // --- octave to radius --------------------------------------------------------------------------

    [Fact]
    public void TheTonicsOwnOctaveSitsOnTheBaseRadius() =>
        DegreeGeometry.RadiusForOctave(0, 50, 10).Should().Be(50);

    [Fact]
    public void RadiusIncreasesWithOctave()
    {
        double[] radii =
        [
            DegreeGeometry.RadiusForOctave(-2, 50, 10),
            DegreeGeometry.RadiusForOctave(-1, 50, 10),
            DegreeGeometry.RadiusForOctave(0, 50, 10),
            DegreeGeometry.RadiusForOctave(1, 50, 10),
            DegreeGeometry.RadiusForOctave(2, 50, 10),
        ];

        radii.Should().Equal([30, 40, 50, 60, 70], "inner rings are lower octaves, outer higher");
        radii.Should().BeInAscendingOrder();
    }

    [Fact]
    public void ABassNoteAndAMelodyNoteOnTheSameDegreeDoNotCollide()
    {
        WheelPoint bass = DegreeGeometry.PointAtCents(
            Layout, 700, DegreeGeometry.RadiusForOctave(Layout, -1));
        WheelPoint melody = DegreeGeometry.PointAtCents(
            Layout, 700, DegreeGeometry.RadiusForOctave(Layout, 1));

        bass.Should().NotBe(melody);
    }

    [Fact]
    public void ExtremeOctavesSaturateRatherThanRunningOffTheControl()
    {
        DegreeGeometry.RadiusForOctave(5, 50, 10)
            .Should().Be(DegreeGeometry.RadiusForOctave(DegreeGeometry.MaxOctaveRings, 50, 10));

        DegreeGeometry.RadiusForOctave(-5, 50, 10)
            .Should().Be(DegreeGeometry.RadiusForOctave(-DegreeGeometry.MaxOctaveRings, 50, 10));
    }

    // --- layout ---------------------------------------------------------------------------------

    [Fact]
    public void TheWheelIsCentredBetweenTheHeaderAndTheCaption()
    {
        WheelLayout layout = DegreeGeometry.LayoutFor(400, 300, topInset: 40, bottomInset: 20, padding: 10);

        layout.CenterX.Should().Be(200);
        layout.CenterY.Should().Be(50 + (220 / 2.0));
        layout.Radius.Should().Be(110, "height is the binding dimension here");
        layout.IsUsable.Should().BeTrue();
    }

    [Fact]
    public void APaneTooSmallToDrawInReportsItselfUnusable() =>
        DegreeGeometry.LayoutFor(60, 60, topInset: 30, bottomInset: 20, padding: 10)
            .IsUsable.Should().BeFalse();

    [Fact]
    public void UsabilityIsDecidedAtTheDocumentedRadiusThreshold()
    {
        new WheelLayout(0, 0, DegreeGeometry.MinimumUsableRadius).IsUsable.Should().BeTrue();
        new WheelLayout(0, 0, DegreeGeometry.MinimumUsableRadius - 0.01).IsUsable.Should().BeFalse();
    }

    [Fact]
    public void ADegeneratePaneSmallerThanItsOwnInsetsReportsUnusableRatherThanThrowing()
    {
        // A pane can be narrower than its own padding while a splitter is being dragged - the
        // arithmetic must produce a negative radius cleanly, not throw or return something unusable
        // downstream code has to remember to clamp.
        WheelLayout layout = DegreeGeometry.LayoutFor(5, 5, topInset: 40, bottomInset: 40, padding: 20);

        layout.IsUsable.Should().BeFalse();
        layout.Radius.Should().BeLessThan(0);
    }

    [Fact]
    public void ATrulyDegeneratePaneOfZeroSizeReportsUnusable() =>
        DegreeGeometry.LayoutFor(0, 0, topInset: 0, bottomInset: 0, padding: 0)
            .IsUsable.Should().BeFalse();

    [Fact]
    public void TheWheelsRadiiFollowTheDocumentedProportionsOfItsOverallRadius()
    {
        WheelLayout layout = new(0, 0, 200);

        layout.RingRadius.Should().Be(200 * DegreeGeometry.RingRadiusFraction);
        layout.LabelRadius.Should().Be(200 * DegreeGeometry.LabelRadiusFraction);
        layout.TrailRadius.Should().Be(200 * DegreeGeometry.TrailRadiusFraction);
        layout.OctaveBaseRadius.Should().Be(200 * DegreeGeometry.OctaveBaseFraction);
        layout.OctaveSpacing.Should().Be(200 * DegreeGeometry.OctaveSpacingFraction);
        layout.HubRadius.Should().Be(200 * DegreeGeometry.HubFraction);
    }

    [Fact]
    public void AFractionalCentsValueAnglesExactlyRatherThanAccumulatingRoundingError()
    {
        // Thai's idealised equidistant tuning is exactly 1200/7 cents per step - a non-terminating
        // decimal - which is exactly the input that would expose a rounding slip in the wrap-then-scale
        // arithmetic.
        double step = 1200.0 / 7.0;

        for (int i = 0; i < 7; i++)
        {
            DegreeGeometry.AngleDegreesAt(step * i).Should().BeApproximately(360.0 / 7.0 * i, 1e-9);
        }
    }

    [Fact]
    public void EveryRadiusIsOrderedFromTheHubOutward()
    {
        WheelLayout layout = new(0, 0, 200);

        layout.HubRadius.Should().BeLessThan(DegreeGeometry.RadiusForOctave(layout, -DegreeGeometry.MaxOctaveRings));
        DegreeGeometry.RadiusForOctave(layout, DegreeGeometry.MaxOctaveRings)
            .Should().BeLessThan(layout.RingRadius);
        layout.RingRadius.Should().BeLessThan(layout.LabelRadius);
        layout.LabelRadius.Should().BeLessThan(layout.Radius);
    }

    // --- which notes sound at a tick ----------------------------------------------------------------

    private static NotationEntry Note(
        long start, long duration, int midi, TieState tie = TieState.None, bool chordMember = false) => new()
        {
            Note = new SpelledNote(0, 4, 0, 0),
            SoundingPitch = Pitch.FromMidi(midi),
            Duration = new NotatedDuration(NoteValue.Quarter),
            StartTicks = start,
            DurationTicks = duration,
            IsChordMember = chordMember,
            Tie = tie,
        };

    private static NotationEntry Rest(long start, long duration) => new()
    {
        Duration = new NotatedDuration(NoteValue.Quarter),
        StartTicks = start,
        DurationTicks = duration,
    };

    private static NotationScore ScoreOf(params NotationEntry[] entries) => new()
    {
        Divisions = 480,
        Parts =
        [
            new NotationPart
            {
                Id = "P1",
                Name = "Part",
                TrackIndex = 0,
                Channel = 0,
                StaffCount = 1,
                Clefs = [Clef.Treble],
                Measures =
                [
                    new NotationMeasure
                    {
                        Number = 1,
                        StartTicks = 0,
                        LengthTicks = 1920,
                        BeatsPerMeasure = 4,
                        BeatUnit = 4,
                        Entries = entries,
                    },
                ],
            },
        ],
    };

    /// <summary>A C major triad on beat 1, a rest on beat 2, a single note on beat 3.</summary>
    private static NotationScore ChordThenRestThenNote() => ScoreOf(
        Note(0, 480, 60),
        Note(0, 480, 64, chordMember: true),
        Note(0, 480, 67, chordMember: true),
        Rest(480, 480),
        Note(960, 480, 72));

    [Fact]
    public void AChordReportsEveryOneOfItsNotesAsSounding()
    {
        DegreeWheelIndex index = DegreeWheelIndex.Build(ChordThenRestThenNote());
        WheelNote[] buffer = new WheelNote[8];

        int count = index.Sounding(200, buffer);

        count.Should().Be(3);
        buffer.Take(3).Select(n => n.Cents).Order().Should().Equal([6000, 6400, 6700]);
    }

    [Fact]
    public void ATickInsideARestSoundsNothing()
    {
        DegreeWheelIndex index = DegreeWheelIndex.Build(ChordThenRestThenNote());
        WheelNote[] buffer = new WheelNote[8];

        index.Sounding(700, buffer).Should().Be(0);
    }

    [Fact]
    public void ANoteIsSoundingAtItsOnsetAndSilentAtItsEnd()
    {
        DegreeWheelIndex index = DegreeWheelIndex.Build(ChordThenRestThenNote());
        WheelNote[] buffer = new WheelNote[8];

        index.Sounding(960, buffer).Should().Be(1, "a note sounds from its own onset");
        index.Sounding(1439, buffer).Should().Be(1);
        index.Sounding(1440, buffer).Should().Be(0, "the span is half-open, so it does not overlap the next");
    }

    [Fact]
    public void ALookupNeverWritesPastTheCallersBuffer()
    {
        DegreeWheelIndex index = DegreeWheelIndex.Build(ChordThenRestThenNote());
        WheelNote[] buffer = new WheelNote[2];

        index.Sounding(200, buffer).Should().Be(2, "the chord has three notes and the buffer holds two");
    }

    [Fact]
    public void AnEmptyOrMissingScoreIndexesToNothing()
    {
        DegreeWheelIndex.Build(null).Count.Should().Be(0);
        DegreeWheelIndex.Build(ScoreOf(Rest(0, 1920))).Count.Should().Be(0);

        WheelNote[] buffer = new WheelNote[4];
        DegreeWheelIndex.Empty.Sounding(0, buffer).Should().Be(0);
        DegreeWheelIndex.Empty.Trail(0, 480, buffer).Should().Be(0);
    }

    [Fact]
    public void ALongNoteIsStillFoundLongAfterItsOnset()
    {
        // The lookup walks back from the playhead only as far as the longest note in the file could
        // reach, so a whole-note pedal under a busy line is the case that catches a wrong bound.
        DegreeWheelIndex index = DegreeWheelIndex.Build(ScoreOf(
            Note(0, 1920, 36),
            Note(1440, 240, 72)));

        WheelNote[] buffer = new WheelNote[8];

        index.Sounding(1500, buffer).Should().Be(2);
    }

    [Fact]
    public void SoundingNotesComeBackInStartOrder()
    {
        DegreeWheelIndex index = DegreeWheelIndex.Build(ScoreOf(
            Note(0, 1920, 36),
            Note(480, 960, 60),
            Note(720, 480, 67)));

        WheelNote[] buffer = new WheelNote[8];
        int count = index.Sounding(900, buffer);

        count.Should().Be(3);
        buffer.Take(3).Select(n => n.StartTicks).Should().Equal([0L, 480L, 720L]);
    }

    // --- the trail ------------------------------------------------------------------------------------

    [Fact]
    public void TheTrailReturnsRecentAttacksNewestFirst()
    {
        DegreeWheelIndex index = DegreeWheelIndex.Build(ScoreOf(
            Note(0, 240, 60),
            Note(240, 240, 62),
            Note(480, 240, 64),
            Note(720, 240, 65)));

        WheelNote[] buffer = new WheelNote[4];
        int count = index.Trail(800, windowTicks: 960, buffer);

        count.Should().Be(4);
        buffer.Take(4).Select(n => n.StartTicks).Should().Equal([720L, 480L, 240L, 0L]);
    }

    [Fact]
    public void TheTrailForgetsAnythingOlderThanItsWindow()
    {
        DegreeWheelIndex index = DegreeWheelIndex.Build(ScoreOf(
            Note(0, 240, 60),
            Note(960, 240, 62)));

        WheelNote[] buffer = new WheelNote[4];

        index.Trail(1000, windowTicks: 480, buffer).Should().Be(1, "the first note is long past");
    }

    [Fact]
    public void TheTrailDoesNotRetriggerOnATiedContinuation()
    {
        // A note held across a barline is one attack, not two. Counting the continuation would draw
        // a melodic move that never happened.
        DegreeWheelIndex index = DegreeWheelIndex.Build(ScoreOf(
            Note(0, 480, 60, TieState.Start),
            Note(480, 480, 60, TieState.Stop)));

        WheelNote[] buffer = new WheelNote[4];

        index.AttackCount.Should().Be(1);
        index.Trail(600, windowTicks: 1920, buffer).Should().Be(1);
        index.Sounding(600, buffer).Should().Be(1, "it is still sounding, even though it did not re-attack");
    }

    [Fact]
    public void TrailStrengthDecaysWithAge()
    {
        DegreeGeometry.TrailStrength(100, 100, 400).Should().Be(1.0, "a note just struck is at full strength");
        DegreeGeometry.TrailStrength(100, 200, 400).Should().BeApproximately(0.75, 1e-9);
        DegreeGeometry.TrailStrength(100, 300, 400).Should().BeApproximately(0.5, 1e-9);
        DegreeGeometry.TrailStrength(100, 500, 400).Should().Be(0, "a note a whole window old has faded out");
    }

    [Fact]
    public void TrailStrengthIsOrderedNewestStrongest()
    {
        double[] strengths =
        [
            DegreeGeometry.TrailStrength(900, 1000, 480),
            DegreeGeometry.TrailStrength(700, 1000, 480),
            DegreeGeometry.TrailStrength(600, 1000, 480),
        ];

        strengths.Should().BeInDescendingOrder();
        strengths.Should().AllSatisfy(s => s.Should().BeInRange(0, 1));
    }

    [Fact]
    public void AFutureAttackHasNoStrengthAtAll() =>
        DegreeGeometry.TrailStrength(1200, 1000, 480).Should().Be(0);

    [Fact]
    public void TrailStepsRunFromWeakestToStrongestAndStayInRange()
    {
        DegreeGeometry.TrailStep(0.0, 5).Should().Be(0);
        DegreeGeometry.TrailStep(0.5, 5).Should().Be(2);
        DegreeGeometry.TrailStep(1.0, 5).Should().Be(4, "a full-strength note must not index past the ladder");
        DegreeGeometry.TrailStep(-0.3, 5).Should().Be(0);
        DegreeGeometry.TrailStep(0.4, 0).Should().Be(0);
    }
}
