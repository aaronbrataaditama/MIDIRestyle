using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Notation;

/// <summary>
/// A pitch as it is <em>written</em>: a letter, an octave, an absolute alteration, and whatever cents
/// are left over that no accidental can express.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Alter"/> here is the MusicXML frame, not the <see cref="DegreeSpelling.Alter"/>
/// frame.</b> It is an absolute alteration of the natural letter, so it depends on the tonic;
/// <see cref="DegreeSpelling.Alter"/> is measured against the major-scale degree at the same index,
/// which is exactly what makes it tonic-independent and storable once per scale. On a D tonic,
/// Hijaz's step 2 has <c>DegreeSpelling.Alter = 0</c> yet is written F-sharp, i.e.
/// <c>SpelledNote.Alter = 1</c>. <see cref="NoteSpeller"/> is the conversion between the two frames
/// and is the only place it happens.
/// </para>
/// </remarks>
/// <param name="Letter">0..6 for C D E F G A B.</param>
/// <param name="Octave">Scientific pitch notation, so middle C (MIDI 60) is C4.</param>
/// <param name="Alter">
/// The absolute MusicXML alteration in semitones, a multiple of
/// <see cref="DegreeSpelling.AlterQuantum"/> within +/-<see cref="DegreeSpelling.MaxAlter"/>. May be
/// +/-0.5 for quarter-tones - the same type and meaning MusicXML's <c>&lt;alter&gt;</c> takes.
/// </param>
/// <param name="ResidualCents">
/// What is left after <paramref name="Alter"/> has taken as much as an accidental can. Never
/// discarded: a staff view draws it as a comma mark rather than letting the written note lie.
/// </param>
public readonly record struct SpelledNote(int Letter, int Octave, double Alter, double ResidualCents)
{
    /// <summary>The letter name, 'C'..'B'.</summary>
    public char LetterName => DegreeSpelling.LetterNames[Letter];

    /// <summary>
    /// The accidental glyph, e.g. <c>""</c>, <c>"#"</c> or the half-flat. Shares
    /// <see cref="DegreeSpelling.AccidentalSymbol"/>'s table deliberately - the glyph for an
    /// alteration is the same question in both frames, and two copies would drift.
    /// </summary>
    public string AccidentalSymbol => new DegreeSpelling(Letter, Alter).AccidentalSymbol;

    /// <summary>
    /// The staff position: <c>Octave * 7 + Letter</c>. This is what a renderer needs, because a
    /// staff line is a letter-and-octave, not a pitch - C-sharp and C-flat sit on the same line.
    /// </summary>
    public int DiatonicIndex => (Octave * DiatonicSpeller.DiatonicSteps) + Letter;

    /// <summary>Whether the alteration is one an accidental can actually draw.</summary>
    public bool IsNotatable => new DegreeSpelling(Letter, Alter, ResidualCents).IsNotatable;

    public override string ToString() => $"{LetterName}{AccidentalSymbol}{Octave}";
}

/// <summary>
/// Turns a sounding <see cref="Pitch"/> into a written note - letter, octave and absolute
/// alteration - using the target scale's own spelling wherever the pitch is a scale degree.
/// </summary>
/// <remarks>
/// <para>
/// A general-purpose transcriber has to guess spelling from key context. This app does not: it
/// <em>chose</em> the target scale, so the scale carries its own spelling and the speller only has to
/// convert frames. The formula, from the plan:
/// </para>
/// <code>
/// letter        = (tonic.Letter + DiatonicStep) % 7;   // with octave carry
/// absoluteCents = the note's ACTUAL cents
/// absAlter      = (absoluteCents - NaturalCents(letter, octave)) / 100;
/// </code>
/// <para>
/// Deriving <c>absAlter</c> from the note's actual cents rather than from the scale's nominal cents is
/// what makes one code path serve both output modes. In microtonal output Rast's third arrives at
/// 350 cents above the tonic and spells E-half-flat; in 12-TET output the same degree has already been
/// quantised to 400 cents and spells a plain E. Neither case needs an <c>OutputMode</c> flag here.
/// </para>
/// <para>
/// Everything octave-related uses floor division and positive modulo. C# truncates toward zero and
/// <c>%</c> keeps the sign of the dividend, so the naive form gets notes below the tonic wrong - and
/// notes below the tonic are not an edge case, they are every bass line in every file.
/// </para>
/// </remarks>
public static class NoteSpeller
{
    /// <summary>
    /// How close a pitch must sit to a degree to be that degree outright, in cents. Generous enough
    /// to absorb accumulated floating-point error, far tighter than any real inflection.
    /// </summary>
    public const double DegreeMatchToleranceCents = 2.0;

    /// <summary>
    /// Octaves searched either side of the note's own octave when matching a degree.
    /// </summary>
    /// <remarks>
    /// One is enough, and it is not optional. A pitch a few cents below the tonic an octave up lands
    /// at ~1198 cents within its octave, which matches no degree; searching the neighbouring octave
    /// finds degree 0 where it belongs. It also makes the result immune to an off-by-one in the
    /// floor division from floating-point noise at an exact octave boundary, since the candidate
    /// carries its own octave index rather than inheriting the computed one.
    /// </remarks>
    private const int OctaveSearchRadius = 1;

