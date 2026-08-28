using System.Text.Json;

namespace MidiRestyle.App.Services;

/// <summary>Which of the two candidate locations settings were loaded from or saved to.</summary>
public enum SettingsLocation
{
    /// <summary>No settings file was found at either location.</summary>
    None,

    /// <summary>Beside the running exe - <see cref="PathProbe.BesideExeDirectory"/>.</summary>
    BesideExe,

    /// <summary>The %APPDATA%\MIDIRestyle fallback - <see cref="PathProbe.AppDataDirectory"/>.</summary>
    AppData,
}

public sealed record SettingsLoadResult(AppSettings Settings, SettingsLocation Location, string Reason);

public sealed record SettingsSaveResult(bool Success, SettingsLocation? Location, string Reason);

/// <summary>
/// Loads and saves <c>MIDIRestyle.settings.json</c>.
///
/// Read precedence: beside-the-exe wins over %APPDATA% whenever both hold a settings file, regardless
/// of which location is currently writable - this makes the USB-stick case predictable after the app
/// has previously been run from a writable folder ("split brain").
///
/// Write precedence: beside-the-exe is used when writable; otherwise the service falls back to
/// %APPDATA%\MIDIRestyle. If neither is writable, saving reports failure and never throws.
///
/// A missing settings file yields defaults with a "no settings found" reason. A corrupt settings file
/// yields defaults with a reason naming the corruption - never a crash, never a silent reset that
/// pretends nothing happened.
/// </summary>
public sealed class SettingsService
{
    public const string SettingsFileName = "MIDIRestyle.settings.json";

    private readonly PathProbe _pathProbe;

    public SettingsService(PathProbe? pathProbe = null)
    {
        _pathProbe = pathProbe ?? new PathProbe();
    }

    public SettingsLoadResult Load()
    {
        var besideExePath = Path.Combine(_pathProbe.BesideExeDirectory, SettingsFileName);
        if (File.Exists(besideExePath))
        {
            return LoadFrom(besideExePath, SettingsLocation.BesideExe, "beside the exe");
        }

        var appDataPath = Path.Combine(_pathProbe.AppDataDirectory, SettingsFileName);
        if (File.Exists(appDataPath))
        {
            return LoadFrom(appDataPath, SettingsLocation.AppData, "in %APPDATA%");
        }

        return new SettingsLoadResult(
            AppSettings.Default,
            SettingsLocation.None,
            "No settings found beside the exe or in %APPDATA%; using defaults.");
    }

    public SettingsSaveResult Save(AppSettings settings)
    {
        var resolved = _pathProbe.ResolveWritableRoot();
        if (!resolved.IsWritable)
        {
            return new SettingsSaveResult(false, Location: null, resolved.Reason);
        }

        var path = Path.Combine(resolved.Root, SettingsFileName);
        try
        {
            Directory.CreateDirectory(resolved.Root);
            var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsSaveResult(false, Location: null, $"Failed to write settings to '{path}': {ex.Message}");
        }

        var location = resolved.IsBesideExe ? SettingsLocation.BesideExe : SettingsLocation.AppData;
        var where = resolved.IsBesideExe ? "beside the exe" : "%APPDATA%";
        return new SettingsSaveResult(true, location, $"Saved settings to '{path}' ({where}).");
    }

    private static SettingsLoadResult LoadFrom(string path, SettingsLocation location, string label)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoadResult(
                AppSettings.Default,
                SettingsLocation.None,
                $"Settings file {label} ('{path}') could not be read ({ex.Message}); using defaults.");
        }

        AppSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        }
        catch (JsonException ex)
        {
            return new SettingsLoadResult(
                AppSettings.Default,
                SettingsLocation.None,
                $"Settings file {label} ('{path}') is corrupt ({ex.Message}); using defaults.");
        }

        if (settings is null)
        {
            return new SettingsLoadResult(
                AppSettings.Default,
                SettingsLocation.None,
                $"Settings file {label} ('{path}') was empty or null; using defaults.");
        }

        return new SettingsLoadResult(settings, location, $"Loaded settings {label} ('{path}').");
    }
}
