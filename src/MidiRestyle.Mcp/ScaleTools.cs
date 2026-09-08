using System.ComponentModel;
using MidiRestyle.Core.Scales;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MidiRestyle.Mcp;

/// <summary>
/// Read-only tools over the scale library. One instance per process, shared by concurrent calls; it
/// holds only the immutable library, so it needs no synchronisation of its own.
/// </summary>
[McpServerToolType]
public sealed class ScaleTools(ScaleLibrary library)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    private static readonly string[] Thirds = ["major", "minor", "neutral", "none"];

    [McpServerTool(Name = "list_scales", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Search and filter the ~170 world scales MIDIRestyle can restyle into. Filters combine with AND. " +
                 "Returns a page of compact summaries; call describe_scale for one scale's full detail.")]
    public CallToolResult ListScales(
        [Description("Free-text search over name, tradition, region and id; every whitespace-separated term must match.")] string? query = null,
        [Description("Exact region, case-insensitive, e.g. 'Middle East'.")] string? region = null,
        [Description("Exact tradition, case-insensitive, e.g. 'Arabic maqam'.")] string? tradition = null,
        [Description("Number of degrees per octave.")] int? degreeCount = null,
        [Description("true: only scales that can be written on a staff (the speller succeeds); false: only those that cannot.")] bool? notatable = null,
        [Description("true: only scales an ordinary 12-TET instrument plays within 5 cents; false: only microtonal scales.")] bool? fitsTwelveTet = null,
        [Description("Keep scales whose largest deviation from 12-TET is at most this many cents.")] double? maxDeviationCents = null,
        [Description("Quality of the scale's third: major, minor, neutral or none.")] string? third = null,
        [Description("Page size 1..200 (default 50).")] int limit = DefaultLimit,
        [Description("Matches to skip (default 0).")] int offset = 0)
    {
        if (limit is < 1 or > MaxLimit) { return ToolResults.Error($"limit must be 1..{MaxLimit}."); }
        if (offset < 0) { return ToolResults.Error("offset must be >= 0."); }
        if (third is not null && !Thirds.Contains(third, StringComparer.OrdinalIgnoreCase))
        {
            return ToolResults.Error($"third '{third}' is not valid; use one of: {string.Join(", ", Thirds)}.");
        }

        IEnumerable<Scale> candidates = string.IsNullOrWhiteSpace(query) ? library.Scales : library.Search(query);
        List<ScaleSummary> matches = [.. candidates
            .Where(s => region is null || string.Equals(s.Region, region, StringComparison.OrdinalIgnoreCase))
            .Where(s => tradition is null || string.Equals(s.Tradition, tradition, StringComparison.OrdinalIgnoreCase))
            .Where(s => degreeCount is null || s.DegreeCount == degreeCount)
            .Select(s => ScaleDescriptors.Summarise(s, library.OriginOf(s.Id)))
            .Where(s => notatable is null || s.Notatable == notatable)
            .Where(s => fitsTwelveTet is null || s.FitsTwelveTet == fitsTwelveTet)
            // A scale with no finite deviation (FidelityBadge.Impossible) can satisfy no ceiling,
            // so a null reports as "does not fit" rather than passing the filter unmeasured.
            .Where(s => maxDeviationCents is null || s.MaxDeviationCents <= maxDeviationCents)
            .Where(s => third is null || string.Equals(s.Third, third, StringComparison.OrdinalIgnoreCase))];

        List<ScaleSummary> page = [.. matches.Skip(offset).Take(limit)];
        return ToolResults.Ok(new ScalePage(matches.Count, offset, offset + page.Count < matches.Count, page));
    }

    [McpServerTool(Name = "describe_scale", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Full detail for one scale: degrees in cents, per-degree deviation from 12-TET, staff spelling if any, " +
                 "source citation, and how many pitch-bend channels it costs.")]
    public CallToolResult DescribeScale(
        [Description("Scale id as returned by list_scales (case-insensitive).")] string scaleId)
    {
        if (!ScaleLookup.TryFind(library, scaleId, "scaleId", out Scale? scale, out string? error))
        {
            return ToolResults.Error(error);
        }

        return ToolResults.Ok(ScaleDescriptors.Describe(scale, library.OriginOf(scale.Id)));
    }
}
