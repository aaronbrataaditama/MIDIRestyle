namespace MidiRestyle.Core.Output;

/// <summary>One track-channel's demand on the budget: how many bend channels its tuning needs.</summary>
/// <param name="TrackIndex">Source track.</param>
/// <param name="Channel">Source channel.</param>
/// <param name="NoteCount">Used to rank importance when something has to be muted.</param>
public readonly record struct TrackChannelDemand(int TrackIndex, int Channel, int NoteCount);

/// <summary>What the budget decided, and what it cost.</summary>
/// <param name="EffectiveToleranceCents">
/// The clustering tolerance actually used, which may be higher than the user asked for.
/// </param>
/// <param name="ClustersPerTrackChannel">How many bend channels each surviving track-channel gets.</param>
/// <param name="WorstErrorCents">The largest tuning error the raised tolerance introduces.</param>
/// <param name="Muted">
/// Track-channels excluded from preview because even one channel each would not fit.
/// </param>
/// <param name="ChannelsUsed">Total physical channels consumed.</param>
public sealed record ChannelBudgetPlan(
    double EffectiveToleranceCents,
    int ClustersPerTrackChannel,
    double WorstErrorCents,
    IReadOnlyList<TrackChannelDemand> Muted,
    int ChannelsUsed)
{
    /// <summary>Whether the budget had to raise the tolerance above what the user chose.</summary>
    public bool ToleranceWasRaised { get; init; }

    public bool AnythingMuted => Muted.Count > 0;

    /// <summary>
    /// A status-bar line, or null when nothing was compromised.
    /// </summary>
    /// <remarks>
    /// Silence here would be the bug. Both compromises this type can make - a coarser tuning and a
    /// muted track - are invisible in the output itself, so if the status bar does not say so the
    /// user has no way to find out.
    /// </remarks>
    public string? Describe()
    {
        List<string> parts = [];

        if (ToleranceWasRaised)
        {
            parts.Add(
                $"tuning accuracy reduced to ±{WorstErrorCents:0.#}¢ " +
                $"(grouping within {EffectiveToleranceCents:0.#}¢) to fit the channel budget");
        }

        if (AnythingMuted)
        {
            parts.Add(
                $"{Muted.Count} track-channel{(Muted.Count == 1 ? "" : "s")} muted in preview: " +
                "even one channel each will not fit");
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}

/// <summary>
/// Decides how many pitch-bend channels each track-channel may have.
/// </summary>
/// <remarks>
/// <para>
/// This is the tightest constraint in the design and it binds in ordinary use, not at the edges. One
/// MIDI port has 16 channels; excluding channel 9 for percussion leaves <b>15</b>. Slendro needs 5
/// clusters at the default tolerance, so <b>four pitched track-channels need 20</b> and three need
/// exactly 15 with no headroom.
/// </para>
/// <para>
/// <b>The fix is to raise the tolerance for the whole project, never to degrade tracks
/// individually.</b> Giving one track 12-TET while another keeps true Slendro is not graceful
/// degradation, it is bitonality: the two clash <b>40 cents</b> on scale degrees 1 and 4 and 20
/// cents on degrees 2 and 3 - audibly worse than either uniform choice. Raising the tolerance keeps
/// every track in one tuning, so the result is always internally consistent and the only thing that
/// varies is how precise it is.
/// </para>
/// <para>
/// If even one channel each will not fit, the excess is <b>muted, not retuned</b>. Muting is honest;
/// mixing tunings is not. Note that more than 15 pitched channels cannot play through a single port
/// in any case, microtonal or not.
/// </para>
/// </remarks>
public static class ChannelBudget
{
    /// <summary>Channels available on one port, excluding channel 9 for percussion.</summary>
    public const int DefaultCeiling = 15;

    /// <summary>
    /// Plans an allocation.
    /// </summary>
    /// <param name="offsets">The distinct cent-offsets the target scale requires.</param>
    /// <param name="demands">The pitched track-channels wanting to be restyled.</param>
    /// <param name="requestedToleranceCents">The user's clustering tolerance. A starting point.</param>
    /// <param name="ceiling">Physical channels available. 15 for one port.</param>
    public static ChannelBudgetPlan Plan(
        IReadOnlyList<double> offsets,
        IReadOnlyList<TrackChannelDemand> demands,
        double requestedToleranceCents = OffsetClusterer.DefaultToleranceCents,
        int ceiling = DefaultCeiling)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentNullException.ThrowIfNull(demands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceiling);

        if (demands.Count == 0)
        {
            return new ChannelBudgetPlan(requestedToleranceCents, 0, 0, [], 0);
        }

        // The ladder starts at whatever the user asked for, then escalates. Anything in the standard
        // ladder below the user's setting is skipped: raising the tolerance is a compromise forced
        // by the budget, so it must never make the tuning coarser than necessary, and never finer
        // than the user chose either.
        double[] ladder =
        [
            requestedToleranceCents,
            .. OffsetClusterer.ToleranceLadder.Where(t => t > requestedToleranceCents),
        ];

        foreach (double tolerance in ladder)
        {
            IReadOnlyList<OffsetCluster> clusters = OffsetClusterer.Cluster(offsets, tolerance);
            int perTrack = Math.Max(1, clusters.Count);

            if (demands.Count * perTrack > ceiling)
            {
                continue;
            }

            double worstError = clusters.Count == 0 ? 0 : clusters.Max(c => c.MaxErrorCents);

            return new ChannelBudgetPlan(
                tolerance,
                perTrack,
                worstError,
                [],
                demands.Count * perTrack)
            {
                // Compare against the request, not against the default: a user who deliberately
                // chose a coarse tolerance has not been compromised by getting it.
                ToleranceWasRaised = tolerance > requestedToleranceCents,
            };
        }

        // Past the end of the ladder. Every scale collapses toward a single cluster at 50 cents, so
        // what remains is simply too many track-channels for one port - mute the least important.
        double finalTolerance = ladder[^1];
        IReadOnlyList<OffsetCluster> finalClusters = OffsetClusterer.Cluster(offsets, finalTolerance);
        int finalPerTrack = Math.Max(1, finalClusters.Count);

        int affordable = Math.Max(0, ceiling / finalPerTrack);

        // Rank by note count: the busiest parts are the ones a listener would miss. Ties break on
        // track then channel so the choice is deterministic rather than dependent on input order.
        List<TrackChannelDemand> ranked = [.. demands
            .OrderByDescending(d => d.NoteCount)
            .ThenBy(d => d.TrackIndex)
            .ThenBy(d => d.Channel)];

        List<TrackChannelDemand> muted = [.. ranked.Skip(affordable)];

        return new ChannelBudgetPlan(
            finalTolerance,
            finalPerTrack,
            finalClusters.Count == 0 ? 0 : finalClusters.Max(c => c.MaxErrorCents),
            muted,
            affordable * finalPerTrack)
        {
            ToleranceWasRaised = finalTolerance > requestedToleranceCents,
        };
    }
}
