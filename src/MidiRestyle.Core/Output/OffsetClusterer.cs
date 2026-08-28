using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Output;

/// <summary>One group of cent-offsets that will share a single pitch-bend channel.</summary>
/// <param name="BendCents">The bend actually emitted: the arithmetic mean of the members.</param>
/// <param name="Members">The offsets folded into this cluster, ascending.</param>
public sealed record OffsetCluster(double BendCents, IReadOnlyList<double> Members)
{
    /// <summary>Distance from the lowest to the highest member.</summary>
    public double SpanCents => Members.Count == 0 ? 0 : Members[^1] - Members[0];

    /// <summary>
    /// Worst error any member suffers by being voiced at <see cref="BendCents"/>. This is what the
    /// UI reports when the channel budget forces the tolerance up.
    /// </summary>
    public double MaxErrorCents => Members.Count == 0 ? 0 : Members.Max(m => Math.Abs(m - BendCents));

    /// <summary>Whether <paramref name="offset"/> was folded into this cluster.</summary>
    public bool Contains(double offset) => Members.Any(m => Math.Abs(m - offset) < 1e-9);
}

/// <summary>
/// Groups the cent-offsets a scale needs into as few pitch-bend channels as a tolerance allows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Greedy and span-bounded, not single-linkage.</b> The distinction is not academic. Pythagorean
/// Gong's offsets are 0, 1.955, 3.910, 5.865, 7.820 - every adjacent gap is exactly 1.955 cents. So
/// chaining any pair within a 5-cent tolerance folds all five into <em>one</em> cluster with 3.9
/// cents of error, while bounding each cluster's total span to 5 cents yields <em>two</em> with under
/// 2 cents. Across the ~20 Turkish makams in the library the choice swings channel demand by up to
/// 2x, which directly changes when the channel budget binds.
/// </para>
/// <para>
/// Span-bounding is the correct choice because it bounds the <em>error</em> a note suffers, which is
/// what a listener hears; single-linkage bounds only the gap between neighbours, which nobody hears.
/// </para>
/// <para>
/// Deterministic and order-independent: the input is sorted first, so the same offset set always
/// yields the same clusters regardless of the order it arrives in.
/// </para>
/// </remarks>
public static class OffsetClusterer
{
    /// <summary>
    /// Default tolerance in cents. At 1 cent, historically-tuned Pythagorean Gong burns five
    /// channels to deliver a maximum 7.8-cent correction - inaudible, and a waste of the scarcest
    /// resource in the design. At 5 cents it collapses to two channels with under 3 cents of error.
    /// </summary>
    public const double DefaultToleranceCents = 5.0;

    /// <summary>
    /// The escalation ladder used when the channel budget does not fit. Applied to the whole project
    /// at once, never per track - mixing tunings within one piece produces bitonality, not
    /// degradation.
    /// </summary>
    public static readonly double[] ToleranceLadder = [5.0, 10.0, 15.0, 25.0, 35.0, 50.0];

    /// <summary>
    /// Clusters <paramref name="offsets"/> so that no cluster spans more than
    /// <paramref name="toleranceCents"/>.
    /// </summary>
    public static IReadOnlyList<OffsetCluster> Cluster(
        IEnumerable<double> offsets,
        double toleranceCents = DefaultToleranceCents)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentOutOfRangeException.ThrowIfNegative(toleranceCents);

        // Distinct-then-sort makes the result independent of input order, and collapses the exact
        // duplicates that are the common case (Rast's two neutral degrees are both -50).
        double[] sorted = [.. offsets.Distinct().OrderBy(o => o)];
        if (sorted.Length == 0)
        {
            return [];
        }

        var clusters = new List<OffsetCluster>();
        int start = 0;

        for (int i = 1; i <= sorted.Length; i++)
        {
            // Extend while the cluster's total span stays within tolerance. Comparing against
            // sorted[start] - the cluster minimum - rather than sorted[i - 1] is precisely what
            // makes this span-bounded rather than single-linkage.
            bool fits = i < sorted.Length
                && sorted[i] - sorted[start] <= toleranceCents + 1e-9;

            if (fits)
            {
                continue;
            }

            double[] members = sorted[start..i];
            clusters.Add(new OffsetCluster(members.Average(), members));
            start = i;
        }

        return clusters;
    }

    /// <summary>
    /// The offsets a scale needs, deduplicated. Derived from the scale's degrees, never from
    /// absolute note pitches - see <c>Scale.DegreeOffsets</c> for why.
    /// </summary>
    public static IReadOnlyList<double> DistinctOffsets(Scales.Scale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return [.. scale.DegreeOffsets.Distinct().OrderBy(o => o)];
    }

    /// <summary>How many channels one track-channel needs for this scale at this tolerance.</summary>
    public static int ClusterCount(Scales.Scale scale, double toleranceCents = DefaultToleranceCents) =>
        Cluster(DistinctOffsets(scale), toleranceCents).Count;

    /// <summary>
    /// Finds the cluster voicing <paramref name="offset"/>. Every offset that went in comes back out
    /// of exactly one cluster, so a miss means the caller clustered a different offset set.
    /// </summary>
    public static OffsetCluster ClusterFor(IReadOnlyList<OffsetCluster> clusters, double offset)
    {
        ArgumentNullException.ThrowIfNull(clusters);

        foreach (OffsetCluster cluster in clusters)
        {
            if (cluster.Contains(offset))
            {
                return cluster;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(offset),
            offset,
            $"No cluster contains this offset. Clusters were built from a different offset set. " +
            $"Available: {string.Join(", ", clusters.Select(c => c.BendCents))}.");
    }

    /// <summary>
    /// Whether every offset is close enough to zero that 12-TET delivers the scale. This is the
    /// <c>OutputMode.Auto</c> test - stated against the tolerance rather than as an exact
    /// float comparison with zero.
    /// </summary>
    public static bool FitsTwelveTet(
        IEnumerable<double> offsets,
        double toleranceCents = DefaultToleranceCents)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        return offsets.All(o => Math.Abs(o) <= toleranceCents + 1e-9);
    }

    /// <summary>Sanity guard: no offset may leave the half-semitone window by construction.</summary>
    internal static void AssertOffsetsInRange(IEnumerable<double> offsets)
    {
        const double Half = MidiRounding.CentsPerSemitone / 2.0;
        foreach (double o in offsets)
        {
            if (o < -Half - 1e-9 || o >= Half + 1e-9)
            {
                throw new InvalidOperationException(
                    $"Offset {o} is outside [-50, +50). Offsets must come from " +
                    "MidiRounding.OffsetFromNearestSemitone, not from arbitrary arithmetic.");
            }
        }
    }
}
