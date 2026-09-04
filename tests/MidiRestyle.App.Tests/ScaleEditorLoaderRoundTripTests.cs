using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The scale editor saves through PathProbe and the loader reads through PathProbe. Under the data-root
/// override both must land on the same directory, or a saved scale silently never appears.
/// </summary>
public sealed class ScaleEditorLoaderRoundTripTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "midirestyle-roundtrip-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_scale_saved_by_the_editor_is_loaded_by_the_loader_under_the_override_root()
    {
        string beside = Path.Combine(_root, "beside");
        string appData = Path.Combine(_root, "appdata");
        string over = Path.Combine(_root, "override");
        Directory.CreateDirectory(beside);
        Directory.CreateDirectory(appData);
        var probe = new PathProbe(beside, appData, over);

        var editor = new ScaleEditorViewModel(probe);
        editor.IdSlug = "roundtrip";
        editor.Name = "Round Trip";
        editor.Tradition = "Test Tradition";
        editor.Region = "Test Region";
        editor.Source = "Hand-authored for this test, 2026";
        editor.Description = "A description that must survive the round trip.";
        editor.Degrees[0].Text = "0";
        editor.Degrees[1].Text = "5/4"; // exercise ratio entry as part of the round trip too
        editor.AddDegreeCommand.Execute(null);
        editor.Degrees[2].Text = "700";
        editor.Notatable = true;

        var saved = editor.Save();
        saved.Success.Should().BeTrue(saved.Reason);

        var loaded = new ScaleLibraryLoader(probe).Load();
        loaded.Library.Contains("user.roundtrip").Should().BeTrue();
        loaded.Library.OriginOf("user.roundtrip").Should().Be(ScaleOrigin.UserDefined);
    }
}
