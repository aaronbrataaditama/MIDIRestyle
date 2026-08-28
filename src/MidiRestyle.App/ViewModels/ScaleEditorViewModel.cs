using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidiRestyle.App.Services;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.App.ViewModels;

/// <summary>
/// Whether one degree-entry field currently holds a usable value, a value still being typed, or a
/// value that cannot become valid without deleting characters first.
/// </summary>
/// <remarks>
/// This is the state machine behind "a half-finished entry must not be treated as an error while the
/// user is still typing". <see cref="Pending"/> exists specifically so <c>5/</c> (a ratio's numerator
/// with the slash just typed) or <c>-</c> (the start of a negative cents value) never render as a
/// rejection - they are exactly as unfinished as <see cref="Empty"/>, just further along.
/// <see cref="Error"/> is reserved for text that <em>cannot</em> become valid by appending more
/// characters: <c>5/4/3</c>, <c>abc</c>, or a ratio with a non-positive numerator or denominator (the
/// one Scala-reader rule - "negative ratios are meaningless" - that still applies to typed input,
/// since no amount of further typing fixes a negative number already committed to the field).
/// </remarks>
public enum DegreeEntryStatus
{
    /// <summary>Nothing typed yet.</summary>
    Empty,

    /// <summary>A prefix of a valid cents or ratio token. Not an error.</summary>
    Pending,

    /// <summary>Parses cleanly to a cents value.</summary>
    Complete,

    /// <summary>Text that cannot become valid by typing more - shown as an error immediately.</summary>
    Error,
}

/// <summary>
/// One row of the degree list: the raw text the user typed, and what it means in cents.
/// </summary>
/// <remarks>
/// <para>
/// <b>Accepts cents or a ratio, per field, decided by the presence of <c>/</c>.</b> This deliberately
/// does <em>not</em> reuse <see cref="ScalaFileReader"/>'s own rule that a bare integer with no
/// decimal point is a ratio (<c>700</c> meaning 700/1, ~11,344 cents) - that rule exists because a
/// <c>.scl</c> file is machine-authored and unambiguous by construction. Here a person is typing into
/// a box, and a user who types <c>200</c> means 200 cents, not a ratio a hundred octaves wide.
/// Interpreting a bare number as cents (matching <see cref="ScalaFileReader"/>'s handling of an
/// explicit decimal-pointed value) and reserving ratio parsing for tokens that actually contain a
/// slash keeps the surface honest about what "700" versus "5/4" means to someone typing it live. The
/// ratio-to-cents formula itself, <c>1200 * log2(num/den)</c>, and the "non-positive ratio is a hard
/// error" rule are both taken directly from the Scala reader, since those two are format facts, not
/// surface-specific ones.
/// </para>
/// <para>
/// <see cref="Status"/> is recomputed on every keystroke via <see cref="Parse"/>, a pure function
/// exposed <c>public static</c> so the cents/ratio-equivalence and mid-edit rules can be asserted
/// directly without driving a text box.
/// </para>
/// </remarks>
public sealed partial class DegreeEntryViewModel : ObservableObject
{
    // Prefixes of a complete ratio token "<digits>/<digits>": zero or more digits, optionally
    // followed by a slash and zero or more digits. Matches "", "5", "5/", "5/4" - not "5/4/3".
    private static readonly Regex RatioPrefix = new(@"^\d*(/\d*)?$", RegexOptions.Compiled);

    // Prefixes of a complete cents token "-?<digits>.<digits>": an optional leading minus, digits,
    // an optional decimal point, more digits. Matches "-", "386", "386.", "-42.5" - not "12-3".
    private static readonly Regex CentsPrefix = new(@"^-?\d*\.?\d*$", RegexOptions.Compiled);

    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>What <see cref="Text"/> currently means: unusable-but-typable, usable, or rejected.</summary>
    public DegreeEntryStatus Status { get; private set; } = DegreeEntryStatus.Empty;

    /// <summary>The parsed value in cents, only when <see cref="Status"/> is <see cref="DegreeEntryStatus.Complete"/>.</summary>
    public double? Cents { get; private set; }

