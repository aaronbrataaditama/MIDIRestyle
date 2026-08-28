using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="NoteSpeller"/> converts between the two spelling frames the plan keeps deliberately
/// apart: <see cref="DegreeSpelling.Alter"/> is measured against the major-scale degree at the same
/// index (tonic-independent, stored once per scale), while a written note's alteration - the MusicXML
/// <c>&lt;alter&gt;</c> - is absolute against the natural letter and therefore depends on the tonic.
/// <para>
/// The guard case is Hijaz on a D tonic: step 2 has <c>DegreeSpelling.Alter = 0</c> yet is written
/// F-sharp. If those two numbers ever agree by accident in every test here, the test suite has
/// stopped testing the thing it exists for.
/// </para>
/// </summary>
public class NoteSpellerTests
{
    // Hijaz in its notated (non-comma) form. Spells C Db E F G Ab Bb on C: Alter -1 on steps 1, 5, 6.
    private static readonly double[] HijazCents = [0, 100, 400, 500, 700, 800, 1000];

    // Maqam Rast. The neutral third and seventh are the quarter-tone case, and they are also the
    // exact +/-50 cent ties that make MidpointRounding.AwayFromZero load-bearing.
    private static readonly double[] RastCents = [0, 200, 350, 500, 700, 900, 1050];

    // Melakarta #1 Kanakangi: C Db Ebb F G Ab Bbb on C. Its double flats are the material for
    // pushing an absolute alteration past what an accidental can write.
    private static readonly double[] KanakangiCents = [0, 100, 200, 500, 700, 800, 900];

    // A cited-measured Slendro. Authored Notatable = false, so it has no staff spelling at all.
    private static readonly double[] SlendroCents = [0, 231, 474, 717, 955];

    private static readonly Scale Hijaz = MakeScale("test.hijaz", "Hijaz", HijazCents);
    private static readonly Scale Rast = MakeScale("test.rast", "Rast", RastCents);
    private static readonly Scale Kanakangi = MakeScale("test.kanakangi", "Kanakangi", KanakangiCents);
    private static readonly Scale Slendro =
        MakeScale("test.slendro", "Slendro", SlendroCents, notatable: false);

    private static readonly TonicSpelling C = TonicSpelling.C;
    private static readonly TonicSpelling D = new(1, 0);
    private static readonly TonicSpelling F = new(3, 0);
    private static readonly TonicSpelling B = new(6, 0);
    private static readonly TonicSpelling BFlat = new(6, -1);
    private static readonly TonicSpelling CFlat = new(0, -1);

    private static Scale MakeScale(string id, string name, double[] cents, bool notatable = true)
    {
        IReadOnlyList<DegreeSpelling>? spelling = null;

        if (notatable)
        {
            SpellingResult result = DiatonicSpeller.Derive(cents, notatable, name);
            result.Succeeded.Should().BeTrue($"the {name} fixture must have a staff spelling");
            spelling = result.Spelling;
        }

        return new Scale(id, name, "Test", "Test", cents, "Test fixture", notatable, spelling);
    }

    /// <summary>Spells degree <paramref name="degree"/> of a scale on a given tonic.</summary>
    private static SpelledNote SpellDegree(Scale scale, int degree, int tonicMidi, TonicSpelling spelling) =>
        NoteSpeller.Spell(
            Pitch.FromMidi(tonicMidi).ShiftCents(scale.DegreeCents[degree]),
            scale,
            Pitch.FromMidi(tonicMidi),
            spelling);

    // ---------------------------------------------------------------------------------------
    // The frame conversion itself
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The fixture is only useful if it really carries the tonic-independent frame, so pin it: the
    /// plan says Hijaz is <c>Alter = -1</c> on steps 1, 5 and 6 and zero elsewhere.
    /// </summary>
    [Fact]
    public void HijazFixtureCarriesTheTonicIndependentFrame()
    {
        Hijaz.Spelling.Should().NotBeNull();
        Hijaz.Spelling!.Select(s => s.DiatonicStep).Should().Equal([0, 1, 2, 3, 4, 5, 6],
            "a heptatonic scale spends every letter exactly once, in order");
        Hijaz.Spelling!.Select(s => s.Alter).Should().Equal([0, -1, 0, 0, 0, -1, -1],
            "Alter is measured against the major scale, not against the natural letter");
    }

