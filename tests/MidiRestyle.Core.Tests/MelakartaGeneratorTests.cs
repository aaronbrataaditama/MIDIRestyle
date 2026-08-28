using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// Regression guard for the melakarta generator's loop order. Getting the nesting wrong (e.g.
/// Ri-Ga outermost instead of Ma) still produces 72 distinct scales, so it looks fine at a glance -
/// it misaligns every one of the 72 canonical names against the wrong cents instead. The exact-cents
/// spot-checks below are what actually catches that.
/// </summary>
public class MelakartaGeneratorTests
{
    [Fact]
    public void GenerateAll_ReturnsExactly72Scales()
    {
        IReadOnlyList<Scale> scales = MelakartaGenerator.GenerateAll();

        scales.Should().HaveCount(72);
    }

    [Fact]
    public void GenerateAll_AllHaveDistinctPitchClassSets()
    {
        IReadOnlyList<Scale> scales = MelakartaGenerator.GenerateAll();

        var pitchClassSets = scales
            .Select(s => string.Join(",", s.DegreeCents))
            .ToList();

        pitchClassSets.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GenerateAll_EveryScaleContainsSaAndPaAndHasSevenDegrees()
    {
        IReadOnlyList<Scale> scales = MelakartaGenerator.GenerateAll();

        foreach (Scale scale in scales)
        {
            scale.DegreeCount.Should().Be(7);
            scale.DegreeCents.Should().Contain(0.0);
            scale.DegreeCents.Should().Contain(700.0);
        }
    }

    [Fact]
    public void GenerateAll_Melas1To36AllContain500CentMa1()
    {
        for (int mela = 1; mela <= 36; mela++)
        {
            Scale scale = MelakartaGenerator.Generate(mela);
            scale.DegreeCents.Should().Contain(500.0, $"mela {mela} should use Ma1");
        }
    }

    [Fact]
    public void GenerateAll_Melas37To72AllContain600CentMa2()
    {
        for (int mela = 37; mela <= 72; mela++)
        {
            Scale scale = MelakartaGenerator.Generate(mela);
            scale.DegreeCents.Should().Contain(600.0, $"mela {mela} should use Ma2");
        }
    }

    [Fact]
    public void Generate_Mela1Kanakangi_HasExpectedCents()
    {
        Scale scale = MelakartaGenerator.Generate(1);

        scale.DegreeCents.Should().Equal(0, 100, 200, 500, 700, 800, 900);
    }

    [Fact]
    public void Generate_Mela15Mayamalavagowla_HasExpectedCents()
    {
        Scale scale = MelakartaGenerator.Generate(15);

        scale.DegreeCents.Should().Equal(0, 100, 400, 500, 700, 800, 1100);
    }

    [Fact]
    public void Generate_Mela29Dheerasankarabharanam_IsTheMajorScale()
    {
        Scale scale = MelakartaGenerator.Generate(29);

        scale.DegreeCents.Should().Equal(0, 200, 400, 500, 700, 900, 1100);
    }

    [Fact]
    public void Generate_Mela65Mechakalyani_IsLydian()
    {
        Scale scale = MelakartaGenerator.Generate(65);

        scale.DegreeCents.Should().Equal(0, 200, 400, 600, 700, 900, 1100);
    }

    [Theory]
    [InlineData(1, "Kanakangi")]
    [InlineData(15, "Mayamalavagowla")]
    [InlineData(29, "Dheerasankarabharanam")]
    [InlineData(65, "Mechakalyani")]
    [InlineData(56, "Chamaram")]
    public void Generate_NamesMatchCanonicalList(int mela, string expectedName)
    {
        Scale scale = MelakartaGenerator.Generate(mela);

        scale.Name.Should().Contain(expectedName);
        MelakartaGenerator.CanonicalNames[mela - 1].Should().Be(expectedName);
    }

    [Fact]
    public void ChakraNameFor_Melas31To36_IsRutu()
    {
        for (int mela = 31; mela <= 36; mela++)
        {
            MelakartaGenerator.ChakraNameFor(mela).Should().Be("Rutu");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(73)]
    public void Generate_OutOfRange_Throws(int melaNumber)
    {
        Action act = () => MelakartaGenerator.Generate(melaNumber);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(73)]
    public void ChakraNameFor_OutOfRange_Throws(int melaNumber)
    {
        Action act = () => MelakartaGenerator.ChakraNameFor(melaNumber);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Generate_SetsExpectedScaleMetadata()
    {
        Scale scale = MelakartaGenerator.Generate(15);

        scale.Tradition.Should().Be("Carnatic");
        scale.Region.Should().Be("South Asia");
        scale.Notatable.Should().BeTrue();
        scale.Spelling.Should().BeNull();
        scale.Id.Should().Be("southasia.carnatic.melakarta.15-mayamalavagowla");
        scale.Source.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateAll_AllIdsAreUnique()
    {
        IReadOnlyList<Scale> scales = MelakartaGenerator.GenerateAll();

        scales.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }
}
