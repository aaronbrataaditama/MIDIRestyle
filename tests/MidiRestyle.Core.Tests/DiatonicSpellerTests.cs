using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// Golden tests for staff-spelling derivation. Every row here is a scale whose conventional Western
/// rendering is already settled by practice, so the expected values are citations rather than
/// opinions - which is the point: two earlier versions of these rules rejected scales that musicians
/// write down every day.
/// </summary>
public class DiatonicSpellerTests
{
    // Accidental glyphs, matching DegreeSpelling.AccidentalSymbol. Source files are UTF-8.
    private const string Flat = "♭";
    private const string Sharp = "♯";
    private const string HalfFlat = "½♭";
    private const string DoubleFlat = "\U0001d12b";

    private static readonly double[] Gong = [0, 200, 400, 700, 900];
    private static readonly double[] JapaneseIn = [0, 100, 500, 700, 800];
    private static readonly double[] Blues = [0, 300, 500, 600, 700, 1000];
    private static readonly double[] Hijaz = [0, 100, 400, 500, 700, 800, 1000];
    private static readonly double[] Rast = [0, 200, 350, 500, 700, 900, 1050];
    private static readonly double[] Kanakangi = [0, 100, 200, 500, 700, 800, 900];
    private static readonly double[] AeuRast = [0, 203.8, 384.9, 498.1, 701.9, 905.7, 1086.8];
    private static readonly double[] Slendro = [0, 240, 480, 720, 960];

    // A melakarta carrying both R3 (300c) and G3 (400c). Under a nearest-step rule both degrees
    // claim E and the scale is wrongly rejected; under the heptatonic rule it spells cleanly.
    private static readonly double[] R3AndG3 = [0, 300, 400, 500, 700, 900, 1100];

    // Eight and nine degrees - the size of several Persian dastgahs and Turkish makams.
    private static readonly double[] EightDegrees = [0, 100, 300, 400, 500, 700, 800, 1000];
    private static readonly double[] NineDegrees = [0, 100, 300, 400, 500, 700, 800, 1000, 1100];

    // Degree 1 sits 250 cents above its major-scale reference: Alter = +2.5, past a double sharp.
    private static readonly double[] BeyondDoubleAccidental = [0, 450, 500, 550, 700, 900, 1100];

    // Degrees 0 and 1 are only 20 cents apart, so both snap to a plain C.
    private static readonly double[] CollidingSpellings = [0, 20, 400, 700];

    [Fact]
    public void GongSpellsAsCDEGA()
    {
        var spelling = Derive(Gong);

        Steps(spelling).Should().Equal(new[] { 0, 1, 2, 4, 5 });
        Alters(spelling).Should().Equal(new[] { 0d, 0d, 0d, 0d, 0d });
        OnC(spelling).Should().Be("C D E G A");
    }

    [Fact]
    public void JapaneseInResolvesTiesToFlats()
    {
        var spelling = Derive(JapaneseIn);

        Steps(spelling).Should().Equal(new[] { 0, 1, 3, 4, 5 });
        Alters(spelling).Should().Equal(new[] { 0d, -1d, 0d, 0d, -1d });
        OnC(spelling).Should().Be($"C D{Flat} F G A{Flat}");
    }

    [Fact]
    public void BluesRepeatsStepFourWithDifferentAlters()
    {
        var spelling = Derive(Blues);

        Steps(spelling).Should().Equal(new[] { 0, 2, 3, 4, 4, 6 });
        Alters(spelling).Should().Equal(new[] { 0d, -1d, 0d, -1d, 0d, -1d });
        OnC(spelling).Should().Be($"C E{Flat} F G{Flat} G B{Flat}");
    }

    [Fact]
    public void HijazAltersStepsOneFiveAndSixByAFlat()
    {
        var spelling = Derive(Hijaz);

        Steps(spelling).Should().Equal(new[] { 0, 1, 2, 3, 4, 5, 6 });
        Alters(spelling).Should().Equal(new[] { 0d, -1d, 0d, 0d, 0d, -1d, -1d });
        OnC(spelling).Should().Be($"C D{Flat} E F G A{Flat} B{Flat}");
    }

    [Fact]
    public void RastHalfFlatsStepsTwoAndSix()
    {
        var spelling = Derive(Rast);

        Steps(spelling).Should().Equal(new[] { 0, 1, 2, 3, 4, 5, 6 });
        Alters(spelling).Should().Equal(new[] { 0d, 0d, -0.5d, 0d, 0d, 0d, -0.5d });
        OnC(spelling).Should().Be($"C D E{HalfFlat} F G A B{HalfFlat}");
    }

