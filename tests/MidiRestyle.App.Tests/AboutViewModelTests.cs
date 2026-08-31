using MidiRestyle.App.ViewModels;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Guards the About box's two links and its version line.
/// </summary>
/// <remarks>
/// These exist because nothing else would catch a mistake in them. A mistyped URL compiles, renders
/// correctly, and looks right in a screenshot; it fails only when a user clicks it - and in the
/// donation link's case, fails by quietly sending the author's money nowhere. The version assertions
/// pin the '+' trimming, which is the one piece of real logic in the view model.
/// </remarks>
public class AboutViewModelTests
{
    [Fact]
    public void DonationUrlIsTheAuthorsPayPalPage()
    {
        // Asserted whole rather than by fragment: a typo in the handle is exactly the failure this
        // test is here to catch, and a "contains paypal.me" check would sail straight past it.
        AboutViewModel.DonationUrl.Should().Be("https://paypal.me/aaronaditama");
    }

    [Fact]
    public void LicenseUrlPointsAtTheMitLicence()
    {
        AboutViewModel.LicenseUrl.Should().Be("https://opensource.org/license/mit");
    }

    [Theory]
    [InlineData(AboutViewModel.LicenseUrl)]
    [InlineData(AboutViewModel.DonationUrl)]
    public void LinksAreAbsoluteHttpsUrls(string url)
    {
        // Both are handed to the shell with UseShellExecute, so "is this actually a web address"
        // is a safety property and not merely a tidiness one.
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed).Should().BeTrue();
        parsed!.Scheme.Should().Be(Uri.UriSchemeHttps);
    }

    [Fact]
    public void VersionLineShowsAVersionAndNoBuildMetadata()
    {
        AboutViewModel.DisplayVersion.Should().NotBeNullOrWhiteSpace();

        // InformationalVersion carries "+<commit sha>" whenever the build has repository
        // information. That is a build identifier, not something to show a user.
        AboutViewModel.DisplayVersion.Should().NotContain("+");

        new AboutViewModel().VersionLine.Should().Be($"Version {AboutViewModel.DisplayVersion}");
    }

    [Fact]
    public void VersionMatchesTheVersionTheProjectDeclares()
    {
        // Pins the csproj <Version> to what the About box will actually print, so bumping one
        // without the other cannot ship silently.
        AboutViewModel.DisplayVersion.Should().StartWith("1.3");
    }

    [Fact]
    public void DescriptionSaysWhatTheAppDoesAndSurvivesReflow()
    {
        AboutViewModel.Description.Should().Contain("re-maps");

        // The paragraph breaks must be explicit newlines and the paragraphs themselves unbroken:
        // a hard line break mid-sentence would stop the TextBlock reflowing to the window width.
        string[] paragraphs = AboutViewModel.Description.Split("\n\n");
        paragraphs.Should().HaveCount(3);
        paragraphs.Should().AllSatisfy(p => p.Should().NotContain("\n"));
    }

    [Fact]
    public void LaunchErrorStartsClearAndIsObservable()
    {
        AboutViewModel vm = new();
        vm.LaunchError.Should().BeNull();

        List<string?> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.LaunchError = "nope";

        // A message the markup binds to but that never announces itself is a message nobody sees.
        raised.Should().Contain(nameof(AboutViewModel.LaunchError));
    }
}