    /// <summary>Why this entry is rejected, only when <see cref="Status"/> is <see cref="DegreeEntryStatus.Error"/>.</summary>
    public string? ErrorMessage { get; private set; }

    partial void OnTextChanged(string value)
    {
        (DegreeEntryStatus status, double? cents, string? error) = Parse(value);
        Status = status;
        Cents = cents;
        ErrorMessage = error;

        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Cents));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    /// <summary>
    /// Parses one degree field's raw text. Pure, and safe to call on every keystroke - it never
    /// throws and never treats an unfinished-but-plausible entry as an error.
    /// </summary>
    public static (DegreeEntryStatus Status, double? Cents, string? Error) Parse(string? rawText)
    {
        string text = (rawText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return (DegreeEntryStatus.Empty, null, null);
        }

        if (text.Contains('/'))
        {
            return ParseRatio(text);
        }

        return ParseCents(text);
    }

    private static (DegreeEntryStatus, double?, string?) ParseRatio(string text)
    {
        if (!RatioPrefix.IsMatch(text))
        {
            return (DegreeEntryStatus.Error, null,
                $"'{text}' is not a valid ratio - expected a single '/', as in '5/4'.");
        }

        string[] parts = text.Split('/');
        string numeratorText = parts[0];
        string denominatorText = parts.Length > 1 ? parts[1] : string.Empty;

        if (numeratorText.Length == 0 || denominatorText.Length == 0)
        {
            // Still typing, e.g. "5/" or "/4".
            return (DegreeEntryStatus.Pending, null, null);
        }

        if (!long.TryParse(numeratorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numerator)
            || !long.TryParse(denominatorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long denominator))
        {
            return (DegreeEntryStatus.Error, null, $"'{text}' is not a valid ratio.");
        }

        if (numerator <= 0 || denominator <= 0)
        {
            // Taken directly from ScalaFileReader: a non-positive ratio is meaningless per the Scala
            // spec, and no amount of further typing turns a negative number already there into one.
            return (DegreeEntryStatus.Error, null,
                $"'{text}' is a non-positive ratio - negative or zero ratios are meaningless.");
        }

        double cents = 1200.0 * Math.Log2(numerator / (double)denominator);
        return (DegreeEntryStatus.Complete, cents, null);
    }

    private static (DegreeEntryStatus, double?, string?) ParseCents(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double cents))
        {
            return (DegreeEntryStatus.Complete, cents, null);
        }

        if (CentsPrefix.IsMatch(text))
        {
            // Still typing, e.g. "-" (a negative value's sign, nothing else yet).
            return (DegreeEntryStatus.Pending, null, null);
        }

        return (DegreeEntryStatus.Error, null, $"'{text}' is not a valid cents value or ratio.");
    }
}

/// <summary>The outcome of saving or deleting from <see cref="ScaleEditorViewModel"/>.</summary>
/// <param name="Success">Whether the operation completed.</param>
/// <param name="Scale">The scale that was saved, or null on failure and on delete.</param>
/// <param name="Reason">
/// Always populated: a success narrates where the file went (matching <see cref="SettingsService"/>'s
/// own convention), a failure states why - an unwritable location, an id clash, or an IO error - none
/// of which are exceptions here. See the type's remarks on why writing never throws.
/// </param>
public sealed record ScaleEditorSaveResult(bool Success, Scale? Scale, string Reason);