    /// <summary>
    /// The rejection threshold must be 2, not 1.5: G1 at index 2 and N1 at index 6 both give
    /// Alter = -2, and a 1.5 limit rejects 22 of the 72 melakartas.
    /// </summary>
    [Fact]
    public void KanakangiIsAcceptedWithDoubleFlats()
    {
        var result = DiatonicSpeller.Derive(MakeScale(Kanakangi, "Kanakangi"));

        result.Succeeded.Should().BeTrue(
            "mela #1 spells as C Db Ebb F G Ab Bbb, its standard Western rendering");
        Steps(result.Spelling!).Should().Equal(new[] { 0, 1, 2, 3, 4, 5, 6 });
        Alters(result.Spelling!).Should().Equal(new[] { 0d, -1d, -2d, 0d, 0d, -1d, -2d });
        OnC(result.Spelling!).Should().Be($"C D{Flat} E{DoubleFlat} F G A{Flat} B{DoubleFlat}");
    }

    [Fact]
    public void HeptatonicRuleSpellsBothR3AndG3RatherThanCollidingOnE()
    {
        var spelling = Derive(R3AndG3);

        Steps(spelling).Should().Equal(new[] { 0, 1, 2, 3, 4, 5, 6 });
        Alters(spelling).Should().Equal(new[] { 0d, 1d, 0d, 0d, 0d, 0d, 0d });
        OnC(spelling).Should().Be($"C D{Sharp} E F G A B");
    }

    [Fact]
    public void AeuRastSpellsAsNaturalsAndKeepsTheCommaInTheResidual()
    {
        var spelling = Derive(AeuRast);

        Steps(spelling).Should().Equal(new[] { 0, 1, 2, 3, 4, 5, 6 });
        Alters(spelling).Should().AllSatisfy(alter => alter.Should().Be(0));
        OnC(spelling).Should().Be("C D E F G A B");

        spelling.Select(s => s.ResidualCents).Should().Equal(
            new[] { 0d, 3.8, -15.1, -1.9, 1.9, 5.7, -13.2 },
            (actual, expected) => Math.Abs(actual - expected) < 1e-6);

        spelling.Max(s => Math.Abs(s.ResidualCents)).Should().BeApproximately(15.1, 0.05,
            "the worst AEU comma is inside the 25-cent limit, so this scale must stay notatable");
    }

