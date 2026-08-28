using MidiRestyle.Core.Model;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="RhythmQuantiser"/> decides, beat by beat, whether a beat is divided straight or as a
/// tuplet. The bias against tuplets is the part worth pinning: a straight beat played by a human
/// must not come back as a triplet, and a real triplet must not be flattened into one.
/// </summary>
public class RhythmQuantiserTests
{
    private const int Ppqn = 480;
    private const long Beat = 480;

    private static Note At(long start, long length = 120) =>
        new(Pitch.FromMidi(60), start, length, 100);

    private static IReadOnlyList<QuantisedNote> Quantise(
        IEnumerable<Note> notes, QuantiseOptions? options = null) =>
        RhythmQuantiser.Quantise([.. notes], Ppqn, Beat, options);

    [Fact]
    public void ExactSixteenthsSnapToThemselvesOnAStraightGrid()
    {
        var result = Quantise([At(0), At(120), At(240), At(360)]);

        result.Select(q => q.StartTicks).Should().Equal([0, 120, 240, 360]);
        result.Should().AllSatisfy(q => q.Tuplet.IsNone.Should().BeTrue());
    }

    [Fact]
    public void SlightlyLooseSixteenthsStaySixteenths()
    {
        // A human playing straight sixteenths, a few ticks either side. This is the case that a
        // trigger-happy tuplet detector gets wrong.
        var result = Quantise([At(3), At(114), At(247), At(355)]);

        result.Select(q => q.StartTicks).Should().Equal([0, 120, 240, 360]);
        result.Should().AllSatisfy(q => q.Tuplet.IsNone.Should().BeTrue(),
            "ordinary human timing on a straight beat is not a tuplet");
    }

    [Fact]
    public void ThreeEvenNotesInABeatAreReadAsATriplet()
    {
        var result = Quantise([At(0, 160), At(160, 160), At(320, 160)]);

        result.Should().AllSatisfy(q => q.Tuplet.Should().Be(Tuplet.Triplet));
        result.Select(q => q.StartTicks).Should().Equal([0, 160, 320]);
    }

    [Fact]
    public void TripletDetectionCanBeTurnedOff()
    {
        var result = Quantise(
            [At(0, 160), At(160, 160), At(320, 160)],
            new QuantiseOptions { DetectTuplets = false });

        result.Should().AllSatisfy(q => q.Tuplet.IsNone.Should().BeTrue());
        result.Select(q => q.StartTicks).Should().Equal([0, 120, 360],
            "without a triplet grid the onsets snap to the nearest sixteenth, warts and all");
    }

    [Fact]
    public void OneBeatMayBeTripletWhileTheNextIsStraight()
    {
        // The normal case in real music: a straight tune with a triplet turn in one beat.
        var result = Quantise([
            At(0, 240), At(240, 240),                       // beat 0: two straight eighths
            At(480, 160), At(640, 160), At(800, 160),       // beat 1: a triplet
        ]);

        result.Take(2).Should().AllSatisfy(q => q.Tuplet.IsNone.Should().BeTrue());
        result.Skip(2).Should().AllSatisfy(q => q.Tuplet.Should().Be(Tuplet.Triplet));
    }

    [Fact]
    public void AVeryShortNoteIsWidenedRatherThanLost()
    {
        // A 4-tick blip would quantise to zero length and disappear from the score entirely.
        var result = Quantise([At(0, 4)]);

        result.Should().ContainSingle();
        result[0].LengthTicks.Should().BeGreaterThan(0, "a note must never quantise out of existence");
    }

    [Fact]
    public void EmptyInputGivesEmptyOutput() =>
        Quantise([]).Should().BeEmpty();

    [Fact]
    public void SixEvenNotesInABeatAreReadAsASextuplet()
    {
        var notes = Enumerable.Range(0, 6).Select(i => At(i * 80, 80));

        Quantise(notes).Should().AllSatisfy(q => q.Tuplet.Should().Be(Tuplet.Sextuplet));
    }

    // ---------------------------------------------------------------------------------------
    // Sparse beats. Every jitter test above puts four onsets in the beat, which is precisely the
    // density at which TupletBias works - and a melody is mostly one- and two-onset beats, where
    // it does not work at all. A third of all single-onset beats were coming back as tuplets.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ASingleOffGridNoteIsNotATuplet()
    {
        // Tick 60 is half a sixteenth late: 60 ticks from the nearest straight line but only 20
        // from the nearest sextuplet line. On mean error alone the sextuplet wins by 3:1, which no
        // value of the bias survives. One onset does not divide a beat, so it is not asked to.
        var result = Quantise([At(60, 480)]);

        result.Should().ContainSingle();
        result[0].Tuplet.IsNone.Should().BeTrue("one onset divides nothing");
        result[0].StartTicks.Should().Be(120);
    }

    [Fact]
    public void TwoOnGridSextupletNotesAreStillNotATuplet()
    {
        // Ticks 80 and 400 sit exactly on the sextuplet grid and 40 ticks off the straight one, so
        // the sextuplet reading fits perfectly and still loses. Two onsets mark at most one
        // internal division, which the straight grid already expresses.
        var result = Quantise([At(80, 160), At(400, 80)]);

        result.Should().AllSatisfy(q => q.Tuplet.IsNone.Should().BeTrue(),
            "two onsets are not evidence of how the beat is divided");
    }

    [Fact]
    public void AChordCountsAsOneOnsetHoweverManyNotesItHas()
    {
        // Three notes, one attack. Counting notes rather than attack points would let a single
        // off-grid chord buy itself a tuplet.
        var result = Quantise([
            new Note(Pitch.FromMidi(60), 60, 480, 100),
            new Note(Pitch.FromMidi(64), 60, 480, 100),
            new Note(Pitch.FromMidi(67), 60, 480, 100),
        ]);

        result.Should().AllSatisfy(q => q.Tuplet.IsNone.Should().BeTrue());
    }

    [Fact]
    public void ThreeOnsetsAreEnoughForARealTripletToStillBeFound()
    {
        // The floor must not be so high that it flattens genuine tuplets. Three is the smallest
        // group that can be printed as one, and it is still detected.
        var result = Quantise([At(2, 160), At(158, 160), At(323, 160)]);

        result.Should().AllSatisfy(q => q.Tuplet.Should().Be(Tuplet.Triplet));
        result.Select(q => q.StartTicks).Should().Equal([0, 160, 320]);
    }

    [Fact]
    public void LoweringTheOnsetFloorBringsTheMisreadingBack()
    {
        // Pins the mechanism rather than the symptom: with the floor at one, the same single note
        // is read as a sextuplet again, bias and all. The bias is not what was fixed.
        var result = Quantise([At(60, 480)], new QuantiseOptions { MinimumTupletOnsets = 1 });

        result[0].Tuplet.Should().Be(Tuplet.Sextuplet);
    }
}
