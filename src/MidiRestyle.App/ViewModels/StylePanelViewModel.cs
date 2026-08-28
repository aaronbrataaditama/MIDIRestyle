using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.ViewModels;

/// <summary>
/// One control inside the <c>Mapping &amp; policies</c> disclosure.
/// </summary>
/// <remarks>
/// Enumerated rather than left implicit in the XAML because the membership of this disclosure is a
/// design decision a later edit could quietly reverse. In particular the <em>target tonic</em> is
/// deliberately absent: it is a per-file setting that defaults from the detected key, not a
/// set-once policy, so it sits outside as a peer of the scale list. A test asserts that.
/// </remarks>
public enum PolicyControl
{
    /// <summary>Degree mapping versus nearest-pitch snapping.</summary>
    MappingStrategy,

    /// <summary>What happens to notes that are not in the source scale.</summary>
    NonScaleNotes,

    /// <summary>What happens when two mapped notes land on the same pitch at the same time.</summary>
    Collisions,

    /// <summary>What happens when a mapped note falls outside MIDI 0..127.</summary>
    Range,

    /// <summary>Auto, forced 12-TET, or forced microtonal.</summary>
    OutputMode,

    /// <summary>How far apart two cent-offsets may be and still share a pitch-bend channel.</summary>
    BendTolerance,
}

/// <summary>
/// One row of the target-scale list: either a sticky region header or a selectable scale.
/// </summary>
/// <remarks>
/// Headers and scales share one flat collection because the list is virtualised - a
/// <c>ListBox</c> over a flat source recycles containers, while a nested
/// <c>ItemsControl</c>-of-<c>ItemsControl</c> does not, and this list is the largest element in the
/// rail. <see cref="IsSelectable"/> is what keeps arrow-key browsing from stopping on a header.
/// </remarks>
public sealed class ScaleListItem
{
    private ScaleListItem(string region, int regionCount, Scale? scale, FidelityReport? fidelity)
    {
        Region = region;
        RegionCount = regionCount;
        Scale = scale;
        Fidelity = fidelity;
    }

    /// <summary>A sticky header introducing one region.</summary>
    public static ScaleListItem ForHeader(string region, int regionCount) =>
        new(region, regionCount, null, null);

    /// <summary>A selectable scale row, carrying its own precomputed fidelity badge.</summary>
    public static ScaleListItem ForScale(Scale scale, FidelityReport fidelity)
    {
        ArgumentNullException.ThrowIfNull(scale);
        ArgumentNullException.ThrowIfNull(fidelity);
        return new ScaleListItem(scale.Region, 0, scale, fidelity);
    }

    /// <summary>The region this row belongs to, header or not - so a header can be styled sticky.</summary>
    public string Region { get; }

    /// <summary>How many scales the header covers. Zero on a scale row.</summary>
    public int RegionCount { get; }

    /// <summary>The scale, or null when this row is a header.</summary>
    public Scale? Scale { get; }

    /// <summary>What 12-TET costs for this scale. Null on a header.</summary>
    public FidelityReport? Fidelity { get; }

    public bool IsHeader => Scale is null;

    /// <summary>Whether selection may land here. False for headers.</summary>
    public bool IsSelectable => Scale is not null;

    /// <summary>The primary line: the region name on a header, the scale name on a row.</summary>
    public string Text => Scale?.Name ?? Region;

    /// <summary>The secondary line: the region's size on a header, the tradition on a row.</summary>
    public string Detail => Scale is { } scale
        ? scale.Tradition
        : $"{RegionCount} scale{(RegionCount == 1 ? "" : "s")}";

    /// <summary>The badge chip's text, or null on a header.</summary>
    public string? FidelityLabel => Fidelity?.Label;

    public override string ToString() => IsHeader ? $"[{Region}]" : Text;
}

