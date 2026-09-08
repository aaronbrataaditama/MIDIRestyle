using MidiRestyle.Core.Scales;
using MidiRestyle.Mcp;

namespace MidiRestyle.Mcp.Tests;

/// <summary>
/// Pins the error text ScaleLookup produces, because Task 18's contract golden freezes these
/// messages and an agent's only route out of a bad scale id is what they say.
/// </summary>
public sealed class ScaleLookupTests
{
    private readonly ScaleLibrary _library = TestLibrary.Load();

    [Fact]
    public void AnExactIdIsFound()
    {
        ScaleLookup.TryFind(_library, "middleeast.arabic.maqam-rast", "targetScaleId", out Scale? scale, out string? error)
            .Should().BeTrue(error);

        scale!.Id.Should().Be("middleeast.arabic.maqam-rast");
        error.Should().BeNull();
    }

    [Fact]
    public void ATypoSuggestsNearMatchesAndNeverFallsBackToAScale()
    {
        ScaleLookup.TryFind(_library, "rast", "targetScaleId", out Scale? scale, out string? error).Should().BeFalse();

        scale.Should().BeNull("a miss must never resolve to a default scale");
        error.Should().Contain("targetScaleId").And.Contain("middleeast.arabic.maqam-rast");
    }

    [Fact]
    public void AnIdMatchingNothingSaysSoWithoutSuggestions()
    {
        ScaleLookup.TryFind(_library, "zzzzz-nonsense", "targetScaleId", out Scale? scale, out string? error).Should().BeFalse();

        scale.Should().BeNull();
        error.Should().Contain("zzzzz-nonsense").And.Contain("list_scales");
        error.Should().NotContain("Did you mean");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankIdIsCalledMissingRatherThanAnsweredWithArbitraryScales(string id)
    {
        // ScaleLibrary.Search returns the whole library for a blank query, so before the fix this
        // reported the library's first three entries as "Did you mean" — suggestions near nothing.
        ScaleLookup.TryFind(_library, id, "targetScaleId", out Scale? scale, out string? error).Should().BeFalse();

        scale.Should().BeNull();
        error.Should().Contain("targetScaleId").And.Contain("required");
        error.Should().NotContain("Did you mean");
    }
}
