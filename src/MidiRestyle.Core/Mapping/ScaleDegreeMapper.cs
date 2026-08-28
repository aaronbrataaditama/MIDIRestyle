using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Mapping;

/// <summary>
/// The default strategy. Decomposes each source note into a signed absolute degree index within the
/// source scale, then re-emits it at that same index in the target scale.
/// </summary>
/// <remarks>
/// <para>
/// Wraparound is the point, not a side effect: it is what lets a 7-note source map into a 5-note
/// target with ascending lines staying ascending. Contour survives; absolute register does not, and
/// the range of the piece is multiplied by <c>targetDegreeCount / sourceDegreeCount</c> - exactly
/// 1.4x for 7 into 5 - which is why <see cref="RangePolicy"/> is applied here and not left to the
/// exporter to discover.
/// </para>
/// <para>
/// <b>Floor division and positive modulo are mandatory in both directions.</b> C# truncates toward
/// zero and <c>%</c> keeps the sign of the dividend, so <c>-1 / 5 == 0</c> and <c>-1 % 5 == -1</c>:
/// the naive formula indexes <c>DegreeCents[-1]</c> and throws. Notes below the tonic are not an
/// edge case - every bass line has them - so this is the common path.
/// </para>
/// <para>
/// The decomposition and the re-emission are exact inverses of each other, so mapping a scale onto
/// itself with the same tonic is the identity. That is asserted in the tests and is the cheapest
/// available check that the two halves agree about what a degree index means.
/// </para>
/// </remarks>
public sealed class ScaleDegreeMapper : IPitchMapper
{
    /// <summary>
    /// How close a note must sit to a degree to count as being on it. Tight on purpose: this is a
    /// float-equality guard, not a musical tolerance. Snapping is <see cref="NonScaleNotePolicy"/>'s
    /// job, and widening this would silently do part of it.
    /// </summary>
    private const double DegreeMatchToleranceCents = 1e-6;

    private readonly double[] _sourceDegrees;
    private readonly double[] _targetDegrees;
    private readonly double _sourceTonicCents;
    private readonly double _targetTonicCents;
    private readonly NonScaleNotePolicy _nonScaleNotes;
    private readonly RangePolicy _range;

    /// <param name="context">
    /// The run's tunings and policies. Must carry a <see cref="MappingContext.SourceScale"/>: a
    /// degree index is only defined relative to one.
    /// </param>
    public ScaleDegreeMapper(MappingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Scale source = context.SourceScale ?? throw new ArgumentException(
            "ScaleDegreeMapper needs a source scale - a note's degree index is only defined " +
            "relative to one. Use NearestPitchMapper when there is no source scale.",
            nameof(context));

        _sourceDegrees = [.. source.DegreeCents];
        _targetDegrees = [.. context.TargetScale.DegreeCents];
        _sourceTonicCents = context.SourceTonic.Cents;
        _targetTonicCents = context.TargetTonic.Cents;
        _nonScaleNotes = context.Options.NonScaleNotes;
        _range = context.Options.Range;
    }

    /// <inheritdoc/>
    public MappingStrategy Strategy => MappingStrategy.ScaleDegree;

    /// <inheritdoc/>
    public bool UsesSourceScale => true;

    /// <inheritdoc/>
    public MappingResult Map(Pitch source)
    {
        double relative = source.Cents - _sourceTonicCents;

        // Floor, not truncation: a note below the tonic has a negative octave, and Math.Floor is
        // what makes -500 cents land in octave -1 rather than octave 0.
        int octave = (int)Math.Floor(relative / MidiRounding.CentsPerOctave);
        double withinOctave = relative - octave * MidiRounding.CentsPerOctave;

        int degree = IndexOfDegree(withinOctave);

        if (degree < 0)
        {
            switch (_nonScaleNotes)
            {
                case NonScaleNotePolicy.Drop:
                    return MappingResult.Dropped(DropCause.NotInSourceScale);

                case NonScaleNotePolicy.PassThrough:
                    return RangeEnforcer.Apply(source, _range);

                default:
                    (degree, octave) = SnapToNearestDegree(withinOctave, octave);
                    break;
            }
        }

        // The signed absolute degree index. Composing the octave and the within-octave index here -
        // rather than anywhere else - is what makes the round trip exact.
        int absoluteDegree = octave * _sourceDegrees.Length + degree;

        return RangeEnforcer.Apply(new Pitch(TargetCentsFor(absoluteDegree)), _range);
    }

    /// <summary>
    /// The target pitch at a signed absolute degree index. This is the formula the whole strategy
    /// rests on; both halves of it are load-bearing.
    /// </summary>
    private double TargetCentsFor(int absoluteDegree)
    {
        int n = _targetDegrees.Length;

        // Floor, NOT integer division: (int)(-3 / 5) is 0 in C#, which would put a note three
        // degrees below the tonic in the tonic's own octave.
        int octave = (int)Math.Floor(absoluteDegree / (double)n);

        // Positive modulo: -3 % 5 is -3 in C#, which would index DegreeCents[-3] and throw.
        int index = ((absoluteDegree % n) + n) % n;

        return _targetTonicCents + octave * MidiRounding.CentsPerOctave + _targetDegrees[index];
    }

    /// <summary>The index of the degree at <paramref name="withinOctave"/> cents, or -1.</summary>
    private int IndexOfDegree(double withinOctave)
    {
        // Linear: scales are capped at 12 degrees, so this beats a binary search outright.
        for (int i = 0; i < _sourceDegrees.Length; i++)
        {
            if (Math.Abs(withinOctave - _sourceDegrees[i]) <= DegreeMatchToleranceCents)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The nearest source degree to <paramref name="withinOctave"/>, which may be the tonic of the
    /// octave above - a note 20 cents under the octave is nearer to it than to the leading note.
    /// Ties resolve upward, matching the away-from-zero convention used everywhere else.
    /// </summary>
    private (int Degree, int Octave) SnapToNearestDegree(double withinOctave, int octave)
    {
        int best = 0;
        double bestDistance = Math.Abs(withinOctave - _sourceDegrees[0]);

        for (int i = 1; i < _sourceDegrees.Length; i++)
        {
            double distance = Math.Abs(withinOctave - _sourceDegrees[i]);

            // <= rather than <: the degrees ascend, so a tie takes the higher one.
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        double distanceToOctaveAbove = MidiRounding.CentsPerOctave - withinOctave;

        return distanceToOctaveAbove <= bestDistance ? (0, octave + 1) : (best, octave);
    }
}
