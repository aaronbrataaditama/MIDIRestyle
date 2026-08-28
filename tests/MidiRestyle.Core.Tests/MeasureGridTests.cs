using MidiRestyle.Core.Model;
using MidiRestyle.Core.Notation;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="MeasureGrid"/> decides where the barlines fall. Both the exporter and the staff
/// renderer read it rather than each working the metre out for themselves, because two
/// implementations would eventually disagree about a mid-piece signature change and produce a file
/// that does not match the screen.
/// </summary>
public class MeasureGridTests
{
    private const int Ppqn = 480;

    [Theory]
    [InlineData(4, 4, 1920)]
    [InlineData(3, 4, 1440)]
    [InlineData(2, 4, 960)]
    [InlineData(6, 8, 1440)]
    [InlineData(5, 4, 2400)]
    [InlineData(2, 2, 1920)]
    [InlineData(12, 8, 2880)]
    public void MeasureLengthFollowsTheSignature(int numerator, int denominator, long expected) =>
        MeasureGrid.MeasureTicks(numerator, denominator, Ppqn).Should().Be(expected);

    [Fact]
    public void AFileWithNoTimeSignatureIsAssumedToBeInFourFour()
    {
        // The MIDI default. A file that never states a signature is common, and refusing to draw
        // barlines at all would be worse than assuming the overwhelmingly likely one.
        var measures = MeasureGrid.Build([], 3840, Ppqn);

        measures.Should().HaveCount(2);
        measures[0].LengthTicks.Should().Be(1920);
        measures[0].Beats.Should().Be(4);
    }

    [Fact]
    public void ASignatureStartingLateStillGetsBarlinesBeforeIt()
    {
        // A file whose first signature event sits at tick 1920 still has an opening measure, and it
        // has to be drawn as something.
        var measures = MeasureGrid.Build([new TimeSignatureChange(1920, 3, 4)], 3840, Ppqn);

        measures[0].StartTicks.Should().Be(0);
        measures[0].Beats.Should().Be(4, "the implicit opening signature is 4/4");
    }

    [Fact]
    public void MeasuresRunConsecutivelyWithNoGapOrOverlap()
    {
        var measures = MeasureGrid.Build([new TimeSignatureChange(0, 4, 4)], 1920 * 5, Ppqn);

        for (int i = 1; i < measures.Count; i++)
        {
            measures[i].StartTicks.Should().Be(
                measures[i - 1].EndTicks, "a barline is both the end of one measure and the start of the next");
        }

        measures.Select(m => m.Number).Should().Equal([.. Enumerable.Range(1, measures.Count)]);
    }

    [Fact]
    public void AMidPieceSignatureChangeTakesEffectAtTheRightBarline()
    {
        var measures = MeasureGrid.Build(
            [new TimeSignatureChange(0, 4, 4), new TimeSignatureChange(3840, 3, 4)],
            3840 + (1440 * 2),
            Ppqn);

        measures[0].Beats.Should().Be(4);
        measures[1].Beats.Should().Be(4);
        measures[2].StartTicks.Should().Be(3840);
        measures[2].Beats.Should().Be(3);
        measures[2].LengthTicks.Should().Be(1440);
    }

    [Fact]
    public void OnlyTheMeasuresWhereTheSignatureChangesAreFlagged()
    {
        // MusicXML prints a time signature wherever it finds one, so repeating it every measure
        // would litter the score with redundant 4/4s.
        var measures = MeasureGrid.Build(
            [new TimeSignatureChange(0, 4, 4), new TimeSignatureChange(3840, 3, 4)],
            3840 + 1440,
            Ppqn);

        measures.Where(m => m.SignatureChanged).Select(m => m.Number).Should().Equal(
            [1, 3], "the opening signature and the change, and nothing in between");
    }

    [Fact]
    public void AnEmptyFileStillGetsOneMeasure()
    {
        // A score with no barline at all reads as broken rather than as empty.
        var measures = MeasureGrid.Build([], 0, Ppqn);

        measures.Should().ContainSingle();
        measures[0].Number.Should().Be(1);
    }

    [Fact]
    public void CompoundTimeCountsItsPrintedBeatNotItsDottedBeat()
    {
        // In 6/8 the printed beat is the eighth. The decomposer splits spans at this value, and
        // splitting at the dotted quarter instead would hide the internal eighths rather than
        // reveal them - which is the whole reason a reader wants 6/8 rather than 3/4.
        var measures = MeasureGrid.Build([new TimeSignatureChange(0, 6, 8)], 1440, Ppqn);

        measures[0].LengthTicks.Should().Be(1440);
        measures[0].BeatTicks.Should().Be(240, "an eighth note at 480 PPQN");
    }

    [Fact]
    public void MeasureAtFindsTheContainingMeasure()
    {
        var measures = MeasureGrid.Build([new TimeSignatureChange(0, 4, 4)], 1920 * 4, Ppqn);

        MeasureGrid.MeasureAt(measures, 0).Number.Should().Be(1);
        MeasureGrid.MeasureAt(measures, 1919).Number.Should().Be(1);
        MeasureGrid.MeasureAt(measures, 1920).Number.Should().Be(2);
        MeasureGrid.MeasureAt(measures, 5000).Number.Should().Be(3);
    }

    [Fact]
    public void ATickPastTheEndClampsToTheLastMeasureRatherThanThrowing()
    {
        var measures = MeasureGrid.Build([new TimeSignatureChange(0, 4, 4)], 1920, Ppqn);

        MeasureGrid.MeasureAt(measures, long.MaxValue).Should().Be(measures[^1]);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-1, 4)]
    public void ACorruptSignatureFallsBackToAWholeMeasureRatherThanDividingByZero(
        int numerator, int denominator) =>
        MeasureGrid.MeasureTicks(numerator, denominator, Ppqn).Should().Be(1920);
}
