using System.Collections.Frozen;

namespace MidiRestyle.Core.Scales;

/// <summary>Where a scale came from. Determines precedence when ids collide.</summary>
public enum ScaleOrigin
{
    /// <summary>Produced in code, e.g. the 72 melakarta. Lowest precedence.</summary>
    Generated,

    /// <summary>Shipped inside the assembly as an embedded resource.</summary>
    Embedded,

    /// <summary>From the <c>scales/</c> folder beside the exe.</summary>
    BesideExe,

    /// <summary>Authored or imported by the user. Highest precedence.</summary>
    UserDefined,
}

/// <summary>A scale plus where it came from.</summary>
public sealed record ScaleEntry(Scale Scale, ScaleOrigin Origin)
{
    public string Id => Scale.Id;
}

/// <summary>One id claimed by more than one source, and how it was resolved.</summary>
/// <remarks>
/// Surfaced rather than silently resolved. Shadowing is a legitimate feature - it is how a user
/// corrects a shipped tuning they disagree with - but a shadow the user did not intend is a
/// confusing bug, and the two are indistinguishable unless the app says which happened.
/// </remarks>
public sealed record ScaleIdCollision(string Id, ScaleOrigin Winner, ScaleOrigin Loser)
{
    public string Describe() =>
        $"'{Id}' is defined by both {Loser} and {Winner}; the {Winner} definition is in use.";
}

/// <summary>
/// The merged scale library: everything the app can restyle into, from every source.
/// </summary>
/// <remarks>
/// Deliberately does no file IO. It is handed already-loaded scale sets, which keeps it pure,
/// trivially testable, and free of any opinion about where files live - that belongs to the app's
/// <c>ScaleLibraryService</c>, which knows about the portable-vs-AppData fallback.
/// </remarks>
public sealed class ScaleLibrary
{
    private readonly ScaleEntry[] _entries;
    private readonly FrozenDictionary<string, ScaleEntry> _byId;

    private ScaleLibrary(ScaleEntry[] entries, IReadOnlyList<ScaleIdCollision> collisions)
    {
        _entries = entries;
        _byId = entries.ToFrozenDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        Collisions = collisions;
    }

    /// <summary>Every scale, in load order within ascending precedence.</summary>
    public IReadOnlyList<ScaleEntry> Entries => _entries;

    public IEnumerable<Scale> Scales => _entries.Select(e => e.Scale);

    public int Count => _entries.Length;

    /// <summary>Ids claimed by more than one source. Empty in the normal case.</summary>
    public IReadOnlyList<ScaleIdCollision> Collisions { get; }

    /// <summary>
    /// Merges sources into one library. Later arguments win on an id collision.
    /// </summary>
    /// <remarks>
    /// Precedence is user > beside-exe > embedded > generated, which is the order that lets a user
    /// override anything the app ships without editing the app. Pass the sets in that order.
    /// </remarks>
    public static ScaleLibrary Build(params (ScaleOrigin Origin, IEnumerable<Scale> Scales)[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        Dictionary<string, ScaleEntry> byId = new(StringComparer.OrdinalIgnoreCase);
        List<ScaleIdCollision> collisions = [];
        List<string> order = [];

        foreach ((ScaleOrigin origin, IEnumerable<Scale> scales) in sources)
        {
            foreach (Scale scale in scales)
            {
                if (byId.TryGetValue(scale.Id, out ScaleEntry? existing))
                {
                    // Later sources are higher precedence, so this one wins - but say so.
                    collisions.Add(new ScaleIdCollision(scale.Id, origin, existing.Origin));
                    byId[scale.Id] = new ScaleEntry(scale, origin);
                    continue;
                }

                byId[scale.Id] = new ScaleEntry(scale, origin);
                order.Add(scale.Id);
            }
        }

        ScaleEntry[] entries = [.. order.Select(id => byId[id])];
        return new ScaleLibrary(entries, collisions);
    }

    public Scale? Find(string id) =>
        _byId.TryGetValue(id, out ScaleEntry? entry) ? entry.Scale : null;

    public ScaleOrigin? OriginOf(string id) =>
        _byId.TryGetValue(id, out ScaleEntry? entry) ? entry.Origin : null;

    public bool Contains(string id) => _byId.ContainsKey(id);

    /// <summary>
    /// Scales grouped by region, for the right rail's sticky-header list.
    /// </summary>
    /// <remarks>
    /// Regions are ordered by size, largest first. The alternative - alphabetical - buries the 82
    /// South Asian scales below Africa's ten and makes the list feel arbitrary when you scroll it.
    /// </remarks>
    public IEnumerable<IGrouping<string, Scale>> ByRegion() =>
        Scales
            .GroupBy(s => s.Region, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Free-text search across name, tradition, region and id.
    /// </summary>
    /// <remarks>
    /// Matches on any whitespace-separated term so "gong pyth" finds Pythagorean Gong. Exact
    /// name matches sort first, then prefix matches, then the rest - typing "rast" should offer
    /// Maqam Rast before every makam whose description mentions it.
    /// </remarks>
    public IEnumerable<Scale> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Scales;
        }

        string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Scales
            .Where(s => terms.All(t => MatchesTerm(s, t)))
            .OrderBy(s => Rank(s, query))
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesTerm(Scale scale, string term) =>
        scale.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
        || scale.Tradition.Contains(term, StringComparison.OrdinalIgnoreCase)
        || scale.Region.Contains(term, StringComparison.OrdinalIgnoreCase)
        || scale.Id.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static int Rank(Scale scale, string query) =>
        scale.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0
        : scale.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1
        : scale.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2
        : 3;
}
