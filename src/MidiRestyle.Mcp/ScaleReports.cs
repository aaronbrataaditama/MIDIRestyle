namespace MidiRestyle.Mcp;

public sealed record ScaleSummary(
    string Id, string Name, string Tradition, string Region, int DegreeCount,
    bool Notatable, bool FitsTwelveTet, double MaxDeviationCents, string FidelityBadge, string Third);

public sealed record FidelityInfo(string Badge, double MaxDeviationCents, int WorstDegreeIndex);

public sealed record ScaleDetail(
    ScaleSummary Summary, string? Description, string Source,
    IReadOnlyList<double> DegreeCents, IReadOnlyList<double> DegreeOffsets,
    IReadOnlyList<string>? SpellingOnC, string? NotatableReason,
    FidelityInfo Fidelity, int BendClustersAtDefaultTolerance, string Origin);

public sealed record ScalePage(int TotalMatches, int Offset, bool Truncated, IReadOnlyList<ScaleSummary> Scales);
