using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Analysis;

/// <summary>
/// A duration-weighted histogram of the twelve pitch classes - the input to key detection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Duration-weighted, not note-counted.</b> A whole note held under a run of passing tones tells
/// you far more about the key than the passing tones do, and counting occurrences gives them equal
/// say. The weight of a bin is the total sounding time, in ticks, of every note in that pitch class.
/// </para>
/// <para>
/// <b>Channel 10 (0-indexed 9) is excluded, always.</b> A percussion note number selects which drum
/// is struck; it is not a pitch, so folding it into a pitch-class histogram is noise dressed as
/// signal. The exclusion is applied inside this type rather than left to callers, because it is an
/// invariant of the domain and not a caller's policy choice.
/// </para>
/// <para>
/// The profile is deliberately <em>not</em> normalised. Pearson correlation is invariant to both
/// scale and offset, so normalising would buy nothing and would only introduce a division that can
/// produce NaN on an empty profile - exactly the case the detector needs to recognise cleanly.
/// </para>
/// </remarks>
public sealed class PitchClassProfile
{
    /// <summary>The number of bins. One per pitch class.</summary>
    public const int BinCount = MidiRounding.SemitonesPerOctave;

    private readonly double[] _weights;

    private PitchClassProfile(double[] weights) => _weights = weights;

    /// <summary>A profile with no weight at all. Detection on this returns no key.</summary>
    public static PitchClassProfile Empty { get; } = new(new double[BinCount]);

    /// <summary>The twelve bin weights, index 0 being C.</summary>
    public IReadOnlyList<double> Weights => _weights;

    /// <summary>The weight of one pitch class, 0..11.</summary>
    public double this[int pitchClass]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(pitchClass);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pitchClass, BinCount);
            return _weights[pitchClass];
        }
    }

    /// <summary>Total weight across all bins.</summary>
    public double Total
    {
        get
        {
            double total = 0;
            foreach (double w in _weights)
            {
                total += w;
            }

            return total;
        }
    }

    /// <summary>Whether nothing was measured - no non-drum notes, or a drums-only file.</summary>
    public bool IsEmpty => Total <= 0;

    /// <summary>
    /// Whether the bins differ from one another at all. A profile whose twelve bins carry identical
    /// weight - a chromatic run in even durations, say - has zero variance, which makes the Pearson
    /// denominator zero and every one of the 24 correlations NaN. The detector tests this first so
    /// that it can report "no key" instead of sorting a list of NaNs.
    /// </summary>
    public bool HasVariance
    {
        get
        {
            for (int i = 1; i < BinCount; i++)
            {
                if (_weights[i] != _weights[0])
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Whether this profile can support a correlation at all.</summary>
    public bool IsUsable => !IsEmpty && HasVariance;

    /// <summary>
    /// Builds the profile for a whole project. Drum track-channels are skipped; every other track
    /// contributes, whatever the user's per-track restyle opt-outs may be - detection describes the
    /// file, not the pending edit. Pass <see cref="FromTracks"/> a filtered set to narrow it.
    /// </summary>
    public static PitchClassProfile FromProject(MidiProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return FromTracks(project.Tracks);
    }

    /// <summary>
    /// Builds the profile from a chosen set of track-channels. Drums are excluded here too, so a
    /// caller cannot accidentally reintroduce them by passing an unfiltered list.
    /// </summary>
    public static PitchClassProfile FromTracks(IEnumerable<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        double[] byDuration = new double[BinCount];
        double[] byCount = new double[BinCount];
        bool anyNotes = false;

        foreach (TrackInfo track in tracks)
        {
            if (track.IsDrums)
            {
                continue;
            }

            foreach (Note note in track.Notes)
            {
                int pc = note.Pitch.PitchClass;
                anyNotes = true;
                byCount[pc] += 1;

                // Negative lengths are not legal, but a corrupt file should skew nothing rather
                // than subtract weight from a bin.
                if (note.LengthTicks > 0)
                {
                    byDuration[pc] += note.LengthTicks;
                }
            }
        }

        if (!anyNotes)
        {
            return Empty;
        }

        // Zero-length notes are legal MIDI and the loader preserves them. A file made entirely of
        // them has notes but no duration, and a strictly duration-weighted profile would report an
        // empty histogram - which the detector would correctly, and unhelpfully, call "no key".
        // Falling back to occurrence counts keeps the answer available; it only ever applies when
        // there is no duration information to prefer.
        bool anyDuration = false;
        foreach (double w in byDuration)
        {
            if (w > 0)
            {
                anyDuration = true;
                break;
            }
        }

        return new PitchClassProfile(anyDuration ? byDuration : byCount);
    }

    /// <summary>
    /// Builds a profile directly from twelve weights. For tests, and for callers that have already
    /// tallied their own histogram; the drum exclusion is then the caller's to honour.
    /// </summary>
    public static PitchClassProfile FromWeights(IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count != BinCount)
        {
            throw new ArgumentException(
                $"A pitch-class profile has exactly {BinCount} bins, one per pitch class; " +
                $"{weights.Count} were supplied.",
                nameof(weights));
        }

        double[] copy = new double[BinCount];
        for (int i = 0; i < BinCount; i++)
        {
            double w = weights[i];
            if (double.IsNaN(w) || double.IsInfinity(w))
            {
                throw new ArgumentException(
                    $"Bin {i} is {w}. A weight must be a finite number; NaN would propagate " +
                    "silently through the correlation and produce an arbitrary ranking.",
                    nameof(weights));
            }

            copy[i] = w;
        }

        return new PitchClassProfile(copy);
    }

    public override string ToString() =>
        string.Join(", ", _weights.Select((w, i) => $"{i}:{w:0.##}"));
}
