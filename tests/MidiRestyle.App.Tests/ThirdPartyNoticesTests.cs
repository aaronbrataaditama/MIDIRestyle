using System.Reflection;
using MidiRestyle.App.Services;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Guards the licence notices the shipped executable is obliged to carry.
/// </summary>
/// <remarks>
/// These are compliance tests, not cosmetic ones. The single-file exe redistributes its
/// dependencies and the .NET runtime, and at least one of them - the Inter typeface, under the SIL
/// Open Font License - *requires* its licence to travel with the binary. A notices file that fails
/// to embed, or that quietly loses a section, breaches that while the application still builds,
/// runs and looks perfectly correct. Nothing else in the suite would notice.
/// </remarks>
public class ThirdPartyNoticesTests
{
    [Fact]
    public void TheNoticesAreEmbeddedInTheAssembly()
    {
        // The loader degrades to an explanatory message rather than throwing, so "did it load"
        // cannot be asked by catching. Ask the assembly directly instead.
        Assembly assembly = typeof(ThirdPartyNotices).Assembly;

        assembly.GetManifestResourceNames()
            .Should().Contain(ThirdPartyNotices.ResourceName,
                "the csproj embeds the notices under this exact LogicalName");
    }

    [Fact]
    public void TheLoadedTextIsTheRealDocumentAndNotTheFallback()
    {
        ThirdPartyNotices.Text.Should().NotContain("This is a packaging fault",
            "that string is the missing-resource fallback, so seeing it means the embed broke");

        ThirdPartyNotices.Text.Length.Should().BeGreaterThan(100_000,
            "the notices carry the full licence texts, not a summary of them");
    }

    /// <summary>
    /// The Inter faces are the reason this file has to exist at all.
    /// </summary>
    /// <remarks>
    /// Avalonia.Fonts.Inter declares MIT in its NuGet metadata, which covers Avalonia's code and
    /// not the font, and it ships no font licence of its own - so this is the one notice that
    /// cannot be recovered from the packages if it is ever dropped. The copyright line asserted
    /// here was read out of the font binaries' own name table.
    /// </remarks>
    [Fact]
    public void TheInterFontCarriesItsOpenFontLicenceInFull()
    {
        string text = ThirdPartyNotices.Text;

        text.Should().Contain("Copyright 2020 The Inter Project Authors");
        text.Should().Contain("SIL OPEN FONT LICENSE Version 1.1");

        // The whole licence, not just its title: all five numbered conditions and the closing
        // disclaimer. A truncated OFL satisfies nothing.
        text.Should().Contain("PREAMBLE");
        text.Should().Contain("PERMISSION & CONDITIONS");
        text.Should().Contain("TERMINATION");
        text.Should().Contain("OTHER DEALINGS IN THE FONT SOFTWARE.");
    }

    [Fact]
    public void TheMitTextIsReproducedForTheComponentsUnderIt()
    {
        ThirdPartyNotices.Text.Should().Contain("Permission is hereby granted, free of charge");
        ThirdPartyNotices.Text.Should().Contain("THE SOFTWARE IS PROVIDED \"AS IS\"");
    }

    /// <summary>
    /// Every component the win-x64 single-file publish actually redistributes must be named.
    /// </summary>
    /// <remarks>
    /// This is the test that catches drift. Adding a package to the App is a one-line change that
    /// silently widens what the exe redistributes, and nothing about the build would object. The
    /// list below was taken from a real publish - <c>-p:PublishSingleFile=false</c> into a scratch
    /// folder, then reading what landed there - rather than from the package list, because most of
    /// the transitive graph (the Linux, macOS and WebAssembly native asset packages) resolves but
    /// never ships on Windows.
    /// </remarks>
    [Theory]
    [InlineData("Avalonia")]
    [InlineData("CommunityToolkit.Mvvm")]
    [InlineData("Melanchall.DryWetMidi")]
    [InlineData("MicroCom.Runtime")]
    [InlineData("SkiaSharp")]
    [InlineData("HarfBuzzSharp")]
    [InlineData("Tmds.DBus.Protocol")]
    [InlineData("ANGLE")]
    [InlineData(".NET runtime")]
    public void EveryRedistributedComponentIsNamed(string component)
    {
        ThirdPartyNotices.Text.Should().Contain(component);
    }

    /// <summary>
    /// The native binaries statically incorporate a further ~20 projects, each with its own notice.
    /// </summary>
    /// <remarks>
    /// Spot-checked rather than enumerated: these four are carried inside libSkiaSharp.dll and
    /// libHarfBuzzSharp.dll, so their absence would mean the SkiaSharp notices had been summarised
    /// instead of reproduced - the exact shortcut this file must not take.
    /// </remarks>
    [Theory]
    [InlineData("freetype")]
    [InlineData("libpng")]
    [InlineData("libjpeg-turbo")]
    [InlineData("libwebp")]
    public void NoticesCarriedInsideTheNativeBinariesSurvive(string project)
    {
        ThirdPartyNotices.Text.Should().Contain(project);
    }

    [Fact]
    public void TheNoticesDoNotClaimToReplaceTheProjectsOwnLicence()
    {
        // A notices file that reads as though it licences MIDIRestyle itself is worse than none:
        // it muddies the one thing the LICENSE file says unambiguously.
        ThirdPartyNotices.Text.Should().Contain("MIDIRestyle itself is released under the MIT License");
    }
}
