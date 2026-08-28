namespace MidiRestyle.Core.Scales;

/// <summary>How faithfully 12-TET can render a scale.</summary>
public enum FidelityBadge
{
    /// <summary>Within 5 cents everywhere. 12-TET loses nothing audible.</summary>
    Exact,

    /// <summary>Within 25 cents. Recognisable, with an audible compromise on some degrees.</summary>
    Close,

    /// <summary>Beyond 25 cents. 12-TET misrepresents the scale rather than approximating it.</summary>
    Approximate,

    /// <summary>12-TET cannot express this scale at all - degrees too close to separate.</summary>
    Impossible,
}

/// <summary>What 12-TET costs for one scale.</summary>
/// <param name="Badge">The summary judgement.</param>
/// <param name="MaxDeviationCents">Worst per-degree error, in cents.</param>
/// <param name="WorstDegreeIndex">Which degree suffers that worst error. -1 when none does.</param>
public sealed record FidelityReport(
    FidelityBadge Badge,
    double MaxDeviationCents,
    int WorstDegreeIndex)
{
    /// <summary>
    /// Whether the UI should present this as a <em>warning</em> rather than as neutral information.
    /// </summary>
    /// <remarks>
    /// Deviation on its own is neutral - it is a fact about a tuning, shown calmly at all times.
    /// It becomes a warning only in the one state where the app is actually failing to deliver what
    /// the user asked for: 12-TET output on a scale 12-TET cannot carry. Inverted, this either cries
    /// wolf on every maqam or goes silent exactly when the user is being short-changed.
    /// </remarks>
    public bool IsWarningIn(bool outputIsTwelveTet) =>
        outputIsTwelveTet && Badge is FidelityBadge.Approximate or FidelityBadge.Impossible;

    /// <summary>A short label for the badge chip.</summary>
    public string Label => Badge switch
    {
        FidelityBadge.Exact => "Exact",
        FidelityBadge.Close => "Close",
        FidelityBadge.Approximate => "Approximate",
        FidelityBadge.Impossible => "Not in 12-TET",
        _ => "Unknown",
    };

    /// <summary>A calm one-line description, suitable at all times.</summary>
    public string Describe() => Badge switch
    {
        FidelityBadge.Exact => "Exact in 12-TET",
        FidelityBadge.Impossible => "Cannot be expressed in 12-TET",
        _ => $"Up to {MaxDeviationCents:0.#} cents from 12-TET",
    };
}

/// <summary>
/// Computes how far a scale sits from its own 12-TET quantisation.
/// </summary>
/// <remarks>
/// <b>Computed, never hand-tagged.</b> A hand-set fidelity field would drift the moment anyone edited
/// a cents value, and would be wrong for every imported or user-authored scale. Deriving it means it
/// stays correct for free and tells the user exactly what 12-TET mode costs them for the scale in
/// front of them.
/// </remarks>
public static class TuningFidelity
{
    /// <summary>
    /// At or under this, 12-TET is indistinguishable. Roughly the just-noticeable difference for
    /// melodic pitch; also the default clustering tolerance, which is not a coincidence - both
    /// answer "is this difference audible?".
    /// </summary>
    public const double ExactThresholdCents = 5.0;

    /// <summary>
    /// At or under this, the scale survives 12-TET recognisably. Half of a quarter-tone: past it,
    /// a degree is closer to its neighbour than to where it belongs.
    /// </summary>
    public const double CloseThresholdCents = 25.0;

    /// <summary>Assesses a scale.</summary>
    public static FidelityReport Assess(Scale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return Assess(scale.DegreeCents);
    }

    /// <summary>Assesses a bare degree list.</summary>
    public static FidelityReport Assess(IReadOnlyList<double> degreeCents)
    {
        ArgumentNullException.ThrowIfNull(degreeCents);

        QuantisationResult quantised = TwelveTetQuantiser.Quantise(degreeCents);
        if (!quantised.Succeeded)
        {
            return new FidelityReport(FidelityBadge.Impossible, double.PositiveInfinity, -1);
        }

        double worst = 0;
        int worstIndex = -1;

        for (int i = 0; i < degreeCents.Count; i++)
        {
            double deviation = Math.Abs(degreeCents[i] - quantised.Degrees[i]);
            if (deviation > worst)
            {
                worst = deviation;
                worstIndex = i;
            }
        }

        FidelityBadge badge =
            worst <= ExactThresholdCents ? FidelityBadge.Exact
            : worst <= CloseThresholdCents ? FidelityBadge.Close
            : FidelityBadge.Approximate;

        return new FidelityReport(badge, worst, worstIndex);
    }
}
