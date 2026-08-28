using System.Globalization;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Notation;

/// <summary>
/// One note expressed as a scale degree rather than a letter, for scales where
/// <see cref="Scale.Notatable"/> is false and there is no staff spelling to fall back on.
/// </summary>
/// <remarks>
/// This is the data model behind cipher (numbered) notation - jianpu for Chinese music, kepatihan
/// for Javanese gamelan - the tradition every equal-step scale in the library is actually read in.
/// A degree number plus an octave dot is meaningful without any staff position at all, which is
/// exactly what an equal-step scale needs: Slendro's degrees fall between Western letter names, but
/// "degree 3, dot above" is unambiguous regardless of where on a stave it would sit.
/// </remarks>
/// <param name="Degree">
/// 1-based scale degree; degree 1 is the tonic. Matches every cipher tradition, which numbers from
/// 1, never 0.
/// </param>
/// <param name="OctaveOffset">
/// Octaves relative to the tonic's own octave: 0 is the same octave as the tonic, +1 is one above
/// (a dot over the numeral in cipher notation), -1 is one below (a dot under).
/// </param>
/// <param name="CentsDeviation">
/// Signed distance from the sounding pitch to the nominal degree pitch, in cents; positive means
/// sharp of the degree. Non-zero even for a genuinely in-scale note whenever the target scale is
/// microtonal and the output has been rounded to 12-TET - the degree survives the rounding, the
/// exact cents do not.
/// </param>
/// <param name="IsInScale">
/// Whether the pitch counts as landing on this degree at all, rather than merely being nearest to
/// it. See <see cref="DegreeReader.Read"/> for the two ways a pitch can qualify.
/// </param>
public readonly record struct DegreeReading(
    int Degree, int OctaveOffset, double CentsDeviation, bool IsInScale)
{
    /// <summary>Combining dot above (U+0307): cipher notation's mark for one octave up.</summary>
    public const char DotAbove = '̇';

    /// <summary>Combining dot below (U+0323): cipher notation's mark for one octave down.</summary>
    public const char DotBelow = '̣';

    /// <summary>
    /// The degree number as cipher notation writes it, or <c>"?"</c> for a pitch that never landed
    /// on the scale - there is no degree to print, and pretending otherwise would misinform rather
    /// than merely round.
    /// </summary>
    public string Numeral => IsInScale ? NumeralFor(Degree) : OutOfScaleNumeral;

    /// <summary>What a pitch that never landed on the scale prints as.</summary>
    public const string OutOfScaleNumeral = "?";

    /// <summary>
    /// Pre-rendered numerals, indexed by degree.
    /// </summary>
    /// <remarks>
    /// <see cref="Numeral"/> is read once per visible note per frame by the degree view, and
    /// <c>int.ToString</c> would allocate on every one of them - the render path is required to
    /// allocate nothing per frame. A scale can hold at most <see cref="Scale.MaxDegrees"/> degrees,
    /// so the whole set fits in a static table.
    /// </remarks>
    private static readonly string[] Numerals =
        [.. Enumerable.Range(0, Scale.MaxDegrees + 2).Select(i => i.ToString(CultureInfo.InvariantCulture))];

    private static string NumeralFor(int degree) =>
        (uint)degree < (uint)Numerals.Length
            ? Numerals[degree]
            : degree.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Combining octave dots: <see cref="DotAbove"/> repeated once per octave for a positive
    /// <see cref="OctaveOffset"/>, <see cref="DotBelow"/> repeated for a negative one, empty at 0.
    /// </summary>
    public string OctaveMarks => OctaveOffset switch
    {
        0 => "",
        > 0 => new string(DotAbove, OctaveOffset),
        _ => new string(DotBelow, -OctaveOffset),
    };

    /// <summary>The numeral with its octave dots, ready to render, e.g. "5" with one dot above.</summary>
    public string Display => Numeral + OctaveMarks;

    public override string ToString() => Display;
}

