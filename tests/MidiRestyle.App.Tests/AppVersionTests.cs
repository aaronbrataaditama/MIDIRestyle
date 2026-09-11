using MidiRestyle.App.Services;

namespace MidiRestyle.App.Tests;

public class AppVersionTests
{
    [Fact]
    public void DisplayIsTheDeclaredVersionWithoutBuildMetadata()
    {
        // Smoke test: verify the attribute is present and shaped correctly
        AppVersion.Display.Should().StartWith("1.5");
        AppVersion.Display.Should().NotContain("+");
    }

    [Fact]
    public void StripBuildMetadataRemovesPlusAndEverythingAfter()
    {
        AppVersion.StripBuildMetadata("1.5.0+abc1234").Should().Be("1.5.0");
    }

    [Fact]
    public void StripBuildMetadataReturnsUnchangedWhenNoPlus()
    {
        AppVersion.StripBuildMetadata("1.5.0").Should().Be("1.5.0");
    }

    [Fact]
    public void StripBuildMetadataKeepsOnlyThePartBeforeFirstPlus()
    {
        AppVersion.StripBuildMetadata("1.5.0+abc+def+ghi").Should().Be("1.5.0");
    }
}
