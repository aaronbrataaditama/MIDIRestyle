using Avalonia.Media;
using Avalonia.Styling;
using MidiRestyle.App.Controls;
using MidiRestyle.App.Services;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Covers <see cref="ThemeService"/> (default/persistence/corrupt-value recovery, mirroring
/// <see cref="SettingsServiceTests"/>) and the piano roll's palette-selection logic. The palette
/// selection is pure - <see cref="PianoRollPalettes.For"/> just picks between two already-built
/// static instances - so it is asserted here as data rather than by spinning up a headless Avalonia
/// runtime and a real control.
/// </summary>
public sealed class ThemeServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _besideExe;
    private readonly string _appData;

    public ThemeServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "midirestyle-theme-tests-" + Guid.NewGuid().ToString("N"));
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

    private SettingsService CreateSettingsService() => new(new PathProbe(_besideExe, _appData));

    private ThemeService CreateThemeService() => new(CreateSettingsService());

    // --- default ---------------------------------------------------------------------------

    [Fact]
    public void Default_is_system_not_a_hard_coded_dark_or_light()
    {
        var service = CreateThemeService();

        service.Current.Should().Be(ThemePreference.System);
    }

    [Fact]
    public void System_maps_to_the_default_theme_variant_a_real_third_state_not_a_synonym()
    {
        ThemeService.ToThemeVariant(ThemePreference.System).Should().Be(ThemeVariant.Default);
        ThemeService.ToThemeVariant(ThemePreference.System).Should().NotBe(ThemeVariant.Light);
        ThemeService.ToThemeVariant(ThemePreference.System).Should().NotBe(ThemeVariant.Dark);
    }

    // --- persistence -------------------------------------------------------------------------

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void Setting_a_theme_persists_and_a_fresh_service_reads_it_back(ThemePreference preference)
    {
        var settingsService = CreateSettingsService();
        var service = new ThemeService(settingsService);

        var result = service.SetTheme(preference);

        result.Success.Should().BeTrue();
        var freshService = new ThemeService(settingsService);
        freshService.Current.Should().Be(preference);
    }

    [Fact]
    public void The_three_states_round_trip_through_settings_in_sequence()
    {
        var settingsService = CreateSettingsService();
        var service = new ThemeService(settingsService);

        service.SetTheme(ThemePreference.Dark);
        new ThemeService(settingsService).Current.Should().Be(ThemePreference.Dark);

        service.SetTheme(ThemePreference.Light);
        new ThemeService(settingsService).Current.Should().Be(ThemePreference.Light);

        service.SetTheme(ThemePreference.System);
        new ThemeService(settingsService).Current.Should().Be(ThemePreference.System);
    }

    [Fact]
    public void Setting_a_theme_raises_theme_changed_with_the_new_preference()
    {
        var service = CreateThemeService();
        ThemePreference? raised = null;
        service.ThemeChanged += (_, preference) => raised = preference;

        service.SetTheme(ThemePreference.Dark);

        raised.Should().Be(ThemePreference.Dark);
    }

    // --- corrupt / unknown persisted values -------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Solarized")]
    [InlineData("dArk-ish")]
    [InlineData("1")]
    public void An_unknown_or_corrupt_persisted_value_falls_back_to_system_with_no_throw(string? raw)
    {
        var act = () => ThemeService.Parse(raw);

        act.Should().NotThrow().Which.Should().Be(ThemePreference.System);
    }

    [Fact]
    public void A_corrupt_settings_file_yields_the_default_theme_preference_with_no_throw()
    {
        File.WriteAllText(Path.Combine(_besideExe, SettingsService.SettingsFileName), "{ not json at all");

        var act = () => CreateThemeService();

        var service = act.Should().NotThrow().Subject;
        service.Current.Should().Be(ThemePreference.System);
    }

    [Fact]
    public void An_unrecognised_theme_name_in_an_otherwise_valid_settings_file_falls_back_to_system()
    {
        var settingsService = CreateSettingsService();
        settingsService.Save(AppSettings.Default with { ThemePreference = "Solarized" });

        var act = () => new ThemeService(settingsService);

        var service = act.Should().NotThrow().Subject;
        service.Current.Should().Be(ThemePreference.System);
    }

    // --- unwritable location -----------------------------------------------------------------

    [Fact]
    public void Saving_into_an_unwritable_location_reports_failure_rather_than_throwing()
    {
        // Mirrors SettingsServiceTests.Save_reports_failure_rather_than_throwing_when_both_locations_are_unwritable:
        // replace both candidate directories with plain files so neither can be (re)created.
        Directory.Delete(_besideExe);
        Directory.Delete(_appData);
        File.WriteAllText(_besideExe, "blocking file");
        File.WriteAllText(_appData, "blocking file");
        var service = CreateThemeService();

        var act = () => service.SetTheme(ThemePreference.Dark);

        var result = act.Should().NotThrow().Subject;
        result.Success.Should().BeFalse();
        result.Reason.Should().NotBeNullOrWhiteSpace();

        // The in-memory preference still takes effect this run even though it could not be saved.
        service.Current.Should().Be(ThemePreference.Dark);
    }

    // --- piano roll palettes -----------------------------------------------------------------

    [Fact]
    public void Dark_and_light_palettes_actually_differ()
    {
        PianoRollPalette dark = PianoRollPalettes.Dark;
        PianoRollPalette light = PianoRollPalettes.Light;

        ColorOf(dark.Background).Should().NotBe(ColorOf(light.Background));
        ColorOf(dark.WhiteRow).Should().NotBe(ColorOf(light.WhiteRow));
        ColorOf(dark.BlackRow).Should().NotBe(ColorOf(light.BlackRow));
        ColorOf(dark.Ghost).Should().NotBe(ColorOf(light.Ghost));
        ColorOf(dark.Note).Should().NotBe(ColorOf(light.Note));
        ColorOf(dark.OctaveLabel).Should().NotBe(ColorOf(light.OctaveLabel));
        ColorOf(dark.Grid.Brush).Should().NotBe(ColorOf(light.Grid.Brush));
        ColorOf(dark.Octave.Brush).Should().NotBe(ColorOf(light.Octave.Brush));
        ColorOf(dark.Playhead.Brush).Should().NotBe(ColorOf(light.Playhead.Brush));
    }

    [Theory]
    [InlineData(nameof(PianoRollPalettes.Dark))]
    [InlineData(nameof(PianoRollPalettes.Light))]
    public void Within_each_palette_the_ghost_brush_differs_from_the_note_brush(string paletteName)
    {
        PianoRollPalette palette = paletteName == nameof(PianoRollPalettes.Dark)
            ? PianoRollPalettes.Dark
            : PianoRollPalettes.Light;

        ColorOf(palette.Ghost).Should().NotBe(ColorOf(palette.Note),
            "the ghost-vs-solid contrast is the entire point of the overlay - a palette " +
            "that colours them alike would silently destroy the feature");
    }

    [Theory]
    [InlineData(nameof(PianoRollPalettes.Dark))]
    [InlineData(nameof(PianoRollPalettes.Light))]
    public void Within_each_palette_black_and_white_key_rows_are_distinguishable(string paletteName)
    {
        PianoRollPalette palette = paletteName == nameof(PianoRollPalettes.Dark)
            ? PianoRollPalettes.Dark
            : PianoRollPalettes.Light;

        ColorOf(palette.WhiteRow).Should().NotBe(ColorOf(palette.BlackRow));
    }

    [Theory]
    [InlineData(nameof(PianoRollPalettes.Dark))]
    [InlineData(nameof(PianoRollPalettes.Light))]
    public void Within_each_palette_the_playhead_is_distinguishable_from_the_background(string paletteName)
    {
        PianoRollPalette palette = paletteName == nameof(PianoRollPalettes.Dark)
            ? PianoRollPalettes.Dark
            : PianoRollPalettes.Light;

        ColorOf(palette.Playhead.Brush).Should().NotBe(ColorOf(palette.Background));
    }

    [Fact]
    public void ForSelectsLightOnlyForTheLightVariantAndDarkOtherwise()
    {
        PianoRollPalettes.For(ThemeVariant.Light).Should().BeSameAs(PianoRollPalettes.Light);
        PianoRollPalettes.For(ThemeVariant.Dark).Should().BeSameAs(PianoRollPalettes.Dark);

        // ActualThemeVariant on a real control never reports Default - Avalonia always resolves
        // "system" down to a concrete Light or Dark before a control sees it - but the selector
        // must still not throw or silently misbehave if it ever receives one.
        PianoRollPalettes.For(ThemeVariant.Default).Should().BeSameAs(PianoRollPalettes.Dark);
    }

    private static Color ColorOf(IBrush? brush) =>
        brush is ISolidColorBrush solid
            ? solid.Color
            : throw new InvalidOperationException($"Expected a solid color brush, got {brush?.GetType()}.");
}
