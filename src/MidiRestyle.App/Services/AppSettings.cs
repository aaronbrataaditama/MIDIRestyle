using System.Text.Json.Serialization;

namespace MidiRestyle.App.Services;

/// <summary>
/// Persisted app settings. Kept deliberately minimal for phase 3; later phases extend it (the
/// selected style, per-track exclusions, tuning-tolerance overrides, etc.) rather than replace it.
/// </summary>
public sealed record AppSettings
{
    public static AppSettings Default { get; } = new();

    /// <summary>The folder the "Open" dialog should start in. Null until a file has been opened.</summary>
    public string? LastOpenedFolder { get; init; }

    public double WindowWidth { get; init; } = 1280;

    public double WindowHeight { get; init; } = 800;

    /// <summary>Default <c>RangePolicy</c> for a new restyle, by name - see CLAUDE.md's range invariant.</summary>
    public string DefaultRangePolicy { get; init; } = "ShiftIntoRange";

    /// <summary>Default output mode by name: microtonal pitch-bend output, or 12-TET-quantised.</summary>
    public string DefaultOutputMode { get; init; } = "Microtonal";

    /// <summary>
    /// The persisted UI theme preference, by <see cref="ThemePreference"/> name. Defaults to
    /// <c>System</c> - a real third state that follows the OS, not a synonym for light or dark. An
    /// unrecognised value (a future downgrade, hand-edited settings, corruption) must fall back to
    /// <c>System</c> rather than throw; see <see cref="ThemeService"/>.
    /// </summary>
    public string ThemePreference { get; init; } = "System";
}

/// <summary>
/// Source-generated serialization context for <see cref="AppSettings"/>, so settings I/O stays
/// reflection-free and trim/AOT-friendly in principle even though the project does not trim today.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
public partial class AppSettingsJsonContext : JsonSerializerContext;
