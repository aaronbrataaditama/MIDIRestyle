using MidiRestyle.Core.Mapping;
using MidiRestyle.Mcp;

namespace MidiRestyle.Mcp.Tests;

public class EnumNamesTests
{
    [Theory]
    [InlineData("scale_degree", MappingStrategy.ScaleDegree)]
    [InlineData("scaleDegree", MappingStrategy.ScaleDegree)]
    [InlineData("SCALEDEGREE", MappingStrategy.ScaleDegree)]
    [InlineData("nearest_pitch", MappingStrategy.NearestPitch)]
    public void ParsesTolerantly(string text, MappingStrategy expected)
    {
        EnumNames.TryParse<MappingStrategy>(text, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Fact]
    public void RejectsUnknownAndListsValidValuesInCamelCase()
    {
        EnumNames.TryParse<RangePolicy>("wrap", out _).Should().BeFalse();
        EnumNames.Valid<RangePolicy>().Should().Equal(["shiftIntoRange", "foldOctave", "drop"]);
        EnumNames.Echo(NonScaleNotePolicy.SnapToNearestSourceDegree).Should().Be("snapToNearestSourceDegree");
    }
}
