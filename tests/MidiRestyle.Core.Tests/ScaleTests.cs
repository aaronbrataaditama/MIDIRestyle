using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="Scale"/> validates every invariant the rest of the domain relies on at construction
/// time, and computes <see cref="Scale.DegreeOffsets"/> lazily from <see cref="Scale.DegreeCents"/>
/// so the channel count can never depend on tonic or octave.
/// </summary>
public class ScaleTests
{
    // Maqam Rast, cited throughout CLAUDE.md.
    private static readonly double[] Rast = [0, 200, 350, 500, 700, 900, 1050];

    // Gamelan Slendro (approximate, 5-equal-ish but not exactly).
    private static readonly double[] Slendro = [0, 240, 480, 720, 960];

    // Pythagorean Gong: 0, whole-tone, ditone, fifth, sixth, all stacked from a 3/2 fifth. Every
    // adjacent gap between its offsets is exactly the same 1.955c - the case OffsetClustererTests
    // guards against a single-linkage "simplification".
    private static readonly double PythagoreanFifth = 1200.0 * Math.Log2(1.5);
    private static double[] PythagoreanGong => [
        0,
        2 * PythagoreanFifth - 1200.0,
        4 * PythagoreanFifth - 2400.0,
        PythagoreanFifth,
        PythagoreanFifth + (2 * PythagoreanFifth - 1200.0),
    ];

    private static Scale MakeScale(
        double[] degreeCents,
        string source = "Unit test fixture, ScaleTests",
        bool notatable = true,
        IReadOnlyList<DegreeSpelling>? spelling = null) =>
        new(
            id: "test.scale",
            name: "Test scale",
            tradition: "Test",
            region: "Test",
            degreeCents: degreeCents,
            source: source,
            notatable: notatable,
            spelling: spelling);

    [Fact]
    public void ValidScaleConstructsAndExposesItsProperties()
    {
        Scale scale = MakeScale(Rast);

        scale.DegreeCents.Should().Equal(Rast);
        scale.DegreeCount.Should().Be(7);
        scale.Source.Should().Be("Unit test fixture, ScaleTests");
    }

    [Theory]
    [InlineData(new double[] { })]
    [InlineData(new double[] { 0 })]
    public void FewerThanTwoDegreesIsRejected(double[] degreeCents)
    {
        Action act = () => MakeScale(degreeCents);

        act.Should().Throw<ScaleValidationException>()
            .WithMessage("*at least 2 degrees*")
            .Which.ScaleId.Should().Be("test.scale");
    }

    [Fact]
    public void MoreThanTwelveDegreesIsRejected()
    {
        double[] thirteen = [.. Enumerable.Range(0, 13).Select(i => i * 90.0)];

        Action act = () => MakeScale(thirteen);

        act.Should().Throw<ScaleValidationException>().WithMessage("*13*12*");
    }

    [Fact]
    public void FirstDegreeNotExactlyZeroIsRejected()
    {
        double[] cents = [10, 200, 400, 700, 900];

        Action act = () => MakeScale(cents);

        act.Should().Throw<ScaleValidationException>().WithMessage("*start at exactly 0*");
    }

    [Theory]
    [InlineData(new double[] { 0, 200, 200, 700, 900 })]  // duplicate
    [InlineData(new double[] { 0, 400, 200, 700, 900 })]  // descending
    public void NonAscendingDegreesAreRejected(double[] degreeCents)
    {
        Action act = () => MakeScale(degreeCents);

        act.Should().Throw<ScaleValidationException>().WithMessage("*strictly ascend*");
    }

    [Fact]
    public void DuplicateDegreeIsRejectedAsNonAscending()
    {
        double[] cents = [0, 200, 200, 700, 900];

        Action act = () => MakeScale(cents);

        // A duplicate is caught by the same "strictly ascend" check as a descent - degree i must be
        // strictly greater than degree i-1, so an equal value is not a separate rule.
        act.Should().Throw<ScaleValidationException>().WithMessage("*strictly ascend*");
    }

    [Fact]
    public void DegreeAtOrAboveTheOctaveIsRejected()
    {
        double[] cents = [0, 200, 400, 700, 1200];

        Action act = () => MakeScale(cents);

        act.Should().Throw<ScaleValidationException>()
            .WithMessage("*1200*duplicates the tonic*");
    }

    [Theory]
    [InlineData("TODO")]
    [InlineData("tbd")]
    [InlineData("FIXME")]
    [InlineData("?")]
    [InlineData("n/a")]
    [InlineData("NA")]
    [InlineData("unknown")]
    [InlineData("xxx")]
    [InlineData("  ")]
    [InlineData("")]
    public void PlaceholderOrBlankSourceIsRejected(string source)
    {
        Action act = () => MakeScale(Rast, source: source);

        act.Should().Throw<ScaleValidationException>().WithMessage("*needs a real Source*");
    }

