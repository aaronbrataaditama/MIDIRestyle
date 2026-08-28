using MidiRestyle.Core.Output;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

public class ChannelBudgetTests
{
    private static readonly double[] SlendroOffsets = [-40, -20, 0, 20, 40];
    private static readonly double[] RastOffsets = [-50, 0];
    private static readonly double[] TwelveTetOffsets = [0];

    private static TrackChannelDemand[] Demands(int count, int notesEach = 100) =>
        [.. Enumerable.Range(0, count).Select(i => new TrackChannelDemand(i, i, notesEach))];

    // --- the common case ----------------------------------------------------------------

    [Fact]
    public void ThreeSlendroTracksFitExactlyAtTheDefaultTolerance()
    {
        // 3 x 5 clusters = 15, precisely the ceiling and no headroom.
        ChannelBudgetPlan plan = ChannelBudget.Plan(SlendroOffsets, Demands(3));

        plan.EffectiveToleranceCents.Should().Be(5.0);
        plan.ClustersPerTrackChannel.Should().Be(5);
        plan.ChannelsUsed.Should().Be(15);
        plan.ToleranceWasRaised.Should().BeFalse();
        plan.AnythingMuted.Should().BeFalse();
        plan.Describe().Should().BeNull("nothing was compromised, so the status bar says nothing");
    }

    /// <summary>
    /// The case the whole design exists for: four tracks in Slendro want 20 channels against 15.
    /// </summary>
    [Fact]
    public void FourSlendroTracksRaiseTheToleranceRatherThanDegradingATrack()
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(SlendroOffsets, Demands(4));

