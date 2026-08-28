using System.Xml.Linq;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Guards the portable single-file publish safety net wired into
/// <c>src/MidiRestyle.App/MidiRestyle.App.csproj</c> - see CLAUDE.md's "Commands" section and
/// phase 12 of the plan.
///
/// This is deliberately a <em>structural</em> check of the csproj's MSBuild XML, not a functional
/// one: actually running <c>dotnet publish</c> takes minutes (a full self-contained win-x64
/// restore + compile + single-file bundle) and must not sit in the `dotnet test` path, which is
/// re-run on every keystroke-scale change. What it verifies is narrower but still real: that the
/// specific MSBuild constructs the publish gate depends on have not been quietly deleted, renamed,
/// or had their targeted filenames drift from what DryWetMIDI 8.0.3 actually ships. It cannot
/// prove the publish still produces exactly one file - only an actual `dotnet publish` (see the
/// commands in CLAUDE.md) proves that.
/// </summary>
public sealed class PublishLayoutTests
{
    private static XDocument LoadAppCsproj() => XDocument.Load(AppCsprojPath());

    private static string AppCsprojPath()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MIDIRestyle.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate the repo root (MIDIRestyle.slnx) walking up from '{AppContext.BaseDirectory}'.");
        }

        return Path.Combine(dir.FullName, "src", "MidiRestyle.App", "MidiRestyle.App.csproj");
    }

    /// <summary>
    /// Elements in a plain (non-SDK-implicit) csproj live in the MSBuild XML namespace unless the
    /// file omits xmlns, which SDK-style projects do. Handle both so this test does not break if
    /// the file's style ever changes.
    /// </summary>
    private static IEnumerable<XElement> Descendants(XDocument doc, string localName) =>
        doc.Descendants().Where(e => e.Name.LocalName == localName);

    [Fact]
    public void Csproj_declares_a_single_file_publish_gate_target_that_runs_after_publish()
    {
        var doc = LoadAppCsproj();

        var gate = Descendants(doc, "Target")
            .FirstOrDefault(t => (string?)t.Attribute("Name") == "AssertSingleFilePublish");

        gate.Should().NotBeNull(
            "the publish gate must exist so a broken portable build fails loudly instead of shipping silently");
        ((string?)gate!.Attribute("AfterTargets")).Should().Be("Publish",
            "the gate must run as part of `dotnet publish` itself, not a separate step nobody remembers to run");

        var errorTask = Descendants(doc, "Error")
            .FirstOrDefault(e => gate.Descendants().Contains(e));
        errorTask.Should().NotBeNull("the gate must fail the build (an <Error>), not just warn");
    }

    [Fact]
    public void Csproj_removes_the_exact_stray_DryWetMidi_natives_by_filename()
    {
        var doc = LoadAppCsproj();

        var removalTarget = Descendants(doc, "Target")
            .FirstOrDefault(t => (string?)t.Attribute("Name") == "RemoveStrayFilesFromPublish");
        removalTarget.Should().NotBeNull();

        var condition = removalTarget!.Descendants()
            .Where(e => e.Name.LocalName is "ResolvedFileToPublish")
            .Select(e => (string?)e.Attribute("Condition"))
            .FirstOrDefault(c => c is not null);

        condition.Should().NotBeNull();
        // Matched by exact filename, not a wildcard - see the csproj's own comment for why: a
        // future DryWetMIDI release that adds a genuinely needed native (e.g. win-arm64) must not
        // match these names and so must not be silently dropped too.
        condition!.Should().Contain("Melanchall_DryWetMidi_Native32.dll");
        condition.Should().Contain("Melanchall_DryWetMidi_Native64.dylib");
        condition.Should().NotContain("*",
            "the removal must target exact filenames, never a wildcard that could catch a future " +
            "genuinely-needed native asset");
    }

    [Fact]
    public void Csproj_strips_pdb_files_from_the_portable_publish()
    {
        var doc = LoadAppCsproj();

        // DebugType=none for the App project itself, scoped to the win-x64 publish only (must not
        // affect ordinary `dotnet build`/`dotnet test`, which have no RuntimeIdentifier set).
        var debugTypeNoneScoped = Descendants(doc, "PropertyGroup")
            .Where(pg => (string?)pg.Attribute("Condition") is { } c && c.Contains("RuntimeIdentifier") && c.Contains("win-x64"))
            .SelectMany(pg => pg.Elements())
            .Any(e => e.Name.LocalName == "DebugType" && e.Value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase));

        debugTypeNoneScoped.Should().BeTrue(
            "DebugType=none must be scoped to the win-x64 publish condition, not applied globally");

        // Referenced-project symbols (MidiRestyle.Core.pdb/MidiRestyle.Playback.pdb) and the huge
        // native SkiaSharp/HarfBuzzSharp symbol files are pulled in by mechanisms DebugType=none
        // does not reach - the removal target must catch every *.pdb by extension as a backstop.
        var removalTarget = Descendants(doc, "Target")
            .First(t => (string?)t.Attribute("Name") == "RemoveStrayFilesFromPublish");
        var condition = removalTarget.Descendants()
            .Where(e => e.Name.LocalName is "ResolvedFileToPublish")
            .Select(e => (string?)e.Attribute("Condition"))
            .First(c => c is not null);

        condition!.Should().Contain(".pdb");
    }

    [Fact]
    public void Csproj_never_reintroduces_the_banned_publish_settings()
    {
        // Checked as actual MSBuild property elements, not raw text - the csproj's own comments
        // legitimately name both properties as a "do NOT use this" reminder, which a plain
        // substring check would trip over.
        var doc = LoadAppCsproj();
        var propertyNames = doc.Descendants()
            .Where(e => e.Parent is { } p && p.Name.LocalName == "PropertyGroup")
            .Select(e => e.Name.LocalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // CLAUDE.md: IncludeAllContentForSelfExtract bundles everything but repoints
        // AppContext.BaseDirectory at the extraction dir, breaking settings-beside-exe - the whole
        // portability story.
        propertyNames.Should().NotContain("IncludeAllContentForSelfExtract");

        // CLAUDE.md: Avalonia's reflection-based binding breaks under trimming.
        propertyNames.Should().NotContain("PublishTrimmed");
    }
}
