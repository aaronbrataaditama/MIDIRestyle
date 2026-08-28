using System.Collections.Frozen;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Scales;

/// <summary>
/// A tuning: the ascending cents of each degree above the tonic, plus how to write and cite it.
/// </summary>
/// <remarks>
/// Validated on construction - see <see cref="ScaleValidationException"/> for why that belongs here
/// rather than in each caller.
/// </remarks>
public sealed record Scale
{
    /// <summary>Fewest degrees a usable scale can have.</summary>
    public const int MinDegrees = 2;

    /// <summary>
    /// Most degrees a scale may have. Bounded by two independent limits that happen to agree: a
    /// 7-letter staff cannot spell more, and 15 pitch-bend channels cannot voice more. The Scala
    /// format itself is unbounded, so imports of 22-shruti or 31-EDO files are rejected here rather
    /// than failing later as non-monotonic quantiser output or a blown channel budget.
    /// </summary>
    public const int MaxDegrees = 12;

    private static readonly FrozenSet<string> PlaceholderSources =
        new[] { "todo", "tbd", "fixme", "?", "n/a", "na", "unknown", "xxx" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly double[] _degreeCents;
    private readonly Lazy<double[]> _degreeOffsets;

    public Scale(
        string id,
        string name,
        string tradition,
        string region,
        IReadOnlyList<double> degreeCents,
        string source,
        bool notatable = true,
        IReadOnlyList<DegreeSpelling>? spelling = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(degreeCents);

        Validate(id, degreeCents, source, notatable, spelling);

        Id = id;
        Name = name;
        Tradition = tradition;
        Region = region;
        Source = source;
        Notatable = notatable;
        Description = description;

        _degreeCents = [.. degreeCents];

        // Notatability is a cultural judgement, not a computation, so an authored false always wins.
        // Slendro CAN be approximated with quarter-tone accidentals to within 10 cents, but no
        // gamelan musician reads that, so deriving a spelling for it would be a lie dressed as
        // precision.
        Spelling = notatable ? spelling : null;

        _degreeOffsets = new Lazy<double[]>(ComputeDegreeOffsets);
    }

    /// <summary>Stable identifier, e.g. seasia.gamelan.slendro.kanyut-mesem.</summary>
    public string Id { get; }

    /// <summary>Display name. Must carry the variant when a tradition has several tunings.</summary>
    public string Name { get; }

    public string Tradition { get; }

    public string Region { get; }

    /// <summary>
    /// Provenance for the tuning. Non-nullable on purpose: wrong cents values make a wrong app and
    /// would never fail a mechanical test, so every scale must say where its numbers came from.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Whether a Western staff spelling exists for this scale at all. <b>Authored, never derived.</b>
    /// False for the equal-step families, where every degree but the tonic falls between two letter
    /// names and the culturally correct notation is cipher rather than a staff.
    /// </summary>
    public bool Notatable { get; }

    /// <summary>
    /// Per-degree staff spelling, or null when none exists. Always null when
    /// <see cref="Notatable"/> is false, whatever derivation would produce.
    /// </summary>
    public IReadOnlyList<DegreeSpelling>? Spelling { get; }

    public string? Description { get; }

    /// <summary>Ascending cents above the tonic. Starts at 0 and excludes the octave.</summary>
    public IReadOnlyList<double> DegreeCents => _degreeCents;

    public int DegreeCount => _degreeCents.Length;

    /// <summary>
    /// The pitch-bend offset each degree needs, in cents, within [-50, +50).
    /// </summary>
    /// <remarks>
    /// <b>Offsets belong to the scale, never to a note.</b> The target tonic is always a 12-TET pitch
    /// and octaves are exactly 1200 cents, so a degree's offset is fully determined by its
    /// <see cref="DegreeCents"/> entry - computed once, here. Deriving offsets from an absolute
    /// note's cents instead makes the channel count depend on tonic and octave, which is how Rast
    /// ends up apparently needing three channels in one key and two in another.
    /// </remarks>
    public IReadOnlyList<double> DegreeOffsets => _degreeOffsets.Value;

    /// <summary>Whether every degree sits exactly on the 12-TET grid.</summary>
    public bool IsTwelveTet => DegreeOffsets.All(o => o == 0.0);

    /// <summary>Largest absolute deviation from 12-TET, in cents.</summary>
    public double MaxOffsetCents => DegreeOffsets.Max(Math.Abs);

    private double[] ComputeDegreeOffsets()
    {
        var offsets = new double[_degreeCents.Length];
        for (int i = 0; i < _degreeCents.Length; i++)
        {
            offsets[i] = MidiRounding.OffsetFromNearestSemitone(_degreeCents[i]);
        }

        return offsets;
    }

    private static void Validate(
        string id,
        IReadOnlyList<double> degreeCents,
        string source,
        bool notatable,
        IReadOnlyList<DegreeSpelling>? spelling)
    {
        if (degreeCents.Count < MinDegrees)
        {
            throw new ScaleValidationException(id,
                $"needs at least {MinDegrees} degrees but has {degreeCents.Count}. A zero- or " +
                "one-degree scale breaks the degree mapper's modulo.");
        }

        if (degreeCents.Count > MaxDegrees)
        {
            throw new ScaleValidationException(id,
                $"has {degreeCents.Count} degrees, more than the {MaxDegrees} supported. Scales this " +
                "dense cannot be spelled on a staff or voiced within the 15-channel budget.");
        }

        if (degreeCents[0] != 0.0)
        {
            throw new ScaleValidationException(id,
                $"must start at exactly 0 cents (the tonic) but starts at {degreeCents[0]}.");
        }

        for (int i = 1; i < degreeCents.Count; i++)
        {
            if (degreeCents[i] <= degreeCents[i - 1])
            {
                throw new ScaleValidationException(id,
                    $"degrees must strictly ascend, but degree {i} ({degreeCents[i]}) is not above " +
                    $"degree {i - 1} ({degreeCents[i - 1]}).");
            }
        }

        double last = degreeCents[^1];
        if (last >= MidiRounding.CentsPerOctave)
        {
            throw new ScaleValidationException(id,
                $"degrees must lie in [0, {MidiRounding.CentsPerOctave}) - the octave is implicit - " +
                $"but the last degree is {last}. A degree at 1200 duplicates the tonic and emits two " +
                "identical pitches per octave.");
        }

        if (string.IsNullOrWhiteSpace(source) || PlaceholderSources.Contains(source.Trim()))
        {
            throw new ScaleValidationException(id,
                "needs a real Source. Wrong cents values make a wrong app and would never fail a " +
                "mechanical test, so provenance is mandatory.");
        }

        if (notatable && spelling is not null && spelling.Count != degreeCents.Count)
        {
            throw new ScaleValidationException(id,
                $"has {spelling.Count} spellings for {degreeCents.Count} degrees.");
        }
    }

    public override string ToString() => $"{Name} [{string.Join(", ", _degreeCents)}]";
}