/// <summary>
/// The right rail: what the file is restyled <em>into</em>, and by what rules.
/// </summary>
/// <remarks>
/// <para>
/// <b>The scale list is not a dropdown.</b> It is the largest element here, always open, grouped by
/// region with sticky headers, and searchable. Browsing scales <em>is</em> this application, so
/// collapsing the list would put a click in front of the only thing users came to do. That decision
/// shapes this type's surface: a flat filtered collection built for a virtualised <c>ListBox</c>,
/// plus <see cref="SearchText"/> that narrows it live.
/// </para>
/// <para>
/// <b>Selection changes are rapid by design.</b> The list is arrow-key browsable with playback
/// running, so the host re-runs the transform per keystroke, debounced at
/// <see cref="SelectionDebounce"/>. The timer lives in the host, not here - a view model that owns a
/// clock cannot be tested without one - but the interval is published so the two cannot disagree
/// about it.
/// </para>
/// <para>
/// Nothing here mutates the project or runs the transform. It holds choices; the host turns them
/// into a <see cref="RestyleSettings"/> via <see cref="BuildSettings"/> and feeds
/// <c>RestyleEngine</c>.
/// </para>
/// </remarks>
public sealed partial class StylePanelViewModel : ObservableObject
{
    /// <summary>
    /// How long the host should wait after the last selection change before re-running the
    /// transform.
    /// </summary>
    /// <remarks>
    /// A held arrow key repeats far faster than a playback sequence can be rebuilt and re-seeked.
    /// The 16 ms transform budget covers the transform alone, not the rebuild behind it, so
    /// keystroke spam has to be collapsed somewhere - here, once, rather than in each caller.
    /// </remarks>
    public static TimeSpan SelectionDebounce { get; } = TimeSpan.FromMilliseconds(150);

    /// <summary>The tonic octave that puts pitch class 0 at middle C, MIDI 60.</summary>
    public const int DefaultTonicOctave = 4;

    /// <summary>Narrowest useful bend-grouping tolerance. Below this, rounding artefacts buy channels.</summary>
    public const double MinToleranceCents = 0.5;

    /// <summary>Widest tolerance the escalation ladder ever reaches - half a semitone.</summary>
    public const double MaxToleranceCents = 50.0;

    /// <summary>
    /// The library id used to seed the source scale from a detected major key.
    /// </summary>
    /// <remarks>
    /// Ionian and Aeolian rather than "major" and "minor" because that is what the library ships.
    /// Probed by id and skipped when absent, so a trimmed or user-replaced library degrades to
    /// "leave the user's choice alone" rather than throwing.
    /// </remarks>
    public const string MajorSourceScaleId = "europe.churchmodes.ionian";

    /// <inheritdoc cref="MajorSourceScaleId"/>
    public const string MinorSourceScaleId = "europe.churchmodes.aeolian";

    /// <summary>
    /// Exactly what the <c>Mapping &amp; policies</c> disclosure contains: the five set-once
    /// policies plus the bend tolerance. The target tonic is <em>not</em> among them.
    /// </summary>
    public static IReadOnlyList<PolicyControl> DisclosureControls { get; } =
    [
        PolicyControl.MappingStrategy,
        PolicyControl.NonScaleNotes,
        PolicyControl.Collisions,
        PolicyControl.Range,
        PolicyControl.OutputMode,
        PolicyControl.BendTolerance,
    ];

    private readonly ScaleLibrary _library;
    private readonly Dictionary<string, FidelityReport> _fidelityCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ScaleListItem> _items = [];
    private Scale[] _filteredScales = [];
    private bool _syncingTonic;

    public StylePanelViewModel(ScaleLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;

        Items = new ReadOnlyObservableCollection<ScaleListItem>(_items);
        RebuildList();
    }

    // ------------------------------------------------------------------ the scale list

    /// <summary>
    /// The list as rendered: region headers interleaved with the scales they cover, filtered by
    /// <see cref="SearchText"/>.
    /// </summary>
    public ReadOnlyObservableCollection<ScaleListItem> Items { get; }

