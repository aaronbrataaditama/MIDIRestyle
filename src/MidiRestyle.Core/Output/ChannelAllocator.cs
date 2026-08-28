using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Output;

/// <summary>
/// One physical output channel, and what it carries.
/// </summary>
/// <param name="OutputChannel">The MIDI channel, 0-15, never 9.</param>
/// <param name="SourceTrackIndex">Which source track this serves.</param>
/// <param name="SourceChannel">Which source channel this serves.</param>
/// <param name="BendCents">The pitch bend held on this channel for its whole life.</param>
/// <param name="Offsets">The scale offsets folded onto this channel.</param>
public sealed record AllocatedChannel(
    int OutputChannel,
    int SourceTrackIndex,
    int SourceChannel,
    double BendCents,
    IReadOnlyList<double> Offsets)
{
    /// <summary>Whether a note needing <paramref name="offsetCents"/> belongs on this channel.</summary>
    public bool Carries(double offsetCents) =>
        Offsets.Any(o => Math.Abs(o - offsetCents) < 1e-9);

    /// <summary>Worst tuning error a note on this channel suffers.</summary>
    public double MaxErrorCents =>
        Offsets.Count == 0 ? 0 : Offsets.Max(o => Math.Abs(o - BendCents));
}

/// <summary>The complete channel plan for one restyle.</summary>
public sealed record ChannelAllocation(
    IReadOnlyList<AllocatedChannel> Channels,
    ChannelBudgetPlan Budget,
    IReadOnlyList<TrackChannelDemand> Muted)
{
    public int ChannelCount => Channels.Count;

    /// <summary>Whether anything was compromised to fit. Drives the status bar.</summary>
    public bool HadToCompromise => Budget.ToleranceWasRaised || Muted.Count > 0;

    public string? Describe() => Budget.Describe();

    /// <summary>
    /// Finds the channel a note belongs on.
    /// </summary>
    /// <remarks>
    /// Keyed on <c>(track, channel, offset)</c> - all three. Keying on channel alone would merge two
    /// Format 1 tracks that legally share a channel with different programs; keying on track alone
    /// cannot express the drum exclusion. Returns null when the track-channel was muted.
    /// </remarks>
    public AllocatedChannel? Find(int trackIndex, int sourceChannel, double offsetCents)
    {
        foreach (AllocatedChannel channel in Channels)
        {
            if (channel.SourceTrackIndex == trackIndex
                && channel.SourceChannel == sourceChannel
                && channel.Carries(offsetCents))
            {
                return channel;
            }
        }

        return null;
    }

    /// <summary>Whether this track-channel was muted rather than allocated.</summary>
    public bool IsMuted(int trackIndex, int sourceChannel) =>
        Muted.Any(m => m.TrackIndex == trackIndex && m.Channel == sourceChannel);
}

/// <summary>
/// Assigns physical MIDI channels to the pitch-bend offsets a restyle needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>One channel per distinct cent-offset, not per voice.</b> Pitch bend is channel-wide, so
/// grouping notes by the offset they need gives unlimited polyphony per channel. Maqam Rast needs
/// two channels, not fifteen - which is the insight that made microtonality affordable in v1 at all.
/// </para>
/// <para>
/// This is the single path shared by playback and export, and <b>both pass the same ceiling</b>, so
/// preview and exported file are always identical. There is no path by which they diverge; if you
/// find yourself adding one, that is the bug.
/// </para>
/// </remarks>
public static class ChannelAllocator
{
    /// <summary>The GM percussion channel, never allocated for pitched output.</summary>
    public const int DrumChannel = TrackInfo.DrumChannel;

    /// <summary>Channels 0-15 excluding 9, in allocation order.</summary>
    public static readonly int[] AvailableChannels =
        [.. Enumerable.Range(0, 16).Where(c => c != DrumChannel)];

    /// <summary>
    /// Plans the channel layout for a restyle.
    /// </summary>
    /// <param name="result">The restyled model.</param>
    /// <param name="ceiling">
    /// Physical channels available. 15 for a single port - and the same value must be passed by
    /// playback and by export.
    /// </param>
    public static ChannelAllocation Allocate(
        RestyleResult result,
        int ceiling = ChannelBudget.DefaultCeiling)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceiling);

        // Only restyled, pitched track-channels compete for channels. Drums keep channel 9 and are
        // never remapped; opted-out tracks keep their original channel and need no bend.
        List<TrackChannelDemand> demands = [.. result.Tracks
            .Where(t => t.WasRestyled && !t.IsDrums && t.Notes.Count > 0)
            .Select(t => new TrackChannelDemand(t.TrackIndex, t.Channel, t.Notes.Count))];

        IReadOnlyList<double> offsets = OffsetClusterer.DistinctOffsets(result.Settings.TargetScale);

        ChannelBudgetPlan budget = ChannelBudget.Plan(
            offsets, demands, result.Settings.ToleranceCents, ceiling);

        IReadOnlyList<OffsetCluster> clusters =
            OffsetClusterer.Cluster(offsets, budget.EffectiveToleranceCents);

        var channels = new List<AllocatedChannel>(budget.ChannelsUsed);
        int next = 0;

        foreach (TrackChannelDemand demand in demands)
        {
            if (budget.Muted.Contains(demand))
            {
                continue;
            }

            foreach (OffsetCluster cluster in clusters)
            {
                if (next >= AvailableChannels.Length)
                {
                    // Unreachable while the budget is honoured, but a silent overrun here would
                    // stomp the drum channel, so it fails loudly rather than wrapping.
                    throw new InvalidOperationException(
                        $"Channel allocation overran the {AvailableChannels.Length} available " +
                        $"channels. The budget planned {budget.ChannelsUsed} but allocation wanted " +
                        $"more - ChannelBudget and ChannelAllocator have drifted apart.");
                }

                channels.Add(new AllocatedChannel(
                    AvailableChannels[next++],
                    demand.TrackIndex,
                    demand.Channel,
                    cluster.BendCents,
                    cluster.Members));
            }
        }

        return new ChannelAllocation(channels, budget, budget.Muted);
    }

    /// <summary>
    /// The offset a note needs, derived from its pitch.
    /// </summary>
    /// <remarks>
    /// Safe to derive per note here, unlike when computing a scale's channel demand: the note is
    /// already mapped, so this is reading back an offset the scale determined rather than deriving
    /// a new one. Scale-level offsets still come from <c>Scale.DegreeOffsets</c>.
    /// </remarks>
    public static double OffsetFor(Pitch pitch) =>
        MidiRounding.OffsetFromNearestSemitone(pitch.Cents);
}
