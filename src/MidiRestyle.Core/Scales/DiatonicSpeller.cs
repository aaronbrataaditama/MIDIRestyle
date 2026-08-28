using System.Diagnostics.CodeAnalysis;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Scales;

/// <summary>Why a scale has no Western staff spelling.</summary>
public enum SpellingFailure
{
    None = 0,

    /// <summary>
    /// The scale is authored <c>Notatable = false</c>. A cultural judgement, so it always wins over
    /// whatever derivation would produce.
    /// </summary>
    NotNotatable,

    /// <summary>More than seven degrees: there are only seven letter names to spend.</summary>
    TooManyDegrees,

    /// <summary>A degree needs a larger alteration than a double accidental can write.</summary>
    AlterationTooLarge,

    /// <summary>
    /// A degree lands too far from any real accidental for the written pitch to be honest.
    /// </summary>
    ResidualTooLarge,

    /// <summary>Two degrees claim the same letter <em>and</em> the same accidental.</summary>
    DuplicateDegreeSpelling,

    /// <summary>Nothing to spell.</summary>
    NoDegrees,
}

/// <summary>The outcome of deriving a staff spelling for a scale.</summary>
/// <param name="Spelling">One entry per degree, or null when no staff spelling exists.</param>
/// <param name="Failure">Why it failed, or <see cref="SpellingFailure.None"/>.</param>
/// <param name="Diagnostic">
/// Human-readable explanation of the failure, for the UI and for scale-library authors. Never null
/// on failure: a silent null here reads as "nobody has implemented this scale yet" rather than
/// "this scale cannot be written down".
/// </param>
public sealed record SpellingResult(
    IReadOnlyList<DegreeSpelling>? Spelling,
    SpellingFailure Failure = SpellingFailure.None,
    string? Diagnostic = null)
{
    [MemberNotNullWhen(true, nameof(Spelling))]
    public bool Succeeded => Spelling is not null;
}

/// <summary>
/// Derives a Western staff spelling from a scale's cents, or explains why none exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>This branches on degree count, because the two cases follow genuinely different rules.</b>
/// A heptatonic scale spends all seven letter names exactly once, so <c>step = degreeIndex</c> - that
/// is simply how Western notation works. Everything else takes the nearest diatonic step.
/// </para>
/// <para>
/// Using the nearest-step rule on a heptatonic scale is a bug, not a simplification. A melakarta
/// carrying both R3 (300c) and G3 (400c) has two degrees nearest to E, so a collision check would
/// reject a scale that spells perfectly cleanly as <c>C D# E F G...</c>.
/// </para>
/// <para>
/// Conversely, the heptatonic rejection threshold is <see cref="DegreeSpelling.MaxAlter"/> = 2, not
/// 1.5. Double accidentals are legitimate notation and legal MusicXML; a 1.5 threshold rejects 22 of
/// the 72 melakartas, since G1 (200c) at index 2 and N1 (900c) at index 6 each give
/// <c>Alter = -2</c>. Mela #1 Kanakangi is <c>C Db Ebb F G Ab Bbb</c> - that is its standard Western
/// rendering, not a contrivance.
/// </para>
/// <para>
/// In the non-heptatonic case a step <em>may repeat</em>: only an identical
/// <c>(DiatonicStep, Alter)</c> pair is a collision. Without that allowance the blues scale is
/// rejected, because 600c and 700c both claim G - yet everyone spells it <c>C Eb F Gb G Bb</c>.
/// Pitch stays strictly ascending because the input does (<see cref="Scale"/> enforces it), so a
/// repeated step never means a repeated pitch.
/// </para>
/// </remarks>
public static class DiatonicSpeller
{
    /// <summary>Letter names per octave. The hard ceiling on how many degrees can be spelled.</summary>
    public const int DiatonicSteps = 7;

    // Ties in the nearest-step search are the common case, not an edge case: every altered degree of
    // the Japanese In and blues scales sits exactly halfway between two letters. Compare with slack
    // so a value that is a hair under the midpoint does not silently fall to the sharp side.
    private const double TieTolerance = 1e-9;

    /// <summary>Derives the staff spelling for <paramref name="scale"/>.</summary>
    public static SpellingResult Derive(Scale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return Derive(scale.DegreeCents, scale.Notatable, scale.Name);
    }