/// <summary>
/// Defines, edits and persists a user-authored <see cref="Scale"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Validation is surfaced, never duplicated.</b> <see cref="Scale"/>'s constructor already rejects
/// every shape this domain cannot use - see its own remarks. This type's only job is to attempt that
/// constructor with whatever the user has typed and relay <see cref="ScaleValidationException.Reason"/>
/// verbatim through <see cref="ValidationMessage"/>: that message already explains the downstream
/// consequence (why a degree at 1200 cents is refused, why degrees must strictly ascend) better than
/// anything reinvented here would.
/// </para>
/// <para>
/// <b>Mid-edit is a distinct state from invalid.</b> While any <see cref="DegreeEntryViewModel"/> is
/// <see cref="DegreeEntryStatus.Empty"/> or <see cref="DegreeEntryStatus.Pending"/>,
/// <see cref="Scale"/>'s constructor is never even invoked - there is nothing yet to construct - and
/// <see cref="ValidationMessage"/> is worded as "still being entered", never with <see cref="Scale"/>'s
/// own rejection language. Only <see cref="DegreeEntryStatus.Error"/> (a token that cannot become
/// valid by typing more) or a fully-typed-but-domain-invalid degree list produce the constructor's own
/// wording.
/// </para>
/// <para>
/// <b>Ids are namespaced <c>user.*</c>, always, automatically.</b> <see cref="IdSlug"/> is what the
/// user types; <see cref="Id"/> prepends <see cref="UserIdPrefix"/> unconditionally. No shipped scale
/// uses that namespace, so a user can never accidentally shadow one just by picking an unlucky name -
/// the alternative, validating a hand-typed prefix, adds a way to get it wrong for no benefit.
/// </para>
/// <para>
/// <b>Shipped scales are copy-on-edit.</b> <see cref="LoadForEdit"/> takes the scale's
/// <see cref="ScaleOrigin"/> from the caller (the panel that knows which library entry was opened) and
/// remembers it. Editing anything that is not already <see cref="ScaleOrigin.UserDefined"/> always
/// saves as a new <c>user.*</c> id - the original is never read again, let alone written to, so the
/// app's own shipped data stays byte-identical and the user's change is visible as an override rather
/// than a silent replacement. Editing an already-<see cref="ScaleOrigin.UserDefined"/> scale updates
/// that same id in place instead.
/// </para>
/// <para>
/// <b><see cref="Notatable"/> defaults to a derived guess, with a manual override that sticks.</b> The
/// guess comes from <see cref="DiatonicSpeller.Derive"/> against whatever degrees currently parse
/// cleanly. Setting <see cref="Notatable"/> directly (rather than through
/// <see cref="UseDerivedNotatableCommand"/>) marks it as a manual override, which then survives further
/// degree edits instead of being silently recomputed out from under the user. Overriding it to false
/// needs no extra plumbing: <see cref="Scale"/> itself nulls <see cref="Scale.Spelling"/> whenever
/// <see cref="Scale.Notatable"/> is false, regardless of what a derived spelling would have been - so
/// this type only ever needs to pass <see cref="Notatable"/> through.
/// </para>
/// <para>
/// <b>Persistence reuses <see cref="ScaleLibraryService"/>'s own path resolution</b> -
/// <see cref="PathProbe.ResolveWritableRoot"/> and <see cref="ScaleLibraryService.UserScalesFileName"/>
/// - rather than re-deriving where <c>user.scales.json</c> lives. An unwritable location (read-only
/// media, a locked-down %APPDATA%) is an expected state, not a bug: <see cref="Save"/> and
/// <see cref="Delete"/> both report it through <see cref="ScaleEditorSaveResult"/> and never throw for
/// it. The one exception is a genuine `ArgumentNullException` on a null constructor argument, which is
/// a programmer error, not user input, and is allowed to throw as usual.
/// </para>
/// </remarks>
public sealed partial class ScaleEditorViewModel : ObservableObject
{
    /// <summary>Every user scale's id is prefixed with this, unconditionally. See the type remarks.</summary>
    public const string UserIdPrefix = "user.";

    /// <summary>
    /// The default <see cref="Scale.Source"/> for a brand new user scale.
    /// </summary>
    /// <remarks>
    /// <see cref="Scale"/>'s constructor demands a non-placeholder <see cref="Scale.Source"/>, but a
    /// user inventing their own tuning is not obliged to cite anything - there is nothing to cite.
    /// <c>"user-defined"</c> is honest (it says exactly what it is, and is not one of
    /// <see cref="Scale"/>'s rejected placeholder strings such as "TODO" or "unknown") while still
    /// being overridable: someone transcribing a tuning from a book or a Scala file should replace it
    /// with the real citation.
    /// </remarks>
    public const string DefaultSource = "user-defined";

    /// <summary>
    /// Explains what editing a shipped scale actually does, for the UI to show once, near the Save
    /// button, whenever <see cref="IsCopyOnEdit"/> is true.
    /// </summary>
    public const string CopyOnEditExplanation =
        "This scale ships with the app. Saving your changes creates a new user scale rather than " +
        "modifying the original, so the app's own library is never altered and your change is always " +
        "visible as an override.";

