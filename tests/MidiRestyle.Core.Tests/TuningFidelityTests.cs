using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="TuningFidelity"/> is computed, never hand-tagged, so it stays correct automatically as
/// scale data changes. These are the badge thresholds and the contextual-warning rule from
/// PROGRESS.md's verification evidence, converted into permanent assertions.
/// </summary>
public class TuningFidelityTests
{
    private static readonly double[] Gong = [0, 200, 400, 700, 900];
    private static readonly double[] Rast = [0, 200, 350, 500, 700, 900, 1050];
    private static readonly double[] Slendro = [0, 240, 480, 720, 960];
    private static readonly double[] AeuRast = [0, 203.8, 384.9, 498.1, 701.9, 905.7, 1086.8];
    private static readonly double PythagoreanFifth = 1200.0 * Math.Log2(1.5);
    private static double[] PythagoreanGong => [
        0,
        2 * PythagoreanFifth - 1200.0,
        4 * PythagoreanFifth - 2400.0,
        PythagoreanFifth,
        PythagoreanFifth + (2 * PythagoreanFifth - 1200.0),
    ];
    private static readonly double[] ThaiSevenEqual =
        [.. Enumerable.Range(0, 7).Select(i => i * 1200.0 / 7)];

    [Fact]
    public void GongIsExactAtZeroDeviation()
    {
        FidelityReport report = TuningFidelity.Assess(Gong);

        report.Badge.Should().Be(FidelityBadge.Exact);
        report.MaxDeviationCents.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void PythagoreanGongIsCloseAt7Point82Cents()
    {
        FidelityReport report = TuningFidelity.Assess(PythagoreanGong);

        report.Badge.Should().Be(FidelityBadge.Close);
        report.MaxDeviationCents.Should().BeApproximately(7.82, 1e-2);
        report.WorstDegreeIndex.Should().Be(2); // the ditone, 407.82c, nearest semitone 400
    }

    [Fact]
    public void TurkishAeuRastIsCloseAt15Point1Cents()
    {
        FidelityReport report = TuningFidelity.Assess(AeuRast);

        report.Badge.Should().Be(FidelityBadge.Close);
        report.MaxDeviationCents.Should().BeApproximately(15.1, 1e-6);
        report.WorstDegreeIndex.Should().Be(2); // 384.9c, nearest semitone 400
    }

    [Fact]
    public void SlendroIsApproximateAt40Cents()
    {
        FidelityReport report = TuningFidelity.Assess(Slendro);

        report.Badge.Should().Be(FidelityBadge.Approximate);
        report.MaxDeviationCents.Should().BeApproximately(40.0, 1e-9);
        report.WorstDegreeIndex.Should().Be(1); // 240c, nearest semitone 200
    }

    [Fact]
    public void RastIsApproximateAt50Cents()
    {
        FidelityReport report = TuningFidelity.Assess(Rast);

        report.Badge.Should().Be(FidelityBadge.Approximate);
        report.MaxDeviationCents.Should().BeApproximately(50.0, 1e-9);
        report.WorstDegreeIndex.Should().Be(2); // first of the two 50c degrees, 350c
    }

    [Fact]
    public void ThaiSevenEqualIsApproximateAt42Point86Cents()
    {
        FidelityReport report = TuningFidelity.Assess(ThaiSevenEqual);

        report.Badge.Should().Be(FidelityBadge.Approximate);
        report.MaxDeviationCents.Should().BeApproximately(42.86, 1e-2);
    }

    /// <summary>
    /// A scale whose quantisation itself fails (degrees too tight to separate onto 12-TET) must
    /// report Impossible rather than propagating the quantiser's empty degree list into a bogus
    /// deviation number.
    /// </summary>
    [Fact]
    public void ScaleThatFailsQuantisationReportsImpossible()
    {
        double[] tooTight = [0, 200, 400, 600, 800, 1000, 1160];

        FidelityReport report = TuningFidelity.Assess(tooTight);

        report.Badge.Should().Be(FidelityBadge.Impossible);
        report.MaxDeviationCents.Should().Be(double.PositiveInfinity);
        report.WorstDegreeIndex.Should().Be(-1);
    }

    /// <summary>
    /// The contextual-badge rule. Deviation is neutral information about a tuning, shown calmly at
    /// all times - it becomes a warning only in the one state where the app is actually failing to
    /// deliver what was asked: 12-TET output on a scale 12-TET cannot carry. Inverted, this either
    /// cries wolf on every maqam (when the user asked for microtonal output, which delivers Rast
    /// perfectly) or goes silent exactly when the user is being short-changed.
    /// </summary>
    [Theory]
    [InlineData(FidelityBadge.Exact, false)]
    [InlineData(FidelityBadge.Close, false)]
    [InlineData(FidelityBadge.Approximate, true)]
    [InlineData(FidelityBadge.Impossible, true)]
    public void IsWarningInTwelveTetOutputIsTrueOnlyForApproximateOrImpossible(
        FidelityBadge badge, bool expectedWarningInTwelveTet)
    {
        var report = new FidelityReport(badge, MaxDeviationCents: 30.0, WorstDegreeIndex: 0);

        report.IsWarningIn(outputIsTwelveTet: true).Should().Be(expectedWarningInTwelveTet);
    }

    [Theory]
    [InlineData(FidelityBadge.Exact)]
    [InlineData(FidelityBadge.Close)]
    [InlineData(FidelityBadge.Approximate)]
    [InlineData(FidelityBadge.Impossible)]
    public void IsWarningInIsAlwaysFalseWhenOutputIsNotTwelveTet(FidelityBadge badge)
    {
        var report = new FidelityReport(badge, MaxDeviationCents: 999.0, WorstDegreeIndex: 0);

        report.IsWarningIn(outputIsTwelveTet: false).Should().BeFalse();
    }
}