        plan.ToleranceWasRaised.Should().BeTrue();
        plan.ChannelsUsed.Should().BeLessThanOrEqualTo(15);
        plan.AnythingMuted.Should().BeFalse("raising the tolerance is enough at four tracks");
        plan.Describe().Should().Contain("tuning accuracy reduced");
    }

    [Fact]
    public void FiveSlendroTracksResolveToThreeClustersAtTwentyFiveCents()
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(SlendroOffsets, Demands(5));

        plan.EffectiveToleranceCents.Should().Be(25.0);
        plan.ClustersPerTrackChannel.Should().Be(3);
        plan.WorstErrorCents.Should().BeApproximately(10.0, 1e-9);
        plan.ChannelsUsed.Should().Be(15);
    }

    [Fact]
    public void SevenSlendroTracksResolveToTwoClustersAtFiftyCents()
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(SlendroOffsets, Demands(7));

        plan.EffectiveToleranceCents.Should().Be(50.0);
        plan.ClustersPerTrackChannel.Should().Be(2);
        plan.WorstErrorCents.Should().BeApproximately(20.0, 1e-9);
        plan.ChannelsUsed.Should().Be(14);
        plan.AnythingMuted.Should().BeFalse();
    }

    /// <summary>
    /// The invariant that gives this design its point: whatever the budget does, every surviving
    /// track gets the SAME number of clusters, so they are all in one tuning.
    /// </summary>
    /// <remarks>
    /// Per-track degradation would clash 40 cents on Slendro's degrees 1 and 4 - bitonality, not
    /// graceful degradation. A single scalar cluster count makes mixed tunings unrepresentable
    /// rather than merely discouraged.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(40)]
    public void EverySurvivingTrackAlwaysGetsTheSameTuning(int trackCount)
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(SlendroOffsets, Demands(trackCount));

        int surviving = trackCount - plan.Muted.Count;
        plan.ChannelsUsed.Should().Be(surviving * plan.ClustersPerTrackChannel);
        plan.ChannelsUsed.Should().BeLessThanOrEqualTo(15);
    }

    // --- muting -------------------------------------------------------------------------

    [Fact]
    public void PastTheLadderTheLeastBusyTracksAreMutedNotRetuned()
    {
        // 20 track-channels cannot fit even at one channel each.
        var demands = new TrackChannelDemand[20];
        for (int i = 0; i < 20; i++)
        {
            demands[i] = new TrackChannelDemand(i, i, NoteCount: (i + 1) * 10);
        }

        ChannelBudgetPlan plan = ChannelBudget.Plan(SlendroOffsets, demands);

        plan.Muted.Should().NotBeEmpty();
        plan.ChannelsUsed.Should().BeLessThanOrEqualTo(15);
        plan.Describe().Should().Contain("muted in preview");

        // The busiest parts survive: the muted set must be the sparsest ones.
        int quietestSurvivingNoteCount = demands
            .Except(plan.Muted)
            .Min(d => d.NoteCount);
        plan.Muted.Should().OnlyContain(m => m.NoteCount <= quietestSurvivingNoteCount);
    }

    [Fact]
    public void MutingIsDeterministicWhenNoteCountsTie()
    {
        var demands = Demands(20, notesEach: 100);

        ChannelBudgetPlan a = ChannelBudget.Plan(SlendroOffsets, demands);
        ChannelBudgetPlan b = ChannelBudget.Plan(SlendroOffsets, [.. demands.Reverse()]);

        a.Muted.Should().BeEquivalentTo(b.Muted,
            "ties break on track then channel, never on input order");
    }

    // --- scales that cost nothing ---------------------------------------------------------

    [Fact]
    public void ATwelveTetScaleNeverStrainsTheBudget()
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(TwelveTetOffsets, Demands(15));

        plan.ClustersPerTrackChannel.Should().Be(1);
        plan.ChannelsUsed.Should().Be(15);
        plan.ToleranceWasRaised.Should().BeFalse();
        plan.AnythingMuted.Should().BeFalse();
    }

    [Fact]
    public void RastFitsSevenTracksWithoutCompromise()
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(RastOffsets, Demands(7));

        plan.ClustersPerTrackChannel.Should().Be(2);
        plan.ChannelsUsed.Should().Be(14);
        plan.ToleranceWasRaised.Should().BeFalse();
    }

    // --- respecting the user's choice --------------------------------------------------------

    /// <summary>
    /// A user who deliberately chose a coarse tolerance has not been compromised by getting it, so
    /// the notice must not fire.
    /// </summary>
    [Fact]
    public void ChoosingACoarseToleranceIsNotReportedAsACompromise()
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(SlendroOffsets, Demands(5), requestedToleranceCents: 25.0);

        plan.EffectiveToleranceCents.Should().Be(25.0);
        plan.ToleranceWasRaised.Should().BeFalse();
        plan.Describe().Should().BeNull();
    }

    /// <summary>The ladder never goes finer than the user asked for, only coarser.</summary>
    [Fact]
    public void TheLadderNeverTightensBelowTheRequestedTolerance()
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(SlendroOffsets, Demands(3), requestedToleranceCents: 30.0);

        plan.EffectiveToleranceCents.Should().BeGreaterThanOrEqualTo(30.0);
    }

    // --- edges ---------------------------------------------------------------------------

    [Fact]
    public void NoTracksNeedsNoChannels()
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(SlendroOffsets, []);

        plan.ChannelsUsed.Should().Be(0);
        plan.Muted.Should().BeEmpty();
        plan.Describe().Should().BeNull();
    }

    /// <summary>
    /// A ceiling too small for even one track mutes everything rather than mis-tuning something.
    /// </summary>
    /// <remarks>
    /// Slendro needs 2 clusters even at the ladder's coarsest 50-cent setting (collapsing it to one
    /// would take about 80 cents, which is a quarter-tone of error and not worth offering). So with
    /// a single channel available nothing fits, and the honest answer is to mute rather than to
    /// invent a tuning nobody chose. Degenerate in practice - the real ceiling is 15 - but the
    /// behaviour should still be defensible rather than accidental.
    /// </remarks>
    [Fact]
    public void ACeilingTooSmallForEvenOneTrackMutesEverything()
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(SlendroOffsets, Demands(4), ceiling: 1);

        plan.ClustersPerTrackChannel.Should().BeGreaterThan(1, "Slendro cannot collapse to one cluster");
        plan.ChannelsUsed.Should().Be(0);
        plan.Muted.Should().HaveCount(4);
        plan.Describe().Should().Contain("muted in preview");
    }

    /// <summary>A 12-TET scale needs one channel per track, so a ceiling of one fits exactly one.</summary>
    [Fact]
    public void ACeilingOfOneFitsOneTrackWhenTheScaleCostsOneChannel()
    {
        ChannelBudgetPlan plan = ChannelBudget.Plan(TwelveTetOffsets, Demands(4), ceiling: 1);

        plan.ClustersPerTrackChannel.Should().Be(1);
        plan.ChannelsUsed.Should().Be(1);
        plan.Muted.Should().HaveCount(3);
    }

    [Fact]
    public void ANonPositiveCeilingIsRejected()
    {
        Action zero = () => ChannelBudget.Plan(SlendroOffsets, Demands(1), ceiling: 0);

        zero.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Cross-check against the real clusterer rather than hard-coded counts, so this test still
    /// means something if the tolerance ladder is ever retuned.
    /// </summary>
    [Fact]
    public void ThePlanAgreesWithTheClustererItIsPlanningFor()
    {
        Scale slendro = new(
            "t.slendro", "Slendro", "Gamelan", "Southeast Asia",
            [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

        ChannelBudgetPlan plan = ChannelBudget.Plan(
            OffsetClusterer.DistinctOffsets(slendro), Demands(5));

        OffsetClusterer
            .Cluster(OffsetClusterer.DistinctOffsets(slendro), plan.EffectiveToleranceCents)
            .Should().HaveCount(plan.ClustersPerTrackChannel);
    }
}