/// <summary>
/// Reads a sounding pitch as a degree of a scale, for the cipher/degree view that stands in for a
/// staff whenever <see cref="Scale.Notatable"/> is false.
/// </summary>
public static class DegreeReader
{
    /// <summary>
    /// A pitch within this many cents of a degree counts as landing on it exactly, independent of
    /// the 12-TET-rounding check below. Small enough that no two degrees in any scale accepted by
    /// <see cref="Scale"/>'s validation (12-EDO's 100-cent spacing is the tightest) are ever both
    /// this close to the same point.
    /// </summary>
    private const double ExactToleranceCents = 2.0;

    /// <summary>
    /// Reads <paramref name="pitch"/> as a degree of <paramref name="scale"/> built on
    /// <paramref name="tonic"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Splits the pitch's distance from the tonic into an octave and an in-octave remainder using
    /// floor division and positive modulo - not C#'s <c>/</c> and <c>%</c>, which truncate toward
    /// zero and keep the sign of the dividend, so a naive <c>rel / 1200</c> / <c>rel % 1200</c> gives
    /// <c>0</c> and a negative remainder for a note just below the tonic, which would then index
    /// <c>DegreeCents[-1]</c> and throw. Notes below the tonic are routine in any bass line, so this
    /// path is exercised constantly, never an edge case.
    /// </para>
    /// <para>
    /// The nearest-degree search also checks the tonic of the octave <em>above</em>, because a note
    /// just under the next octave boundary can be nearer to that tonic (degree 1, octave + 1) than
    /// to the scale's own highest degree - the wrap has to carry the octave forward, not attach the
    /// note to the top degree it merely resembles.
    /// </para>
    /// <para>
    /// <see cref="DegreeReading.IsInScale"/> is true two different ways: the pitch sits within
    /// <see cref="ExactToleranceCents"/> of the nominal degree, or the pitch is exactly the 12-TET
    /// rounding of that degree's exact pitch. The second case is what keeps a microtonal degree
    /// legible after 12-TET output mode has rounded it to the nearest semitone - Slendro's 240-cent
    /// second degree rounds to a 200-cent MIDI note 40 cents away, but it is still degree 2, not an
    /// unscaled passing tone.
    /// </para>
    /// </remarks>
    public static DegreeReading Read(Pitch pitch, Scale scale, Pitch tonic)
    {
        double rel = pitch.Cents - tonic.Cents;

        int octave = (int)Math.Floor(rel / MidiRounding.CentsPerOctave);
        double remainder = rel - octave * MidiRounding.CentsPerOctave;

        int bestIndex = 0;
        bool wrapsToNextOctave = false;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < scale.DegreeCount; i++)
        {
            double distance = Math.Abs(remainder - scale.DegreeCents[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        // The octave-above tonic sits at exactly 1200c in this remainder's frame. Always a
        // candidate: the scale's own top degree can be arbitrarily close to it or arbitrarily far,
        // depending on the scale, so there is no shortcut that skips checking it.
        double distanceToNextTonic = Math.Abs(remainder - MidiRounding.CentsPerOctave);
        if (distanceToNextTonic < bestDistance)
        {
            bestIndex = 0;
            wrapsToNextOctave = true;
        }

        int octaveOffset = wrapsToNextOctave ? octave + 1 : octave;

        // scale.DegreeCents[0] is always 0 (Scale's own validation guarantees it), so this is
        // correct whether or not the wrap fired - no separate zero needed for that branch.
        double nominalCents =
            tonic.Cents + octaveOffset * MidiRounding.CentsPerOctave + scale.DegreeCents[bestIndex];
        double deviation = pitch.Cents - nominalCents;

        bool isInScale =
            Math.Abs(deviation) <= ExactToleranceCents
            || pitch.MidiNote == MidiRounding.ToNearestSemitone(nominalCents);

        return new DegreeReading(bestIndex + 1, octaveOffset, deviation, isInScale);
    }
}