    /// <summary>Spells <paramref name="pitch"/> using <paramref name="scale"/>'s own spelling.</summary>
    /// <param name="pitch">The sounding pitch, in absolute cents.</param>
    /// <param name="scale">The target scale. Its <see cref="Scale.Spelling"/> may be null.</param>
    /// <param name="tonic">The target tonic, a 12-TET pitch, as an absolute pitch.</param>
    /// <param name="tonicSpelling">
    /// How the tonic is written. Required, not derivable: MIDI 61 may be C-sharp or D-flat, and every
    /// letter downstream follows from which one it is.
    /// </param>
    /// <returns>
    /// The scale-derived spelling, or <see cref="SpellChromatic"/>'s result when the pitch is not a
    /// degree of the scale, when the scale has no staff spelling at all (the equal-step families are
    /// authored <c>Notatable = false</c>), or when the spelling would need more than a double
    /// accidental.
    /// </returns>
    public static SpelledNote Spell(Pitch pitch, Scale scale, Pitch tonic, TonicSpelling tonicSpelling)
    {
        ArgumentNullException.ThrowIfNull(scale);

        // Null Spelling is the authored answer for Slendro, Thai 7-equal and the rest of the
        // equal-step families: a cultural judgement that they are not staff music. Honour it and
        // fall back rather than inventing letters they are never read in.
        if (scale.Spelling is not { } spelling)
        {
            return SpellChromatic(pitch, tonicSpelling);
        }

        (int degree, int octaveIndex) = FindDegree(pitch, scale, tonic);
        if (degree < 0)
        {
            return SpellChromatic(pitch, tonicSpelling);
        }

        int tonicOctave = OctaveOf(tonic, tonicSpelling);

        // Letter carry: on a B tonic, step 1 is a C - which belongs to the next octave, exactly as
        // the sounding pitch does.
        int rawLetter = tonicSpelling.Letter + spelling[degree].DiatonicStep;
        int letterCarry = FloorDiv(rawLetter, DiatonicSpeller.DiatonicSteps);
        int letter = rawLetter - (letterCarry * DiatonicSpeller.DiatonicSteps);
        int octave = tonicOctave + octaveIndex + letterCarry;

        // The absolute alteration comes from the note's ACTUAL cents, never the scale's nominal
        // cents. That is the whole reason 12-TET output needs no special case here.
        double naturalCents = NaturalCents(letter, octave);
        double rawAlter = (pitch.Cents - naturalCents) / MidiRounding.CentsPerSemitone;
        double alter =
            MidiRounding.ToNearestInt(rawAlter / DegreeSpelling.AlterQuantum)
            * DegreeSpelling.AlterQuantum;

        if (Math.Abs(alter) > DegreeSpelling.MaxAlter)
        {
            // Past a double accidental the scale-derived letter stops being readable. A chromatic
            // spelling of the same pitch always is.
            return SpellChromatic(pitch, tonicSpelling);
        }

        double residual = pitch.Cents - (naturalCents + (alter * MidiRounding.CentsPerSemitone));

        return new SpelledNote(letter, octave, alter, residual);
    }

    /// <summary>
    /// Spells <paramref name="pitch"/> as the nearest 12-TET note, choosing sharps or flats by the
    /// tonic's accidental direction.
    /// </summary>
    /// <remarks>
    /// The direction rule: a tonic written with a flat spells the black keys as flats, anything else
    /// spells them as sharps. F is the one natural letter treated as flat-side, because F major's key
    /// signature has a B-flat in it; C is neutral and takes the sharp table by default.
    /// <para>
    /// The whole deviation from the chosen semitone is kept in
    /// <see cref="SpelledNote.ResidualCents"/> rather than being rounded into a quarter-tone
    /// accidental. This is the path a non-notatable scale takes, and for those the honest rendering
    /// is a plain letter plus a comma mark - Slendro's second degree is a D that is 40 cents sharp,
    /// not a D-half-sharp that is 10 cents flat.
    /// </para>
    /// </remarks>
    public static SpelledNote SpellChromatic(Pitch pitch, TonicSpelling tonicSpelling)
    {
        int midi = pitch.MidiNote;
        int pitchClass = PositiveMod(midi, MidiRounding.SemitonesPerOctave);
        int octave = FloorDiv(midi, MidiRounding.SemitonesPerOctave) - 1;

        (int letter, double alter) = PrefersSharps(tonicSpelling)
            ? SharpSpellings[pitchClass]
            : FlatSpellings[pitchClass];

        // Both tables spell every pitch class with a natural letter inside the same octave block, so
        // the octave from the MIDI note needs no adjustment. (Neither table contains B-sharp or
        // C-flat, which are the only spellings that would cross it.)
        return new SpelledNote(letter, octave, alter, pitch.BendCents);
    }