    /// <summary>
    /// Notatability is a cultural judgement, not a computation. Slendro <em>can</em> be approximated
    /// with quarter-tone accidentals to within 10 cents - which is precisely why derivation must not
    /// be allowed to overrule the authored flag.
    /// </summary>
    [Fact]
    public void AuthoredNotatableFalseWinsOverDerivation()
    {
        var result = DiatonicSpeller.Derive(MakeScale(Slendro, "Slendro", notatable: false));

        AssertFailed(result, SpellingFailure.NotNotatable);
        result.Diagnostic.Should().Contain("Notatable");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    public void MoreThanSevenDegreesFailsWithADiagnosticNamingTheCount(int degreeCount)
    {
        double[] cents = degreeCount == 8 ? EightDegrees : NineDegrees;
        cents.Should().HaveCount(degreeCount);

        var result = DiatonicSpeller.Derive(MakeScale(cents, "Oversized dastgah"));

        AssertFailed(result, SpellingFailure.TooManyDegrees);
        result.Diagnostic.Should().Contain(degreeCount.ToString(provider: null))
            .And.Contain("7", "the diagnostic must name both the count and the ceiling");
    }

    [Fact]
    public void AlterationBeyondADoubleAccidentalFails()
    {
        var result = DiatonicSpeller.Derive(MakeScale(BeyondDoubleAccidental, "Contrived"));

        AssertFailed(result, SpellingFailure.AlterationTooLarge);
        result.Diagnostic.Should().Contain("2.5").And.Contain("degree 1");
    }

    [Fact]
    public void IdenticalStepAndAlterCollides()
    {
        var result = DiatonicSpeller.Derive(MakeScale(CollidingSpellings, "Two Cs"));

        AssertFailed(result, SpellingFailure.DuplicateDegreeSpelling);
        result.Diagnostic.Should().Contain("degrees 0 and 1");
    }

    [Theory]
    [InlineData(new double[] { 0, 200, 400, 700, 900 })]
    [InlineData(new double[] { 0, 100, 500, 700, 800 })]
    [InlineData(new double[] { 0, 300, 500, 600, 700, 1000 })]
    [InlineData(new double[] { 0, 100, 400, 500, 700, 800, 1000 })]
    [InlineData(new double[] { 0, 200, 350, 500, 700, 900, 1050 })]
    [InlineData(new double[] { 0, 100, 200, 500, 700, 800, 900 })]
    [InlineData(new double[] { 0, 203.8, 384.9, 498.1, 701.9, 905.7, 1086.8 })]
    [InlineData(new double[] { 0, 300, 400, 500, 700, 900, 1100 })]
    public void EveryAcceptedDegreeIsWritable(double[] degreeCents)
    {
        var spelling = Derive(degreeCents);

        spelling.Should().HaveCount(degreeCents.Length);
        spelling.Should().AllSatisfy(degree =>
        {
            degree.IsNotatable.Should().BeTrue();
            degree.DiatonicStep.Should().BeInRange(0, DiatonicSpeller.DiatonicSteps - 1);

            double quanta = degree.Alter / DegreeSpelling.AlterQuantum;
            quanta.Should().Be(Math.Round(quanta),
                "every alteration must be a whole multiple of half a semitone");

            Math.Abs(degree.Alter).Should().BeLessThanOrEqualTo(DegreeSpelling.MaxAlter);
            Math.Abs(degree.ResidualCents)
                .Should().BeLessThanOrEqualTo(DegreeSpelling.MaxResidualCents);
        });
    }

    [Fact]
    public void ScaleAndBareCentsOverloadsAgree()
    {
        var fromScale = DiatonicSpeller.Derive(MakeScale(Blues, "Blues"));
        var fromCents = DiatonicSpeller.Derive(Blues);

        fromScale.Spelling.Should().Equal(fromCents.Spelling);
    }

    [Fact]
    public void FailureAlwaysCarriesADiagnostic()
    {
        SpellingResult[] failures =
        [
            DiatonicSpeller.Derive(MakeScale(Slendro, "Slendro", notatable: false)),
            DiatonicSpeller.Derive(MakeScale(NineDegrees, "Nine")),
            DiatonicSpeller.Derive(MakeScale(BeyondDoubleAccidental, "Contrived")),
            DiatonicSpeller.Derive(MakeScale(CollidingSpellings, "Two Cs")),
            DiatonicSpeller.Derive([]),
        ];

        failures.Should().AllSatisfy(result =>
        {
            result.Succeeded.Should().BeFalse();
            result.Spelling.Should().BeNull();
            result.Failure.Should().NotBe(SpellingFailure.None);
            result.Diagnostic.Should().NotBeNullOrWhiteSpace();
        });
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        Action fromNullScale = () => DiatonicSpeller.Derive((Scale)null!);
        Action fromNullCents = () => DiatonicSpeller.Derive((IReadOnlyList<double>)null!);

        fromNullScale.Should().Throw<ArgumentNullException>();
        fromNullCents.Should().Throw<ArgumentNullException>();
    }

    private static IReadOnlyList<DegreeSpelling> Derive(IReadOnlyList<double> degreeCents)
    {
        var result = DiatonicSpeller.Derive(degreeCents);
        result.Succeeded.Should().BeTrue(result.Diagnostic ?? "it carried no diagnostic");
        return result.Spelling!;
    }

    private static void AssertFailed(SpellingResult result, SpellingFailure expected)
    {
        result.Succeeded.Should().BeFalse();
        result.Spelling.Should().BeNull();
        result.Failure.Should().Be(expected);
        result.Diagnostic.Should().NotBeNullOrWhiteSpace();
    }

    private static IEnumerable<int> Steps(IReadOnlyList<DegreeSpelling> spelling) =>
        spelling.Select(s => s.DiatonicStep);

    private static IEnumerable<double> Alters(IReadOnlyList<DegreeSpelling> spelling) =>
        spelling.Select(s => s.Alter);

    private static string OnC(IReadOnlyList<DegreeSpelling> spelling) =>
        string.Join(' ', spelling.Select(s => s.ToStringOnC()));

    private static Scale MakeScale(double[] degreeCents, string name, bool notatable = true) =>
        new(
            id: "test.diatonic-speller",
            name: name,
            tradition: "Test",
            region: "Test",
            degreeCents: degreeCents,
            source: "Unit test fixture, DiatonicSpellerTests",
            notatable: notatable);
}
