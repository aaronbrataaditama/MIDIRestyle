namespace MidiRestyle.Mcp;

/// <remarks>
/// <see cref="MaxDeviationCents"/> is nullable: <see cref="MidiRestyle.Core.Scales.FidelityBadge.Impossible"/>
/// reports <see cref="double.PositiveInfinity"/> from <see cref="MidiRestyle.Core.Scales.TuningFidelity"/>,
/// and <c>System.Text.Json</c> throws on a non-finite <c>double</c> rather than carry it. Null plus
/// <see cref="FidelityBadge"/> = <c>"impossible"</c> tells an agent the truth; a numeric infinity on the
/// wire would either crash serialisation or (with <c>AllowNamedFloatingPointLiterals</c>) let it leak
/// into every other numeric field too.
/// </remarks>
public sealed record ScaleSummary(
    string Id, string Name, string Tradition, string Region, int DegreeCount,
    bool Notatable, bool FitsTwelveTet, double? MaxDeviationCents, string FidelityBadge, string Third);

/// <remarks>See <see cref="ScaleSummary"/>'s remarks on why <see cref="MaxDeviationCents"/> is nullable.</remarks>
public sealed record FidelityInfo(string Badge, double? MaxDeviationCents, int WorstDegreeIndex);

public sealed record ScaleDetail(
    ScaleSummary Summary, string? Description, string Source,
    IReadOnlyList<double> DegreeCents, IReadOnlyList<double> DegreeOffsets,
    IReadOnlyList<string>? SpellingOnC, string? NotatableReason,
    FidelityInfo Fidelity, int BendClustersAtDefaultTolerance, string Origin);

public sealed record ScalePage(int TotalMatches, int Offset, bool Truncated, IReadOnlyList<ScaleSummary> Scales);
