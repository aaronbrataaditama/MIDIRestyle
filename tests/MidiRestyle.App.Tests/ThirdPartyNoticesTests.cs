using System.Reflection;
using System.Text.Json;
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
    [InlineData("ModelContextProtocol")]
    [InlineData("Microsoft.Extensions")]
    public void EveryRedistributedComponentIsNamed(string component)
    {
        ThirdPartyNotices.Text.Should().Contain(component);
    }

    /// <summary>
    /// The companion to <see cref="EveryRedistributedComponentIsNamed"/>, and the half that was
    /// missing: that one is a hand-written allowlist, so it can only catch a component being
    /// REMOVED from the notices. It is structurally incapable of catching one being ADDED to the
    /// build, which is the drift CLAUDE.md warns a new package causes.
    /// </summary>
    /// <remarks>
    /// It happened. Task 19's ProjectReference to MidiRestyle.Mcp widened the shipped set by twelve
    /// assemblies - ModelContextProtocol x2 (Apache-2.0, not MIT) and Microsoft.Extensions.* x10 -
    /// and every notices test stayed green. The App's own dependency manifest is the authority here,
    /// not the package list: it names exactly the libraries that carry code. Avalonia ships as a
    /// dozen assemblies from one project under one licence, so a library also counts as named when
    /// its root package is.
    /// </remarks>
    [Fact]
    public void EveryLibraryTheAppShipsIsNamedInTheNotices()
    {
        string manifest = Path.Combine(AppContext.BaseDirectory, "MIDIRestyle.deps.json");
        File.Exists(manifest).Should().BeTrue("the App ProjectReference copies its dependency manifest beside the exe");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
        var shipped = new SortedSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty target in document.RootElement.GetProperty("targets").EnumerateObject())
        {
            foreach (JsonProperty library in target.Value.EnumerateObject())
            {
                if (library.Value.TryGetProperty("runtime", out _) || library.Value.TryGetProperty("native", out _))
                {
                    shipped.Add(library.Name.Split('/')[0]);
                }
            }
        }

        // Without this the assertion below passes by iterating nothing, which is how eight
        // cannot-fail assertions reached this branch.
        shipped.Should().HaveCountGreaterThan(20, "a manifest this small means the parse changed, not the app");

        string notices = ThirdPartyNotices.Text;
        List<string> unnamed = [.. shipped
            .Where(name => !name.StartsWith("MidiRestyle", StringComparison.Ordinal) && name != "MIDIRestyle")
            .Where(name => !IsNamed(name, notices))];

        unnamed.Should().BeEmpty(
            "every library the exe redistributes must be named in the notices - read its real licence out of the package, never from memory");
    }


    /// <summary>First segments that name no library on their own.</summary>
    /// <remarks>
    /// The notices mention both of these for unrelated reasons - the .NET runtime section and the
    /// copyright lines - so a library matching only on one of them has not been named at all.
    /// </remarks>
    private static readonly string[] NonDistinctiveRoots = ["Microsoft", "System"];

    /// <summary>Whether the notices name <paramref name="library"/>, or the package it ships under.</summary>
    /// <remarks>
    /// <para>
    /// Avalonia ships a dozen assemblies from one project under one licence, so a library counts as
    /// named when a shorter form of it is - but only down to a form that still identifies something.
    /// Matching bare first segments was the original rule and it left a hole: every
    /// <c>Microsoft.Extensions.*</c> assembly reduced to "Microsoft", which the notices contain in
    /// the .NET runtime section, so all ten passed without being named anywhere.
    /// </para>
    /// <para>
    /// Demonstrated rather than assumed: renaming every <c>Microsoft.Extensions</c> entry out of the
    /// notices left this test green, while the ten assemblies it covers went unnamed. That is the
    /// exact licence-compliance breach Task 19's review found, surviving inside the test written to
    /// prevent it.
    /// </para>
    /// </remarks>
    private static bool IsNamed(string library, string notices)
    {
        if (notices.Contains(library, StringComparison.Ordinal))
        {
            return true;
        }

        string[] parts = library.Split('.');

        for (int take = parts.Length - 1; take >= 1; take--)
        {
            string prefix = string.Join('.', parts[..take]);

            if (take == 1 && NonDistinctiveRoots.Contains(prefix, StringComparer.Ordinal))
            {
                return false;
            }

            if (notices.Contains(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The Apache-2.0 licence text is reproduced in full for the MCP SDK.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apache-2.0 section 4(a) requires a copy of the licence to travel with the redistribution, so
    /// for a single-file exe that means the text has to be embedded - the same reason the Inter font
    /// carries its OFL and the MIT components carry theirs. Those two already have tests; this one
    /// did not, which left the only Apache component on the branch as the only licence whose body
    /// nothing pinned.
    /// </para>
    /// <para>
    /// The four markers span the whole document - title, version line, the head of the terms, and
    /// the closing line - so a truncated or summarised copy fails rather than a merely mentioned
    /// one. Naming the component is <see cref="EveryRedistributedComponentIsNamed"/>'s job; this is
    /// about the licence body being present to satisfy the condition it is redistributed under.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheApacheLicenceIsReproducedForTheMcpSdk()
    {
        string text = ThirdPartyNotices.Text;

        text.Should().Contain("Apache License");
        text.Should().Contain("Version 2.0, January 2004");
        text.Should().Contain("TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION");
        text.Should().Contain("END OF TERMS AND CONDITIONS");
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
