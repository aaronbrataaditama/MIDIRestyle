using MidiRestyle.App.Services;

namespace MidiRestyle.App.Tests;

public class AppVersionTests
{
    [Fact]
    public void DisplayIsTheDeclaredVersionWithoutBuildMetadata()
    {
        AppVersion.Display.Should().StartWith("1.5");
        AppVersion.Display.Should().NotContain("+");
    }
}