    /// <summary>
    /// Explains what the <see cref="Notatable"/> flag actually controls, for the UI to show next to
    /// its checkbox.
    /// </summary>
    public const string NotatableExplanation =
        "Whether this scale has a Western staff spelling at all. This is a cultural judgement, not " +
        "something computed from the cents alone - a scale can be numerically close enough to " +
        "quarter-tone notation and still be one no musician in that tradition would read that way. " +
        "The suggested value below is what can actually be derived; override it if that guess is wrong " +
        "for this tradition.";

    private readonly PathProbe _pathProbe;

    private Scale? _editingSource;
    private ScaleOrigin? _editingSourceOrigin;
    private string? _originalUserScaleId;
    private bool _notatableManuallySet;
    private bool _updatingNotatableInternally;
    private Scale? _preview;
    private string? _validationMessage;

    public ScaleEditorViewModel(PathProbe? pathProbe = null)
    {
        _pathProbe = pathProbe ?? new PathProbe();
        Degrees.CollectionChanged += OnDegreesCollectionChanged;
        LoadForNew();
    }

    // ------------------------------------------------------------------ fields

    /// <summary>
    /// What the user types for the id - everything after the <see cref="UserIdPrefix"/>. See
    /// <see cref="Id"/>.
    /// </summary>
    [ObservableProperty]
    private string _idSlug = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _tradition = string.Empty;

    [ObservableProperty]
    private string _region = string.Empty;

    [ObservableProperty]
    private string _source = DefaultSource;

    [ObservableProperty]
    private string? _description;

    /// <summary>Whether this scale has a Western staff spelling. See the type remarks and <see cref="NotatableExplanation"/>.</summary>
    [ObservableProperty]
    private bool _notatable = true;

    /// <summary>The full, namespaced id that will actually be saved: <see cref="UserIdPrefix"/> plus <see cref="IdSlug"/>.</summary>
    public string Id => $"{UserIdPrefix}{IdSlug.Trim()}";

    /// <summary>The degree rows, in order. Row 0 is conventionally the tonic (0 cents), but is fully editable - see the "first degree not 0" test this enables.</summary>
    public ObservableCollection<DegreeEntryViewModel> Degrees { get; } = [];

    partial void OnIdSlugChanged(string value)
    {
        OnPropertyChanged(nameof(Id));
        Revalidate();
    }

    partial void OnNameChanged(string value) => Revalidate();

    partial void OnTraditionChanged(string value) => Revalidate();

    partial void OnRegionChanged(string value) => Revalidate();

    partial void OnSourceChanged(string value) => Revalidate();

    partial void OnDescriptionChanged(string? value) => Revalidate();

    partial void OnNotatableChanged(bool value)
    {
        if (!_updatingNotatableInternally)
        {
            _notatableManuallySet = true;
        }

        Revalidate();
    }

    // ------------------------------------------------------------------ notatability guess

    /// <summary>Whether <see cref="Notatable"/> currently reflects a manual choice rather than the derived guess.</summary>
    public bool IsNotatableManualOverride => _notatableManuallySet;

    /// <summary>
    /// What <see cref="DiatonicSpeller"/> can actually derive from the degrees typed so far, ignoring
    /// any manual override. Recomputed on demand - cheap, since a scale has at most 12 degrees.
    /// </summary>
    public bool DerivedNotatableGuess => DeriveNotatableGuess();

    /// <summary>Returns <see cref="Notatable"/> to tracking <see cref="DerivedNotatableGuess"/> as the degrees change.</summary>
    [RelayCommand]
    private void UseDerivedNotatable()
    {
        _notatableManuallySet = false;
        OnPropertyChanged(nameof(IsNotatableManualOverride));
        RefreshDerivedNotatable();
    }

    private bool DeriveNotatableGuess()
    {
        List<double> complete = [.. Degrees.Where(d => d.Status == DegreeEntryStatus.Complete).Select(d => d.Cents!.Value)];

        // Too little typed to guess anything meaningful yet - default optimistic rather than flash a
        // "not notatable" guess before the user has entered enough to judge.
        if (complete.Count < Scale.MinDegrees)
        {
            return true;
        }

        return DiatonicSpeller.Derive(complete, notatable: true, scaleName: Name).Succeeded;
    }

