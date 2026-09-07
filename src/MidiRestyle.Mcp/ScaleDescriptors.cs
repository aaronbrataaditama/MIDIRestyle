using MidiRestyle.Core.Output;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.Mcp;

/// <summary>
/// Everything an agent can filter or read about a scale that is not authored data: the quality of its
/// third, whether it can actually be spelled, whether 12-TET carries it, its channel cost. All derived
/// from Core, never re-derived here - see each member's remark for which Core predicate it wraps.
/// </summary>
public static class ScaleDescriptors
{
    /// <summary>
    /// The first degree, ascending, inside one of three fixed bands: minor 280–330¢, neutral 330–370¢,
    /// major 370–420¢; <c>none</c> otherwise. Fixed bands, not "nearest degree": Slendro's ~240 and
    /// ~480 must not read as a third at all, and Rast's 350 is neutral only because the band says so.
    /// </summary>
    public static string Third(Scale scale)
    {
        foreach (double c in scale.DegreeCents)
        {
            if (c >= 280 && c <= 330) { return "minor"; }
            if (c > 330 && c <= 370) { return "neutral"; }
            if (c > 370 && c <= 420) { return "major"; }
        }

        return "none";
    }

    /// <summary>CLAUDE.md: the flag gates, the speller decides. An eight-degree scale flagged notatable is not.</summary>
    public static bool IsNotatable(Scale scale, out string? reason)
    {
        reason = null;
        if (!scale.Notatable)
        {
            return false;
        }

        SpellingResult result = scale.Spelling is not null ? new SpellingResult(scale.Spelling) : DiatonicSpeller.Derive(scale);
        reason = result.Succeeded ? null : result.Diagnostic;
        return result.Succeeded;
    }

    /// <summary>Core's own <c>OutputMode.Auto</c> test, at the default tolerance.</summary>
    public static bool FitsTwelveTet(Scale scale) =>
        OffsetClusterer.FitsTwelveTet(scale.DegreeOffsets, RestyleDefaults.ToleranceCents);

    public static ScaleSummary Summarise(Scale scale, ScaleOrigin? origin)
    {
        FidelityReport fidelity = TuningFidelity.Assess(scale);
        return new ScaleSummary(
            scale.Id, scale.Name, scale.Tradition, scale.Region, scale.DegreeCount,
            IsNotatable(scale, out _), FitsTwelveTet(scale), Round(fidelity.MaxDeviationCents),
            EnumNames.Echo(fidelity.Badge), Third(scale));
    }

    public static ScaleDetail Describe(Scale scale, ScaleOrigin? origin)
    {
        FidelityReport fidelity = TuningFidelity.Assess(scale);
        bool notatable = IsNotatable(scale, out string? reason);
        IReadOnlyList<string>? spelling = notatable
            ? (scale.Spelling ?? DiatonicSpeller.Derive(scale).Spelling)?.Select(s => s.ToStringOnC()).ToList()
            : null;

        return new ScaleDetail(
            Summarise(scale, origin), scale.Description, scale.Source,
            [.. scale.DegreeCents.Select(Round)], [.. scale.DegreeOffsets.Select(Round)],
            spelling, reason,
            new FidelityInfo(EnumNames.Echo(fidelity.Badge), Round(fidelity.MaxDeviationCents), fidelity.WorstDegreeIndex),
            OffsetClusterer.ClusterCount(scale, RestyleDefaults.ToleranceCents),
            origin is null ? "unknown" : EnumNames.Echo(origin.Value));
    }

    public static double Round(double cents) => Math.Round(cents, 2, MidpointRounding.AwayFromZero);
}
