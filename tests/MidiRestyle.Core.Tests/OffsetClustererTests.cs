using MidiRestyle.Core.Output;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="OffsetClusterer"/> groups the cent-offsets a scale needs into as few pitch-bend
/// channels as a tolerance allows. It is greedy and <b>span-bounded</b>, not single-linkage - the
/// distinction is the whole point of this file, and <see cref="PythagoreanGongOffsets"/> is the case
/// that tells the two apart.
/// </summary>
public class OffsetClustererTests
{
    private static readonly double[] RastOffsets = [-50, 0];
    private static readonly double[] SlendroOffsets = [-40, -20, 0, 20, 40];
    private static readonly double[] ThaiSevenEqualOffsets =
        [-42.857142857142854, -28.571428571428573, -14.285714285714286, 0, 14.285714285714286, 28.571428571428573, 42.857142857142854];
    private static readonly double[] PythagoreanGongOffsets = [0, 1.9550008653874, 3.9100017307748, 5.8650025961622, 7.8200034615496];
    private static readonly double[] TwelveTetGongOffsets = [0];

    [Theory]
    [InlineData(5.0, 2)]
    [InlineData(1.0, 2)]
    public void RastGivesTwoClustersAtBothTolerances(double tolerance, int expectedClusters) =>
        OffsetClusterer.Cluster(RastOffsets, tolerance).Should().HaveCount(expectedClusters);

    [Theory]
    [InlineData(5.0, 5)]
    [InlineData(1.0, 5)]
    public void SlendroGivesFiveClustersAtBothTolerances(double tolerance, int expectedClusters) =>
        OffsetClusterer.Cluster(SlendroOffsets, tolerance).Should().HaveCount(expectedClusters);

    [Theory]
    [InlineData(5.0, 7)]
    [InlineData(1.0, 7)]
    public void ThaiSevenEqualGivesSevenClustersAtBothTolerances(double tolerance, int expectedClusters) =>
        OffsetClusterer.Cluster(ThaiSevenEqualOffsets, tolerance).Should().HaveCount(expectedClusters);

    /// <summary>
    /// The guard case. Every adjacent gap in these offsets is exactly 1.955c. A single-linkage
    /// (chaining) implementation would fold all five into one cluster at 5c tolerance, because every
    /// neighbour pair is within tolerance of its neighbour. Span-bounding instead measures each
    /// candidate against the cluster's first member, so it closes the cluster once the *total span*
    /// would exceed tolerance - giving two clusters, not one, and under 3c of error instead of 3.9c.
    /// </summary>
    [Fact]
    public void PythagoreanGongGivesTwoClustersAt5CentsDespiteEveryAdjacentGapBeingUnder2Cents()
    {
        for (int i = 1; i < PythagoreanGongOffsets.Length; i++)
        {
            (PythagoreanGongOffsets[i] - PythagoreanGongOffsets[i - 1])
                .Should().BeApproximately(1.955, 1e-3, "every adjacent gap is the same size");
        }

        OffsetClusterer.Cluster(PythagoreanGongOffsets, toleranceCents: 5.0).Should().HaveCount(2);
    }

    [Fact]
    public void PythagoreanGongGivesFiveClustersAt1CentEvenThoughItGaveTwoAt5Cents()
    {
        OffsetClusterer.Cluster(PythagoreanGongOffsets, toleranceCents: 1.0).Should().HaveCount(5);
    }

    [Theory]
    [InlineData(5.0, 1)]
    [InlineData(1.0, 1)]
    public void TwelveTetGongAlwaysCollapsesToOneCluster(double tolerance, int expectedClusters) =>
        OffsetClusterer.Cluster(TwelveTetGongOffsets, tolerance).Should().HaveCount(expectedClusters);

    [Fact]
    public void ClusterBendCentsIsTheMemberMean()
    {
        IReadOnlyList<OffsetCluster> clusters = OffsetClusterer.Cluster([-40, -20, 0], toleranceCents: 50.0);

        clusters.Should().HaveCount(1);
        clusters[0].BendCents.Should().BeApproximately(-20.0, 1e-9);
        clusters[0].Members.Should().Equal([-40.0, -20.0, 0.0]);
    }