    private void RefreshDerivedNotatable()
    {
        if (_notatableManuallySet)
        {
            return;
        }

        bool guess = DeriveNotatableGuess();
        if (guess == Notatable)
        {
            return;
        }

        _updatingNotatableInternally = true;
        try
        {
            Notatable = guess;
        }
        finally
        {
            _updatingNotatableInternally = false;
        }
    }

    // ------------------------------------------------------------------ degree rows

    /// <summary>Appends an empty degree row, up to <see cref="Scale.MaxDegrees"/>.</summary>
    [RelayCommand]
    private void AddDegree()
    {
        if (Degrees.Count >= Scale.MaxDegrees)
        {
            return;
        }

        Degrees.Add(new DegreeEntryViewModel());
    }

    /// <summary>Removes a degree row, refusing to drop below <see cref="Scale.MinDegrees"/>.</summary>
    public void RemoveDegree(DegreeEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (Degrees.Count <= Scale.MinDegrees)
        {
            return;
        }

        Degrees.Remove(entry);
    }

    private void OnDegreesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (DegreeEntryViewModel entry in e.NewItems)
            {
                entry.PropertyChanged += OnDegreePropertyChanged;
            }
        }

        RefreshDerivedNotatable();
        Revalidate();
    }

    private void OnDegreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DegreeEntryViewModel.Status))
        {
            RefreshDerivedNotatable();
            Revalidate();
        }
    }

    // ------------------------------------------------------------------ validity

    /// <summary>Whether <see cref="Preview"/> holds a scale that would actually save. Drives the Save button.</summary>
    public bool IsValid => _validationMessage is null;

    /// <summary>
    /// Why <see cref="IsValid"/> is false, or null when it is true. Never phrased as a hard rejection
    /// while a degree is only mid-typed - see the type remarks.
    /// </summary>
    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (_validationMessage == value)
            {
                return;
            }

            _validationMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValid));
        }
    }

    /// <summary>The scale as it would be saved right now, or null while <see cref="IsValid"/> is false.</summary>
    public Scale? Preview => _preview;

    private void Revalidate()
    {
        _preview = null;

        for (int i = 0; i < Degrees.Count; i++)
        {
            if (Degrees[i].Status == DegreeEntryStatus.Error)
            {
                ValidationMessage = $"Degree {i + 1}: {Degrees[i].ErrorMessage}";
                return;
            }
        }

        if (Degrees.Count < Scale.MinDegrees)
        {
            ValidationMessage = $"Add at least {Scale.MinDegrees} degrees ({Degrees.Count} so far).";
            return;
        }

        for (int i = 0; i < Degrees.Count; i++)
        {
            if (Degrees[i].Status is DegreeEntryStatus.Empty or DegreeEntryStatus.Pending)
            {
                // Mid-edit, not invalid: Scale's constructor is not even called yet, and the message
                // deliberately does not echo its rejection wording.
                ValidationMessage = $"Degree {i + 1} is still being entered.";
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(IdSlug))
        {
            ValidationMessage = "Enter an id for this scale.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationMessage = "Enter a name for this scale.";
            return;
        }

        double[] degreeCents = [.. Degrees.Select(d => d.Cents!.Value)];
        string effectiveSource = string.IsNullOrWhiteSpace(Source) ? DefaultSource : Source;

        IReadOnlyList<DegreeSpelling>? spelling = Notatable
            ? DiatonicSpeller.Derive(degreeCents, notatable: true, scaleName: Name).Spelling
            : null;

        try
        {
            _preview = new Scale(
                id: Id,
                name: Name,
                tradition: Tradition,
                region: Region,
                degreeCents: degreeCents,
                source: effectiveSource,
                notatable: Notatable,
                spelling: spelling,
                description: string.IsNullOrWhiteSpace(Description) ? null : Description);

            ValidationMessage = null;
        }
        catch (ScaleValidationException ex)
        {
            // Relayed verbatim - see the type remarks on why duplicating this wording would be worse.
            ValidationMessage = ex.Reason;
        }
        catch (ArgumentException ex)
        {
            ValidationMessage = ex.Message;
        }
    }

    // ------------------------------------------------------------------ loading

    /// <summary>Resets every field to a blank, brand-new scale.</summary>
    public void LoadForNew()
    {
        _editingSource = null;
        _editingSourceOrigin = null;
        _originalUserScaleId = null;
        _notatableManuallySet = false;

        IdSlug = string.Empty;
        Name = string.Empty;
        Tradition = string.Empty;
        Region = string.Empty;
        Source = DefaultSource;
        Description = null;
        Notatable = true;

        Degrees.Clear();
        Degrees.Add(new DegreeEntryViewModel { Text = "0" });
        Degrees.Add(new DegreeEntryViewModel());

        Revalidate();
    }

    /// <summary>
    /// Loads an existing library scale for editing. <paramref name="origin"/> decides what
    /// <see cref="Save"/> does with it - see the type remarks on copy-on-edit.
    /// </summary>
    public void LoadForEdit(Scale scale, ScaleOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(scale);

        _editingSource = scale;
        _editingSourceOrigin = origin;
        _originalUserScaleId = origin == ScaleOrigin.UserDefined ? scale.Id : null;

        IdSlug = origin == ScaleOrigin.UserDefined ? StripUserPrefix(scale.Id) : Slugify(scale.Id);
        Name = scale.Name;
        Tradition = scale.Tradition;
        Region = scale.Region;
        Source = scale.Source;
        Description = scale.Description;

        _updatingNotatableInternally = true;
        try
        {
            Notatable = scale.Notatable;
        }
        finally
        {
            _updatingNotatableInternally = false;
        }

        // The scale being edited already carries an authored Notatable value - treat it as an
        // explicit choice, not something to recompute out from under the user the moment a degree
        // is touched. UseDerivedNotatableCommand can still opt back into the guess.
        _notatableManuallySet = true;

        Degrees.Clear();
        foreach (double cents in scale.DegreeCents)
        {
            Degrees.Add(new DegreeEntryViewModel { Text = cents.ToString("0.####", CultureInfo.InvariantCulture) });
        }

        Revalidate();
    }

    /// <summary>Whether an existing library scale is currently loaded (as opposed to a brand new one).</summary>
    public bool IsEditingExisting => _editingSource is not null;

    /// <summary>Whether the loaded scale is not already the user's own - so <see cref="Save"/> will copy rather than update. See <see cref="CopyOnEditExplanation"/>.</summary>
    public bool IsCopyOnEdit => _editingSource is not null && _editingSourceOrigin != ScaleOrigin.UserDefined;

    /// <summary>Whether <see cref="Delete"/> can do anything - only true for an already-saved user scale.</summary>
    public bool CanDelete => _originalUserScaleId is not null;

    private static string StripUserPrefix(string id) =>
        id.StartsWith(UserIdPrefix, StringComparison.OrdinalIgnoreCase) ? id[UserIdPrefix.Length..] : id;

    private static string Slugify(string id) => id.Replace('.', '-');

    // ------------------------------------------------------------------ persistence

    /// <summary>
    /// Saves the current fields as a user scale. Never throws for an expected failure - an unwritable
    /// location, an invalid scale, or an id clash with an unrelated user scale all come back as a
    /// failed <see cref="ScaleEditorSaveResult"/> instead.
    /// </summary>
    public ScaleEditorSaveResult Save()
    {
        Revalidate();
        if (_preview is null)
        {
            return new ScaleEditorSaveResult(false, null, ValidationMessage ?? "The scale is not valid yet.");
        }

        Scale candidate = _preview;

        WritableRootResult resolved = _pathProbe.ResolveWritableRoot();
        if (!resolved.IsWritable)
        {
            return new ScaleEditorSaveResult(false, null, resolved.Reason);
        }

        string path = Path.Combine(resolved.Root, ScaleLibraryService.UserScalesFileName);

        List<Scale> current;
        try
        {
            current = LoadExistingUserScales(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ScaleEditorSaveResult(false, null, $"Could not read '{path}': {ex.Message}");
        }

        // An unrelated user scale already claiming this id is a real clash, not this same scale being
        // re-saved (which legitimately shares the id it already had, or is being renamed away from it).
        bool clashesWithAnotherScale = current.Any(s =>
            string.Equals(s.Id, candidate.Id, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(s.Id, _originalUserScaleId, StringComparison.OrdinalIgnoreCase));

        if (clashesWithAnotherScale)
        {
            return new ScaleEditorSaveResult(false, null,
                $"A user scale with id '{candidate.Id}' already exists. Choose a different name, or " +
                "open that scale to edit it directly.");
        }

        List<Scale> updated =
        [
            .. current.Where(s =>
                !string.Equals(s.Id, _originalUserScaleId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(s.Id, candidate.Id, StringComparison.OrdinalIgnoreCase)),
            candidate,
        ];

        try
        {
            ScaleJsonStore.SaveToFile(path, updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ScaleEditorSaveResult(false, null, $"Could not write '{path}': {ex.Message}");
        }

        // This scale is now the user's own copy under its (possibly new) id: a further Save() without
        // reloading updates it in place rather than adding a duplicate.
        _editingSource = candidate;
        _editingSourceOrigin = ScaleOrigin.UserDefined;
        _originalUserScaleId = candidate.Id;
        OnPropertyChanged(nameof(IsEditingExisting));
        OnPropertyChanged(nameof(IsCopyOnEdit));
        OnPropertyChanged(nameof(CanDelete));

        return new ScaleEditorSaveResult(true, candidate, $"Saved '{candidate.Id}' to '{path}'.");
    }

    /// <summary>
    /// Deletes the user scale currently loaded. A no-op success when nothing is loaded to delete or
    /// the file already lacks it - deleting is idempotent by design. Never touches shipped data: a
    /// copy-on-edit scale that has never been saved has no <see cref="_originalUserScaleId"/> yet, so
    /// there is nothing here to remove.
    /// </summary>
    public ScaleEditorSaveResult Delete()
    {
        if (_originalUserScaleId is null)
        {
            return new ScaleEditorSaveResult(false, null,
                "Only a saved user scale can be deleted - there is nothing on disk to remove yet.");
        }

        WritableRootResult resolved = _pathProbe.ResolveWritableRoot();
        if (!resolved.IsWritable)
        {
            return new ScaleEditorSaveResult(false, null, resolved.Reason);
        }

        string path = Path.Combine(resolved.Root, ScaleLibraryService.UserScalesFileName);
        if (!File.Exists(path))
        {
            return new ScaleEditorSaveResult(true, null, "There was nothing to delete.");
        }

        List<Scale> current;
        try
        {
            current = LoadExistingUserScales(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ScaleEditorSaveResult(false, null, $"Could not read '{path}': {ex.Message}");
        }

        string deletedId = _originalUserScaleId;
        List<Scale> updated = [.. current.Where(s => !string.Equals(s.Id, deletedId, StringComparison.OrdinalIgnoreCase))];

        try
        {
            ScaleJsonStore.SaveToFile(path, updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ScaleEditorSaveResult(false, null, $"Could not write '{path}': {ex.Message}");
        }

        _originalUserScaleId = null;
        _editingSource = null;
        _editingSourceOrigin = null;
        OnPropertyChanged(nameof(IsEditingExisting));
        OnPropertyChanged(nameof(IsCopyOnEdit));
        OnPropertyChanged(nameof(CanDelete));

        return new ScaleEditorSaveResult(true, null, $"Deleted '{deletedId}'.");
    }

    /// <summary>
    /// Reads whatever is currently in <c>user.scales.json</c>. A file that fails to parse at all has
    /// nothing worth preserving - the same tolerance <see cref="ScaleLibraryService"/> already applies
    /// to bad data elsewhere - so <see cref="Save"/> proceeds as if it were empty rather than blocking
    /// on it; only a genuine IO failure (caught by the caller) stops a save.
    /// </summary>
    private static List<Scale> LoadExistingUserScales(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        string json = File.ReadAllText(path);
        ScaleJsonLoadResult result = ScaleJsonStore.LoadFromString(json);
        return result.FileError is not null ? [] : [.. result.Scales];
    }
}
