using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidiRestyle.App.Controls;
using MidiRestyle.App.Services;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Playback;

namespace MidiRestyle.App.ViewModels;

/// <summary>Which pane fills the centre of the window.</summary>
public enum CentreView
{
    /// <summary>The piano roll: every track at once, at true cents.</summary>
    PianoRoll,

    /// <summary>Western staff notation. Only meaningful for a scale that can be spelled.</summary>
    Staff,

    /// <summary>
    /// Degree / cipher notation. The only readable option for the equal-step families - Slendro,
    /// Thai 7-equal - which carry no staff spelling at all, and a useful degree read-out otherwise.
    /// </summary>
    Degrees,
}

/// <summary>
/// The window's root view model: what is loaded, what the user chose, and what to tell them.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>Opens a file picker and returns the chosen path, or null if cancelled.</summary>
    public delegate Task<string?> OpenFilePrompt();

    private readonly OpenFilePrompt? _promptForFile;
    private readonly SettingsService? _settings;

    public MainWindowViewModel(
        OpenFilePrompt? promptForFile = null,
        SettingsService? settings = null)
    {
        _promptForFile = promptForFile;
        _settings = settings;

        if (settings is null)
        {
            return;
        }

        // Where settings live is genuinely interesting to a portable app: run from a writable
        // folder then copied to a read-only USB stick, it silently changes. The status bar says
        // which location is in use rather than leaving the user to guess why a preference vanished.
        SettingsLoadResult loaded = settings.Load();
        Settings = loaded.Settings;
        Status.SettingsLocation = loaded.Location switch
        {
            SettingsLocation.BesideExe => "Settings: beside the exe",
            SettingsLocation.AppData => "Settings: %APPDATA%",
            _ => "Settings: defaults",
        };

        if (loaded.Location == SettingsLocation.None && !loaded.Reason.Contains("no settings", StringComparison.OrdinalIgnoreCase))
        {
            // Defaults because something went wrong, not because this is a first run. Say so.
            Status.Report(loaded.Reason, StatusSeverity.Warning);
        }
    }

    /// <summary>Loaded preferences. Defaults when no settings file was found or it was unreadable.</summary>
    public AppSettings Settings { get; private set; } = AppSettings.Default;

    public StatusBarViewModel Status { get; } = new();

    /// <summary>
    /// The appearance preference, mirrored here so the View menu's radio items can show a checkmark.
    /// </summary>
    /// <remarks>
    /// Mirrored rather than owned: <c>ThemeService</c> is the source of truth and persists it. Three
    /// booleans rather than a converter because a menu item's <c>IsChecked</c> is a bool, and three
    /// trivial properties are easier to read than an enum-to-bool converter parameterised per item.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeIsSystem))]
    [NotifyPropertyChangedFor(nameof(ThemeIsLight))]
    [NotifyPropertyChangedFor(nameof(ThemeIsDark))]
    private ThemePreference _themePreference = ThemePreference.System;

    public bool ThemeIsSystem => ThemePreference == ThemePreference.System;

    public bool ThemeIsLight => ThemePreference == ThemePreference.Light;

    public bool ThemeIsDark => ThemePreference == ThemePreference.Dark;

    /// <summary>
    /// The right rail. Null until a scale library has been supplied.
    /// </summary>
    /// <remarks>
    /// Injected rather than constructed here so the window can build it from
    /// <c>ScaleLibraryService</c> - which needs an initialised Avalonia runtime for its asset
    /// loader and therefore cannot run inside a plain unit test.
    /// </remarks>
    [ObservableProperty]
    private StylePanelViewModel? _stylePanel;

    /// <summary>
    /// Re-runs the transform from the style panel's current choices, if it can.
    /// </summary>
    /// <remarks>
    /// The host debounces calls to this - see <see cref="StylePanelViewModel.SelectionDebounce"/> -
    /// because the scale list is arrow-key browsable and a held key would otherwise queue work
    /// faster than it completes. Silently does nothing when there is no file or no target scale;
    /// both are ordinary states, not errors.
    /// </remarks>
    public void ReapplyFromStylePanel()
    {
        if (StylePanel is not { } panel || Project is null)
        {
            return;
        }

        // No target scale yet is an ordinary state - it is where every file starts. The piano roll
        // already shows the source material at this point, so the notation views must too, or they
        // sit empty beside a full roll and read as broken.
        if (!panel.CanRestyle)
        {
            ShowSourceNotation(panel);
            return;
        }

        HashSet<(int Track, int Channel)> excluded = [.. Tracks
            .Where(t => !t.WillBeRestyled && !t.IsLocked)
            .Select(t => (t.TrackIndex, t.Track.Channel))];

        ApplyRestyle(panel.BuildSettings(excluded));
    }

    /// <summary>
    /// Notates the file as it stands, before any restyling.
    /// </summary>
    /// <remarks>
    /// Spelled against the <i>source</i> key rather than a target, so what appears is the piece the
    /// user just opened rather than a transformation of it. Choosing a target scale replaces this
    /// with the restyled score through the usual path; nothing here is retained.
    /// </remarks>
    private void ShowSourceNotation(StylePanelViewModel panel)
    {
        if (!RebuildOriginalScore(panel.EffectiveSourceScale, panel.SourceTonic))
        {
            return;
        }

        _restyled = null;
        ShowRestyledScore = false;
        RefreshScore();
    }

    /// <summary>One notated reading of the piece: the score, and the scale it was spelled against.</summary>
    private readonly record struct Notated(
        NotationScore Score, Scale Scale, MidiRestyle.Core.Tuning.Pitch Tonic);

    /// <summary>
    /// Whether the notation shows the restyled reading rather than the original.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="HearingRestyled"/>, which is display state read back
    /// from the playback engine and must never be used as an input - driving the score off it would
    /// reintroduce exactly the disagreement its own remarks warn about. This is moved by the only
    /// two things entitled to move it: choosing a target scale, and the A/B toggle, which moves the
    /// audio and the notation together so the screen always agrees with the speakers.
    /// </remarks>
    [ObservableProperty]
    private bool _showRestyledScore;

    partial void OnShowRestyledScoreChanged(bool value) => RefreshScore();

    private Notated? _original;
    private Notated? _restyled;

    /// <summary>What the original score was last built from, so it is not rebuilt needlessly.</summary>
    private (MidiProject Project, Scale Scale, double TonicCents)? _originalBuiltFrom;

    /// <summary>
    /// Notates the piece as written, against the source key.
    /// </summary>
    /// <remarks>
    /// Cached deliberately. The original reading depends only on the file and the <i>source</i> key,
    /// so it does not change when the user picks a different target scale - and the scale list is
    /// arrow-key browsable, so rebuilding it per keystroke would double the notation cost of every
    /// keypress for a result that is identical each time.
    /// </remarks>
    private bool RebuildOriginalScore(Scale? scale, MidiRestyle.Core.Tuning.Pitch tonic)
    {
        if (Project is not { } project || scale is null)
        {
            return false;
        }

        if (_originalBuiltFrom is { } built
            && ReferenceEquals(built.Project, project)
            && ReferenceEquals(built.Scale, scale)
            && built.TonicCents == tonic.Cents)
        {
            return true;
        }

        RestyledTrack[] asWritten = [.. project.Tracks
            .Where(t => !t.IsDrums && t.NoteCount > 0)
            .Select(t => new RestyledTrack(t.TrackIndex, t.Channel, t.Notes, WasRestyled: false))];

        _original = new Notated(
            NotationBuilder.Build(project, asWritten, SettingsFor(scale, tonic)), scale, tonic);

        _originalBuiltFrom = (project, scale, tonic.Cents);
        return true;
    }

    private static RestyleSettings SettingsFor(Scale scale, MidiRestyle.Core.Tuning.Pitch tonic) => new()
    {
        TargetScale = scale,
        TargetTonic = tonic,
        SourceScale = scale,
        SourceTonic = tonic,
        TonicSpelling = TonicSpelling.FromPitchClass(tonic.PitchClass),
    };

    /// <summary>
    /// Publishes whichever reading the user should be looking at.
    /// </summary>
    /// <remarks>
    /// Choosing a target scale switches the view to the restyled reading, because that is the thing
    /// the user just asked to see. From then on the A/B "Hearing" toggle drives it, so what is on
    /// screen is what is in the speakers.
    /// </remarks>
    private void RefreshScore()
    {
        Notated? shown = ShowRestyledScore && _restyled is not null ? _restyled : _original;

        Score = shown?.Score;
        NotationScale = shown?.Scale;
        NotationTonic = shown?.Tonic ?? MidiRestyle.Core.Tuning.Pitch.FromMidi(60);
    }

    public ObservableCollection<TrackViewModel> Tracks { get; } = [];

    // Every computed property that depends on Project is declared here, beside it. Doing this by
    // remembering to call a Raise method from each mutation site is how the Play button shipped
    // permanently disabled: the value was right, nothing announced it, and no test noticed because
    // the tests asserted values rather than notifications.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyPropertyChangedFor(nameof(ShowPianoRollPane))]
    [NotifyPropertyChangedFor(nameof(ShowStaffPane))]
    [NotifyPropertyChangedFor(nameof(ShowDegreesPane))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    [NotifyPropertyChangedFor(nameof(PlayDisabledReason))]
    [NotifyPropertyChangedFor(nameof(CompareDisabledReason))]
    [NotifyPropertyChangedFor(nameof(DurationTicks))]
    private MidiProject? _project;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private MetadataViewModel? _metadata;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Which of the three centre views is showing. The per-view booleans below are what the tab
    /// strip and the View menu's radio items actually bind to, so each needs announcing here - a
    /// computed property a control binds to without a notification is the bug that shipped the Play
    /// button permanently disabled.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPianoRollView))]
    [NotifyPropertyChangedFor(nameof(IsStaffView))]
    [NotifyPropertyChangedFor(nameof(IsDegreesView))]
    [NotifyPropertyChangedFor(nameof(ShowPianoRollPane))]
    [NotifyPropertyChangedFor(nameof(ShowStaffPane))]
    [NotifyPropertyChangedFor(nameof(ShowDegreesPane))]
    private CentreView _centreView = CentreView.PianoRoll;

    public bool IsPianoRollView => CentreView == CentreView.PianoRoll;

    public bool IsStaffView => CentreView == CentreView.Staff;

    public bool IsDegreesView => CentreView == CentreView.Degrees;

    // Selecting a tab is not on its own enough to draw it: with no file loaded every view would
    // render an empty frame on top of the "open a file" prompt.
    public bool ShowPianoRollPane => HasProject && IsPianoRollView;

    public bool ShowStaffPane => HasProject && IsStaffView;

    public bool ShowDegreesPane => HasProject && IsDegreesView;

    /// <summary>
    /// Whether a staff can honestly be drawn for the current scale. The same question as
    /// <see cref="CanExportMusicXml"/>, under the name the view binds to - the export gate and the
    /// staff gate are one decision, and giving them one source stops them drifting apart.
    /// </summary>
    public bool CanShowStaff => StaffUnavailableReason is null;

    /// <summary>
    /// The notated score, rebuilt whenever the transform re-runs. Both the staff view and the
    /// degree view read this same object: the measures, ties and rests are identical either way,
    /// and only the glyphs differ.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScore))]
    [NotifyPropertyChangedFor(nameof(CanShowStaff))]
    [NotifyPropertyChangedFor(nameof(CanExportMusicXml))]
    [NotifyPropertyChangedFor(nameof(MusicXmlMenuHeader))]
    [NotifyPropertyChangedFor(nameof(StaffUnavailableReason))]
    private NotationScore? _score;

    /// <summary>
    /// The scale the score was spelled against. Kept separately from the score because whether a
    /// staff can be drawn at all is a property of the scale, not of the notes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowStaff))]
    [NotifyPropertyChangedFor(nameof(CanExportMusicXml))]
    [NotifyPropertyChangedFor(nameof(MusicXmlMenuHeader))]
    [NotifyPropertyChangedFor(nameof(StaffUnavailableReason))]
    private Scale? _notationScale;

    /// <summary>The tonic the degree view reads pitches against.</summary>
    [ObservableProperty]
    private MidiRestyle.Core.Tuning.Pitch _notationTonic = MidiRestyle.Core.Tuning.Pitch.FromMidi(60);

    public bool HasScore => Score is { IsEmpty: false };

    /// <summary>
    /// Why the staff cannot be drawn, or <c>null</c> when it can. The staff menu item is never
    /// greyed: selecting it on a non-notatable scale is a reasonable thing to try, and the view
    /// explaining itself and offering the degree view is far better than a dead menu entry.
    /// </summary>
    public string? StaffUnavailableReason
    {
        get
        {
            if (!HasScore)
            {
                return "Load a MIDI file and pick a target scale to see it notated.";
            }

            if (_staffDiagnostic is { } reason)
            {
                return reason + " Use the Degrees view instead.";
            }

            return null;
        }
    }

    /// <summary>
    /// Why the current target scale cannot be written on a staff, cached because it depends only on
    /// the scale and <see cref="StaffUnavailableReason"/> is read on every binding pass.
    /// </summary>
    private string? _staffDiagnostic;

    /// <summary>
    /// Recomputed when the scale changes. Notatability is asked of <see cref="DiatonicSpeller"/>
    /// rather than read off <c>Scale.Notatable</c>, because the flag alone is not the whole answer:
    /// a scale may be flagged notatable and still have no seven-letter spelling - several Persian
    /// dastgahs and Turkish makams run to eight or nine degrees - and the speller is the thing that
    /// knows. It also returns the diagnostic, so the view can say which of the two happened.
    /// </summary>
    partial void OnNotationScaleChanged(Scale? value)
    {
        if (value is null)
        {
            _staffDiagnostic = null;
            return;
        }

        SpellingResult spelling = value.Spelling is not null
            ? new SpellingResult(value.Spelling)
            : DiatonicSpeller.Derive(value);

        _staffDiagnostic = spelling.Succeeded
            ? null
            : spelling.Diagnostic
              ?? $"{value.Name} has no Western staff spelling.";
    }

    /// <summary>
    /// MusicXML export, unlike the staff view, <i>is</i> disabled when it cannot proceed - a file
    /// that cannot be written is not worth offering, and the menu states the reason rather than
    /// leaving a grey entry to be puzzled over.
    /// </summary>
    public bool CanExportMusicXml => CanShowStaff;

    public string MusicXmlMenuHeader => CanExportMusicXml
        ? "Export Music_XML..."
        : NotationScale is { Notatable: false } scale
            ? $"Export Music_XML...  ({scale.Name} has no staff spelling)"
            : "Export Music_XML...  (nothing to export yet)";

    [RelayCommand]
    private void ShowPianoRoll() => CentreView = CentreView.PianoRoll;

    [RelayCommand]
    private void ShowStaff() => CentreView = CentreView.Staff;

    [RelayCommand]
    private void ShowDegrees() => CentreView = CentreView.Degrees;

    public bool HasProject => Project is not null;

    /// <summary>Window title, showing the loaded file so the taskbar is useful with several open.</summary>
    public string WindowTitle => Metadata?.FileName is { } name
        ? $"{name} - MIDIRestyle"
        : "MIDIRestyle";

    /// <summary>
    /// Three real views now, so the strip earns its place. It was suppressed while two of the three
    /// were inert, on the grounds that a tab that does nothing is worse than no tab at all.
    /// </summary>
    public static bool ShowViewTabs => true;

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (_promptForFile is null)
        {
            Status.Report("No file picker is available in this context.", StatusSeverity.Error);
            return;
        }

        string? path = await _promptForFile().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Load(path);
    }

    /// <summary>
    /// Loads a file, replacing whatever was open. A failed load leaves the previous project intact -
    /// losing the user's work because the next file was corrupt would be its own bug.
    /// </summary>
    public void Load(string path)
    {
        IsBusy = true;
        try
        {
            if (!MidiFileLoader.TryLoad(path, out MidiProject? loaded, out string? failure))
            {
                Status.Report(failure ?? $"Could not open {Path.GetFileName(path)}.", StatusSeverity.Error);
                return;
            }

            Adopt(loaded!);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Adopts an already-loaded project. Kept separate from <see cref="Load"/> so tests need no files.</summary>
    public void Adopt(MidiProject loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        Project = loaded;
        Metadata = new MetadataViewModel(loaded);

        Tracks.Clear();
        foreach (TrackInfo track in loaded.Tracks)
        {
            Tracks.Add(new TrackViewModel(track));
        }

        RebuildRollNotes(loaded);

        // A new file invalidates the previous transform; leaving stale restyled notes on screen
        // over a different piece would be worse than showing none.
        Restyle = null;
        Allocation = null;
        RestyledRollNotes = [];
        Score = null;
        _original = null;
        _restyled = null;
        _originalBuiltFrom = null;
        ShowRestyledScore = false;
        Status.ClearTransformNotices();

        string[] bendTracks = [.. loaded.TracksWithExistingPitchBend.Select(t => t.DisplayName)];
        Status.PitchBendNotice = bendTracks.Length == 0
            ? null
            : $"Already uses pitch bend: {string.Join(", ", bendTracks)}. "
              + "Microtonal output would conflict - consider 12-TET for this file.";

        Status.Report(
            $"Loaded {Metadata.FileName} - {loaded.Tracks.Count} track-channels, "
            + $"{loaded.TotalNoteCount} notes.");

    }

    /// <summary>Track-channels the user has actually opted into restyling.</summary>
    public IEnumerable<TrackViewModel> RestylableSelection => Tracks.Where(t => t.WillBeRestyled);

    /// <summary>
    /// Every source note, flattened and sorted by start tick for the piano roll.
    /// </summary>
    /// <remarks>
    /// Built once per load rather than per frame. The roll's culling relies on the sort order, and
    /// re-sorting tens of thousands of notes on every scroll would defeat the point of culling at
    /// all. Drums are included: the roll shows what the file contains, and excluding them would
    /// misrepresent it even though they are never restyled.
    /// </remarks>
    [ObservableProperty]
    private RollNote[] _sourceRollNotes = [];

    /// <summary>
    /// The restyled notes, drawn solid over the ghosts. Empty until a transform has run.
    /// </summary>
    [ObservableProperty]
    private RollNote[] _restyledRollNotes = [];

    /// <summary>The most recent transform, or null if none has run for the loaded file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    [NotifyPropertyChangedFor(nameof(CompareDisabledReason))]
    private RestyleResult? _restyle;

    /// <summary>The channel plan for the most recent transform.</summary>
    [ObservableProperty]
    private ChannelAllocation? _allocation;

    /// <summary>
    /// Runs the transform and publishes everything that depends on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cheap by design - the scale list is arrow-key browsable, so this runs on every keystroke.
    /// <c>RestyleEngine</c> is a pure function of the project and the settings and does 20,000 notes
    /// in about 3 ms; the host debounces at roughly 150 ms so that a held arrow key does not queue
    /// up work faster than it completes.
    /// </para>
    /// <para>
    /// The allocation is planned here rather than at export time, and against the same ceiling
    /// playback will use, so what the status bar reports is what a file would actually contain.
    /// Planning it twice with different inputs is precisely the divergence the single-allocator
    /// design exists to prevent.
    /// </para>
    /// </remarks>
    public void ApplyRestyle(RestyleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (Project is not { } project)
        {
            return;
        }

        RestyleResult result = RestyleEngine.Restyle(project, settings);
        ChannelAllocation allocation = ChannelAllocator.Allocate(result);

        Restyle = result;
        Allocation = allocation;
        RestyledRollNotes = ToRollNotes(result);

        // Notation is derived from the same result and the same settings, so the staff, the degree
        // view and any exported MusicXML can never drift from what the roll shows or what plays.
        RebuildOriginalScore(settings.SourceScale ?? settings.TargetScale, settings.SourceTonic);

        _restyled = new Notated(
            NotationBuilder.Build(project, result.Tracks, settings),
            settings.TargetScale,
            settings.TargetTonic);

        // Picking a scale shows it - that is the thing the user just asked for. The A/B toggle
        // takes over from here, and moves the audio and the notation together.
        _preferredSide = PlaybackSide.Restyled;
        ShowRestyledScore = true;
        RefreshScore();

        Status.ClearTransformNotices();

        // Everything the engine decided quietly gets a line. Both of these return null when there
        // is nothing to say, so a clean transform stays silent rather than reassuring the user
        // about work it did not have to do.
        Status.DroppedNotesNotice = result.Tally.Describe();
        Status.ToleranceNotice = allocation.Budget.ToleranceWasRaised ? allocation.Describe() : null;
        Status.MutedTracksNotice = allocation.Muted.Count == 0
            ? null
            : $"Muted in preview: {string.Join(", ", allocation.Muted.Select(m => $"track {m.TrackIndex + 1} ch {m.Channel + 1}"))}. "
              + "The exported file is unaffected.";

        Status.Report(
            $"{settings.TargetScale.Name} - {result.TotalNoteCount} notes on "
            + $"{allocation.ChannelCount} channel{(allocation.ChannelCount == 1 ? "" : "s")}.");
    }

    // ---- transport ---------------------------------------------------------------------------

    private IPlaybackEngine? _engine;

    /// <summary>Whether audio is available at all. False on a machine with no MIDI device.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    [NotifyPropertyChangedFor(nameof(PlayDisabledReason))]
    [NotifyPropertyChangedFor(nameof(CompareDisabledReason))]
    private bool _audioAvailable;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>Which side the A/B toggle is on.</summary>
    [ObservableProperty]
    private bool _hearingRestyled;

    /// <summary>Playhead position, in ticks, for the piano roll. Negative hides it.</summary>
    [ObservableProperty]
    private double _playheadTicks = -1;

    public bool CanPlay => AudioAvailable && Project is not null;

    /// <summary>
    /// Whether Stop would do anything. False when nothing is sounding and the playhead is at the start.
    /// </summary>
    /// <remarks>
    /// Stop both silences and rewinds, so it is meaningful while paused mid-piece as well as while
    /// playing - but not when there is nothing to silence and nowhere to rewind from.
    /// </remarks>
    public bool CanStop => CanPlay && (IsPlaying || PlayheadTicks > 0);

    /// <summary>The A/B toggle only means something once there is a transform to compare against.</summary>
    public bool CanCompare => CanPlay && Restyle is not null;

    public string PlayPauseLabel => IsPlaying ? "Pause" : "Play";

    public string AbLabel => HearingRestyled ? "Hearing: restyled" : "Hearing: original";

    /// <summary>
    /// Why playback is unavailable, or null when it is available. Shown as the button's tooltip.
    /// </summary>
    /// <remarks>
    /// A disabled control with no reason is the single most common way a working app looks broken.
    /// Both causes are ordinary states, not errors: no MIDI device, or no file open yet.
    /// </remarks>
    public string? PlayDisabledReason =>
        !AudioAvailable ? _engine?.Reason ?? "No MIDI output device is available."
        : Project is null ? "Open a MIDI file first."
        : null;

    /// <summary>Why the A/B toggle is unavailable, or null when it is available.</summary>
    public string? CompareDisabledReason =>
        PlayDisabledReason
        ?? (Restyle is null ? "Choose a target scale first - there is nothing to compare yet." : null);

    /// <summary>
    /// Supplies the engine the window created, and reports whether audio is available.
    /// </summary>
    /// <remarks>
    /// Injected rather than constructed here because choosing an engine means probing for a MIDI
    /// device, which belongs to the platform-bound assembly. No device is a normal state - the app
    /// stays fully functional minus audio - so this reports rather than throws.
    /// </remarks>
    public void AttachEngine(IPlaybackEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        _engine = engine;
        AudioAvailable = engine.IsAvailable;

        // A notice, not a message: a message is transient and loading a file would overwrite it,
        // leaving the disabled Play button unexplained.
        Status.AudioNotice = engine.IsAvailable ? null : engine.Reason;

        RaiseTransportState();
    }

    /// <summary>
    /// Prepares audio: both sides when a transform exists, the original alone when it does not.
    /// </summary>
    /// <remarks>
    /// Playing before choosing a scale is the first thing anyone does with a MIDI player, so it must
    /// work. Requiring a target scale first would be a gate with no purpose - there is simply nothing
    /// to compare against yet, which is what disables the A/B toggle rather than Play.
    /// </remarks>
    public bool PrepareAudio()
    {
        if (_engine is null || Project is not { } project)
        {
            return false;
        }

        PlaybackBuildResult built = Restyle is { } restyled
            ? PlaybackSequenceBuilder.Build(restyled)
            : PlaybackSequenceBuilder.BuildOriginalOnly(project);
        if (!built.Success)
        {
            Status.Report(built.Message ?? "Audio could not be prepared.", StatusSeverity.Warning);
            return false;
        }

        _engine.Load(built.Sequences!);

        // Loading starts on the original side, so put the user's choice back rather than quietly
        // overriding it. Only meaningful when there is a restyled side to switch to.
        if (_preferredSide == PlaybackSide.Restyled && Restyle is not null)
        {
            _engine.SwitchTo(PlaybackSide.Restyled);
        }

        SyncHearingFromEngine();
        RaiseTransportState();
        return true;
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (_engine is not { IsAvailable: true } engine)
        {
            return;
        }

        if (engine.IsPlaying)
        {
            engine.Pause();
            IsPlaying = false;
            return;
        }

        // Prepare lazily: the user may have browsed a dozen scales since the last transform, and
        // rebuilding on every keystroke would be wasted work nobody asked for.
        if (!engine.IsLoaded && !PrepareAudio())
        {
            return;
        }

        engine.Play();
        IsPlaying = engine.IsPlaying;
        SyncHearingFromEngine();
    }

    [RelayCommand]
    private void StopPlayback()
    {
        _engine?.Stop();
        IsPlaying = false;
        PlayheadTicks = -1;
    }

    /// <summary>
    /// Which side the user asked to hear. Remembered across a reload, so it is never silently changed.
    /// </summary>
    private PlaybackSide _preferredSide = PlaybackSide.Original;

    /// <summary>
    /// Flips between hearing the original and the restyled version, keeping the playhead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This command is the <em>only</em> thing that changes which side is heard.
    /// <see cref="HearingRestyled"/> is display state read back from the engine, never an input - the
    /// toggle button binds it one-way for that reason. Binding it two-way alongside this command made
    /// a click do the work twice: the binding set the flag, then the command toggled the engine and
    /// set the flag again, so the two could disagree and the label would lie about what was sounding.
    /// </para>
    /// <para>
    /// The choice is also remembered. Re-preparing audio starts the engine on the original side, so
    /// without <see cref="_preferredSide"/> a user who had selected "restyled" would be silently
    /// moved back to the original the next time anything invalidated the load.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void ToggleAb()
    {
        if (_engine is not { IsAvailable: true, IsLoaded: true } engine)
        {
            return;
        }

        _preferredSide = engine.Toggle();
        SyncHearingFromEngine();

        // The notation follows the sound.
        ShowRestyledScore = _preferredSide == PlaybackSide.Restyled;
    }

    /// <summary>Reads which side is actually sounding back out of the engine.</summary>
    private void SyncHearingFromEngine()
    {
        if (_engine is { IsAvailable: true, IsLoaded: true } engine)
        {
            HearingRestyled = engine.ActiveSide == PlaybackSide.Restyled;
        }
    }

    /// <summary>
    /// Starts playback from a tick, or moves the playhead there if stopped.
    /// </summary>
    /// <remarks>
    /// Clicking a point in the roll and hearing from there is the single most useful thing a
    /// transport can do while you are comparing tunings - it lets you re-hear one phrase repeatedly
    /// instead of waiting through the piece. Preparing audio lazily here means a click works as the
    /// very first action on a freshly opened file.
    /// </remarks>
    public void SeekToTicks(double ticks)
    {
        if (_engine is not { IsAvailable: true } engine || Project is null)
        {
            return;
        }

        if (!engine.IsLoaded && !PrepareAudio())
        {
            return;
        }

        TimeSpan target = TimeForTicks(Math.Max(0, ticks));
        engine.Seek(target);
        PlayheadTicks = Math.Max(0, ticks);
        SyncHearingFromEngine();
    }

    /// <summary>
    /// Converts a tick to wall-clock time, integrating the tempo map.
    /// </summary>
    /// <remarks>The inverse of <see cref="TicksFor"/>. Zero for a SMPTE file, which has no tempo map.</remarks>
    public TimeSpan TimeForTicks(double ticks)
    {
        if (Project is not { } project || project.Division is not TicksPerQuarterNote ppqn
            || ppqn.Ticks <= 0)
        {
            return TimeSpan.Zero;
        }

        const int DefaultMicrosecondsPerQuarter = 500_000;
        double seconds = 0;
        long cursor = 0;
        int tempo = DefaultMicrosecondsPerQuarter;

        foreach (TempoChange change in project.TempoMap.OrderBy(t => t.Ticks))
        {
            if (change.Ticks >= ticks)
            {
                break;
            }

            seconds += (change.Ticks - cursor) / (double)ppqn.Ticks * tempo / 1_000_000.0;
            cursor = change.Ticks;
            tempo = change.MicrosecondsPerQuarterNote;
        }

        seconds += (ticks - cursor) / (double)ppqn.Ticks * tempo / 1_000_000.0;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Called from the UI thread's timer; never from the engine's own thread.</summary>
    public void SamplePlayhead()
    {
        if (_engine is not { IsAvailable: true, IsLoaded: true } engine || !engine.IsPlaying)
        {
            return;
        }

        PlayheadTicks = TicksFor(engine.Position);
        IsPlaying = engine.IsPlaying;
    }

    /// <summary>
    /// Converts a wall-clock position to ticks, integrating the tempo map.
    /// </summary>
    /// <remarks>
    /// Only correct for a musical timebase. A SMPTE-timed file has no tempo map by definition, so
    /// the playhead is left hidden rather than drawn in the wrong place.
    /// </remarks>
    private double TicksFor(TimeSpan position)
    {
        if (Project is not { } project || project.Division is not TicksPerQuarterNote ppqn)
        {
            return -1;
        }

        const int DefaultMicrosecondsPerQuarter = 500_000;
        double remaining = position.TotalSeconds;
        double ticks = 0;
        int tempo = DefaultMicrosecondsPerQuarter;
        long cursor = 0;

        foreach (TempoChange change in project.TempoMap.OrderBy(t => t.Ticks))
        {
            double segmentSeconds = (change.Ticks - cursor) / (double)ppqn.Ticks * tempo / 1_000_000.0;
            if (segmentSeconds >= remaining)
            {
                break;
            }

            remaining -= segmentSeconds;
            ticks = change.Ticks;
            cursor = change.Ticks;
            tempo = change.MicrosecondsPerQuarterNote;
        }

        return ticks + (remaining * 1_000_000.0 / tempo * ppqn.Ticks);
    }

    /// <summary>
    /// For the one input that is not an observable property: the engine, handed over once.
    /// </summary>
    private void RaiseTransportState()
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanCompare));
        OnPropertyChanged(nameof(PlayDisabledReason));
        OnPropertyChanged(nameof(CompareDisabledReason));
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseLabel));
        OnPropertyChanged(nameof(CanStop));
    }

    partial void OnPlayheadTicksChanged(double value) => OnPropertyChanged(nameof(CanStop));

    partial void OnHearingRestyledChanged(bool value) => OnPropertyChanged(nameof(AbLabel));

    partial void OnRestyleChanged(RestyleResult? value)
    {
        // Unload, not just stop. Stopping leaves the previous sequences loaded, so the next Play
        // replays the transform - or the file - the user has just moved on from. That was a real bug:
        // opening a second file and pressing Play sounded the first one.
        InvalidateAudio();
        RaiseTransportState();
    }

    partial void OnProjectChanged(MidiProject? value) => InvalidateAudio();

    /// <summary>
    /// Whether an export is in progress. Drives the progress indicator and disables the transport.
    /// </summary>
    /// <remarks>
    /// Export is fast for a small file and not for a dense one: it walks every note, builds a
    /// `MidiFile`, and compresses. Doing that on the UI thread freezes the window with no explanation,
    /// which is indistinguishable from a hang.
    /// </remarks>
    [ObservableProperty]
    private bool _isExporting;

    /// <summary>Discards prepared audio, because what it was prepared from has changed.</summary>
    private void InvalidateAudio()
    {
        _engine?.Unload();
        IsPlaying = false;
        PlayheadTicks = -1;

        // The engine forgets which side it was on; the user's preference is deliberately kept, and
        // reapplied by PrepareAudio. What is shown falls back to the original because that is what an
        // unloaded engine would play.
        HearingRestyled = false;
    }

    /// <summary>Clears the restyled layer, leaving the original showing.</summary>
    public void ClearRestyle()
    {
        Restyle = null;
        Allocation = null;
        RestyledRollNotes = [];
        Status.ClearTransformNotices();
    }

    private static RollNote[] ToRollNotes(RestyleResult result)
    {
        Note[] flat = RestyleEngine.FlattenSortedByStart(result);
        var notes = new RollNote[flat.Length];

        for (int i = 0; i < flat.Length; i++)
        {
            notes[i] = new RollNote(
                flat[i].StartTicks, flat[i].LengthTicks, flat[i].Pitch.Cents, flat[i].Velocity);
        }

        return notes;
    }

    /// <summary>Total length in ticks, for the horizontal scrollbar's extent.</summary>
    public long DurationTicks => Project?.DurationTicks ?? 0;

    /// <summary>The suggested initial viewport: centred on the music, not on the whole MIDI range.</summary>
    public (double TopCents, double PixelsPerTick) SuggestedViewport { get; private set; } = (9600, 0.06);

    private void RebuildRollNotes(MidiProject loaded)
    {
        var notes = new List<RollNote>(loaded.TotalNoteCount);
        foreach (TrackInfo track in loaded.Tracks)
        {
            foreach (Note note in track.Notes)
            {
                notes.Add(new RollNote(
                    note.StartTicks, note.LengthTicks, note.Pitch.Cents, note.Velocity));
            }
        }

        notes.Sort(static (a, b) => a.StartTicks.CompareTo(b.StartTicks));
        SourceRollNotes = [.. notes];

        // Frame the music: put the highest note near the top and fit the piece across the width.
        double top = loaded.HighestPitch is { } high ? high.Cents + 400 : 9600;
        double span = Math.Max(1, loaded.DurationTicks);
        SuggestedViewport = (top, Math.Clamp(1200.0 / span, 0.002, 4.0));
    }

    /// <summary>
    /// Persists preferences, reporting rather than throwing when the location is read-only.
    /// </summary>
    /// <remarks>
    /// A read-only install directory is an expected state for this app, not an error - it is what a
    /// USB stick or Program Files looks like. Failing to save must therefore be a status message,
    /// never an exception on the shutdown path.
    /// </remarks>
    public void SaveSettings(double windowWidth, double windowHeight)
    {
        if (_settings is null)
        {
            return;
        }

        AppSettings updated = Settings with
        {
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            LastOpenedFolder = Project?.FilePath is { } path
                ? Path.GetDirectoryName(path)
                : Settings.LastOpenedFolder,
        };

        SettingsSaveResult result = _settings.Save(updated);
        if (result.Success)
        {
            Settings = updated;
        }
        else
        {
            Status.Report(result.Reason, StatusSeverity.Warning);
        }
    }
}