    [Fact]
    public void RealSourceIsAccepted() =>
        MakeScale(Rast, source: "Farhat, The Maqam Music Tradition, 1990").Source
            .Should().Be("Farhat, The Maqam Music Tradition, 1990");

    // Values from PROGRESS.md's verification evidence, reproduced here so any future change to the
    // rounding gate or the offset computation is caught by a real test rather than a throwaway
    // script.
    [Fact]
    public void DegreeOffsetsMatchVerifiedValuesForRast()
    {
        Scale scale = MakeScale(Rast);

        // 350c and 1050c are both quarter-tone ties: away-from-zero rounds both up, giving both
        // degrees the same -50c offset (2 channels), not two different offsets (3 channels).
        scale.DegreeOffsets.Should().Equal([0.0, 0.0, -50.0, 0.0, 0.0, 0.0, -50.0]);
        scale.DegreeOffsets.Distinct().Should().BeEquivalentTo([0.0, -50.0]);
    }

    [Fact]
    public void DegreeOffsetsMatchVerifiedValuesForSlendro()
    {
        Scale scale = MakeScale(Slendro);

        scale.DegreeOffsets.Distinct().OrderBy(o => o).Should()
            .BeEquivalentTo([-40.0, -20.0, 0.0, 20.0, 40.0]);
    }

    [Fact]
    public void DegreeOffsetsMatchVerifiedValuesForPythagoreanGong()
    {
        Scale scale = MakeScale(PythagoreanGong);

        double[] sortedOffsets = [.. scale.DegreeOffsets.OrderBy(o => o)];
        sortedOffsets.Should().HaveCount(5);
        sortedOffsets[0].Should().BeApproximately(0.0, 1e-6);
        sortedOffsets[1].Should().BeApproximately(1.955, 1e-3);
        sortedOffsets[2].Should().BeApproximately(3.910, 1e-3);
        sortedOffsets[3].Should().BeApproximately(5.865, 1e-3);
        sortedOffsets[4].Should().BeApproximately(7.820, 1e-3);
    }

    /// <summary>
    /// Lazy and memoized: <see cref="Scale.DegreeOffsets"/> must not recompute (and must not return
    /// a fresh array) on every access, since <c>RestyleEngine</c> is required to finish inside 16ms
    /// per run and re-runs on every keystroke in the scale picker.
    /// </summary>
    [Fact]
    public void DegreeOffsetsIsComputedLazilyAndCachedAcrossAccesses()
    {
        Scale scale = MakeScale(Rast);

        var first = scale.DegreeOffsets;
        var second = scale.DegreeOffsets;

        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public void IsTwelveTetIsTrueOnlyWhenAllOffsetsAreZero()
    {
        MakeScale([0, 200, 400, 700, 900]).IsTwelveTet.Should().BeTrue();
        MakeScale(Rast).IsTwelveTet.Should().BeFalse();
    }

    [Fact]
    public void MaxOffsetCentsIsTheWorstAbsoluteOffset()
    {
        MakeScale(Rast).MaxOffsetCents.Should().BeApproximately(50.0, 1e-9);
        MakeScale(Slendro).MaxOffsetCents.Should().BeApproximately(40.0, 1e-9);
    }

    /// <summary>
    /// Notatability is authored, never derived. A caller passing a spelling alongside
    /// <c>notatable: false</c> must still get <c>null</c> back - otherwise Slendro's cipher-notation
    /// scales would silently gain a fabricated staff spelling.
    /// </summary>
    [Fact]
    public void NotatableFalseForcesSpellingToNullEvenWhenOneIsSupplied()
    {
        IReadOnlyList<DegreeSpelling> spelling =
            [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0)];

        Scale scale = MakeScale(Slendro, notatable: false, spelling: spelling);

        scale.Notatable.Should().BeFalse();
        scale.Spelling.Should().BeNull();
    }

    [Fact]
    public void NotatableTrueKeepsTheSuppliedSpelling()
    {
        IReadOnlyList<DegreeSpelling> spelling =
            [new(0, 0), new(1, 0), new(2, 0), new(4, 0), new(5, 0)];

        Scale scale = MakeScale([0, 200, 400, 700, 900], notatable: true, spelling: spelling);

        scale.Notatable.Should().BeTrue();
        scale.Spelling.Should().Equal(spelling);
    }
}
