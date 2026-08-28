using MidiRestyle.Core.Notation;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="DurationDecomposer"/> turns a raw tick span into the tied written durations a reader
/// would expect. Two things are being pinned here: that the arithmetic adds up exactly, and that
/// the <i>spelling</i> is the conventional one - 7 eighths is a double-dotted half, not a
/// half tied to a dotted quarter, and a span that straddles a beat is split at the beat.
/// </summary>
public class DurationDecomposerTests
{
    private const int Ppqn = 480;

    private static long TotalTicks(IReadOnlyList<NotatedDuration> parts) =>
        (long)Math.Round(parts.Sum(p => p.Ticks(Ppqn)));

    [Theory]
    [InlineData(480, NoteValue.Quarter, 0)]
    [InlineData(960, NoteValue.Half, 0)]
    [InlineData(1920, NoteValue.Whole, 0)]
    [InlineData(240, NoteValue.Eighth, 0)]
    [InlineData(120, NoteValue.Sixteenth, 0)]
    [InlineData(720, NoteValue.Quarter, 1)]   // dotted quarter
    [InlineData(1680, NoteValue.Half, 2)]     // double-dotted half = 7 eighths
    public void ExactValuesBecomeASingleNote(long ticks, NoteValue expected, int expectedDots)
    {
        var parts = DurationDecomposer.Decompose(ticks, Ppqn);

        parts.Should().ContainSingle();
        parts[0].Value.Should().Be(expected);
        parts[0].Dots.Should().Be(expectedDots);
    }

    [Fact]
    public void AwkwardSpanTiesLongestFirstAndAddsUpExactly()
    {
        // Five sixteenths: a quarter tied to a sixteenth, in that order.
        var parts = DurationDecomposer.Decompose(600, Ppqn);

        parts.Select(p => (p.Value, p.Dots)).Should().Equal(
            [(NoteValue.Quarter, 0), (NoteValue.Sixteenth, 0)],
            "greedy longest-first is the conventional spelling");
        TotalTicks(parts).Should().Be(600);
    }

    [Fact]
    public void ATripletEighthIsNotExpressibleWithoutItsRatio()
    {
        // 160 ticks at 480 PPQN is a third of a quarter - no dotted straight value reaches it,
        // so a decomposition that ignores tuplets has to approximate. Told the ratio, it is one note.
        var straight = DurationDecomposer.Decompose(160, Ppqn);
        var triplet = DurationDecomposer.Decompose(160, Ppqn, Tuplet.Triplet);

        straight.Count.Should().BeGreaterThan(1, "no single straight value is exactly a third of a beat");
        triplet.Should().ContainSingle();
        triplet[0].Value.Should().Be(NoteValue.Eighth);
        triplet[0].EffectiveTuplet.Should().Be(Tuplet.Triplet);
    }

    [Fact]
    public void ZeroLengthDecomposesToNothing() =>
        DurationDecomposer.Decompose(0, Ppqn).Should().BeEmpty();

    [Fact]
    public void ASpanShorterThanTheShortestValueStillReturnsThatValue()
    {
        // Below a 64th there is nothing to write. Rather than return empty - which would silently
        // delete the note - the decomposer floors at the shortest value it has.
        var parts = DurationDecomposer.Decompose(3, Ppqn);

        parts.Should().ContainSingle();
        parts[0].Value.Should().Be(NoteValue.SixtyFourth);
    }

    [Fact]
    public void NoteStartingOffBeatIsSplitAtTheNextBeat()
    {
        // An eighth-note upbeat running through the following beat: written as two tied eighths,
        // not one quarter, because a quarter here would hide where beat 2 falls.
        var parts = DurationDecomposer.DecomposeAt(
            startInMeasure: 240, lengthTicks: 480, ppqn: Ppqn, beatTicks: 480);

        parts.Should().HaveCount(2);
        parts.Should().AllSatisfy(p => p.Value.Should().Be(NoteValue.Eighth));
        TotalTicks(parts).Should().Be(480);
    }

