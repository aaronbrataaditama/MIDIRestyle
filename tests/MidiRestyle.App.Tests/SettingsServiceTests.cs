using System.Text.Json;
using MidiRestyle.App.Services;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Every test points a <see cref="PathProbe"/> at unique temp directories, so nothing here ever
/// touches the real beside-the-exe folder or the user's actual %APPDATA%.
/// </summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _besideExe;
    private readonly string _appData;

    public SettingsServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "midirestyle-settings-tests-" + Guid.NewGuid().ToString("N"));
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

    private SettingsService CreateService() => new(new PathProbe(_besideExe, _appData));

    [Fact]
    public void Load_yields_defaults_flagged_as_no_settings_found_when_no_file_exists_anywhere()
    {
        var service = CreateService();

        var result = service.Load();

        result.Settings.Should().Be(AppSettings.Default);
        result.Location.Should().Be(SettingsLocation.None);
        result.Reason.Should().ContainEquivalentOf("no settings found");
    }

    [Fact]
    public void Save_then_load_round_trips_equal_settings()
    {
        var service = CreateService();
        var settings = AppSettings.Default with
        {
            LastOpenedFolder = "C:/tunes",
            WindowWidth = 1600,
            WindowHeight = 900,
            DefaultRangePolicy = "Drop",
            DefaultOutputMode = "TwelveTet",
        };

        var saveResult = service.Save(settings);
        var loadResult = service.Load();

        saveResult.Success.Should().BeTrue();
        loadResult.Settings.Should().Be(settings);
    }

    [Fact]
    public void Load_yields_defaults_with_a_stated_reason_when_the_settings_file_is_corrupt_and_does_not_throw()
    {
        File.WriteAllText(Path.Combine(_besideExe, SettingsService.SettingsFileName), "{ this is not valid json ");
        var service = CreateService();

        var act = () => service.Load();

        var result = act.Should().NotThrow().Subject;
        result.Settings.Should().Be(AppSettings.Default);
        result.Location.Should().Be(SettingsLocation.None);
        result.Reason.Should().ContainEquivalentOf("corrupt");
    }

    [Fact]
    public void Load_prefers_beside_the_exe_when_both_locations_hold_a_settings_file_and_says_so()
    {
        WriteSettingsFile(_besideExe, AppSettings.Default with { LastOpenedFolder = "beside" });
        WriteSettingsFile(_appData, AppSettings.Default with { LastOpenedFolder = "appdata" });
        var service = CreateService();

        var result = service.Load();

        result.Settings.LastOpenedFolder.Should().Be("beside");
        result.Location.Should().Be(SettingsLocation.BesideExe);
        result.Reason.Should().ContainEquivalentOf("beside the exe");
    }

    [Fact]
    public void Save_reports_failure_rather_than_throwing_when_both_locations_are_unwritable()
    {
        // Replace both candidate directories with plain files so neither can be (re)created -
        // a portable, admin-free stand-in for a read-only location (e.g. a USB stick).
        Directory.Delete(_besideExe);
        Directory.Delete(_appData);
        File.WriteAllText(_besideExe, "blocking file");
        File.WriteAllText(_appData, "blocking file");
        var service = CreateService();

        var act = () => service.Save(AppSettings.Default);

        var result = act.Should().NotThrow().Subject;
        result.Success.Should().BeFalse();
        result.Location.Should().BeNull();
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Save_falls_back_to_appdata_and_reports_which_location_was_used_when_beside_the_exe_is_unwritable()
    {
        Directory.Delete(_besideExe);
        File.WriteAllText(_besideExe, "blocking file");
        var service = CreateService();

        var result = service.Save(AppSettings.Default);

        result.Success.Should().BeTrue();
        result.Location.Should().Be(SettingsLocation.AppData);
        File.Exists(Path.Combine(_appData, SettingsService.SettingsFileName)).Should().BeTrue();
    }

    [Fact]
    public void Save_then_Load_round_trip_under_the_override_root()
    {
        string over = Path.Combine(Path.GetDirectoryName(_besideExe)!, "override");
        var service = new SettingsService(new PathProbe(_besideExe, _appData, over));

        var saved = service.Save(AppSettings.Default with { WindowWidth = 1234 });
        saved.Success.Should().BeTrue(saved.Reason);
        saved.Location.Should().Be(SettingsLocation.Override);
        File.Exists(Path.Combine(over, SettingsService.SettingsFileName)).Should().BeTrue();

        var loaded = service.Load();
        loaded.Location.Should().Be(SettingsLocation.Override);
        loaded.Settings.WindowWidth.Should().Be(1234);
    }

    private static void WriteSettingsFile(string directory, AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        File.WriteAllText(Path.Combine(directory, SettingsService.SettingsFileName), json);
    }
}
