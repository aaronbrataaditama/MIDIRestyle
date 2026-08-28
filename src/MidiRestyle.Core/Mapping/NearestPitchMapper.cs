using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Mapping;

/// <summary>
/// Snaps each note to the nearest pitch the target scale offers, in whatever octave that happens to
/// be. Preserves absolute register, flattens contour.
/// </summary>
/// <remarks>
/// <para>
/// <b>This mapper consults neither the source scale nor the detected key</b>, and its constructor
/// says so by refusing to accept them. Nothing about the source tuning can reach it, which is what
/// entitles the UI to dim those controls with a reason.
/// </para>
/// <para>
/// The candidate set is built once, in the constructor. Rebuilding it per note would be
/// O(notes x candidates) against a 16 ms budget for 20,000 notes; as it stands each note costs one
/// binary search over an array of roughly <c>degreeCount x 11</c> doubles.
/// </para>
/// <para>
/// Because the candidates are filtered to MIDI 0..127 at construction, every result is in range by
/// construction and <see cref="RangePolicy"/> can never bind here. That is a property of snapping,
/// not an omission: a snap cannot leave the range it snapped within.
/// </para>
/// </remarks>
public sealed class NearestPitchMapper : IPitchMapper
{
    /// <summary>Ascending, distinct, and every entry inside MIDI 0..127.</summary>
    private readonly double[] _candidateCents;

    /// <param name="targetScale">The tuning to snap into.</param>
    /// <param name="targetTonic">Where the target scale's degree 0 sits.</param>
    /// <param name="options">
    /// The policies. Retained for the caller's benefit only:
    /// <see cref="MappingOptions.NonScaleNotes"/> is meaningless without a source scale, and
    /// <see cref="MappingOptions.Range"/> cannot bind - see the remarks on this type.
    /// </param>
    public NearestPitchMapper(Scale targetScale, Pitch targetTonic, MappingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(targetScale);

        Options = options ?? MappingOptions.Default;
        TargetScale = targetScale;
        TargetTonic = targetTonic;
        _candidateCents = BuildCandidates(targetScale, targetTonic);
    }

    /// <inheritdoc/>
    public MappingStrategy Strategy => MappingStrategy.NearestPitch;

    /// <inheritdoc/>
    public bool UsesSourceScale => false;

    /// <summary>The tuning being snapped into.</summary>
    public Scale TargetScale { get; }

    /// <summary>Where the target scale's degree 0 sits.</summary>
    public Pitch TargetTonic { get; }

    /// <summary>The policies in force.</summary>
    public MappingOptions Options { get; }

    /// <summary>How many distinct target pitches exist inside the MIDI range.</summary>
    public int CandidateCount => _candidateCents.Length;

    /// <inheritdoc/>
    public MappingResult Map(Pitch source)
    {
        double cents = source.Cents;

        int found = Array.BinarySearch(_candidateCents, cents);
        if (found >= 0)
        {
            return MappingResult.Mapped(new Pitch(_candidateCents[found]));
        }

        int above = ~found;

        if (above == 0)
        {
            return MappingResult.Mapped(new Pitch(_candidateCents[0]));
        }

        if (above == _candidateCents.Length)
        {
            return MappingResult.Mapped(new Pitch(_candidateCents[^1]));
        }

        double lower = _candidateCents[above - 1];
        double upper = _candidateCents[above];

        // Ties away from zero. Every candidate is at or above 0 cents, so away from zero is upward:
        // the strict < leaves an exact tie to the upper candidate.
        double nearest = cents - lower < upper - cents ? lower : upper;

        return MappingResult.Mapped(new Pitch(nearest));
    }

    private static double[] BuildCandidates(Scale scale, Pitch tonic)
    {
        // Widen by an octave at each end so the filter, not the loop bounds, decides what is in
        // range. The range spans under 11 octaves, so this stays a few hundred doubles.
        int lowestOctave =
            (int)Math.Floor((RangeEnforcer.MinCents - tonic.Cents) / MidiRounding.CentsPerOctave) - 1;
        int highestOctave =
            (int)Math.Ceiling((RangeEnforcer.MaxCents - tonic.Cents) / MidiRounding.CentsPerOctave) + 1;

        var candidates = new List<double>((highestOctave - lowestOctave + 1) * scale.DegreeCount);

        // Ascending by construction: degrees are strictly ascending within [0, 1200) and the octave
        // step is exactly 1200, so no sort is needed and BinarySearch stays valid.
        for (int octave = lowestOctave; octave <= highestOctave; octave++)
        {
            double octaveBase = tonic.Cents + octave * MidiRounding.CentsPerOctave;

            for (int degree = 0; degree < scale.DegreeCount; degree++)
            {
                var pitch = new Pitch(octaveBase + scale.DegreeCents[degree]);

                if (pitch.IsInMidiRange)
                {
                    candidates.Add(pitch.Cents);
                }
            }
        }

        return [.. candidates];
    }
}