    /// <summary>The scales currently shown, without headers, in list order. What arrow keys walk.</summary>
    public IReadOnlyList<Scale> FilteredScales => _filteredScales;

    /// <summary>Every scale the library holds, filtered or not.</summary>
    public int TotalScaleCount => _library.Count;

    /// <summary>How many scales survive the current search.</summary>
    public int MatchCount => _filteredScales.Length;

    public bool HasMatches => _filteredScales.Length > 0;

    /// <summary>The count shown beside the "Target scale" label.</summary>
    public string MatchSummary => string.IsNullOrWhiteSpace(SearchText)
        ? $"{TotalScaleCount} scale{(TotalScaleCount == 1 ? "" : "s")}"
        : $"{MatchCount} of {TotalScaleCount}";

    /// <summary>
    /// Free text across name, tradition, region and id. Filters live, on every keystroke.
    /// </summary>
    /// <remarks>
    /// Multiple whitespace-separated terms <em>narrow</em> - all must match - so "gong pyth" finds
    /// Pythagorean Gong. Widening on extra terms would make typing more of a name return more
    /// results, which is the opposite of what a search box promises.
    /// </remarks>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The chosen target scale. The most-changed property in the app.</summary>
    [ObservableProperty]
    private Scale? _selectedScale;

    /// <summary>
    /// The selected row, for two-way binding to the <c>ListBox</c>.
    /// </summary>
    /// <remarks>
    /// Kept in step with <see cref="SelectedScale"/> in both directions so the host may set either.
    /// A header lands here only if the view lets selection reach one; when it does, the scale
    /// selection is left alone rather than cleared - a stray click on a header must not silently
    /// undo the user's choice and blank the transform.
    /// </remarks>
    [ObservableProperty]
    private ScaleListItem? _selectedItem;

    partial void OnSearchTextChanged(string value) => RebuildList();

    partial void OnSelectedItemChanged(ScaleListItem? value)
    {
        if (value?.Scale is { } scale)
        {
            SelectedScale = scale;
        }
    }

    partial void OnSelectedScaleChanged(Scale? value)
    {
        if (value is null)
        {
            SelectedItem = null;
        }
        else if (!ReferenceEquals(SelectedItem?.Scale, value))
        {
            SelectedItem = _items.FirstOrDefault(i => ReferenceEquals(i.Scale, value));
        }

        RaiseSelectionDependents();
    }

    /// <summary>Moves the selection one scale down the visible list, skipping headers.</summary>
    /// <returns>Whether the selection moved.</returns>
    public bool SelectNext() => MoveSelection(+1);

    /// <summary>Moves the selection one scale up the visible list, skipping headers.</summary>
    /// <returns>Whether the selection moved.</returns>
    public bool SelectPrevious() => MoveSelection(-1);

    private bool MoveSelection(int delta)
    {
        if (_filteredScales.Length == 0)
        {
            return false;
        }

        int current = SelectedScale is null ? -1 : Array.IndexOf(_filteredScales, SelectedScale);
        int next = current < 0
            ? (delta > 0 ? 0 : _filteredScales.Length - 1)
            : current + delta;

        if (next < 0 || next >= _filteredScales.Length)
        {
            return false;
        }

        SelectedScale = _filteredScales[next];
        return true;
    }