    [Fact]
    public void MaxErrorCentsAndSpanCentsAreCorrect()
    {
        IReadOnlyList<OffsetCluster> clusters = OffsetClusterer.Cluster([-40, -20, 0], toleranceCents: 50.0);
        OffsetCluster cluster = clusters[0];

        cluster.SpanCents.Should().BeApproximately(40.0, 1e-9); // 0 - (-40)
        cluster.MaxErrorCents.Should().BeApproximately(20.0, 1e-9); // |-40 - (-20)| and |0 - (-20)|
    }

    [Fact]
    public void SpanCentsNeverExceedsTheTolerance()
    {
        foreach (double tolerance in new[] { 1.0, 5.0, 10.0, 25.0, 50.0 })
        {
            foreach (var offsets in new[] { RastOffsets, SlendroOffsets, ThaiSevenEqualOffsets, PythagoreanGongOffsets })
            {
                foreach (OffsetCluster cluster in OffsetClusterer.Cluster(offsets, tolerance))
                {
                    cluster.SpanCents.Should().BeLessThanOrEqualTo(tolerance + 1e-6);
                }
            }
        }
    }

    [Fact]
    public void ClusteringIsOrderIndependent()
    {
        var rng = new Random(1234);
        double[] shuffled = [.. SlendroOffsets.OrderBy(_ => rng.Next())];

        IReadOnlyList<OffsetCluster> fromSorted = OffsetClusterer.Cluster(SlendroOffsets);
        IReadOnlyList<OffsetCluster> fromShuffled = OffsetClusterer.Cluster(shuffled);

        fromShuffled.Should().BeEquivalentTo(fromSorted, options => options.WithStrictOrdering());
    }

    [Fact]
    public void FitsTwelveTetIsTrueForOffsetsWithinToleranceAndFalseOtherwise()
    {
        OffsetClusterer.FitsTwelveTet(TwelveTetGongOffsets).Should().BeTrue();
        OffsetClusterer.FitsTwelveTet(RastOffsets).Should().BeFalse();
    }

    [Fact]
    public void ClusterForFindsTheClusterContainingAnOffset()
    {
        IReadOnlyList<OffsetCluster> clusters = OffsetClusterer.Cluster(RastOffsets);

        OffsetCluster found = OffsetClusterer.ClusterFor(clusters, -50);

        found.Contains(-50).Should().BeTrue();
    }

    [Fact]
    public void ClusterForThrowsForAnOffsetThatWasNeverClustered()
    {
        IReadOnlyList<OffsetCluster> clusters = OffsetClusterer.Cluster(RastOffsets);

        Action act = () => OffsetClusterer.ClusterFor(clusters, 999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// The escalation ladder, applied to the whole project when the channel budget does not fit.
    /// Slendro's five equally-spaced offsets (-40,-20,0,20,40, each 20c apart) collapse in stages as
    /// tolerance rises, and the worst per-cluster error grows in lockstep - this is what the UI
    /// reports back to the user when it has to widen the tolerance.
    /// </summary>
    [Theory]
    [InlineData(5.0, 5, 0.0)]
    [InlineData(25.0, 3, 10.0)]
    [InlineData(50.0, 2, 20.0)]
    public void ToleranceLadderOnSlendroGivesTheVerifiedClusterCountsAndWorstError(
        double tolerance, int expectedClusters, double expectedWorstError)
    {
        IReadOnlyList<OffsetCluster> clusters = OffsetClusterer.Cluster(SlendroOffsets, tolerance);

        clusters.Should().HaveCount(expectedClusters);
        clusters.Max(c => c.MaxErrorCents).Should().BeApproximately(expectedWorstError, 1e-9);
    }

    [Fact]
    public void ToleranceLadderConstantMatchesTheDocumentedEscalationSteps() =>
        OffsetClusterer.ToleranceLadder.Should().Equal([5.0, 10.0, 15.0, 25.0, 35.0, 50.0]);
}
