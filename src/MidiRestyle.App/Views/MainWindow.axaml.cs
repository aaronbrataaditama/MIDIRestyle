using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.ComponentModel;
using Avalonia.Threading;
using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Notation;
using MidiRestyle.App.Controls;
using MidiRestyle.App.Services;
using MidiRestyle.Playback;
using System.Diagnostics;
using MidiRestyle.App.ViewModels;

namespace MidiRestyle.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The picker is injected as a delegate rather than referenced from the view model, so the
        // view model stays headlessly testable - no storage provider, no window, no Avalonia.
        _viewModel = new MainWindowViewModel(PickMidiFileAsync, new SettingsService());
        DataContext = _viewModel;

        if (_viewModel.Settings is { WindowWidth: > 0, WindowHeight: > 0 } size)
        {
            Width = size.WindowWidth;
            Height = size.WindowHeight;
        }

        // The roll takes arrays through a method rather than a binding, because setting them also
        // recomputes the longest-note figure that culling needs. Pushing that through a converter
        // would hide a real precondition.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Coalesces the burst of selection changes a held arrow key produces. A timer rather than a
        // rebuild-completion callback because the transform is already comfortably inside a frame;
        // if the sequence rebuild in phase 10 turns out to cost more than the interval, coalescing
        // on completion is the better fix and this is where it goes.
        _restyleDebounce = new DispatcherTimer { Interval = StylePanelViewModel.SelectionDebounce };
        _restyleDebounce.Tick += (_, _) =>
        {
            _restyleDebounce!.Stop();
            _viewModel.ReapplyFromStylePanel();
        };

        if (Avalonia.Application.Current is App startupApp)
        {
            _viewModel.ThemePreference = startupApp.ThemeService.Current;
        }

        BuildStylePanel();

        // No MIDI device is a normal state, not an error: the factory hands back a null engine and
        // the app stays fully functional minus audio.
        _viewModel.AttachEngine(PlaybackEngineFactory.Create());

        // The engine raises playhead changes on a background thread, so nothing is marshalled from
        // it. The UI samples instead, at 60 Hz, which is the plan's rule: never per MIDI event.
        _playheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0) };
        _playheadTimer.Tick += (_, _) =>
        {
            _viewModel.SamplePlayhead();
            PianoRollView.PlayheadTicks = _viewModel.PlayheadTicks;
            // The roll draws a fractional playhead because it scrolls continuously; the notation
            // views place it against a discrete tick, so it rounds here.
            long playheadTicks = (long)Math.Round(_viewModel.PlayheadTicks);
            StaffNotationView.PlayheadTicks = playheadTicks;
            DegreeNotationView.PlayheadTicks = playheadTicks;

            // Follow only while playing. Auto-scrolling a stopped roll would fight the user the
            // moment they scrolled somewhere deliberately.
            if (_viewModel.IsPlaying)
            {
                PianoRollView.FollowPlayhead();

                // The staff follows too, and its follow moves down the page to whichever system the
                // playhead has entered. Without this call the red line simply ran off the system and
                // was never seen again - the view had no follow method at all until now.
                StaffNotationView.FollowPlayhead();
            }

            SyncScrollBars();
        };
        _playheadTimer.Start();

        PianoRollView.PointerPressed += OnRollPointerPressed;
        PianoRollView.PointerReleased += OnRollPointerReleased;
        StaffNotationView.PointerPressed += OnStaffPointerPressed;
        StaffNotationView.PointerReleased += OnStaffPointerReleased;
    }

    private readonly DispatcherTimer _playheadTimer;

    private readonly DispatcherTimer _restyleDebounce;

    /// <summary>
    /// Assembles the scale library and hands the rail a view model over it.
    /// </summary>
    /// <remarks>
    /// Runs here rather than in the view model because <c>ScaleLibraryService</c> reads embedded
    /// assets through Avalonia's <c>AssetLoader</c>, which throws without an initialised runtime.
    /// A failure is reported, not thrown: a corrupt user scale file must not stop the app opening.
    /// </remarks>
    private void BuildStylePanel()
    {
        try
        {
            ScaleLibraryLoadResult loaded = new ScaleLibraryService().Load();

            StylePanelViewModel panel = new(loaded.Library);
            panel.PropertyChanged += OnStylePanelPropertyChanged;
            _viewModel.StylePanel = panel;

            if (loaded.Failures.Count > 0)
            {
                _viewModel.Status.Report(
                    $"{loaded.Failures.Count} scale(s) could not be loaded: "
                    + string.Join("; ", loaded.Failures.Take(3).Select(f => $"{f.Id} - {f.Reason}")),
                    StatusSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            _viewModel.Status.Report(
                $"The scale library could not be loaded: {ex.Message}", StatusSeverity.Error);
        }
    }

    private void OnStylePanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any choice that changes the output restarts the debounce. Search text and the disclosure
        // toggle deliberately do not - they change what you are looking at, not what you hear.
        if (e.PropertyName is nameof(StylePanelViewModel.SearchText)
            or nameof(StylePanelViewModel.IsPoliciesExpanded)
            or nameof(StylePanelViewModel.Items))
        {
            return;
        }

        _restyleDebounce.Stop();
        _restyleDebounce.Start();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.RestyledRollNotes))
        {
            // The solid layer. Ghosts stay put underneath, so the transform is visible as movement
            // rather than as a replacement.
            PianoRollView.SetNotes(_viewModel.RestyledRollNotes);
            return;
        }

        // The lit keys must show what is sounding, not what is drawn solid, so they follow the A/B
        // toggle rather than the layer.
        if (e.PropertyName == nameof(MainWindowViewModel.HearingRestyled))
        {
            PianoRollView.HighlightRestyled = _viewModel.HearingRestyled;
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.Metadata))
        {
            PianoRollView.SetBars(_viewModel.BarStartTicks);
            return;
        }

        if (e.PropertyName != nameof(MainWindowViewModel.SourceRollNotes))
        {
            return;
        }

        PianoRollView.SetGhostNotes(_viewModel.SourceRollNotes);

        // Key detection is a suggestion that seeds the tonic, never a silent decision - the panel
        // surfaces the candidates and the user can override every part of it.
        if (_viewModel.Project is { } project && _viewModel.StylePanel is { } panel)
        {
            panel.ApplyDetectedKey(KeyDetector.Detect(project));
        }

        // Frame the newly loaded piece rather than leaving the previous file's viewport.
        (double topCents, double pixelsPerTick) = _viewModel.SuggestedViewport;
        PianoRollView.TopCents = topCents;
        PianoRollView.PixelsPerTick = pixelsPerTick;
        PianoRollView.ScrollTicks = 0;
        SyncScrollBars();
    }

    private readonly MainWindowViewModel _viewModel;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _viewModel.SaveSettings(Width, Height);
        base.OnClosing(e);
    }

    private async Task<string?> PickMidiFileAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Open a MIDI file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("MIDI files")
                    {
                        Patterns = ["*.mid", "*.midi", "*.rmi"],
                        MimeTypes = ["audio/midi"],
                    },
                    FilePickerFileTypes.All,
                ],
            });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    // ---- appearance ----------------------------------------------------------------------------

    private void OnThemeSystemClicked(object? sender, RoutedEventArgs e) =>
        SetTheme(ThemePreference.System);

    private void OnThemeLightClicked(object? sender, RoutedEventArgs e) =>
        SetTheme(ThemePreference.Light);

    private void OnThemeDarkClicked(object? sender, RoutedEventArgs e) =>
        SetTheme(ThemePreference.Dark);

    /// <summary>
    /// Records the choice and lets <c>App</c> apply it.
    /// </summary>
    /// <remarks>
    /// The service raises <c>ThemeChanged</c> and <c>App</c> re-applies, so this never touches
    /// <c>Application.Current</c> itself - one place decides how a preference becomes a variant.
    /// A failed save is reported, not thrown: read-only media is an expected state, and the theme
    /// still applies for this session even when it cannot be remembered for the next one.
    /// </remarks>
    private void SetTheme(ThemePreference preference)
    {
        if (Avalonia.Application.Current is not App app)
        {
            return;
        }

        SettingsSaveResult saved = app.ThemeService.SetTheme(preference);
        _viewModel.ThemePreference = app.ThemeService.Current;

        if (!saved.Success)
        {
            _viewModel.Status.Report(
                $"The appearance was changed but could not be remembered: {saved.Reason}",
                StatusSeverity.Warning);
        }
    }

    // ---- click to seek -------------------------------------------------------------------------

    /// <summary>How far the pointer may move and still count as a click rather than a drag.</summary>
    private const double ClickSlopPixels = 4.0;

    private Point? _rollPressPoint;

    private Point? _staffPressPoint;

    /// <summary>
    /// Distinguishes a click from a pan.
    /// </summary>
    /// <remarks>
    /// The roll already uses left-drag to scroll, so a naive "seek on release" would jump the
    /// playhead every time the user finished panning. Recording the press point and comparing on
    /// release separates the two intents. Handled here rather than inside <c>PianoRoll</c> because
    /// seeking is a transport concern the control has no business knowing about.
    /// </remarks>
    private void OnRollPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _rollPressPoint = e.GetCurrentPoint(PianoRollView).Properties.IsLeftButtonPressed
            ? e.GetPosition(PianoRollView)
            : null;
    }

    private void OnRollPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_rollPressPoint is not { } pressed)
        {
            return;
        }

        _rollPressPoint = null;

        Point released = e.GetPosition(PianoRollView);
        if (Math.Abs(released.X - pressed.X) > ClickSlopPixels
            || Math.Abs(released.Y - pressed.Y) > ClickSlopPixels)
        {
            // A pan, not a click. Leave the playhead alone.
            return;
        }

        if (PianoRollView.PixelsPerTick <= 0)
        {
            return;
        }

        // The keyboard column is not part of the timeline, so a click on it is not a seek - and a
        // click just right of it is tick zero, not GutterWidth pixels' worth of ticks into the piece.
        if (released.X < PianoRoll.GutterWidth || released.Y < PianoRoll.RulerHeight)
        {
            return;
        }

        double ticks = PianoRollView.ScrollTicks
            + ((released.X - PianoRoll.GutterWidth) / PianoRollView.PixelsPerTick);
        _viewModel.SeekToTicks(ticks);
        PianoRollView.PlayheadTicks = _viewModel.PlayheadTicks;
        SyncScrollBars();
    }

    // ---- staff click-to-seek -------------------------------------------------------------------

    private void OnStaffPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _staffPressPoint = e.GetCurrentPoint(StaffNotationView).Properties.IsLeftButtonPressed
            ? e.GetPosition(StaffNotationView)
            : null;
    }

    /// <summary>
    /// Seeks to the clicked position in the score.
    /// </summary>
    /// <remarks>
    /// The same gesture as the piano roll's, deliberately: the two views show the same music against
    /// the same transport, so clicking a bar has to mean the same thing in both. The slop check is
    /// what stops a drag - or a wheel-scroll that happens to land a click - from being read as a
    /// seek, and it is measured on release rather than press for that reason.
    /// </remarks>
    private void OnStaffPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_staffPressPoint is not { } pressed)
        {
            return;
        }

        _staffPressPoint = null;

        Point released = e.GetPosition(StaffNotationView);
        if (Math.Abs(released.X - pressed.X) > ClickSlopPixels
            || Math.Abs(released.Y - pressed.Y) > ClickSlopPixels)
        {
            return;
        }

        if (!StaffNotationView.TryTickAt(released, out long tick))
        {
            return;
        }

        _viewModel.SeekToTicks(tick);

        // Straight away rather than waiting on the 60 Hz timer: a click that leaves the playhead
        // where it was for a frame reads as a click that did not register.
        long moved = (long)Math.Round(_viewModel.PlayheadTicks);
        StaffNotationView.PlayheadTicks = moved;
        DegreeNotationView.PlayheadTicks = moved;
        PianoRollView.PlayheadTicks = _viewModel.PlayheadTicks;
    }

    // ---- roll scrolling ------------------------------------------------------------------------

    private bool _syncingScrollBars;

    /// <summary>
    /// Pushes the roll's viewport into the scrollbars.
    /// </summary>
    /// <remarks>
    /// One-way, from the roll outward. The roll is the source of truth for the viewport - it is also
    /// scrolled by dragging, by the wheel, by zooming and by following the playhead - so the bars
    /// reflect it rather than the other way round. The guard flag stops a bar's own Scroll event,
    /// raised while we are writing to it, from being read back as a user gesture.
    /// </remarks>
    private void SyncScrollBars()
    {
        if (_viewModel.Project is null)
        {
            return;
        }

        _syncingScrollBars = true;
        try
        {
            double visibleTicks = PianoRollView.VisibleTicks;
            double totalTicks = Math.Max(_viewModel.DurationTicks, visibleTicks);

            HorizontalRollScroll.Minimum = 0;
            HorizontalRollScroll.Maximum = Math.Max(0, totalTicks - visibleTicks);
            HorizontalRollScroll.ViewportSize = visibleTicks;
            HorizontalRollScroll.LargeChange = visibleTicks * 0.9;
            HorizontalRollScroll.SmallChange = visibleTicks * 0.1;
            HorizontalRollScroll.Value =
                Math.Clamp(PianoRollView.ScrollTicks, 0, HorizontalRollScroll.Maximum);

            // Vertical runs in cents and is inverted: a scrollbar's zero is at the top, and the top
            // of a piano roll is the HIGHEST pitch.
            double visibleCents = PianoRollView.VisibleCents;
            const double TopCentsLimit = 13_200;

            VerticalRollScroll.Minimum = 0;
            VerticalRollScroll.Maximum = Math.Max(0, TopCentsLimit - visibleCents);
            VerticalRollScroll.ViewportSize = visibleCents;
            VerticalRollScroll.LargeChange = visibleCents * 0.9;
            VerticalRollScroll.SmallChange = visibleCents * 0.1;
            VerticalRollScroll.Value =
                Math.Clamp(TopCentsLimit - PianoRollView.TopCents, 0, VerticalRollScroll.Maximum);

            // The staff is laid out as wrapped systems down a page, so its bar is vertical and
            // measured in pixels. The degree view is a wheel - it has nothing to scroll.
            double page = StaffNotationView.Bounds.Height;
            double content = StaffNotationView.ContentHeight;

            StaffScroll.Minimum = 0;
            StaffScroll.Maximum = Math.Max(0, content - page);
            StaffScroll.ViewportSize = page;
            StaffScroll.LargeChange = page * 0.9;
            StaffScroll.SmallChange = page * 0.1;
            StaffScroll.Value = Math.Clamp(StaffNotationView.ScrollY, 0, StaffScroll.Maximum);
        }
        finally
        {
            _syncingScrollBars = false;
        }
    }

    private void OnStaffScroll(object? sender, ScrollEventArgs e)
    {
        if (_syncingScrollBars)
        {
            return;
        }

        StaffNotationView.ScrollY = StaffScroll.Value;
    }

    private void OnHorizontalRollScroll(object? sender, ScrollEventArgs e)
    {
        if (_syncingScrollBars)
        {
            return;
        }

        PianoRollView.ScrollTicks = HorizontalRollScroll.Value;
    }

    private void OnVerticalRollScroll(object? sender, ScrollEventArgs e)
    {
        if (_syncingScrollBars)
        {
            return;
        }

        PianoRollView.TopCents = 13_200 - VerticalRollScroll.Value;
    }

    // ---- export ------------------------------------------------------------------------------

    /// <summary>
    /// Writes the restyled file, using the same allocation playback used.
    /// </summary>
    /// <remarks>
    /// Deliberately routed through the allocation the view model already planned rather than a fresh
    /// one: preview and export must agree, and planning twice is the one way they could stop agreeing.
    /// </remarks>
    /// <summary>
    /// Exports the notated score as MusicXML.
    /// </summary>
    /// <remarks>
    /// Deliberately shaped like the MIDI export beside it, but gated differently. MIDI export needs
    /// only a restyle result; MusicXML needs a score that can actually be written on a staff, so the
    /// menu entry is disabled outright when it cannot be - and the guard here still states a reason
    /// rather than trusting the menu to have been right.
    /// </remarks>
    private async void OnExportMusicXmlClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.Score is not { } score || !_viewModel.CanExportMusicXml)
        {
            _viewModel.Status.Report(
                _viewModel.StaffUnavailableReason ?? "There is nothing to export yet.",
                StatusSeverity.Warning);
            return;
        }

        string suggested = _viewModel.Metadata?.FileName is { } name
            ? $"{Path.GetFileNameWithoutExtension(name)} - {_viewModel.NotationScale?.Name}.musicxml"
            : "restyled.musicxml";

        IStorageFile? target = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export the restyled score as MusicXML",
                SuggestedFileName = SanitiseFileName(suggested),
                DefaultExtension = "musicxml",
                FileTypeChoices =
                [
                    new FilePickerFileType("MusicXML files") { Patterns = ["*.musicxml", "*.xml"] },
                ],
            }).ConfigureAwait(true);

        if (target?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        _viewModel.IsExporting = true;
        _viewModel.Status.Report($"Exporting {Path.GetFileName(path)}...");

        try
        {
            await Task.Run(() => MusicXmlExporter.Write(score, path)).ConfigureAwait(true);

            int measures = score.MeasureCount;
            _viewModel.Status.Report(
                $"Exported {Path.GetFileName(path)} - {score.Parts.Count} "
                + $"part{(score.Parts.Count == 1 ? "" : "s")}, {measures} "
                + $"measure{(measures == 1 ? "" : "s")}.");
        }
        catch (MusicXmlExportException ex)
        {
            _viewModel.Status.Report(ex.Message, StatusSeverity.Error);
        }
        finally
        {
            _viewModel.IsExporting = false;
        }
    }

    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.Restyle is not { } restyled)
        {
            _viewModel.Status.Report(
                "Choose a target scale first - there is nothing to export yet.",
                StatusSeverity.Warning);
            return;
        }

        string suggested = _viewModel.Metadata?.FileName is { } name
            ? $"{Path.GetFileNameWithoutExtension(name)} - {restyled.Settings.TargetScale.Name}.mid"
            : "restyled.mid";

        IStorageFile? target = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export the restyled MIDI file",
                SuggestedFileName = SanitiseFileName(suggested),
                DefaultExtension = "mid",
                FileTypeChoices =
                [
                    new FilePickerFileType("MIDI files") { Patterns = ["*.mid"] },
                ],
            }).ConfigureAwait(true);

        if (target?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        // Off the UI thread: export walks every note, builds a MidiFile and compresses it, which on
        // a dense piece is long enough that doing it inline would freeze the window with no
        // explanation - indistinguishable from a hang.
        _viewModel.IsExporting = true;
        _viewModel.Status.Report($"Exporting {Path.GetFileName(path)}...");

        try
        {
            var allocation = _viewModel.Allocation;

            ExportResult result = await Task.Run(() => allocation is not null
                ? MidiFileExporter.Export(restyled, path, allocation)
                : MidiFileExporter.Export(restyled, path)).ConfigureAwait(true);

            if (!result.Success)
            {
                // A refusal is a stated reason, not a crash - an out-of-range note or a microtonal
                // result with no allocation are both things the user can act on.
                _viewModel.Status.Report(
                    result.Message ?? "The file could not be exported.", StatusSeverity.Error);
                return;
            }

            string channels = allocation is { } used
                ? $" on {used.ChannelCount} channel{(used.ChannelCount == 1 ? "" : "s")}"
                : string.Empty;

            _viewModel.Status.Report($"Exported {Path.GetFileName(path)}{channels}.");
        }
        catch (MidiFileExportException ex)
        {
            _viewModel.Status.Report(ex.Message, StatusSeverity.Error);
        }
        finally
        {
            // In a finally block on purpose: a spinner left running after a failure is its own bug.
            _viewModel.IsExporting = false;
        }
    }

    /// <summary>Strips what a scale name may legitimately contain but a filename may not.</summary>
    private static string SanitiseFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '-' : c));
    }

    // ---- Scales menu -------------------------------------------------------------------------

    private async void OnNewScaleClicked(object? sender, RoutedEventArgs e)
    {
        ScaleEditorViewModel editor = new();
        editor.LoadForNew();
        await ShowEditorAsync(editor).ConfigureAwait(true);
    }

    /// <summary>
    /// Edits whatever the rail has selected.
    /// </summary>
    /// <remarks>
    /// A shipped scale is copy-on-edit: the editor produces a <c>user.*</c> copy rather than mutating
    /// the original, so the app's own data stays intact and the override is visible as an override.
    /// The editor explains that itself when it applies.
    /// </remarks>
    private async void OnEditScaleClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.StylePanel?.SelectedScale is not { } selected)
        {
            _viewModel.Status.Report(
                "Select a scale in the list first, then choose Edit.", StatusSeverity.Warning);
            return;
        }

        ScaleOrigin origin =
            _viewModel.StylePanel.SourceScaleOptions
                .FirstOrDefault(entry => entry.Id == selected.Id)?.Origin
            ?? ScaleOrigin.Embedded;

        ScaleEditorViewModel editor = new();
        editor.LoadForEdit(selected, origin);
        await ShowEditorAsync(editor).ConfigureAwait(true);
    }

    private async Task ShowEditorAsync(ScaleEditorViewModel editor)
    {
        ScaleEditorWindow window = new(editor);
        await window.ShowDialog(this).ConfigureAwait(true);

        if (!window.LibraryChanged)
        {
            return;
        }

        // A written scale changes the library, so rebuild it. Cheap enough to do wholesale, and far
        // less error-prone than trying to splice one entry into a merged, precedence-ordered set.
        string? previouslySelected = _viewModel.StylePanel?.SelectedScale?.Id;
        BuildStylePanel();

        if (previouslySelected is not null && _viewModel.StylePanel is { } panel)
        {
            panel.SelectedScale = panel.FilteredScales.FirstOrDefault(s => s.Id == previouslySelected)
                ?? panel.SelectedScale;
        }
    }

    private async void OnImportScalaClicked(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import a Scala tuning file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Scala tuning files") { Patterns = ["*.scl"] },
                    FilePickerFileTypes.All,
                ],
            }).ConfigureAwait(true);

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        ScalaImportResult imported = ScalaFileReader.ReadFromFile(path);

        if (!imported.Success)
        {
            // Malformed .scl files are user input, not programmer error, so the reason is shown
            // rather than thrown. The reader's own message names the offending line or rule.
            _viewModel.Status.Report(
                $"Could not import {Path.GetFileName(path)}: {imported.Error?.Message}",
                StatusSeverity.Error);
            return;
        }

        // Hand it straight to the editor rather than saving silently: an imported tuning arrives
        // with no name worth showing and Notatable defaulted to false, and the user should see and
        // confirm both before it joins their library.
        ScaleEditorViewModel editor = new();
        editor.LoadForEdit(imported.Scale!, ScaleOrigin.UserDefined);
        editor.Name = Path.GetFileNameWithoutExtension(path);
        await ShowEditorAsync(editor).ConfigureAwait(true);
    }

    /// <summary>Opens the folder the library actually reads from, wherever that turned out to be.</summary>
    private void OnOpenScalesFolderClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            ScaleLibraryLoadResult loaded = new ScaleLibraryService().Load();
            Directory.CreateDirectory(loaded.ScalesDirectory);

            Process.Start(new ProcessStartInfo
            {
                FileName = loaded.ScalesDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _viewModel.Status.Report(
                $"Could not open the scales folder: {ex.Message}", StatusSeverity.Error);
        }
    }

    // ---- Help menu ---------------------------------------------------------------------------

    private async void OnAboutClicked(object? sender, RoutedEventArgs e) =>
        await new AboutWindow().ShowDialog(this).ConfigureAwait(true);

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();
}
