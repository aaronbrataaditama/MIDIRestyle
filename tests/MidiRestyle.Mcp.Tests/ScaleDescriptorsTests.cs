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
        ScaleSummary summary = ScaleDescriptors.Summarise(TestLibrary.Rast, ScaleOrigin.Embedded);
        summary.Id.Should().Be("middleeast.arabic.maqam-rast");
        summary.DegreeCount.Should().Be(7);
        summary.MaxDeviationCents.Should().Be(50);
        summary.FidelityBadge.Should().Be("approximate");

        ScaleDetail detail = ScaleDescriptors.Describe(TestLibrary.Rast, ScaleOrigin.Embedded);
        detail.Source.Should().NotBeNullOrWhiteSpace();
        detail.DegreeCents.Should().HaveCount(7);
        detail.DegreeOffsets.Should().OnlyContain(o => o == Math.Round(o, 2));
        detail.BendClustersAtDefaultTolerance.Should().Be(2, "CLAUDE.md: Rast is two clusters at 5 cents");
        detail.Origin.Should().Be("embedded");
        detail.SpellingOnC.Should().NotBeNull();
    }
}
