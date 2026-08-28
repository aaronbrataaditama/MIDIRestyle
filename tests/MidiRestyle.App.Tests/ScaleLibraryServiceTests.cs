using MidiRestyle.App.Services;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Every test points a <see cref="PathProbe"/> at unique temp directories, so nothing here ever
/// touches the real beside-the-exe folder or the user's actual %APPDATA%. Most tests use a small
/// synthetic <see cref="IEmbeddedScaleSource"/> so precedence/merge behaviour can be pinned down
/// exactly; <see cref="Load_assembles_the_real_embedded_assets_into_at_least_170_scales_with_no_id_collisions"/>
/// and its neighbours load the real nine JSON files straight off disk (bypassing Avalonia's
/// AssetLoader entirely - see <see cref="AvaloniaEmbeddedScaleSource"/>'s remarks for why that
/// class itself needs a live Avalonia platform and is not exercised here).
/// </summary>
public sealed class ScaleLibraryServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _besideExe;
    private readonly string _appData;

    public ScaleLibraryServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "midirestyle-scalelib-tests-" + Guid.NewGuid().ToString("N"));
        _besideExe = Path.Combine(_tempRoot, "beside-exe");
        _appData = Path.Combine(_tempRoot, "appdata");
        Directory.CreateDirectory(_besideExe);
        Directory.CreateDirectory(_appData);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private ScaleLibraryService CreateService(IEmbeddedScaleSource source) =>
        new(new PathProbe(_besideExe, _appData), source);

    // ---- test doubles -----------------------------------------------------------------

    private sealed class FakeEmbeddedScaleSource(params EmbeddedScaleAsset[] assets) : IEmbeddedScaleSource
    {
        public IReadOnlyList<EmbeddedScaleAsset> ReadAll() => assets;
    }

    /// <summary>
    /// Reads the real nine shipped scale JSON files straight off disk. Exercises the merge with real
    /// data without touching Avalonia's asset loader, which - confirmed via a throwaway console app,
    /// see <see cref="AvaloniaEmbeddedScaleSource"/> - needs a live Avalonia platform a plain xunit
    /// process does not provide.
    /// </summary>
    private sealed class FileSystemEmbeddedScaleSource(string directory) : IEmbeddedScaleSource
    {
        public IReadOnlyList<EmbeddedScaleAsset> ReadAll() =>
            [.. Directory.EnumerateFiles(directory, "*.json")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => new EmbeddedScaleAsset(Path.GetFileName(p), File.ReadAllText(p)))];
    }

    private static string RealScalesAssetsDirectory()
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

        return Path.Combine(dir.FullName, "src", "MidiRestyle.App", "Assets", "scales");
    }

    private static string ScaleJson(string id, double[]? degreeCents = null, string name = "Test scale") =>
        $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "tradition": "Test",
          "region": "Test",
          "degreeCents": [{{string.Join(", ", degreeCents ?? [0, 200, 400, 700, 900])}}],
          "notatable": true,
          "source": "Unit test fixture, ScaleLibraryServiceTests"
        }
        """;

    private static string LibraryJson(params string[] scaleEntries) =>
        $$"""
        {"schema": "midirestyle-scales-v1", "scales": [{{string.Join(",", scaleEntries)}}]}
        """;

    private void WriteUserScalesFile(string content) =>
        File.WriteAllText(Path.Combine(_besideExe, ScaleLibraryService.UserScalesFileName), content);

    // ---- real embedded assets -----------------------------------------------------------

    [Fact]
    public void Load_assembles_the_real_embedded_assets_into_at_least_170_scales_with_no_id_collisions()
    {
        var service = CreateService(new FileSystemEmbeddedScaleSource(RealScalesAssetsDirectory()));

        var result = service.Load();

        result.Library.Count.Should().BeGreaterThanOrEqualTo(170, "99 authored + 72 generated");
        result.Collisions.Should().BeEmpty();
        result.Failures.Should().BeEmpty("every shipped asset is expected to parse cleanly");
    }

    [Fact]
    public void Load_includes_all_72_melakarta()
    {
        var service = CreateService(new FileSystemEmbeddedScaleSource(RealScalesAssetsDirectory()));

        var result = service.Load();

        for (int mela = MelakartaGenerator.MinMela; mela <= MelakartaGenerator.MaxMela; mela++)
        {
            string id = MelakartaGenerator.Generate(mela).Id;
            result.Library.Contains(id).Should().BeTrue($"mela {mela} ('{id}') should be present");
        }
    }

    [Fact]
    public void Load_includes_at_least_one_scale_from_each_of_the_nine_asset_files()
    {
        var service = CreateService(new FileSystemEmbeddedScaleSource(RealScalesAssetsDirectory()));

        var result = service.Load();

        // One known id per shipped file, confirmed against the real assets.
        string[] representativeIds =
        [
            "africa.ethiopia.tizita-major",
            "americas.bluesjazz.blueshexatonic",
            "eastasia.china.gong",
            "europe.churchmodes.ionian",
            "middleeast.arabic.maqam-rast",
            "middleeast.persian.dastgah-shur",
            "southasia.hindustani.bilawal",
            "seasia.gamelan.slendro-kanyut-mesem",
            "middleeast.turkish.makam-rast",
        ];

        foreach (string id in representativeIds)
        {
            result.Library.Contains(id).Should().BeTrue($"'{id}' should have loaded from its asset file");
        }
    }

    // ---- precedence -----------------------------------------------------------------------

    [Fact]
    public void Load_lets_a_user_scale_override_a_shipped_scale_of_the_same_id_and_reports_the_collision()
    {
        var embedded = new FakeEmbeddedScaleSource(
            new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo", [0, 200, 400, 700, 900]))));
        var service = CreateService(embedded);

        // First load materialises the scales/ folder from the embedded asset.
        service.Load();

        WriteUserScalesFile(LibraryJson(ScaleJson("test.solo", [0, 300, 500, 800, 1000], name: "User's solo")));

        var result = service.Load();

        Scale? scale = result.Library.Find("test.solo");
        scale.Should().NotBeNull();
        scale!.Name.Should().Be("User's solo");
        scale.DegreeCents.Should().Equal(0, 300, 500, 800, 1000);
        result.Library.OriginOf("test.solo").Should().Be(ScaleOrigin.UserDefined);
        result.Collisions.Should().Contain(c => c.Id == "test.solo" && c.Winner == ScaleOrigin.UserDefined);
    }

    [Fact]
    public void Load_lets_a_beside_exe_scale_override_an_embedded_one_but_lose_to_a_user_scale()
    {
        var embedded = new FakeEmbeddedScaleSource(
            new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo", [0, 200, 400, 700, 900]))));
        var service = CreateService(embedded);

        // Materialise the scales/ folder, then hand-edit the copy - the on-disk file now diverges
        // from what the embedded asset would supply.
        service.Load();
        string scalesFile = Path.Combine(_besideExe, ScaleLibraryService.ScalesFolderName, "solo.json");
        File.WriteAllText(scalesFile, LibraryJson(ScaleJson("test.solo", [0, 100, 300, 600, 900], name: "Edited copy")));

        var afterEdit = service.Load();

        afterEdit.Library.Find("test.solo")!.DegreeCents.Should().Equal(0, 100, 300, 600, 900);
        afterEdit.Library.OriginOf("test.solo").Should().Be(ScaleOrigin.BesideExe);

        // Now add a user scale of the same id - it must win over the beside-exe copy.
        WriteUserScalesFile(LibraryJson(ScaleJson("test.solo", [0, 400, 700], name: "User wins")));

        var afterUser = service.Load();

        afterUser.Library.Find("test.solo")!.Name.Should().Be("User wins");
        afterUser.Library.OriginOf("test.solo").Should().Be(ScaleOrigin.UserDefined);
        afterUser.Collisions.Should().Contain(c => c.Id == "test.solo" && c.Winner == ScaleOrigin.UserDefined);
    }

    // ---- first-run materialisation ---------------------------------------------------------

    [Fact]
    public void Load_writes_the_scales_folder_on_first_run_and_leaves_an_edited_file_alone_on_the_next_run()
    {
        var embedded = new FakeEmbeddedScaleSource(
            new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo"))));
        var service = CreateService(embedded);

        var first = service.Load();

        string scalesFile = Path.Combine(_besideExe, ScaleLibraryService.ScalesFolderName, "solo.json");
        first.ScalesDirectory.Should().Be(Path.Combine(_besideExe, ScaleLibraryService.ScalesFolderName));
        first.ScalesDirectoryIsBesideExe.Should().BeTrue();
        File.Exists(scalesFile).Should().BeTrue("first run must materialise the embedded asset");

        string editedContent = LibraryJson(ScaleJson("test.solo", name: "Hand edited"));
        File.WriteAllText(scalesFile, editedContent);

        service.Load();

        File.ReadAllText(scalesFile).Should().Be(editedContent, "a second run must not overwrite an edited file");
    }

    // ---- unwritable beside-exe --------------------------------------------------------------

    [Fact]
    public void Load_falls_back_to_appdata_and_states_the_reason_when_beside_the_exe_is_unwritable()
    {
        Directory.Delete(_besideExe);
        File.WriteAllText(_besideExe, "blocking file");

        var embedded = new FakeEmbeddedScaleSource(
            new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo"))));
        var service = CreateService(embedded);

        var result = service.Load();

        result.ScalesDirectoryIsBesideExe.Should().BeFalse();
        result.ScalesDirectory.Should().StartWith(_appData);
        result.Reason.Should().ContainEquivalentOf("APPDATA");
        result.Library.Contains("test.solo").Should().BeTrue();
        result.Library.Count.Should().BeGreaterThanOrEqualTo(73, "72 melakarta plus the one fake embedded scale");
    }

    // ---- malformed user data must not throw or lose everything else -------------------------

    [Fact]
    public void Load_reports_a_stated_reason_and_still_loads_everything_else_when_user_scales_json_is_malformed()
    {
        var embedded = new FakeEmbeddedScaleSource(
            new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo"))));
        var service = CreateService(embedded);
        service.Load(); // materialise the scales/ folder first

        WriteUserScalesFile("{ this is not valid json ");

        var act = () => service.Load();

        var result = act.Should().NotThrow().Subject;
        result.Failures.Should().Contain(f =>
            f.Id.Contains(ScaleLibraryService.UserScalesFileName) && !string.IsNullOrWhiteSpace(f.Reason));
        result.Library.Contains("test.solo").Should().BeTrue();
        result.Library.Count.Should().BeGreaterThanOrEqualTo(73);
    }

    [Fact]
    public void Load_reports_a_single_invalid_scale_by_id_while_its_siblings_in_the_same_file_still_load()
    {
        var service = CreateService(new FakeEmbeddedScaleSource());

        string goodOne = ScaleJson("user.good-one");
        string goodTwo = ScaleJson("user.good-two", [0, 300, 700]);
        // A scale whose degrees do not start at 0 - invalid per Scale's own validation.
        string bad = ScaleJson("user.bad", [100, 300, 700]);
        WriteUserScalesFile(LibraryJson(goodOne, goodTwo, bad));

        var result = service.Load();

        result.Library.Contains("user.good-one").Should().BeTrue();
        result.Library.Contains("user.good-two").Should().BeTrue();
        result.Library.Contains("user.bad").Should().BeFalse();
        result.Failures.Should().Contain(f => f.Id.Contains("user.bad"));
    }
}
