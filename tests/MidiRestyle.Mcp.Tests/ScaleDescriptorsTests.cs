using MidiRestyle.Core.Scales;
using MidiRestyle.Mcp;

namespace MidiRestyle.Mcp.Tests;

public class ScaleDescriptorsTests
{
    private static Scale Make(string id, double[] cents, bool notatable = true) =>
        new(id, id, "Test", "Test", cents, "Descriptor test fixture", notatable);

    [Fact]
    public void ThirdIsReadFromExplicitBandsNotNearestDegree()
    {
        ScaleDescriptors.Third(TestLibrary.Rast).Should().Be("neutral");     // 350
        ScaleDescriptors.Third(TestLibrary.Slendro).Should().Be("none");     // ~240 / ~480
        ScaleDescriptors.Third(TestLibrary.Ionian).Should().Be("major");     // 400
        ScaleDescriptors.Third(TestLibrary.Aeolian).Should().Be("minor");    // 300
        ScaleDescriptors.Third(Make("t.edge", [0, 280, 700])).Should().Be("minor");
        ScaleDescriptors.Third(Make("t.edge2", [0, 421, 700])).Should().Be("none");
    }

    [Fact]
    public void ThirdBandBoundariesAreExact()
    {
        // minor 280-330 (inclusive both ends), neutral (330, 370] (exclusive/inclusive), major (370, 420].
        ScaleDescriptors.Third(Make("t.b330", [0, 330, 700])).Should().Be("minor", "330 is minor's own upper edge");
        ScaleDescriptors.Third(Make("t.b331", [0, 331, 700])).Should().Be("neutral", "331 has crossed into neutral");
        ScaleDescriptors.Third(Make("t.b370", [0, 370, 700])).Should().Be("neutral", "370 is neutral's own upper edge");
        ScaleDescriptors.Third(Make("t.b371", [0, 371, 700])).Should().Be("major", "371 has crossed into major");
        ScaleDescriptors.Third(Make("t.b420", [0, 420, 700])).Should().Be("major", "420 is major's own upper edge");
    }

    [Fact]
    public void NotatableMeansTheSpellerSucceedsNotJustTheFlag()
    {
        ScaleDescriptors.IsNotatable(TestLibrary.Slendro, out string? reason).Should().BeFalse();
        reason.Should().BeNull("an authored false needs no speller diagnostic");

        var eightDegrees = Make("t.eight", [0, 150, 300, 400, 500, 700, 800, 1000]);
        eightDegrees.Notatable.Should().BeTrue("flag says yes");
        ScaleDescriptors.IsNotatable(eightDegrees, out reason).Should().BeFalse("the speller says no");
        reason.Should().NotBeNullOrWhiteSpace();

        ScaleDescriptors.IsNotatable(TestLibrary.Rast, out reason).Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void FitsTwelveTetUsesCoresPredicate()
    {
        ScaleDescriptors.FitsTwelveTet(TestLibrary.Ionian).Should().BeTrue();
        ScaleDescriptors.FitsTwelveTet(TestLibrary.Rast).Should().BeFalse();
    }

    [Fact]
    public void SummaryAndDetailCarryTheComputedFieldsWithCentsRoundedToTwoDecimals()
    {
        // Every field pinned by name, against values read off the catalog and hand-derived from
        // DiatonicSpeller's rules - not just spot-checked - so a swapped constructor argument
        // (e.g. Notatable/FitsTwelveTet trading places) fails here instead of compiling silently.
        ScaleSummary summary = ScaleDescriptors.Summarise(TestLibrary.Rast, ScaleOrigin.Embedded);
        summary.Id.Should().Be("middleeast.arabic.maqam-rast");
        summary.Name.Should().Be("Maqam Rast (notated, 24-quarter-tone)");
        summary.Tradition.Should().Be("Arabic maqam");
        summary.Region.Should().Be("Middle East");
        summary.DegreeCount.Should().Be(7);
        summary.Notatable.Should().BeTrue("Rast is authored notatable and the speller succeeds on it");
        summary.FitsTwelveTet.Should().BeFalse("its neutral third sits 50 cents from any 12-TET degree");
        summary.MaxDeviationCents.Should().Be(50);
        summary.FidelityBadge.Should().Be("approximate");
        summary.Third.Should().Be("neutral");

        ScaleDetail detail = ScaleDescriptors.Describe(TestLibrary.Rast, ScaleOrigin.Embedded);
        detail.Summary.Should().Be(summary);
        detail.Source.Should().NotBeNullOrWhiteSpace();
        detail.DegreeCents.Should().HaveCount(7);
        detail.DegreeOffsets.Should().OnlyContain(o => o == Math.Round(o, 2));
        detail.NotatableReason.Should().BeNull();
        detail.Fidelity.Badge.Should().Be("approximate");
        detail.Fidelity.MaxDeviationCents.Should().Be(50);
        detail.Fidelity.WorstDegreeIndex.Should().Be(2, "degree 2 (350c) is the first to hit the 50-cent worst deviation");
        detail.BendClustersAtDefaultTolerance.Should().Be(2, "CLAUDE.md: Rast is two clusters at 5 cents");
        detail.Origin.Should().Be("embedded");
        // C D E-half-flat F G A B-half-flat, as the scale's own source field states.
        detail.SpellingOnC.Should().Equal(["C", "D", "E½♭", "F", "G", "A", "B½♭"]);
    }

    [Fact]
    public void DescribeReportsWhyNotatabilityFailedAndOmitsASpelling()
    {
        var eightDegrees = Make("t.eight", [0, 150, 300, 400, 500, 700, 800, 1000]);

        ScaleDetail detail = ScaleDescriptors.Describe(eightDegrees, ScaleOrigin.UserDefined);

        detail.Summary.Notatable.Should().BeFalse("the speller rejects >7 degrees even though the flag says yes");
        detail.NotatableReason.Should().NotBeNullOrWhiteSpace();
        detail.SpellingOnC.Should().BeNull();
    }

    [Fact]
    public void DescribeReportsUnknownOriginWhenNoneIsSupplied()
    {
        ScaleDescriptors.Describe(TestLibrary.Ionian, null).Origin.Should().Be("unknown");
    }
}