    /// <summary>Chromatic spellings by pitch class, sharp side.</summary>
    private static readonly (int Letter, double Alter)[] SharpSpellings =
    [
        (0, 0), (0, 1), (1, 0), (1, 1), (2, 0), (3, 0),
        (3, 1), (4, 0), (4, 1), (5, 0), (5, 1), (6, 0),
    ];

    /// <summary>Chromatic spellings by pitch class, flat side.</summary>
    private static readonly (int Letter, double Alter)[] FlatSpellings =
    [
        (0, 0), (1, -1), (1, 0), (2, -1), (2, 0), (3, 0),
        (4, -1), (4, 0), (5, -1), (5, 0), (6, -1), (6, 0),
    ];

    /// <summary>The letter index of F, the only natural letter whose key signature carries a flat.</summary>
    private const int LetterF = 3;

    private static bool PrefersSharps(TonicSpelling tonicSpelling) =>
        tonicSpelling.Alter > 0
        || (tonicSpelling.Alter == 0 && tonicSpelling.Letter != LetterF);

    /// <summary>
    /// Which degree of <paramref name="scale"/> <paramref name="pitch"/> is, and in which octave
    /// above the tonic, or <c>(-1, 0)</c> if it is not a degree at all.
    /// </summary>
    /// <remarks>
    /// Two passes, in this order. A tight cents match is the microtonal case and is unambiguous. The
    /// 12-TET match - the pitch shares a MIDI note with the degree's quantisation - is the 12-TET
    /// output case, where Rast's 350-cent third has already become a plain 400-cent E and no cents
    /// comparison could recover it. Running the tight pass first keeps the 12-TET pass from claiming
    /// a note the scale expresses exactly.
    /// </remarks>
    private static (int Degree, int OctaveIndex) FindDegree(Pitch pitch, Scale scale, Pitch tonic)
    {
        double rel = pitch.Cents - tonic.Cents;
        int centreOctave = (int)Math.Floor(rel / MidiRounding.CentsPerOctave);

        (int Degree, int OctaveIndex) best = (-1, 0);
        double bestDistance = double.MaxValue;

        for (int k = centreOctave - OctaveSearchRadius; k <= centreOctave + OctaveSearchRadius; k++)
        {
            for (int i = 0; i < scale.DegreeCount; i++)
            {
                double candidate =
                    tonic.Cents + (k * MidiRounding.CentsPerOctave) + scale.DegreeCents[i];
                double distance = Math.Abs(pitch.Cents - candidate);

                if (distance <= DegreeMatchToleranceCents && distance < bestDistance)
                {
                    best = (i, k);
                    bestDistance = distance;
                }
            }
        }

        if (best.Degree >= 0)
        {
            return best;
        }

        for (int k = centreOctave - OctaveSearchRadius; k <= centreOctave + OctaveSearchRadius; k++)
        {
            for (int i = 0; i < scale.DegreeCount; i++)
            {
                double candidate =
                    tonic.Cents + (k * MidiRounding.CentsPerOctave) + scale.DegreeCents[i];

                if (pitch.MidiNote != MidiRounding.ToNearestSemitone(candidate))
                {
                    continue;
                }

                // Two degrees can quantise onto one semitone - Rast's 350 and a hypothetical 400
                // both land on E. Nearest in cents is the deterministic answer.
                double distance = Math.Abs(pitch.Cents - candidate);
                if (distance < bestDistance)
                {
                    best = (i, k);
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    /// <summary>The scientific-notation octave the tonic's <em>written</em> letter belongs to.</summary>
    /// <remarks>
    /// Derived from the letter and its alteration rather than from the pitch class, so C-flat 4 stays
    /// in octave 4 even though it sounds a B in octave 3.
    /// </remarks>
    private static int OctaveOf(Pitch tonic, TonicSpelling tonicSpelling)
    {
        double semitonesAboveC =
            TonicSpelling.NaturalSemitones[tonicSpelling.Letter] + tonicSpelling.Alter;

        return FloorDiv(
            MidiRounding.ToNearestInt(tonic.MidiNote - semitonesAboveC),
            MidiRounding.SemitonesPerOctave) - 1;
    }

    /// <summary>Absolute cents of a natural letter in a given scientific-notation octave.</summary>
    private static double NaturalCents(int letter, int octave) =>
        (((octave + 1) * MidiRounding.SemitonesPerOctave) + TonicSpelling.NaturalSemitones[letter])
        * MidiRounding.CentsPerSemitone;

    /// <summary>Floor division. C#'s <c>/</c> truncates toward zero, which is wrong below the tonic.</summary>
    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return (value % divisor != 0 && ((value < 0) != (divisor < 0))) ? quotient - 1 : quotient;
    }

    /// <summary>Positive modulo. C#'s <c>%</c> keeps the sign of the dividend.</summary>
    private static int PositiveMod(int value, int modulus)
    {
        int remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }
}
