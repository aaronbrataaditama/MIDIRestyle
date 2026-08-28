using System.Text;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// Covers the four Scala .scl rules that break a naive parser: the implicit 1/1 + explicit 2/1,
/// the cents-vs-ratio value rule, the non-2/1 period rejection, and the degree-cardinality cap.
/// </summary>
public class ScalaFileReaderTests
{
    // ---- Rule 1: the implicit 1/1 and the explicit 2/1 -------------------------------------

    [Fact]
    public void PrependsImplicitTonicAndStripsTrailingPeriod_TwelveToneFile()
    {
        // 12-TET expressed as cents, terminated by the ratio form of the octave - exercises both
        // the prepend and the strip in one file, per the mandate's worked example.
        const string scl = """
            ! twelve-tet.scl
            !
            12-tone equal temperament, as cents plus a ratio period
             12
            !
             100.0
             200.0
             300.0
             400.0
             500.0
             600.0
             700.0
             800.0
             900.0
             1000.0
             1100.0
             2/1
            """;

        var result = ScalaFileReader.ReadFromString(scl, "twelve-tet.scl");

        result.Success.Should().BeTrue();
        var scale = result.Scale!;
        scale.DegreeCount.Should().Be(12);

        double[] expected = [0, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 1100];
        for (int i = 0; i < expected.Length; i++)
        {
            scale.DegreeCents[i].Should().BeApproximately(expected[i], 0.0001);
        }

        // No 1200-cent entry anywhere - the octave must not duplicate the tonic.
        scale.DegreeCents.Should().NotContain(1200.0);
    }

    [Fact]
    public void DeclaredCountExcludesImplicitTonicButIncludesPeriod_BareIntegerPeriod()
    {
        // A minimal file: one interior degree plus a bare-integer period. Declared count is 2
        // (the interior degree + the period), even though the implicit 1/1 is a third note.
        const string scl = """
            ! minimal.scl
            A fifth plus a bare-integer octave
            2
            701.955
            2
            """;

        var result = ScalaFileReader.ReadFromString(scl);

        result.Success.Should().BeTrue();
        result.Scale!.DegreeCents.Should().HaveCount(2);
        result.Scale!.DegreeCents[0].Should().Be(0.0);
        result.Scale!.DegreeCents[1].Should().BeApproximately(701.955, 0.0001);
    }

    [Fact]
    public void RoundTrips_WellFormedSevenNoteFile_MaqamRast()
    {
        // Maqam Rast, cited in CLAUDE.md's own invariants: [0,200,350,500,700,900,1050].
        const string scl = """
            ! rast.scl
            Maqam Rast
             7
             200.0
             350.0
             500.0
             700.0
             900.0
             1050.0
             2/1
            """;

        var result = ScalaFileReader.ReadFromString(scl, "rast.scl");

        result.Success.Should().BeTrue();
        double[] expected = [0, 200, 350, 500, 700, 900, 1050];
        result.Scale!.DegreeCount.Should().Be(7);
        for (int i = 0; i < expected.Length; i++)
        {
            result.Scale!.DegreeCents[i].Should().BeApproximately(expected[i], 0.01);
        }
    }

    // ---- Rule 2: cents vs. ratio, and the assorted value-parsing edge cases ----------------

    [Fact]
    public void CentsValue_WithDecimalPoint_IsParsedAsCents()
    {
        var outcome = ScalaFileReader.ParsePitchToken("386.31");

        outcome.Success.Should().BeTrue();
        outcome.Cents.Should().BeApproximately(386.31, 0.0001);
    }

    [Fact]
    public void CentsValue_WithTrailingPeriodAndNoFraction_IsParsedAsCents()
    {
        var outcome = ScalaFileReader.ParsePitchToken("408.");

        outcome.Success.Should().BeTrue();
        outcome.Cents.Should().BeApproximately(408.0, 0.0001);
    }

    [Fact]
    public void NegativeCents_IsLegalSyntax()
    {
        var outcome = ScalaFileReader.ParsePitchToken("-5.0");

        outcome.Success.Should().BeTrue();
        outcome.Cents.Should().BeApproximately(-5.0, 0.0001);
    }

    [Fact]
    public void RatioValue_WithSlash_IsConvertedToCents()
    {
        var outcome = ScalaFileReader.ParsePitchToken("5/4");

        outcome.Success.Should().BeTrue();
        outcome.Cents.Should().BeApproximately(1200.0 * Math.Log2(5.0 / 4.0), 0.0001);
    }

    [Fact]
    public void BareInteger_IsTreatedAsRatioOverOne_NotAsCents()
    {
        // The mandate's own example: bare 700 means the ratio 700/1 (~11,344 cents), not 700 cents.
        var outcome = ScalaFileReader.ParsePitchToken("700");

        outcome.Success.Should().BeTrue();
        outcome.Cents.Should().BeApproximately(1200.0 * Math.Log2(700.0), 0.0001);
        outcome.Cents.Should().BeGreaterThan(10_000.0);
    }

    [Fact]
    public void BareInteger_Two_IsTreatedAsTheRatioTwoOverOne()
    {
        var outcome = ScalaFileReader.ParsePitchToken("2");

        outcome.Success.Should().BeTrue();
        outcome.Cents.Should().BeApproximately(1200.0, 0.0001);
    }