    /// <summary>
    /// <b>The documented trap.</b> Hijaz's step 2 is <c>Alter = 0</c> in the scale's own frame - it
    /// is the unaltered third degree - yet on a D tonic it is written F-sharp, which MusicXML must
    /// emit as <c>&lt;alter&gt;1&lt;/alter&gt;</c>. Two different numbers for one degree.
    /// </summary>
    [Fact]
    public void HijazStepTwoOnDIsRelativeAlterZeroButAbsoluteAlterOne()
    {
        Hijaz.Spelling![2].Alter.Should().Be(0, "the scale's frame is relative to the major third");

        SpelledNote note = SpellDegree(Hijaz, degree: 2, tonicMidi: 62, D);

        note.Alter.Should().Be(1, "the written frame is absolute against the natural letter F");
        note.LetterName.Should().Be('F');
        note.ToString().Should().Be("F♯4");
    }

    [Theory]
    [InlineData(0, "D4")]
    [InlineData(1, "E♭4")]
    [InlineData(2, "F♯4")]
    [InlineData(3, "G4")]
    [InlineData(4, "A4")]
    [InlineData(5, "B♭4")]
    [InlineData(6, "C5")]
    public void HijazOnDNotatesDEFlatFSharpGABFlatC(int degree, string expected) =>
        SpellDegree(Hijaz, degree, tonicMidi: 62, D).ToString().Should().Be(expected);

    [Theory]
    [InlineData(0, "B♭4")]
    [InlineData(1, "C♭5")]
    [InlineData(2, "D5")]
    [InlineData(3, "E♭5")]
    [InlineData(4, "F5")]
    [InlineData(5, "G♭5")]
    [InlineData(6, "A♭5")]
    public void HijazOnBFlatNotatesBFlatCFlatDEFlatFGFlatAFlat(int degree, string expected) =>
        SpellDegree(Hijaz, degree, tonicMidi: 70, BFlat).ToString().Should().Be(expected);

    /// <summary>
    /// C-flat 5 sounds a B4 but is written in octave 5, because the octave belongs to the letter and
    /// not to the pitch. Getting this from the MIDI note instead would put the second degree of
    /// B-flat Hijaz an octave below its own tonic.
    /// </summary>
    [Fact]
    public void CFlatIsWrittenInTheOctaveOfItsLetterNotOfItsSoundingPitch()
    {
        SpelledNote note = SpellDegree(Hijaz, degree: 1, tonicMidi: 70, BFlat);

        note.Octave.Should().Be(5);
        note.Letter.Should().Be(0, "the letter is C");
        note.DiatonicIndex.Should().Be(35, "C5 sits one staff position above B4");
        Pitch.FromMidi(70).ShiftCents(100).MidiNote.Should().Be(71, "yet it sounds a B4");
    }

    // ---------------------------------------------------------------------------------------
    // Floor division and positive modulo - notes below the tonic
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// C# truncates <c>/</c> toward zero and <c>%</c> keeps the sign of the dividend, so the naive
    /// octave arithmetic indexes a degree of -1 and throws. Notes below the tonic are not an edge
    /// case: every bass line in every file has them.
    /// </summary>
    [Theory]
    [InlineData(38, "D2")]   // tonic, two octaves down
    [InlineData(39, "E♭2")]
    [InlineData(34, "B♭1")]
    [InlineData(48, "C3")]
    [InlineData(26, "D1")]   // three octaves down
    [InlineData(14, "D0")]
    [InlineData(2, "D-1")]   // and into the negative octave
    public void NotesBelowTheTonicSpellCorrectly(int midi, string expected) =>
        NoteSpeller.Spell(Pitch.FromMidi(midi), Hijaz, Pitch.FromMidi(62), D)
            .ToString().Should().Be(expected);