    [Fact]
    public void NoteStartingOnTheBeatKeepsItsDottedSpelling()
    {
        // Same length, but starting on the beat, a dotted quarter is both correct and clearer.
        var parts = DurationDecomposer.DecomposeAt(
            startInMeasure: 0, lengthTicks: 720, ppqn: Ppqn, beatTicks: 480);

        parts.Should().ContainSingle();
        parts[0].Value.Should().Be(NoteValue.Quarter);
        parts[0].Dots.Should().Be(1);
    }

    [Theory]
    [InlineData(30)]     // a 64th, the shortest writable value
    [InlineData(240)]
    [InlineData(480)]
    [InlineData(600)]
    [InlineData(1920)]
    [InlineData(2640)]
    public void ARepresentableSpanSumsBackExactly(long ticks) =>
        TotalTicks(DurationDecomposer.Decompose(ticks, Ppqn)).Should().Be(ticks);

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(481)]
    [InlineData(2647)]
    public void AnUnrepresentableSpanRoundsUpByLessThanTheShortestValue(long ticks)
    {
        // Notation has no way to write a span that is not a multiple of a 64th, so quantisation is
        // the caller's job. What is guaranteed here is the direction and the bound: the decomposer
        // never returns *less* than it was given - which would silently shorten the note - and
        // never overshoots by a whole 64th, which would be a rounding bug rather than a limit.
        //
        // This is only half a contract. The other half is that the caller advances by what was
        // written, not by what it asked for - see WrittenTicks below, and
        // NotationBuilderTests.TheReviewsMinimalOverrunCaseFillsItsMeasureExactly for the
        // compensation actually happening. Pinning this half alone is what let a whole test suite
        // stay green while every jittered file exported an overlong measure.
        long sixtyFourth = (long)NoteValue.SixtyFourth.UndottedTicks(Ppqn);
        long written = TotalTicks(DurationDecomposer.Decompose(ticks, Ppqn));

        written.Should().BeGreaterThanOrEqualTo(ticks);
        (written - ticks).Should().BeLessThan(sixtyFourth);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(481)]
    [InlineData(2647)]
    public void WrittenTicksReportsTheOvershootRatherThanLeavingTheCallerToGuess(long ticks)
    {
        // The number the caller has to advance its cursor by. It is the same arithmetic the caller
        // would do by hand, which is the point: there were two call sites doing it by hand and both
        // used the span instead of the total.
        var parts = DurationDecomposer.Decompose(ticks, Ppqn);

        DurationDecomposer.WrittenTicks(parts, Ppqn).Should().Be(TotalTicks(parts));
        DurationDecomposer.WrittenTicks(parts, Ppqn).Should().BeGreaterThan(ticks,
            "these spans are all unwritable, so all of them round up");
    }

    [Theory]
    [InlineData(30)]
    [InlineData(600)]
    [InlineData(1920)]
    public void AWritableSpanIsWrittenWithNothingLeftOver(long ticks)
    {
        var parts = DurationDecomposer.Decompose(ticks, Ppqn);

        DurationDecomposer.IsExactlyWritable(ticks, Ppqn).Should().BeTrue();
        DurationDecomposer.WrittenTicks(parts, Ppqn).Should().Be(ticks);
    }

    [Theory]
    // Multiples of a straight 64th at 480 PPQN.
    [InlineData(30, 1, 1, true)]
    [InlineData(120, 1, 1, true)]
    // A sextuplet step is 80 ticks, which is not a whole number of straight 64ths...
    [InlineData(80, 1, 1, false)]
    // ...but is a whole number of triplet ones, which is why a span is spelled on the grid its
    // own beat was read on and is cut wherever that grid changes.
    [InlineData(80, 3, 2, true)]
    [InlineData(40, 6, 4, true)]
    [InlineData(35, 3, 2, false)]
    public void WritabilityDependsOnTheTupletInForce(
        long ticks, int actual, int normal, bool expected) =>
        DurationDecomposer.IsExactlyWritable(ticks, Ppqn, new Tuplet(actual, normal))
            .Should().Be(expected);
}