    private void RebuildList()
    {
        // With an empty query Search returns the library in load order, so grouping it reproduces
        // ByRegion exactly. ByRegion is called directly in that case so the two cannot drift.
        IEnumerable<IGrouping<string, Scale>> groups = string.IsNullOrWhiteSpace(SearchText)
            ? _library.ByRegion()
            : GroupByRegion(_library.Search(SearchText));

        _items.Clear();
        List<Scale> flat = [];

        foreach (IGrouping<string, Scale> group in groups)
        {
            Scale[] members = [.. group];
            _items.Add(ScaleListItem.ForHeader(group.Key, members.Length));

            foreach (Scale scale in members)
            {
                _items.Add(ScaleListItem.ForScale(scale, FidelityOf(scale)));
                flat.Add(scale);
            }
        }

        _filteredScales = [.. flat];

        // The selection deliberately survives a filter that hides it: typing in the search box must
        // not change what is playing.
        if (SelectedScale is { } selected && !ReferenceEquals(SelectedItem?.Scale, selected))
        {
            SelectedItem = _items.FirstOrDefault(i => ReferenceEquals(i.Scale, selected));
        }

        OnPropertyChanged(nameof(FilteredScales));
        OnPropertyChanged(nameof(MatchCount));
        OnPropertyChanged(nameof(HasMatches));
        OnPropertyChanged(nameof(MatchSummary));
    }

