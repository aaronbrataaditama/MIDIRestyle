using MidiRestyle.Core.Model;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

public class ChannelAllocatorTests
{
    private static readonly Scale CMajor = new(
        "t.cmajor", "C major", "Western", "Europe & Balkans",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    private static readonly Scale Rast = new(
        "t.rast", "Maqam Rast", "Arabic maqam", "Middle East",
        [0, 200, 350, 500, 700, 900, 1050], "Test fixture, 2026");

    private static readonly Scale Slendro = new(
        "t.slendro", "Slendro", "Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

    private static readonly Scale Gong = new(
        "t.gong", "Gong", "Chinese Wusheng", "East Asia",
        [0, 200, 400, 700, 900], "Test fixture, 2026");

    private static TrackInfo Track(int channel, int trackIndex, int noteCount = 8) => new()
    {
        TrackIndex = trackIndex,
        Channel = channel,
        Notes = [.. Enumerable.Range(0, noteCount)
            .Select(i => new Note(Pitch.FromMidi(60 + (i % 12)), i * 240, 240, 90))],
    };

    private static ChannelAllocation Allocate(Scale target, int trackCount, int ceiling = 15)
    {
        TrackInfo[] tracks = [.. Enumerable.Range(0, trackCount).Select(i => Track(i, i))];
        MidiProject project = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = tracks,
        };

        RestyleResult result = RestyleEngine.Restyle(project, new RestyleSettings
        {
            TargetScale = target,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        });

        return ChannelAllocator.Allocate(result, ceiling);
    }

    // --- the headline counts ---------------------------------------------------------------

    [Fact]
    public void RastNeedsTwoChannelsPerTrack()
    {
        ChannelAllocation allocation = Allocate(Rast, trackCount: 1);

        allocation.ChannelCount.Should().Be(2,
            "both of Rast's neutral degrees need the same -50 cent bend, so one channel carries both");
        allocation.HadToCompromise.Should().BeFalse();
    }

    [Fact]
    public void SlendroNeedsFiveChannelsPerTrack()
    {
        Allocate(Slendro, trackCount: 1).ChannelCount.Should().Be(5);
    }

    [Fact]
    public void ATwelveTetScaleNeedsOneChannelPerTrack()
    {
        Allocate(Gong, trackCount: 1).ChannelCount.Should().Be(1);
    }

    // --- the drum channel --------------------------------------------------------------------

    /// <summary>
    /// Channel 9 is percussion and is never handed out. Allocating it would silently turn a melodic
    /// part into drum hits.
    /// </summary>
    [Fact]
    public void ChannelNineIsNeverAllocated()
    {
        ChannelAllocation allocation = Allocate(Slendro, trackCount: 3);

        allocation.Channels.Should().OnlyContain(c => c.OutputChannel != 9);
        ChannelAllocator.AvailableChannels.Should().HaveCount(15).And.NotContain(9);
    }

    [Fact]
    public void ADrumTrackConsumesNoAllocatedChannels()
    {
        MidiProject project = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [Track(0, 0), Track(TrackInfo.DrumChannel, 1)],
        };

        RestyleResult result = RestyleEngine.Restyle(project, new RestyleSettings
        {
            TargetScale = Slendro,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        });

        ChannelAllocation allocation = ChannelAllocator.Allocate(result);

        allocation.ChannelCount.Should().Be(5, "only the one pitched track competes for channels");
        allocation.Channels.Should().OnlyContain(c => c.SourceChannel != TrackInfo.DrumChannel);
    }

    // --- the key --------------------------------------------------------------------------

    /// <summary>
    /// Allocation is keyed on <c>(track, channel, offset)</c>. Two Format 1 tracks may legally share
    /// a channel with different programs, so keying on channel alone would merge them.
    /// </summary>
    [Fact]
    public void TwoTracksSharingASourceChannelGetSeparateOutputChannels()
    {
        MidiProject project = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [Track(channel: 0, trackIndex: 0), Track(channel: 0, trackIndex: 1)],
        };

        RestyleResult result = RestyleEngine.Restyle(project, new RestyleSettings
        {
            TargetScale = Rast,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        });

        ChannelAllocation allocation = ChannelAllocator.Allocate(result);

