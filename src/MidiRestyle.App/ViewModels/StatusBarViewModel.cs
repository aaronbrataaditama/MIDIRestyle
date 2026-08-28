using CommunityToolkit.Mvvm.ComponentModel;

namespace MidiRestyle.App.ViewModels;

/// <summary>How prominently a status message should read.</summary>
public enum StatusSeverity
{
    /// <summary>Neutral fact. The default, and where most things belong.</summary>
    Info,

    /// <summary>The app did something the user did not ask for and should know about.</summary>
    Warning,

    /// <summary>The app could not do what was asked.</summary>
    Error,
}

/// <summary>
/// The status bar: everything the engine decides on the user's behalf has its home here.
/// </summary>
/// <remarks>
/// This exists because several decisions in this app are silent by nature - raising the clustering
/// tolerance to fit the channel budget, muting a track that will not fit, dropping a note that
/// mapped out of MIDI range, falling back to <c>%APPDATA%</c> because the install directory is
/// read-only. Each is defensible; none is acceptable to do without saying so.
/// </remarks>
public sealed partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = "Ready.";

    [ObservableProperty]
    private StatusSeverity _severity = StatusSeverity.Info;

    /// <summary>Where settings are being read from and written to, for the portable case.</summary>
    [ObservableProperty]
    private string? _settingsLocation;

    /// <summary>
    /// Set when the channel budget forced the clustering tolerance above the user's choice.
    /// Names the effective tolerance and the worst resulting error.
    /// </summary>
    [ObservableProperty]
    private string? _toleranceNotice;

    /// <summary>Set when track-channels had to be muted in preview because even one cluster each would not fit.</summary>
    [ObservableProperty]
    private string? _mutedTracksNotice;

    /// <summary>Set when notes were dropped by the range policy.</summary>
    [ObservableProperty]
    private string? _droppedNotesNotice;

    /// <summary>Set when a loaded track already carries pitch bend.</summary>
    [ObservableProperty]
    private string? _pitchBendNotice;

    /// <summary>
    /// Set when audio is unavailable, e.g. no MIDI output device.
    /// </summary>
    /// <remarks>
    /// A notice rather than a message, because it must outlive the next thing that happens. Reporting
    /// it as a message meant loading a file overwrote it, leaving a greyed-out Play button with no
    /// explanation anywhere - which reads as a broken app rather than as a machine without a synth.
    /// </remarks>
    [ObservableProperty]
    private string? _audioNotice;

    /// <summary>Every active notice, in the order the status bar should show them.</summary>
    public IEnumerable<string> ActiveNotices =>
        new[] { AudioNotice, ToleranceNotice, MutedTracksNotice, DroppedNotesNotice, PitchBendNotice }
            .Where(n => !string.IsNullOrWhiteSpace(n))!;

    public bool HasNotices => ActiveNotices.Any();

    public void Report(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        Message = message;
        Severity = severity;
    }

    /// <summary>Clears the per-transform notices. Does not clear <see cref="SettingsLocation"/>, which is per-session.</summary>
    public void ClearTransformNotices()
    {
        ToleranceNotice = null;
        MutedTracksNotice = null;
        DroppedNotesNotice = null;
    }

    partial void OnToleranceNoticeChanged(string? value) => RaiseNoticeAggregates();

    partial void OnMutedTracksNoticeChanged(string? value) => RaiseNoticeAggregates();

    partial void OnDroppedNotesNoticeChanged(string? value) => RaiseNoticeAggregates();

    partial void OnPitchBendNoticeChanged(string? value) => RaiseNoticeAggregates();

    partial void OnAudioNoticeChanged(string? value) => RaiseNoticeAggregates();

    private void RaiseNoticeAggregates()
    {
        OnPropertyChanged(nameof(ActiveNotices));
        OnPropertyChanged(nameof(HasNotices));
    }
}
