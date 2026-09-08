using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// Every test points a <see cref="PathProbe"/> at unique temp directories, so nothing here ever
/// touches the real beside-the-exe folder or the user's actual %APPDATA%. Most tests use a small
/// synthetic list of <see cref="EmbeddedScaleAsset"/> so precedence/merge behaviour can be pinned
/// down exactly; <see cref="Load_assembles_the_real_embedded_assets_into_at_least_170_scales_with_no_id_collisions"/>
/// and its neighbours load the real embedded assets via <see cref="EmbeddedScaleAssets.ReadAll"/>.
/// </summary>
public sealed class ScaleLibraryLoaderTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _besideExe;
    private readonly string _appData;

    public ScaleLibraryLoaderTests()
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

    private ScaleLibraryLoader CreateLoader(params EmbeddedScaleAsset[] embedded) =>
        new(new PathProbe(_besideExe, _appData), embedded);

    private ScaleLibraryLoader CreateLoaderWithRealAssets() =>
        new(new PathProbe(_besideExe, _appData));

    // ---- test fixtures -----------------------------------------------------------------

    private static string ScaleJson(string id, double[]? degreeCents = null, string name = "Test scale") =>
        $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "tradition": "Test",
          "region": "Test",
          "degreeCents": [{{string.Join(", ", degreeCents ?? [0, 200, 400, 700, 900])}}],
          "notatable": true,
          "source": "Unit test fixture, ScaleLibraryLoaderTests"
        }
        """;

    private static string LibraryJson(params string[] scaleEntries) =>
        $$"""
        {"schema": "midirestyle-scales-v1", "scales": [{{string.Join(",", scaleEntries)}}]}
        """;

    private void WriteUserScalesFile(string content) =>
        File.WriteAllText(Path.Combine(_besideExe, ScaleLibraryLoader.UserScalesFileName), content);

    // ---- real embedded assets -----------------------------------------------------------

    [Fact]
    public void Load_assembles_the_real_embedded_assets_into_at_least_170_scales_with_no_id_collisions()
    {
        var loader = CreateLoaderWithRealAssets();

        var result = loader.Load();

        result.Library.Count.Should().Be(171, "99 authored + 72 generated");
        result.Collisions.Should().BeEmpty();
        result.Failures.Should().BeEmpty("every shipped asset is expected to parse cleanly");
    }

    [Fact]
    public void Load_includes_all_72_melakarta()
    {
        var loader = CreateLoaderWithRealAssets();

        var result = loader.Load();

        for (int mela = MelakartaGenerator.MinMela; mela <= MelakartaGenerator.MaxMela; mela++)
        {
            string id = MelakartaGenerator.Generate(mela).Id;
            result.Library.Contains(id).Should().BeTrue($"mela {mela} ('{id}') should be present");
        }
    }

    [Fact]
    public void Load_includes_at_least_one_scale_from_each_of_the_nine_asset_files()
    {
        var loader = CreateLoaderWithRealAssets();

        var result = loader.Load();

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
        var embedded = new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo", [0, 200, 400, 700, 900])));
        var loader = CreateLoader(embedded);

        // First load materialises the scales/ folder from the embedded asset.
        loader.Load();

        WriteUserScalesFile(LibraryJson(ScaleJson("test.solo", [0, 300, 500, 800, 1000], name: "User's solo")));

        var result = loader.Load();

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
        var embedded = new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo", [0, 200, 400, 700, 900])));
        var loader = CreateLoader(embedded);

        // Materialise the scales/ folder, then hand-edit the copy - the on-disk file now diverges
        // from what the embedded asset would supply.
        loader.Load();
        string scalesFile = Path.Combine(_besideExe, ScaleLibraryLoader.ScalesFolderName, "solo.json");
        File.WriteAllText(scalesFile, LibraryJson(ScaleJson("test.solo", [0, 100, 300, 600, 900], name: "Edited copy")));

        var afterEdit = loader.Load();

        afterEdit.Library.Find("test.solo")!.DegreeCents.Should().Equal(0, 100, 300, 600, 900);
        afterEdit.Library.OriginOf("test.solo").Should().Be(ScaleOrigin.BesideExe);

        // Now add a user scale of the same id - it must win over the beside-exe copy.
        WriteUserScalesFile(LibraryJson(ScaleJson("test.solo", [0, 400, 700], name: "User wins")));

        var afterUser = loader.Load();

        afterUser.Library.Find("test.solo")!.Name.Should().Be("User wins");
        afterUser.Library.OriginOf("test.solo").Should().Be(ScaleOrigin.UserDefined);
        afterUser.Collisions.Should().Contain(c => c.Id == "test.solo" && c.Winner == ScaleOrigin.UserDefined);
    }

    // ---- first-run materialisation ---------------------------------------------------------

    [Fact]
    public void Load_writes_the_scales_folder_on_first_run_and_leaves_an_edited_file_alone_on_the_next_run()
    {
        var embedded = new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo")));
        var loader = CreateLoader(embedded);

        var first = loader.Load();

        string scalesFile = Path.Combine(_besideExe, ScaleLibraryLoader.ScalesFolderName, "solo.json");
        first.ScalesDirectory.Should().Be(Path.Combine(_besideExe, ScaleLibraryLoader.ScalesFolderName));
        first.ScalesDirectoryIsBesideExe.Should().BeTrue();
        File.Exists(scalesFile).Should().BeTrue("first run must materialise the embedded asset");

        string editedContent = LibraryJson(ScaleJson("test.solo", name: "Hand edited"));
        File.WriteAllText(scalesFile, editedContent);

        loader.Load();

        File.ReadAllText(scalesFile).Should().Be(editedContent, "a second run must not overwrite an edited file");
    }

    // ---- unwritable beside-exe --------------------------------------------------------------

    [Fact]
    public void Load_falls_back_to_appdata_and_states_the_reason_when_beside_the_exe_is_unwritable()
    {
        Directory.Delete(_besideExe);
        File.WriteAllText(_besideExe, "blocking file");

        var embedded = new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo")));
        var loader = CreateLoader(embedded);

        var result = loader.Load();

        result.ScalesDirectoryIsBesideExe.Should().BeFalse();
        result.ScalesDirectory.Should().StartWith(_appData);
        result.Reason.Should().ContainEquivalentOf("APPDATA");
        result.Library.Contains("test.solo").Should().BeTrue();
        result.Library.Count.Should().BeGreaterThanOrEqualTo(73, "72 melakarta plus the one fake embedded scale");
    }

    [Fact]
    public void Load_never_throws_and_still_yields_embedded_plus_generated_when_no_root_is_writable()
    {
        Directory.Delete(_besideExe); File.WriteAllText(_besideExe, "blocker");
        Directory.Delete(_appData); File.WriteAllText(_appData, "blocker");

        var result = CreateLoaderWithRealAssets().Load();

        result.Library.Count.Should().Be(171);
        result.Reason.Should().Contain("not writable");
    }

    // ---- malformed user data must not throw or lose everything else -------------------------

    [Fact]
    public void Load_reports_a_stated_reason_and_still_loads_everything_else_when_user_scales_json_is_malformed()
    {
        var embedded = new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo")));
        var loader = CreateLoader(embedded);
        loader.Load(); // materialise the scales/ folder first

        WriteUserScalesFile("{ this is not valid json ");

        var act = () => loader.Load();

        var result = act.Should().NotThrow().Subject;
        result.Failures.Should().Contain(f =>
            f.Id.Contains(ScaleLibraryLoader.UserScalesFileName) && !string.IsNullOrWhiteSpace(f.Reason));
        result.Library.Contains("test.solo").Should().BeTrue();
        result.Library.Count.Should().BeGreaterThanOrEqualTo(73);
    }

    [Fact]
    public void Load_reports_a_single_invalid_scale_by_id_while_its_siblings_in_the_same_file_still_load()
    {
        var loader = CreateLoader();

        string goodOne = ScaleJson("user.good-one");
        string goodTwo = ScaleJson("user.good-two", [0, 300, 700]);
        // A scale whose degrees do not start at 0 - invalid per Scale's own validation.
        string bad = ScaleJson("user.bad", [100, 300, 700]);
        WriteUserScalesFile(LibraryJson(goodOne, goodTwo, bad));

        var result = loader.Load();

        result.Library.Contains("user.good-one").Should().BeTrue();
        result.Library.Contains("user.good-two").Should().BeTrue();
        result.Library.Contains("user.bad").Should().BeFalse();
        result.Failures.Should().Contain(f => f.Id.Contains("user.bad"));
    }

    // ---- robustness under concurrency and total unwritability -------------------------------

    [Fact]
    public void Concurrent_first_runs_produce_complete_files_and_no_failures()
    {
        var asset = new EmbeddedScaleAsset("solo.json", LibraryJson(ScaleJson("test.solo")));
        var results = new ScaleLibraryLoadResult[8];

        Parallel.For(0, results.Length, i => results[i] = CreateLoader(asset).Load());

        results.Should().AllSatisfy(r => r.Failures.Should().BeEmpty());
        Directory.EnumerateFiles(Path.Combine(_besideExe, ScaleLibraryLoader.ScalesFolderName))
            .Should().ContainSingle(p => p.EndsWith("solo.json", StringComparison.Ordinal),
                "no temp files may be left behind");
    }

    /// <summary>
    /// <see cref="EmbeddedScaleAssets.ReadAll"/> throws when a manifest resource is listed but will not
    /// open, and <see cref="ScaleLibraryLoader.Load"/> is documented as never throwing - a contract the
    /// MCP server's startup depends on. So the read is guarded: the library comes up without the
    /// embedded tier, saying why, rather than the failure escaping.
    /// </summary>
    [Fact]
    public void Load_reports_a_failure_and_still_returns_a_library_when_the_embedded_assets_cannot_be_read()
    {
        var loader = new ScaleLibraryLoader(
            new PathProbe(_besideExe, _appData),
            () => throw new InvalidOperationException("Manifest resource 'x.json' listed but not readable."));

        ScaleLibraryLoadResult result = loader.Load();

        result.Failures.Should().ContainSingle()
            .Which.Reason.Should().Contain("listed but not readable");
        result.Library.Count.Should().Be(72, "the generated melakarta are unaffected by an embedded-tier failure");
        Directory.Exists(Path.Combine(_besideExe, ScaleLibraryLoader.ScalesFolderName)).Should().BeTrue(
            "the writable folder is still resolved and created; there was simply nothing to materialise");
    }
}