    /// <summary>Derives the staff spelling for a bare list of ascending degree cents.</summary>
    /// <param name="degreeCents">Ascending cents above the tonic, starting at 0.</param>
    /// <param name="notatable">
    /// The scale's authored notatability. False short-circuits to a failure whatever the cents would
    /// derive: Slendro <em>can</em> be approximated with quarter-tone accidentals to within 10 cents,
    /// but no gamelan musician reads that, so deriving it anyway would be a lie dressed as precision.
    /// </param>
    /// <param name="scaleName">Optional name, used only to make diagnostics readable.</param>
    public static SpellingResult Derive(
        IReadOnlyList<double> degreeCents,
        bool notatable = true,
        string? scaleName = null)
    {
        ArgumentNullException.ThrowIfNull(degreeCents);

        string label = string.IsNullOrWhiteSpace(scaleName) ? "This scale" : $"'{scaleName}'";

        if (!notatable)
        {
            return Fail(
                SpellingFailure.NotNotatable,
                $"{label} is authored Notatable = false, so it has no staff spelling by cultural " +
                "judgement rather than by arithmetic. Its degrees could be approximated with " +
                "quarter-tone accidentals, but the tradition reads cipher notation, not a staff.");
        }

        if (degreeCents.Count == 0)
        {
            return Fail(SpellingFailure.NoDegrees, $"{label} has no degrees to spell.");
        }

        if (degreeCents.Count > DiatonicSteps)
        {
            return Fail(
                SpellingFailure.TooManyDegrees,
                $"{label} has {degreeCents.Count} degrees, more than the {DiatonicSteps} letter " +
                "names a Western octave provides, so no staff spelling exists. Several Persian " +
                "dastgahs and Turkish makams are 8-9 notes.");
        }

        bool heptatonic = degreeCents.Count == DiatonicSteps;
        var spellings = new DegreeSpelling[degreeCents.Count];

        for (int i = 0; i < degreeCents.Count; i++)
        {
            double cents = degreeCents[i];

            // A heptatonic scale uses every letter once, in order. Anything else takes the nearest.
            int step = heptatonic ? i : NearestDiatonicStep(cents);

            double reference = DegreeSpelling.MajorScaleCents[step];

            // Alter is measured against the major-scale degree at this index, NOT against the
            // natural letter - that is the MusicXML frame and it is deliberately not this one.
            double rawAlter = (cents - reference) / MidiRounding.CentsPerSemitone;

            // Comma-based scales derive alterations no renderer can draw: AEU Rast wants -0.151
            // semitones on its third. Snap to a real accidental and keep the remainder, so a staff
            // view can mark the comma instead of the spelling silently lying by 15 cents.
            double alter =
                MidiRounding.ToNearestInt(rawAlter / DegreeSpelling.AlterQuantum)
                * DegreeSpelling.AlterQuantum;

            double residualCents = cents - (reference + (alter * MidiRounding.CentsPerSemitone));

            var spelling = new DegreeSpelling(step, alter, residualCents);

            // Deferring to IsNotatable keeps the accept/reject boundary in exactly one place, so a
            // spelling this method returns can never be one the type itself calls unwritable.
            if (!spelling.IsNotatable)
            {
                return Math.Abs(alter) > DegreeSpelling.MaxAlter
                    ? Fail(
                        SpellingFailure.AlterationTooLarge,
                        $"{label} degree {i} at {cents:0.###} cents needs Alter = {alter:0.##} " +
                        $"against the major-scale degree at {reference:0.###} cents, beyond the " +
                        $"+/-{DegreeSpelling.MaxAlter:0.#} a double accidental can write.")
                    : Fail(
                        // Currently unreachable by arithmetic: MaxResidualCents (25) is exactly half
                        // of AlterQuantum (50 cents), so snapping can never leave more behind. Kept
                        // because it goes live the moment either constant moves.
                        SpellingFailure.ResidualTooLarge,
                        $"{label} degree {i} at {cents:0.###} cents sits {residualCents:0.#} cents " +
                        $"from its nearest accidental, past the {DegreeSpelling.MaxResidualCents:0.#} " +
                        "cent limit, so the written pitch would mislead more than it informs.");
            }

            spellings[i] = spelling;
        }

        if (!heptatonic)
        {
            // Only an identical (step, alter) pair collides. Steps repeat legitimately - that is
            // exactly what makes Gb and G spellable on one letter in the blues scale.
            for (int i = 1; i < spellings.Length; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (spellings[i].DiatonicStep == spellings[j].DiatonicStep
                        && spellings[i].Alter == spellings[j].Alter)
                    {
                        return Fail(
                            SpellingFailure.DuplicateDegreeSpelling,
                            $"{label} degrees {j} and {i} both spell as " +
                            $"{spellings[i].ToStringOnC()}, which would write two different pitches " +
                            "as one note.");
                    }
                }
            }
        }

        return new SpellingResult(spellings);
    }

    /// <summary>
    /// The diatonic step whose major-scale degree is closest to <paramref name="cents"/>, ties
    /// resolving to the <em>higher</em> step.
    /// </summary>
    /// <remarks>
    /// The tie rule is what makes alterations come out as flats: 100 cents is equidistant from C and
    /// D, and the higher step spells it Db rather than C#. That matches the rest of this library and
    /// the way the Japanese In scale is conventionally written.
    /// </remarks>
    private static int NearestDiatonicStep(double cents)
    {
        int best = 0;
        double bestDistance = Math.Abs(cents - DegreeSpelling.MajorScaleCents[0]);

        for (int step = 1; step < DiatonicSteps; step++)
        {
            double distance = Math.Abs(cents - DegreeSpelling.MajorScaleCents[step]);
            if (distance <= bestDistance + TieTolerance)
            {
                best = step;
                bestDistance = Math.Min(bestDistance, distance);
            }
        }

        return best;
    }

    private static SpellingResult Fail(SpellingFailure failure, string diagnostic) =>
        new(null, failure, diagnostic);
}