    [Fact]
    public void ANoteBelowTheTonicKeepsItsDegreeSpellingRatherThanFallingBackToChromatic()
    {
        // MIDI 34 is B-flat 1. On a D tonic that is Hijaz degree 5, three octaves down. A chromatic
        // fallback would still say B-flat here, so assert the alteration came from the scale by
        // checking the letter is B and not the enharmonic A-sharp a sharp-side fallback would give.
        SpelledNote note = NoteSpeller.Spell(Pitch.FromMidi(34), Hijaz, Pitch.FromMidi(62), D);

        note.Letter.Should().Be(6);
        note.Alter.Should().Be(-1);
        note.Octave.Should().Be(1);
        note.ResidualCents.Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------
    // Octave carry
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// On a B tonic every letter but the first wraps past B into the next octave, so the carry runs
    /// on six of the seven degrees rather than on an unlucky one.
    /// </summary>
    [Theory]
    [InlineData(0, "B3")]
    [InlineData(1, "C4")]
    [InlineData(2, "D♯4")]
    [InlineData(3, "E4")]
    [InlineData(4, "F♯4")]
    [InlineData(5, "G4")]
    [InlineData(6, "A4")]
    public void LetterCarriesPastBIntoTheNextOctave(int degree, string expected) =>
        SpellDegree(Hijaz, degree, tonicMidi: 59, B).ToString().Should().Be(expected);

    [Fact]
    public void StaffPositionsAscendOneStepPerDegreeAcrossAnOctaveCarry()
    {
        int[] positions = [.. Enumerable.Range(0, Hijaz.DegreeCount)
            .Select(d => SpellDegree(Hijaz, d, tonicMidi: 59, B).DiatonicIndex)];

        positions.Should().Equal([27, 28, 29, 30, 31, 32, 33],
            "a heptatonic scale climbs exactly one staff position per degree");
    }

    // ---------------------------------------------------------------------------------------
    // Quarter tones, in both output modes
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 2, -0.5, "E½♭4")]
    [InlineData(6, 6, -0.5, "B½♭4")]
    public void RastNeutralDegreesSpellAsHalfFlatsInMicrotonalOutput(
        int degree, int expectedLetter, double expectedAlter, string expected)
    {
        SpelledNote note = SpellDegree(Rast, degree, tonicMidi: 60, C);

        note.Letter.Should().Be(expectedLetter);
        note.Alter.Should().Be(expectedAlter);
        note.ResidualCents.Should().Be(0, "350 and 1050 cents are exact quarter-tones");
        note.ToString().Should().Be(expected);
    }

    /// <summary>
    /// The same two degrees after 12-TET quantisation. The absolute alteration is derived from the
    /// note's <em>actual</em> cents, not the scale's nominal cents, so this needs no output-mode
    /// flag: a quantised neutral third is simply a natural E.
    /// </summary>
    [Theory]
    [InlineData(2, 2, "E4")]
    [InlineData(6, 6, "B4")]
    public void RastNeutralDegreesQuantisedToTwelveTetSpellAsPlainNaturals(
        int degree, int expectedLetter, string expected)
    {
        Pitch sounding = Pitch.FromMidi(60).ShiftCents(Rast.DegreeCents[degree]);
        Pitch quantised = Pitch.FromMidi(sounding.MidiNote);

        SpelledNote note = NoteSpeller.Spell(quantised, Rast, Pitch.FromMidi(60), C);

        note.Letter.Should().Be(expectedLetter);
        note.Alter.Should().Be(0, "the half-flat was rounded away before it reached the speller");
        note.AccidentalSymbol.Should().BeEmpty();
        note.ResidualCents.Should().Be(0);
        note.ToString().Should().Be(expected);
    }

    /// <summary>
    /// The 12-TET match only works because both neutral degrees round upward. Under banker's
    /// rounding 6350 goes to 64 (E) but 7050 goes to 70 (B-flat), which is a different letter and a
    /// different accidental from one inflection - the exact failure MidiRounding exists to prevent.
    /// </summary>
    [Fact]
    public void BothRastNeutralDegreesRoundAwayFromZeroOntoTheSameInflection()
    {
        new Pitch(6350).MidiNote.Should().Be(64, "ties round away from zero, not to even");
        new Pitch(7050).MidiNote.Should().Be(71, "and so does the seventh, not down to 70");
    }

    // ---------------------------------------------------------------------------------------
    // Chromatic fallback
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Slendro is authored <c>Notatable = false</c>, so <see cref="Scale.Spelling"/> is null however
    /// well quarter-tone accidentals would approximate it. The speller must fall back, not throw.
    /// </summary>
    [Fact]
    public void ANonNotatableScaleFallsBackToChromaticSpelling()
    {
        Slendro.Spelling.Should().BeNull("Notatable = false always wins over derivation");

        SpelledNote note = SpellDegree(Slendro, degree: 1, tonicMidi: 60, C);

        note.Letter.Should().Be(1, "231 cents above C4 is nearest to D4");
        note.Octave.Should().Be(4);
        note.Alter.Should().Be(0);
        note.ResidualCents.Should().BeApproximately(31, 1e-9,
            "the whole deviation is kept for a comma mark rather than faked as an accidental");
    }

    [Fact]
    public void EveryDegreeOfANonNotatableScaleSpellsWithoutThrowing()
    {
        for (int degree = 0; degree < Slendro.DegreeCount; degree++)
        {
            SpelledNote note = SpellDegree(Slendro, degree, tonicMidi: 60, C);
            note.Letter.Should().BeInRange(0, 6);
            Math.Abs(note.ResidualCents).Should().BeLessThanOrEqualTo(50);
        }
    }

    /// <summary>MIDI 68 is not a degree of Hijaz on D in any octave, so it spells chromatically.</summary>
    [Fact]
    public void APitchThatIsNotAScaleDegreeFallsBackToChromatic()
    {
        SpelledNote note = NoteSpeller.Spell(Pitch.FromMidi(68), Hijaz, Pitch.FromMidi(62), D);

        note.ToString().Should().Be("G♯4", "a natural D tonic takes the sharp side");
    }

    /// <summary>
    /// Kanakangi on a C-flat tonic would need a triple flat on its third - E-flat-flat-flat - which
    /// no accidental writes. Beyond a double accidental the chromatic spelling is the readable one.
    /// </summary>
    [Fact]
    public void AnAlterationBeyondADoubleAccidentalFallsBackToChromatic()
    {
        Kanakangi.Spelling![2].Alter.Should().Be(-2, "a double flat is legitimate in the scale's frame");

        SpelledNote note = SpellDegree(Kanakangi, degree: 2, tonicMidi: 59, CFlat);

        Math.Abs(note.Alter).Should().BeLessThanOrEqualTo(DegreeSpelling.MaxAlter);
        note.ToString().Should().Be("D♭4", "a flat tonic takes the flat side");
    }

    [Theory]
    [InlineData(1, 0.0, "C♯4")]    // D tonic, sharp side
    [InlineData(0, 0.0, "C♯4")]    // C tonic, neutral, defaults to sharps
    [InlineData(0, 1.0, "C♯4")]    // C-sharp tonic
    [InlineData(6, -1.0, "D♭4")]   // B-flat tonic, flat side
    [InlineData(3, 0.0, "D♭4")]    // F is the one natural letter whose key signature has a flat
    public void SharpSideAndFlatSideTonicsSpellTheBlackKeysDifferently(
        int letter, double alter, string expected) =>
        NoteSpeller.SpellChromatic(Pitch.FromMidi(61), new TonicSpelling(letter, alter))
            .ToString().Should().Be(expected);

    [Fact]
    public void ChromaticSpellingKeepsTheDeviationFromTwelveTetAsResidual()
    {
        SpelledNote note = NoteSpeller.SpellChromatic(Pitch.FromMidi(69, 30), F);

        note.LetterName.Should().Be('A');
        note.Octave.Should().Be(4);
        note.Alter.Should().Be(0);
        note.ResidualCents.Should().BeApproximately(30, 1e-9);
    }

    [Theory]
    [InlineData(0, "C-1")]
    [InlineData(60, "C4")]
    [InlineData(127, "G9")]
    public void ChromaticSpellingUsesScientificPitchNotationAcrossTheWholeMidiRange(
        int midi, string expected) =>
        NoteSpeller.SpellChromatic(Pitch.FromMidi(midi), C).ToString().Should().Be(expected);

    // ---------------------------------------------------------------------------------------
    // SpelledNote itself
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void DiatonicIndexIsAStaffPositionSoEnharmonicsOnOneLetterShareIt()
    {
        new SpelledNote(0, 4, 1, 0).DiatonicIndex.Should().Be(28, "C-sharp 4");
        new SpelledNote(0, 4, -1, 0).DiatonicIndex.Should().Be(28, "C-flat 4 sits on the same line");
        new SpelledNote(6, 3, 0, 0).DiatonicIndex.Should().Be(27, "B3 is one line below C4");
    }

    [Theory]
    [InlineData(0.0, "")]
    [InlineData(1.0, "♯")]
    [InlineData(-1.0, "♭")]
    [InlineData(2.0, "𝄪")]
    [InlineData(-2.0, "𝄫")]
    [InlineData(0.5, "½♯")]
    [InlineData(-0.5, "½♭")]
    [InlineData(1.5, "¾♯")]
    [InlineData(-1.5, "¾♭")]
    public void AccidentalSymbolCoversEveryWritableAlteration(double alter, string expected) =>
        new SpelledNote(0, 4, alter, 0).AccidentalSymbol.Should().Be(expected);

    // ---------------------------------------------------------------------------------------
    // Guards
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void SpellRejectsANullScale()
    {
        Action act = () => NoteSpeller.Spell(Pitch.FromMidi(60), null!, Pitch.FromMidi(60), C);
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// A pitch a hair under the tonic an octave up is 1198 cents into its own octave and matches no
    /// degree there. The neighbouring octave has to be searched or it falls out to chromatic.
    /// </summary>
    [Fact]
    public void APitchJustBelowTheOctaveStillMatchesTheTonicDegreeAbove()
    {
        SpelledNote note = NoteSpeller.Spell(
            Pitch.FromMidi(62).ShiftCents(1198), Hijaz, Pitch.FromMidi(62), D);

        note.LetterName.Should().Be('D');
        note.Octave.Should().Be(5);
        note.Alter.Should().Be(0);
        note.ResidualCents.Should().BeApproximately(-2, 1e-9);
    }
}
