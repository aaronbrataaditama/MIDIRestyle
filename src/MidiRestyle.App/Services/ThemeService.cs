using Avalonia;
using Avalonia.Styling;

namespace MidiRestyle.App.Services;

/// <summary>
/// The three theme choices a user can make. <see cref="System"/> is a real third state, not a
/// synonym for <see cref="Light"/> or <see cref="Dark"/> - Avalonia expresses it as
/// <see cref="ThemeVariant.Default"/>, which inherits whatever the platform is currently using and
/// keeps following it if the OS setting changes while the app is running.
/// </summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Owns the app's theme preference: reads it at startup, lets the UI change it, persists the
/// change through <see cref="SettingsService"/>, and translates it to the Avalonia
/// <see cref="ThemeVariant"/> that <c>Application.RequestedThemeVariant</c> expects.
/// </summary>
/// <remarks>
/// <para>
/// This service only ever reads and writes the theme field of <see cref="AppSettings"/> - it loads
/// the current settings once at construction and re-saves them with the theme field replaced, so it
/// never clobbers other fields (last-opened folder, window size, restyle defaults) that some other
/// part of the app has already changed in memory but not yet persisted itself. Since
/// <see cref="SettingsService"/> already reports save failure rather than throwing (a read-only
/// beside-the-exe folder, an unwritable %APPDATA%), this service does the same: <see cref="SetTheme"/>
/// returns the <see cref="SettingsSaveResult"/> unchanged rather than swallowing it.
/// </para>
/// <para>
/// Deliberately has no dependency on <c>Application.Current</c> so the preference logic - default,
/// persistence, corrupt-value recovery - can be unit tested without an initialised Avalonia runtime.
/// <see cref="Apply"/> is the one method that touches a live <see cref="Application"/>, and it is a
/// thin one-liner precisely so it needs no test of its own.
/// </para>
/// </remarks>
public sealed class ThemeService
{
    private readonly SettingsService _settingsService;
    private AppSettings _settings;

    public ThemeService(SettingsService? settingsService = null)
    {
        _settingsService = settingsService ?? new SettingsService();
        _settings = _settingsService.Load().Settings;
        Current = Parse(_settings.ThemePreference);
    }

    /// <summary>The current theme preference. Never throws its way to an invalid value.</summary>
    public ThemePreference Current { get; private set; }

    /// <summary>
    /// Raised after <see cref="Current"/> changes, whether or not the save that followed succeeded -
    /// the in-memory choice still changes so the UI reflects what the user just picked. Subscribers
    /// (the piano roll, the View menu) use this to re-render without needing a restart.
    /// </summary>
    public event EventHandler<ThemePreference>? ThemeChanged;

    /// <summary>
    /// Sets and persists the theme preference. Never throws: a save failure (read-only beside-the-exe
    /// folder and an unwritable %APPDATA%, mirroring <see cref="SettingsService.Save"/>) is reported
    /// back in the result, not raised as an exception - the in-memory preference still takes effect
    /// for the rest of this run.
    /// </summary>
    public SettingsSaveResult SetTheme(ThemePreference preference)
    {
        Current = preference;
        _settings = _settings with { ThemePreference = preference.ToString() };
        SettingsSaveResult result = _settingsService.Save(_settings);
        ThemeChanged?.Invoke(this, preference);
        return result;
    }

    /// <summary>Applies <see cref="Current"/> to a live application instance.</summary>
    public void Apply(Application application) => application.RequestedThemeVariant = ToThemeVariant(Current);

    /// <summary>
    /// Translates a <see cref="ThemePreference"/> to the Avalonia <see cref="ThemeVariant"/> that
    /// <c>RequestedThemeVariant</c> expects. <see cref="ThemePreference.System"/> maps to
    /// <see cref="ThemeVariant.Default"/>, which is what makes it a real third state rather than a
    /// value baked in at the moment the user picked it.
    /// </summary>
    public static ThemeVariant ToThemeVariant(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>
    /// Parses a persisted theme name, falling back to <see cref="ThemePreference.System"/> - never
    /// throwing - for anything unrecognised: an empty string, a typo from hand-edited settings, a
    /// value written by a future version this build does not know about, or a bare number. Matches
    /// only the exact case-insensitive names deliberately, rather than <c>Enum.TryParse</c> alone:
    /// that overload also accepts the underlying numeric value of a defined member (<c>"1"</c> would
    /// silently parse as <see cref="ThemePreference.Light"/>), which is exactly the kind of stray
    /// value a corrupted or hand-edited settings file could contain and must not be honoured as if
    /// it were a real choice.
    /// </summary>
    public static ThemePreference Parse(string? raw)
    {
        if (string.Equals(raw, nameof(ThemePreference.Light), StringComparison.OrdinalIgnoreCase))
        {
            return ThemePreference.Light;
        }

        if (string.Equals(raw, nameof(ThemePreference.Dark), StringComparison.OrdinalIgnoreCase))
        {
            return ThemePreference.Dark;
        }

        return ThemePreference.System;
    }
}