        allocation.ChannelCount.Should().Be(4, "two tracks x two clusters, kept apart");
        allocation.Channels.Select(c => c.OutputChannel).Should().OnlyHaveUniqueItems();
        allocation.Channels.Select(c => c.SourceTrackIndex).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void EveryAllocatedChannelIsDistinct()
    {
        ChannelAllocation allocation = Allocate(Slendro, trackCount: 3);

        allocation.Channels.Select(c => c.OutputChannel).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void FindLocatesTheChannelCarryingAGivenOffset()
    {
        ChannelAllocation allocation = Allocate(Slendro, trackCount: 1);

        foreach (double offset in Slendro.DegreeOffsets.Distinct())
        {
            AllocatedChannel? channel = allocation.Find(0, 0, offset);
            channel.Should().NotBeNull($"every offset the scale needs must have a home ({offset}c)");
            channel!.Carries(offset).Should().BeTrue();
        }
    }

    [Fact]
    public void FindReturnsNullForATrackThatWasNeverAllocated() =>
        Allocate(Rast, trackCount: 1).Find(99, 99, 0).Should().BeNull();

    // --- the budget, integrated ---------------------------------------------------------------

    [Fact]
    public void ThreeSlendroTracksFitExactlyWithoutCompromise()
    {
        ChannelAllocation allocation = Allocate(Slendro, trackCount: 3);

        allocation.ChannelCount.Should().Be(15);
        allocation.HadToCompromise.Should().BeFalse();
        allocation.Describe().Should().BeNull();
    }

    /// <summary>
    /// The case the design exists for: four Slendro tracks want 20 channels against 15.
    /// </summary>
    [Fact]
    public void FourSlendroTracksRaiseTheToleranceForEveryTrackAtOnce()
    {
        ChannelAllocation allocation = Allocate(Slendro, trackCount: 4);

        allocation.ChannelCount.Should().BeLessThanOrEqualTo(15);
        allocation.Budget.ToleranceWasRaised.Should().BeTrue();
        allocation.Muted.Should().BeEmpty();
        allocation.Describe().Should().Contain("tuning accuracy reduced");
    }

    /// <summary>
    /// The invariant the whole adaptive-tolerance design exists to guarantee: every surviving
    /// track-channel is voiced with the SAME set of bends.
    /// </summary>
    /// <remarks>
    /// Per-track degradation would clash 40 cents on Slendro's degrees 1 and 4 - bitonality, not
    /// graceful degradation. Asserting it here, at the allocator, is what makes it true of the
    /// actual output rather than only of the plan.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void EverySurvivingTrackIsVoicedWithTheSameBends(int trackCount)
    {
        ChannelAllocation allocation = Allocate(Slendro, trackCount);

        var bendsPerTrack = allocation.Channels
            .GroupBy(c => (c.SourceTrackIndex, c.SourceChannel))
            .Select(g => g.Select(c => Math.Round(c.BendCents, 6)).OrderBy(b => b).ToArray())
            .ToList();

        bendsPerTrack.Should().OnlyContain(b => b.SequenceEqual(bendsPerTrack[0]),
            "two tracks in different tunings is bitonality, not degradation");
    }

    [Fact]
    public void NeverExceedsTheCeiling()
    {
        for (int tracks = 1; tracks <= 20; tracks++)
        {
            Allocate(Slendro, tracks).ChannelCount.Should().BeLessThanOrEqualTo(15);
        }
    }

    [Fact]
    public void MutedTrackChannelsGetNoChannelsAndAreReported()
    {
        ChannelAllocation allocation = Allocate(Slendro, trackCount: 20);

        allocation.Muted.Should().NotBeEmpty();
        allocation.Describe().Should().Contain("muted in preview");

        foreach (TrackChannelDemand muted in allocation.Muted)
        {
            allocation.IsMuted(muted.TrackIndex, muted.Channel).Should().BeTrue();
            allocation.Channels.Should().NotContain(c =>
                c.SourceTrackIndex == muted.TrackIndex && c.SourceChannel == muted.Channel);
        }
    }

    // --- preview and export agree -----------------------------------------------------------

    /// <summary>
    /// Playback and export call this with the same ceiling, so they cannot diverge. That guarantee
    /// is why the allocator is one component rather than two.
    /// </summary>
    [Fact]
    public void TheSameCeilingAlwaysProducesTheSameAllocation()
    {
        ChannelAllocation playback = Allocate(Slendro, trackCount: 4, ceiling: 15);
        ChannelAllocation export = Allocate(Slendro, trackCount: 4, ceiling: 15);

        export.Channels.Should().BeEquivalentTo(playback.Channels);
        export.Budget.EffectiveToleranceCents.Should().Be(playback.Budget.EffectiveToleranceCents);
    }

    // --- bends ------------------------------------------------------------------------------

    [Fact]
    public void EachChannelsBendIsTheMeanOfTheOffsetsItCarries()
    {
        ChannelAllocation allocation = Allocate(Slendro, trackCount: 5);

        foreach (AllocatedChannel channel in allocation.Channels)
        {
            channel.BendCents.Should().BeApproximately(channel.Offsets.Average(), 1e-9);
        }
    }

    [Fact]
    public void ReportedErrorNeverExceedsTheEffectiveTolerance()
    {
        ChannelAllocation allocation = Allocate(Slendro, trackCount: 5);

        allocation.Channels.Should().OnlyContain(c =>
            c.MaxErrorCents <= allocation.Budget.EffectiveToleranceCents + 1e-9);
    }

    [Fact]
    public void RastsTwoChannelsCarryZeroAndMinusFiftyCents()
    {
        ChannelAllocation allocation = Allocate(Rast, trackCount: 1);

        allocation.Channels.Select(c => c.BendCents).OrderBy(b => b)
            .Should().Equal([-50.0, 0.0]);
    }

    [Fact]
    public void OffsetForReadsBackTheBendANoteNeeds()
    {
        ChannelAllocator.OffsetFor(Pitch.FromMidi(60)).Should().Be(0);
        ChannelAllocator.OffsetFor(new Pitch(6350)).Should().Be(-50);
        ChannelAllocator.OffsetFor(new Pitch(6240)).Should().BeApproximately(40, 1e-9);
    }
}
