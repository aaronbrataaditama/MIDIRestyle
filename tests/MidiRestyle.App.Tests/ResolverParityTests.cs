using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Scales;
using MidiRestyle.Mcp;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The GUI's <see cref="StylePanelViewModel.BuildSettings"/> and the MCP
/// <see cref="RestyleRequestResolver"/> are two builders of one <see cref="RestyleSettings"/>.
/// </summary>
/// <remarks>
/// <para>
/// Given the same file, scale and detection they must agree on every field the engine reads. They
/// are separate code paths by necessity - one reads a view model the user has been clicking on, the
/// other a JSON request - so nothing but a test holds them together, and a divergence would not
/// show up as a crash. It would show up as an agent quietly producing a different file from the one
/// the user hears when they press Play, which is the single failure this whole MCP surface has to
/// not have.
/// </para>
/// <para>
/// Asserted field by field rather than by comparing the two <c>RestyleSettings</c> whole: a record
/// equality check would pass or fail as one opaque boolean, and <see cref="RestyleSettings"/> is
/// not a record anyway. Field by field, a failure names the field that drifted.
/// </para>
/// </remarks>
public sealed class ResolverParityTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "midirestyle-parity-" + Guid.NewGuid().ToString("N"));

    public ResolverParityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file left open by a failed assertion must not turn one red test into two.
        }
    }

    /// <summary>
    /// An empty request - nothing but a file and a target scale - must equal the panel as it stands
    /// after the user accepts the detected key, which is the GUI's own default path.
    /// </summary>
    /// <param name="tolerance">
    /// Null exercises the defaulted path on both sides; 0.5 proves the supplied value is carried
    /// rather than the default being echoed back, which would pass a null-only test.
    /// </param>
    [Theory]
    [InlineData(null)]
    [InlineData(0.5)]
    public void AnEmptyRequestEqualsThePanelAfterApplyDetectedKey(double? tolerance)
    {
        PathProbe probe = new(
            Path.Combine(_root, "beside"), Path.Combine(_root, "appdata"), Path.Combine(_root, "data"));
        ScaleLibrary library = new ScaleLibraryLoader(probe).Load().Library;
        string input = MidiFixtures.Write(Path.Combine(_root, "major.mid"), MidiFixtures.CMajorNotes);

        Scale rast = library.Find("middleeast.arabic.maqam-rast")
            ?? throw new InvalidOperationException("the Rast fixture scale is missing from the library");

        // GUI side: exactly what the panel holds once the user accepts the suggested key.
        MidiProject project = MidiFileLoader.Load(input);
        StylePanelViewModel panel = new(library) { SelectedScale = rast };
        panel.ApplyDetectedKey(KeyDetector.Detect(project));

        if (tolerance is { } t)
        {
            panel.ToleranceCents = t;
        }

        RestyleSettings gui = panel.BuildSettings();

        // MCP side: the same two choices and nothing else.
        RestyleRequest request = new(
            input, rast.Id, null, null, null, null, null, null, null, null, tolerance, null, false);

        new RestyleRequestResolver(library)
            .TryResolve(request, out Resolution? resolution, out string? error)
            .Should().BeTrue(error);

        RestyleSettings mcp = resolution!.Settings;

        // Scales compared by id: the library hands out the same instances here, so reference
        // equality would pass without proving the resolver looked anything up.
        mcp.TargetScale.Id.Should().Be(gui.TargetScale.Id);
        mcp.TargetTonic.Should().Be(gui.TargetTonic);
        mcp.TonicSpelling.Should().Be(gui.TonicSpelling);
        mcp.SourceScale!.Id.Should().Be(gui.SourceScale!.Id);
        mcp.SourceTonic.Should().Be(gui.SourceTonic);
        mcp.Mapping.Should().Be(gui.Mapping);
        mcp.ToleranceCents.Should().Be(gui.ToleranceCents);
        mcp.Excluded.Should().BeEquivalentTo(gui.Excluded);

        // The ninth field, which the brief's list omitted. RestyleRequest carries no output mode, so
        // the two agree here only because neither side is asked to change it - parity by shared
        // default, not by construction. Pinned anyway: if either default moves, the MCP path would
        // start writing files in a mode the user never chose, and nothing else would notice.
        mcp.OutputMode.Should().Be(gui.OutputMode);
    }
}
