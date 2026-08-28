using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Scales;

/// <summary>Why a scale cannot be expressed in 12 equal semitones.</summary>
public enum QuantisationFailure
{
    None = 0,

    /// <summary>
    /// Pushing collided degrees apart drove the top degree to or past the octave, where it would
    /// duplicate the tonic.
    /// </summary>
    ExceedsOctave,
}

/// <summary>The outcome of quantising a scale onto the 12-TET grid.</summary>
/// <param name="Degrees">The quantised degrees, or an empty list on failure.</param>
/// <param name="Failure">Why it failed, or <see cref="QuantisationFailure.None"/>.</param>
/// <param name="Reason">Human-readable explanation, for the UI. Null on success.</param>
public sealed record QuantisationResult(
    IReadOnlyList<double> Degrees,
    QuantisationFailure Failure = QuantisationFailure.None,
    string? Reason = null)
{
    public bool Succeeded => Failure == QuantisationFailure.None;
}

/// <summary>
/// Snaps a scale's degrees onto the 12-TET grid, preserving the degree count.
/// </summary>
/// <remarks>
/// <para>
/// Used by 12-TET output mode and by <see cref="TuningFidelity"/>. Preserving the <em>count</em>
/// matters because the degree mapper indexes by degree: a quantisation that merged two degrees would
/// silently change which target degree every source note maps to.
/// </para>
/// <para>
/// <b>The collision rule is a cascade, not a single push.</b> Rounding can drive two degrees onto the
/// same semitone; pushing the upper one up 100 cents can then collide it with the <em>next</em>
/// degree. Three degrees inside one semitone is not hypothetical - it is what a 22-shruti or 31-EDO
/// Scala import looks like. A single push leaves those non-monotonic, which corrupts the mapper
/// downstream rather than failing here.
/// </para>
/// <para>
/// The octave guard is equally load-bearing: without it a scale whose top degree sits near 1160
/// cents quantises to a degree at exactly 1200, which duplicates the tonic and emits two identical
/// pitches in every octave.
/// </para>
/// </remarks>
public static class TwelveTetQuantiser
{
    /// <summary>Quantises <paramref name="degreeCents"/> onto the 12-TET grid.</summary>
    public static QuantisationResult Quantise(IReadOnlyList<double> degreeCents)
    {
        ArgumentNullException.ThrowIfNull(degreeCents);

        if (degreeCents.Count == 0)
        {
            return new QuantisationResult([]);
        }

        var q = new double[degreeCents.Count];
        q[0] = MidiRounding.ToNearestSemitoneCents(degreeCents[0]);

        for (int i = 1; i < degreeCents.Count; i++)
        {
            double rounded = MidiRounding.ToNearestSemitoneCents(degreeCents[i]);

            // The cascade: each degree must clear the one below it by a full semitone. Taking the
            // max chains automatically, so three degrees inside one semitone spread to three
            // consecutive semitones rather than two colliding ones.
            q[i] = Math.Max(rounded, q[i - 1] + MidiRounding.CentsPerSemitone);
        }

        if (q[^1] >= MidiRounding.CentsPerOctave)
        {
            return new QuantisationResult(
                [],
                QuantisationFailure.ExceedsOctave,
                $"Quantising to 12-TET pushes the top degree to {q[^1]:0.#} cents, at or beyond the " +
                $"octave. This scale has degrees too closely spaced to separate onto distinct " +
                $"semitones - use microtonal output instead.");
        }

        return new QuantisationResult(q);
    }

    /// <summary>Quantises a scale, keeping its identity and metadata.</summary>
    public static QuantisationResult Quantise(Scale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return Quantise(scale.DegreeCents);
    }
}