    [Fact]
    public void SubUnityRatio_TenOverTwenty_IsLegalAndNegative()
    {
        var outcome = ScalaFileReader.ParsePitchToken("10/20");

        outcome.Success.Should().BeTrue();
        outcome.Cents.Should().BeApproximately(-1200.0, 0.0001);
    }

    [Fact]
    public void NegativeRatio_IsRejectedAsAReadError()
    {
        var outcome = ScalaFileReader.ParsePitchToken("-3/2");

        outcome.Success.Should().BeFalse();
        outcome.Reason.Should().Be(ScalaImportFailureReason.NegativeRatio);
    }

    [Fact]
    public void TrailingTextAfterCentsValue_IsIgnored()
    {
        var outcome = ScalaFileReader.ParsePitchToken("100.0 cents");

        outcome.Success.Should().BeTrue();
        outcome.Cents.Should().BeApproximately(100.0, 0.0001);
    }

    [Fact]
    public void TrailingTextAfterRatioValue_IsIgnored()
    {
        var outcome = ScalaFileReader.ParsePitchToken("5/4  E`");

        outcome.Success.Should().BeTrue();
        outcome.Cents.Should().BeApproximately(1200.0 * Math.Log2(5.0 / 4.0), 0.0001);
    }

    [Fact]
    public void InterleavedCommentLines_BetweenPitchLines_AreIgnored()
    {
        const string scl = """
            ! header comment
            A scale with comments between pitch lines
            3
            ! comment before first pitch
            200.0
            ! comment between pitches
            400.0
            ! comment before period
            2/1
            """;

        var result = ScalaFileReader.ReadFromString(scl);

        result.Success.Should().BeTrue();
        result.Scale!.DegreeCents.Should().HaveCount(3);
        result.Scale!.DegreeCents[1].Should().BeApproximately(200.0, 0.0001);
        result.Scale!.DegreeCents[2].Should().BeApproximately(400.0, 0.0001);
    }

    [Fact]
    public void Latin1Bytes_AreDecodedCorrectly()
    {
        // é (U+00E9) is a single byte (0xE9) in Latin-1 but two bytes in UTF-8 - decoding a Latin-1
        // file as UTF-8 would corrupt or mis-length this description line.
        const string scl = """
            ! café.scl
            Scale named café
            2
            700.0
            2/1
            """;

        string tempFile = Path.Combine(Path.GetTempPath(), $"scala-latin1-{Guid.NewGuid():N}.scl");
        File.WriteAllText(tempFile, scl, Encoding.Latin1);

        try
        {
            var result = ScalaFileReader.ReadFromFile(tempFile);

            result.Success.Should().BeTrue();
            result.Scale!.Name.Should().Be("Scale named café");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ---- Rule 3: the period must be ~1200 cents, whatever the file's last entry is --------

    [Fact]
    public void NonOctavePeriod_IsRejectedWithAStatedReason()
    {
        // Bohlen-Pierce-style: ends 3/1 (~1901.96 cents), not 2/1.
        const string scl = """
            ! bohlen-p-like.scl
            Bohlen-Pierce-like, period 3/1
            2
            300.0
            3/1
            """;

        var result = ScalaFileReader.ReadFromString(scl);

        result.Success.Should().BeFalse();
        result.Error!.Reason.Should().Be(ScalaImportFailureReason.NonOctavePeriod);
        result.Error!.Message.Should().Contain("1901");
    }

    // ---- Rule 4: cardinality is capped at Scale.MaxDegrees ---------------------------------

    [Fact]
    public void ThirtyOneDegreeFile_IsRejectedByCardinalityCap_WithExplanatoryMessage()
    {
        var lines = new List<string> { "! thirty-one-edo.scl", "31-EDO", "31" };
        for (int i = 1; i <= 30; i++)
        {
            lines.Add((i * 1200.0 / 31.0).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
        }
        lines.Add("2/1");

        string scl = string.Join('\n', lines);

        var result = ScalaFileReader.ReadFromString(scl);

        result.Success.Should().BeFalse();
        result.Error!.Reason.Should().Be(ScalaImportFailureReason.TooManyDegrees);
        result.Error!.Message.Should().Contain("31");
        result.Error!.Message.Should().Contain(Scale.MaxDegrees.ToString());
    }

    // ---- Structural rejections --------------------------------------------------------------

    [Fact]
    public void DeclaredCountMismatch_IsRejected()
    {
        const string scl = """
            ! mismatch.scl
            Declares 5 but only has 3
            5
            200.0
            400.0
            2/1
            """;

        var result = ScalaFileReader.ReadFromString(scl);

        result.Success.Should().BeFalse();
        result.Error!.Reason.Should().Be(ScalaImportFailureReason.DeclaredCountMismatch);
    }

    [Fact]
    public void ImportedScale_HasRealSourceAndIsNotNotatable()
    {
        const string scl = """
            ! provenance.scl
            Provenance check
            2
            700.0
            2/1
            """;

        var result = ScalaFileReader.ReadFromString(scl, "provenance.scl");

        result.Success.Should().BeTrue();
        result.Scale!.Notatable.Should().BeFalse();
        result.Scale!.Spelling.Should().BeNull();
        result.Scale!.Source.Should().NotBeNullOrWhiteSpace();
        result.Scale!.Source.Should().Contain("provenance.scl");
    }
}