    /// <summary>
    /// Groups a filtered subset the way <see cref="ScaleLibrary.ByRegion"/> groups the whole
    /// library: largest region first, ties alphabetical, load order preserved within a region.
    /// </summary>
    private static IEnumerable<IGrouping<string, Scale>> GroupByRegion(IEnumerable<Scale> scales) =>
        scales
            .GroupBy(s => s.Region, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

    private FidelityReport FidelityOf(Scale scale)
    {
        // Assessed once per scale and cached: the list is rebuilt on every search keystroke, and
        // re-quantising ~170 scales per character is work with a known answer.
        if (!_fidelityCache.TryGetValue(scale.Id, out FidelityReport? report))
        {
            report = TuningFidelity.Assess(scale);
            _fidelityCache[scale.Id] = report;
        }

        return report;
    }

    // ------------------------------------------------------------------ the fidelity badge

    /// <summary>What 12-TET costs for the selected scale, or null when nothing is selected.</summary>
    public FidelityReport? Fidelity => SelectedScale is { } scale ? FidelityOf(scale) : null;

    /// <summary>The badge chip's text, e.g. <c>Close</c>. Empty when nothing is selected.</summary>
    public string FidelityLabel => Fidelity?.Label ?? string.Empty;

    /// <summary>The calm one-liner shown at all times, e.g. "Up to 40 cents from 12-TET".</summary>
    public string FidelityDescription => Fidelity?.Describe() ?? string.Empty;

    /// <summary>
    /// Whether the badge should read as a <em>warning</em> rather than as neutral information.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule the design says is easy to get backwards, so the decision is not remade here:
    /// <see cref="FidelityReport.IsWarningIn"/> owns it. Deviation from 12-TET is a fact about a
    /// tuning, shown calmly always. It escalates only when output is actually on the semitone grid
    /// <em>and</em> the scale exceeds 25 cents - the one state where the app is failing to deliver
    /// the scale the user picked. Inverted, it cries wolf on every maqam, or goes silent exactly
    /// when the user is being short-changed.
    /// </para>
    /// <para>
    /// Under <see cref="Core.Output.OutputMode.Auto"/> the question is settled by Auto's own test -
    /// <c>max|offset| &lt;= tolerance</c> - rather than assumed either way, so a near-grid scale is
    /// judged as the 12-TET it will actually be rendered as, and a maqam is not.
    /// </para>
    /// </remarks>
    public bool IsFidelityWarning => Fidelity?.IsWarningIn(OutputIsTwelveTet) == true;

    /// <summary>The warning's text, or null when the badge is neutral - which is most of the time.</summary>
    public string? FidelityWarning => IsFidelityWarning && SelectedScale is { } scale
        ? $"12-TET output cannot carry {scale.Name}: degrees land up to "
          + $"{Fidelity!.MaxDeviationCents:0.#} cents from where they belong. "
          + "Switch output to Microtonal to hear the scale as tuned."
        : null;

    /// <summary>Whether output will actually sit on the semitone grid, which is what the badge asks.</summary>
    private bool OutputIsTwelveTet => OutputMode switch
    {
        Core.Output.OutputMode.TwelveTet => true,
        Core.Output.OutputMode.Microtonal => false,
        _ => SelectedScale is { } scale && scale.MaxOffsetCents <= ToleranceCents,
    };

    // ---------------------------------------------- target tonic (outside the disclosure)

    /// <summary>
    /// Where the target scale's degree 0 sits, as a pitch class.
    /// </summary>
    /// <remarks>
    /// A peer of the scale list, not a policy, which is why it is absent from
    /// <see cref="DisclosureControls"/>. It is per-file and defaults from the detected key, so
    /// burying it behind a closed disclosure would hide the one setting most likely to need changing
    /// for the file just opened.
    /// </remarks>
    [ObservableProperty]
    private int _targetTonicPitchClass;

    /// <summary>Which octave the tonic sits in. 4 puts pitch class 0 at middle C, MIDI 60.</summary>
    [ObservableProperty]
    private int _targetTonicOctave = DefaultTonicOctave;

    /// <summary>
    /// The letter and accidental the tonic is written as.
    /// </summary>
    /// <remarks>
    /// Not derivable from the pitch class: MIDI 61 may be C# or Db, and every letter downstream
    /// follows from which. Setting it moves <see cref="TargetTonicPitchClass"/> to match, and setting
    /// the pitch class resets this to the library's conventional flat-preferring spelling - so the
    /// two can never describe different notes.
    /// </remarks>
    [ObservableProperty]
    private TonicSpelling _targetTonicSpelling = TonicSpelling.C;

    partial void OnTargetTonicPitchClassChanged(int value)
    {
        if (!_syncingTonic)
        {
            _syncingTonic = true;
            try
            {
                TargetTonicSpelling = TonicSpelling.FromPitchClass(value);
            }
            finally
            {
                _syncingTonic = false;
            }
        }

        RaiseTonicDependents();
    }

    partial void OnTargetTonicSpellingChanged(TonicSpelling value)
    {
        if (!_syncingTonic)
        {
            _syncingTonic = true;
            try
            {
                TargetTonicPitchClass = value.PitchClass;
            }
            finally
            {
                _syncingTonic = false;
            }
        }

        RaiseTonicDependents();
    }

    partial void OnTargetTonicOctaveChanged(int value) => RaiseTonicDependents();

    /// <summary>The tonic as a MIDI note number, clamped into the representable range.</summary>
    public int TargetTonicMidiNote => Math.Clamp(
        (TargetTonicOctave + 1) * MidiRounding.SemitonesPerOctave + TargetTonicPitchClass,
        Pitch.MinMidiNote,
        Pitch.MaxMidiNote);

    /// <summary>The tonic as a pitch. Always exactly on the 12-TET grid - the scale's offsets do the rest.</summary>
    public Pitch TargetTonic => Pitch.FromMidi(TargetTonicMidiNote);

    /// <summary>The tonic as a display name, e.g. <c>Db4</c>.</summary>
    public string TargetTonicName => $"{TargetTonicSpelling}{TargetTonicOctave}";

    private void RaiseTonicDependents()
    {
        OnPropertyChanged(nameof(TargetTonicMidiNote));
        OnPropertyChanged(nameof(TargetTonic));
        OnPropertyChanged(nameof(TargetTonicName));
    }

    // ------------------------------------------------------------------ source key and scale

    /// <summary>
    /// What key detection concluded, or null before a file is loaded.
    /// </summary>
    /// <remarks>Held so the UI can offer the runners-up: detection is a suggestion, never a decision.</remarks>
    [ObservableProperty]
    private KeyDetectionResult? _detectedKey;

    /// <summary>Where the source scale's degree 0 sits, as a pitch class.</summary>
    [ObservableProperty]
    private int _sourceTonicPitchClass;

    /// <summary>Which octave the source tonic sits in.</summary>
    [ObservableProperty]
    private int _sourceTonicOctave = DefaultTonicOctave;

    /// <summary>
    /// The tuning being mapped out of. A setting, not an assumption.
    /// </summary>
    /// <remarks>
    /// Krumhansl-Schmuckler only ever reports major or minor, but a file may already be pentatonic
    /// or in a maqam, and a degree index computed against the wrong source scale is simply wrong.
    /// </remarks>
    [ObservableProperty]
    private Scale? _sourceScale;

    /// <summary>Everything the source scale may be set to - the same library the target comes from.</summary>
    public IReadOnlyList<ScaleEntry> SourceScaleOptions => _library.Entries;

    /// <summary>
    /// The same options as bare <see cref="Scale"/>s, so a picker can bind its selection straight to
    /// <see cref="SourceScale"/> without a converter.
    /// </summary>
    public IReadOnlyList<Scale> SourceScaleChoices => [.. _library.Scales];

    /// <summary>
    /// The scale the source material should be <i>read</i> against, with a major-scale fallback.
    /// </summary>
    /// <remarks>
    /// Used to notate a file before any target has been chosen. <see cref="SourceScale"/> is null
    /// until key detection has run or the user has picked one, and the notation views need
    /// something to spell against from the moment a file opens - showing nothing until a target
    /// scale is selected reads as a broken view, since the piano roll beside it is already full.
    /// </remarks>
    public Scale? EffectiveSourceScale => SourceScale ?? _library.Find(MajorSourceScaleId);

    /// <summary>The source tonic as a pitch.</summary>
    public Pitch SourceTonic => Pitch.FromMidi(Math.Clamp(
        (SourceTonicOctave + 1) * MidiRounding.SemitonesPerOctave + SourceTonicPitchClass,
        Pitch.MinMidiNote,
        Pitch.MaxMidiNote));

    /// <summary>The detected key as one line of text, or null when there is nothing to say.</summary>
    public string? SourceKeySummary => DetectedKey switch
    {
        null => null,
        { Outcome: KeyDetectionOutcome.NoKeyDetected } => "No key detected",
        { Outcome: KeyDetectionOutcome.Ambiguous } d =>
            $"Ambiguous: {string.Join(", ", d.Candidates.Select(c => c.Name))}",
        { Candidates.Count: > 0 } d =>
            $"{d.Candidates[0].Name} - margin {d.Margin:0.###}, {d.Candidates.Count - 1} alternates",
        _ => null,
    };

    /// <summary>
    /// Whether the source key control is live.
    /// </summary>
    /// <remarks>
    /// False under <see cref="MappingStrategy.NearestPitch"/>, which consults neither the source key
    /// nor the source scale. Leaving them enabled would imply an influence they do not have, and a
    /// control that visibly does nothing is how a user learns to distrust the whole panel.
    /// </remarks>
    public bool IsSourceKeyEnabled => Strategy != MappingStrategy.NearestPitch;

    /// <inheritdoc cref="IsSourceKeyEnabled"/>
    public bool IsSourceScaleEnabled => Strategy != MappingStrategy.NearestPitch;

    /// <summary>Why those two are dimmed, for the tooltip. Null when they are live.</summary>
    public string? SourceControlsDisabledReason => Strategy == MappingStrategy.NearestPitch
        ? "Nearest-pitch snapping maps every note to the closest pitch in the target scale, so it "
          + "reads neither the source key nor the source scale. Switch to degree mapping to use them."
        : null;

    /// <summary>
    /// Seeds the source key, the source scale and the target tonic from key detection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The target tonic defaults from the detected key because restyling in place is the
    /// overwhelmingly common case - a D minor file into Maqam Rast on D. It is a default, not a lock:
    /// the control sits outside the disclosure precisely so it can be changed per file.
    /// </para>
    /// <para>
    /// A <see cref="KeyDetectionOutcome.NoKeyDetected"/> result changes nothing. Substituting C major
    /// for "we could not tell" would silently transpose the user's whole output on the strength of a
    /// non-answer.
    /// </para>
    /// </remarks>
    public void ApplyDetectedKey(KeyDetectionResult? detection)
    {
        DetectedKey = detection;

        if (detection?.TopCandidate is not { } best)
        {
            return;
        }

        SourceTonicPitchClass = best.PitchClass;
        TargetTonicPitchClass = best.PitchClass;

        if (_library.Find(best.IsMinor ? MinorSourceScaleId : MajorSourceScaleId) is { } seeded)
        {
            SourceScale = seeded;
        }
    }

    partial void OnDetectedKeyChanged(KeyDetectionResult? value) =>
        OnPropertyChanged(nameof(SourceKeySummary));

    partial void OnSourceTonicPitchClassChanged(int value) => OnPropertyChanged(nameof(SourceTonic));

    partial void OnSourceTonicOctaveChanged(int value) => OnPropertyChanged(nameof(SourceTonic));

    partial void OnSourceScaleChanged(Scale? value) => RaiseReadinessDependents();

    // ------------------------------------------------------------------ the policies disclosure

    /// <summary>
    /// Whether the <c>Mapping &amp; policies</c> disclosure is open. <b>Closed by default.</b>
    /// </summary>
    /// <remarks>
    /// These are the controls people set once and forget. Open by default they would compete with
    /// the scale list for the rail's height, inverting the hierarchy the design exists to state.
    /// </remarks>
    [ObservableProperty]
    private bool _isPoliciesExpanded;

    /// <summary>Degree mapping (the default) or nearest-pitch snapping.</summary>
    [ObservableProperty]
    private MappingStrategy _strategy = MappingStrategy.ScaleDegree;

    /// <summary>What happens to notes outside the source scale.</summary>
    [ObservableProperty]
    private NonScaleNotePolicy _nonScaleNotes = NonScaleNotePolicy.SnapToNearestSourceDegree;

    /// <summary>What happens when two mapped notes collide on one pitch.</summary>
    [ObservableProperty]
    private CollisionPolicy _collisions = CollisionPolicy.Merge;

    /// <summary>What happens when a mapped note leaves MIDI 0..127.</summary>
    [ObservableProperty]
    private RangePolicy _range = RangePolicy.ShiftIntoRange;

    /// <summary>Auto, forced 12-TET, or forced microtonal.</summary>
    [ObservableProperty]
    private OutputMode _outputMode = OutputMode.Auto;

    /// <summary>
    /// How far apart two cent-offsets may be and still share one pitch-bend channel.
    /// </summary>
    /// <remarks>
    /// The user's preference and a starting point, not a guarantee: when the channel budget does not
    /// fit, the allocator raises it for the whole project and the status bar reports the effective
    /// value. At 1 cent, Pythagorean Gong burns five channels for an inaudible 7.8-cent correction.
    /// </remarks>
    [ObservableProperty]
    private double _toleranceCents = OffsetClusterer.DefaultToleranceCents;

    partial void OnStrategyChanged(MappingStrategy value)
    {
        OnPropertyChanged(nameof(IsSourceKeyEnabled));
        OnPropertyChanged(nameof(IsSourceScaleEnabled));
        OnPropertyChanged(nameof(SourceControlsDisabledReason));
        RaiseReadinessDependents();
    }

    partial void OnOutputModeChanged(OutputMode value) => RaiseFidelityDependents();

    partial void OnToleranceCentsChanged(double value) => RaiseFidelityDependents();

    /// <summary>Widens the bend-grouping tolerance by one cent, up to <see cref="MaxToleranceCents"/>.</summary>
    [RelayCommand]
    private void WidenTolerance() =>
        ToleranceCents = Math.Min(MaxToleranceCents, Math.Round(ToleranceCents + 1.0, 2));

    /// <summary>Narrows the bend-grouping tolerance by one cent, down to <see cref="MinToleranceCents"/>.</summary>
    [RelayCommand]
    private void NarrowTolerance() =>
        ToleranceCents = Math.Max(MinToleranceCents, Math.Round(ToleranceCents - 1.0, 2));

    // ------------------------------------------------------------------ output

    /// <summary>
    /// Whether <see cref="BuildSettings"/> can produce a usable settings object.
    /// </summary>
    /// <remarks>
    /// A non-notatable target scale does <em>not</em> block anything. Notatability affects the v1.1
    /// staff view and the MusicXML export and nothing else; Slendro is an ordinary, fully supported
    /// choice and the panel must not imply otherwise.
    /// </remarks>
    public bool CanRestyle => RestyleBlockedReason is null;

    /// <summary>Why a transform cannot run yet, or null when it can.</summary>
    public string? RestyleBlockedReason
    {
        get
        {
            if (SelectedScale is null)
            {
                return "Choose a target scale.";
            }

            if (Strategy == MappingStrategy.ScaleDegree && SourceScale is null)
            {
                return "Degree mapping needs a source scale - a note's degree index is only defined "
                     + "relative to one. Choose one, or switch to nearest-pitch mapping.";
            }

            return null;
        }
    }

    /// <summary>
    /// Whether the selected scale has a Western staff spelling. Informational only.
    /// </summary>
    /// <remarks>
    /// Consumed by the v1.1 staff view and by the MusicXML export's disabled reason. Deliberately
    /// <em>not</em> wired to any warning or block here: choosing Slendro is normal.
    /// </remarks>
    public bool SelectedScaleIsNotatable => SelectedScale?.Notatable ?? false;

    /// <summary>
    /// Turns the panel's state into the settings the engine consumes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source scale is carried through even under <see cref="MappingStrategy.NearestPitch"/>,
    /// which ignores it. Dropping it would discard the user's choice the moment they tried the other
    /// strategy, and the mapper is already the place that enforces the rule.
    /// </para>
    /// <para>
    /// Track exclusions belong to the left rail, so they are passed in rather than held here. Drums
    /// need no entry: they are excluded by <c>TrackInfo.IsRestylable</c> regardless.
    /// </para>
    /// </remarks>
    /// <param name="excluded">Track-channels the user opted out of, or null for none.</param>
    /// <exception cref="InvalidOperationException">
    /// When <see cref="CanRestyle"/> is false. The message is <see cref="RestyleBlockedReason"/>.
    /// </exception>
    public RestyleSettings BuildSettings(IReadOnlySet<(int Track, int Channel)>? excluded = null)
    {
        if (RestyleBlockedReason is { } blocked)
        {
            throw new InvalidOperationException(blocked);
        }

        return new RestyleSettings
        {
            TargetScale = SelectedScale!,
            TargetTonic = TargetTonic,
            TonicSpelling = TargetTonicSpelling,
            SourceScale = SourceScale,
            SourceTonic = SourceTonic,
            Mapping = new MappingOptions
            {
                Strategy = Strategy,
                NonScaleNotes = NonScaleNotes,
                Collisions = Collisions,
                Range = Range,
            },
            OutputMode = OutputMode,
            ToleranceCents = ToleranceCents,
            Excluded = excluded ?? new HashSet<(int, int)>(),
        };
    }

    private void RaiseSelectionDependents()
    {
        RaiseFidelityDependents();
        RaiseReadinessDependents();
        OnPropertyChanged(nameof(SelectedScaleIsNotatable));
    }

    private void RaiseFidelityDependents()
    {
        OnPropertyChanged(nameof(Fidelity));
        OnPropertyChanged(nameof(FidelityLabel));
        OnPropertyChanged(nameof(FidelityDescription));
        OnPropertyChanged(nameof(IsFidelityWarning));
        OnPropertyChanged(nameof(FidelityWarning));
    }

    private void RaiseReadinessDependents()
    {
        OnPropertyChanged(nameof(CanRestyle));
        OnPropertyChanged(nameof(RestyleBlockedReason));
    }
}
