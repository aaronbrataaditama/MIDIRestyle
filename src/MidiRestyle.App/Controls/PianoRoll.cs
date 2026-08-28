using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

// PianoRollPalette/PianoRollPalettes stay internal - they are rendering plumbing, not part of the
// control's public surface - but the palette-selection logic still needs a direct unit test (see
// CLAUDE.md's TDD-for-Core convention extended here: pure logic gets tested even when it lives in
// the App project). Mirrors the InternalsVisibleTo already used for MidiRestyle.Core.Tests.
[assembly: InternalsVisibleTo("MidiRestyle.App.Tests")]

namespace MidiRestyle.App.Controls;

/// <summary>
/// One complete set of drawing resources for the piano roll - a background, row stripes, the ghost
/// and note brushes, the label/grid/octave/playhead pens.
/// </summary>
/// <remarks>
/// All members are created exactly once, in <see cref="PianoRollPalettes.Dark"/> and
/// <see cref="PianoRollPalettes.Light"/> - never per <see cref="PianoRoll.Render"/> call. Selecting a
/// palette is just picking which of those two already-built instances to point at, so switching
/// theme costs nothing per frame; see the allocation-free requirement on
/// <see cref="PianoRoll.Render"/>.
/// </remarks>
internal sealed class PianoRollPalette(
    IBrush background,
    IBrush whiteRow,
    IBrush blackRow,
    IBrush ghost,
    IBrush note,
    IBrush octaveLabel,
    Pen grid,
    Pen octave,
    Pen playhead)
{
    public IBrush Background { get; } = background;
    public IBrush WhiteRow { get; } = whiteRow;
    public IBrush BlackRow { get; } = blackRow;
    public IBrush Ghost { get; } = ghost;
    public IBrush Note { get; } = note;
    public IBrush OctaveLabel { get; } = octaveLabel;
    public Pen Grid { get; } = grid;
    public Pen Octave { get; } = octave;
    public Pen Playhead { get; } = playhead;
}

/// <summary>
/// The two palettes the piano roll can render in, and the pure selection logic that picks between
/// them. Kept separate from <see cref="PianoRoll"/> itself so the selection - "which palette for
/// this <see cref="ThemeVariant"/>" - can be unit tested without an initialised Avalonia runtime or
/// a live <see cref="Avalonia.Application"/>; only <see cref="SolidColorBrush"/>/<see cref="Pen"/>
/// construction is needed, and those do not require the runtime to be running.
/// </summary>
internal static class PianoRollPalettes
{
    /// <summary>
    /// The original hard-coded palette, unchanged: a near-black background, a translucent white
    /// grid, a bright blue note brush, a muted grey-lavender ghost, a warm red-orange playhead.
    /// </summary>
    public static readonly PianoRollPalette Dark = new(
        background: new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D)),
        whiteRow: new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x26)),
        blackRow: new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1F)),
        ghost: new SolidColorBrush(Color.FromArgb(0x55, 0x8A, 0x8A, 0x96)),
        note: new SolidColorBrush(Color.FromRgb(0x5B, 0x8F, 0xF9)),
        octaveLabel: new SolidColorBrush(Color.FromArgb(0x99, 0xAA, 0xAA, 0xB4)),
        grid: new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1),
        octave: new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1),
        playhead: new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0x6A, 0x5C)), 1.5));

    /// <summary>
    /// A light counterpart built to keep the same two contrasts the dark palette relies on: the
    /// translucent grey ghost against the saturated blue solid note (the whole point of the ghost
    /// overlay), and black/white key rows a clear step apart rather than the near-flat wash a naive
    /// "just invert the dark colours" pass would produce. The note brush is a deeper blue than the
    /// dark palette's (not merely the same hue on a light ground) because the same #5B8FF9 sits too
    /// close in lightness to a white background to read as solid; the playhead is likewise darkened
    /// for the same reason. Grid lines flip from translucent white to translucent black, since a
    /// white-on-white line would be invisible.
    /// </summary>
    public static readonly PianoRollPalette Light = new(
        background: new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xF0)),
        whiteRow: new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFB)),
        blackRow: new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE7)),
        ghost: new SolidColorBrush(Color.FromArgb(0x50, 0x55, 0x5A, 0x66)),
        note: new SolidColorBrush(Color.FromRgb(0x2F, 0x5F, 0xD9)),
        octaveLabel: new SolidColorBrush(Color.FromArgb(0xAA, 0x40, 0x40, 0x48)),
        grid: new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x00, 0x00)), 1),
        octave: new Pen(new SolidColorBrush(Color.FromArgb(0x45, 0x00, 0x00, 0x00)), 1),
        playhead: new Pen(new SolidColorBrush(Color.FromRgb(0xD1, 0x48, 0x3A)), 1.5));

    /// <summary>
    /// Picks a palette for an <em>actual</em> (already-resolved) theme variant - never
    /// <see cref="ThemeVariant.Default"/>, which is what a control's <c>ActualThemeVariant</c> never
    /// reports since Avalonia always resolves "system" down to a concrete Light or Dark before it
    /// reaches a control. Anything that is not exactly <see cref="ThemeVariant.Light"/> - including a
    /// custom or unrecognised variant - falls back to <see cref="Dark"/>, matching the palette this
    /// control has always shipped with.
    /// </summary>
    public static PianoRollPalette For(ThemeVariant variant) => variant == ThemeVariant.Light ? Light : Dark;
}

/// <summary>
/// The piano roll: original notes as muted ghosts, restyled notes solid on top.
/// </summary>
/// <remarks>
/// <para>
/// A custom <see cref="Control"/> overriding <see cref="Render"/>, <b>not</b> a panel of per-note
/// elements. A dense file is tens of thousands of notes; that many visual children would cost a
/// layout pass and a live object each, and would make scrolling unusable long before the file got
/// interesting.
/// </para>
/// <para>
/// The render path is written to allocate nothing per frame: brushes and pens are created once, in
/// <see cref="PianoRollPalettes"/>, note quads go into a reused buffer, and the culling maths lives
/// in <see cref="PianoRollGeometry"/> where it can be measured without a window. The one deliberate
/// exception is the key labels, which need <see cref="FormattedText"/> - so they are drawn only for
/// C rows, only when a row is tall enough to read, and cached by note number.
/// </para>
/// </remarks>
public sealed class PianoRoll : Control
{
    // --- theme -----------------------------------------------------------------------------

    /// <summary>
    /// The active palette, chosen from <see cref="PianoRollPalettes"/> - never rebuilt, only
    /// re-pointed. Starts on <see cref="PianoRollPalettes.Dark"/> so a control built and rendered
    /// before it is attached to a themed visual tree (unusual, but not impossible in tests) still
    /// has something sane to draw with.
    /// </summary>
    private PianoRollPalette _palette = PianoRollPalettes.Dark;

    /// <summary>
    /// Avalonia resolves <c>ActualThemeVariant</c> down through the visual tree, so this control
    /// does not need its own reference to <see cref="MidiRestyle.App.Services.ThemeService"/> - it
    /// only needs to notice when its own actual variant changes, whether that is because the user
    /// picked Light/Dark explicitly or because the OS theme changed while a System preference is
    /// active. Re-selecting the palette and invalidating the visual is all a live theme change needs.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        _palette = PianoRollPalettes.For(ActualThemeVariant);
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _palette = PianoRollPalettes.For(ActualThemeVariant);

        // The label cache holds FormattedText instances with the octave-label brush baked in at
        // construction time (FormattedText takes its brush as a constructor argument, not at draw
        // time), so a stale cache would keep drawing last theme's label colour forever.
        _labelCache.Clear();

        InvalidateVisual();
    }

    // --- immutable drawing resources, created once ------------------------------------------

    private static readonly Typeface LabelTypeface = new("Inter");

    /// <summary>Which pitch classes are black keys, for the row striping.</summary>
    private static readonly bool[] IsBlackKey =
        [false, true, false, true, false, false, true, false, true, false, true, false];

    // --- reused per-frame buffers -------------------------------------------------------------

    private NoteQuad[] _quadBuffer = new NoteQuad[1024];
    private RollNote[] _ghosts = [];
    private RollNote[] _notes = [];
    private long _ghostMaxLength;
    private long _noteMaxLength;
    private readonly Dictionary<int, FormattedText> _labelCache = [];

    // --- viewport ------------------------------------------------------------------------------

    public static readonly StyledProperty<double> ScrollTicksProperty =
        AvaloniaProperty.Register<PianoRoll, double>(nameof(ScrollTicks));

    public static readonly StyledProperty<double> TopCentsProperty =
        AvaloniaProperty.Register<PianoRoll, double>(nameof(TopCents), 9600.0);

    public static readonly StyledProperty<double> PixelsPerTickProperty =
        AvaloniaProperty.Register<PianoRoll, double>(nameof(PixelsPerTick), 0.06);

    public static readonly StyledProperty<double> PixelsPerCentProperty =
        AvaloniaProperty.Register<PianoRoll, double>(nameof(PixelsPerCent), 0.12);

    public static readonly StyledProperty<double> PlayheadTicksProperty =
        AvaloniaProperty.Register<PianoRoll, double>(nameof(PlayheadTicks), -1);

    static PianoRoll() =>
        AffectsRender<PianoRoll>(
            ScrollTicksProperty,
            TopCentsProperty,
            PixelsPerTickProperty,
            PixelsPerCentProperty,
            PlayheadTicksProperty);

    /// <summary>Tick at the left edge.</summary>
    public double ScrollTicks
    {
        get => GetValue(ScrollTicksProperty);
        set => SetValue(ScrollTicksProperty, value);
    }

    /// <summary>Cents at the top edge. Defaults to C8, near the top of the piano.</summary>
    public double TopCents
    {
        get => GetValue(TopCentsProperty);
        set => SetValue(TopCentsProperty, value);
    }

    public double PixelsPerTick
    {
        get => GetValue(PixelsPerTickProperty);
        set => SetValue(PixelsPerTickProperty, value);
    }

    public double PixelsPerCent
    {
        get => GetValue(PixelsPerCentProperty);
        set => SetValue(PixelsPerCentProperty, value);
    }

    /// <summary>Playhead position in ticks. Negative hides it.</summary>
    public double PlayheadTicks
    {
        get => GetValue(PlayheadTicksProperty);
        set => SetValue(PlayheadTicksProperty, value);
    }

    /// <summary>How many ticks the viewport spans at the current zoom.</summary>
    public double VisibleTicks =>
        PixelsPerTick > 0 && Bounds.Width > 0 ? Bounds.Width / PixelsPerTick : 0;

    /// <summary>How many cents the viewport spans at the current zoom.</summary>
    public double VisibleCents =>
        PixelsPerCent > 0 && Bounds.Height > 0 ? Bounds.Height / PixelsPerCent : 0;

    /// <summary>
    /// Scrolls so the playhead stays visible while playing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only acts when the playhead has actually left the comfortable band, and then jumps it to a
    /// fixed position rather than centring continuously. Continuous centring means the notes slide
    /// under a stationary line, which is much harder to read than a line crossing stationary notes -
    /// and it makes the display move even when nothing needs to.
    /// </para>
    /// <para>
    /// Returns false when it did nothing, so the caller can leave the user's own scrolling alone.
    /// </para>
    /// </remarks>
    public bool FollowPlayhead()
    {
        double span = VisibleTicks;
        if (span <= 0 || PlayheadTicks < 0)
        {
            return false;
        }

        double playhead = PlayheadTicks;
        double left = ScrollTicks;
        double right = left + span;

        // A band from 10% to 85%: leaving headroom on the right means a page jump shows what is
        // coming rather than landing the playhead at the very edge.
        double comfortableLeft = left + (span * 0.10);
        double comfortableRight = left + (span * 0.85);

        if (playhead >= comfortableLeft && playhead <= comfortableRight)
        {
            return false;
        }

        // Behind the viewport - a seek backwards, or a rewind - so show it with a little lead-in.
        ScrollTicks = playhead < left
            ? Math.Max(0, playhead - (span * 0.10))
            : playhead - (span * 0.10);

        return true;
    }

    /// <summary>
    /// The original notes, drawn as muted ghosts under the restyled ones.
    /// </summary>
    /// <remarks>Must be sorted by <see cref="RollNote.StartTicks"/>; culling relies on it.</remarks>
    public void SetGhostNotes(RollNote[] sortedByStart)
    {
        _ghosts = sortedByStart ?? [];
        _ghostMaxLength = PianoRollGeometry.MaxLength(_ghosts);
        EnsureBufferForBoth();
        InvalidateVisual();
    }

    /// <summary>The restyled notes, drawn solid on top. Must be sorted by start tick.</summary>
    public void SetNotes(RollNote[] sortedByStart)
    {
        _notes = sortedByStart ?? [];
        _noteMaxLength = PianoRollGeometry.MaxLength(_notes);
        EnsureBufferForBoth();
        InvalidateVisual();
    }

    /// <summary>
    /// Grows the quad buffer ahead of rendering, so a frame never allocates.
    /// </summary>
    /// <remarks>
    /// Sized to the whole note set, not to a guess at what fits on screen. An earlier version capped
    /// this at 8192 on the reasoning that nobody can read more notes than that at once - which is
    /// true, and irrelevant: the default zoom frames the entire piece, so a 20,000-note file put all
    /// 20,000 in view and the cap silently dropped the last 12,000, leaving the right-hand side of
    /// the roll blank. Truncating the visible set is a correctness bug however illegible the result
    /// would have been.
    /// <para>
    /// At 40 bytes per quad this is 800 KB for a 20,000-note file, and the ceiling caps a
    /// pathological file at about 5 MB. Both are cheap next to holding the notes themselves.
    /// </para>
    /// </remarks>
    private const int MaxQuads = 131_072;

    private void EnsureBuffer(int noteCount)
    {
        int wanted = Math.Min(Math.Max(1024, noteCount), MaxQuads);
        if (_quadBuffer.Length < wanted)
        {
            _quadBuffer = new NoteQuad[wanted];
        }
    }

    /// <summary>
    /// Sizes the buffer for both note sets together, since either may be the larger.
    /// </summary>
    private void EnsureBufferForBoth() => EnsureBuffer(Math.Max(_ghosts.Length, _notes.Length));

    // --- input ----------------------------------------------------------------------------

    /// <summary>Tightest and widest horizontal zoom, in pixels per tick.</summary>
    private const double MinPixelsPerTick = 0.002;
    private const double MaxPixelsPerTick = 4.0;

    /// <summary>Vertical zoom bounds, in pixels per cent. 0.02 shows the whole keyboard.</summary>
    private const double MinPixelsPerCent = 0.02;
    private const double MaxPixelsPerCent = 1.0;

    private Point? _dragOrigin;
    private double _dragStartScrollTicks;
    private double _dragStartTopCents;

    /// <summary>
    /// Wheel scrolls, Shift+wheel scrolls horizontally, Ctrl+wheel zooms.
    /// </summary>
    /// <remarks>
    /// Zoom is anchored on the pointer rather than the viewport corner, so the note under the
    /// cursor stays put. Anchoring on the corner is the obvious implementation and feels broken -
    /// the content slides away from wherever you were looking.
    /// </remarks>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        Point position = e.GetPosition(this);
        bool zoom = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool horizontal = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (zoom)
        {
            double factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;

            if (horizontal || Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X))
            {
                // Keep the tick under the cursor fixed while the scale changes.
                double tickUnderCursor = ScrollTicks + (position.X / PixelsPerTick);
                PixelsPerTick = Math.Clamp(PixelsPerTick * factor, MinPixelsPerTick, MaxPixelsPerTick);
                ScrollTicks = tickUnderCursor - (position.X / PixelsPerTick);
            }
            else
            {
                double centsUnderCursor = TopCents - (position.Y / PixelsPerCent);
                PixelsPerCent = Math.Clamp(PixelsPerCent * factor, MinPixelsPerCent, MaxPixelsPerCent);
                TopCents = centsUnderCursor + (position.Y / PixelsPerCent);
            }
        }
        else if (horizontal)
        {
            ScrollTicks -= e.Delta.Y * 120 / PixelsPerTick;
        }
        else
        {
            TopCents += e.Delta.Y * 40 / PixelsPerCent;
        }

        ClampViewport();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragOrigin = e.GetPosition(this);
        _dragStartScrollTicks = ScrollTicks;
        _dragStartTopCents = TopCents;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragOrigin is not { } origin)
        {
            return;
        }

        Point now = e.GetPosition(this);
        ScrollTicks = _dragStartScrollTicks - ((now.X - origin.X) / PixelsPerTick);
        TopCents = _dragStartTopCents + ((now.Y - origin.Y) / PixelsPerCent);
        ClampViewport();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragOrigin = null;
        e.Pointer.Capture(null);
    }

    /// <summary>Keeps the viewport somewhere useful - scrolling into empty space is disorienting.</summary>
    private void ClampViewport()
    {
        if (ScrollTicks < 0)
        {
            ScrollTicks = 0;
        }

        // A little past each end of the MIDI range, so the top and bottom notes are not flush.
        TopCents = Math.Clamp(TopCents, 200, 13_200);
    }

    public override void Render(DrawingContext context)
    {
        PianoRollPalette palette = _palette;
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(palette.Background, bounds);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        RollViewport viewport = new(
            ScrollTicks,
            TopCents,
            bounds.Width,
            bounds.Height,
            PixelsPerTick,
            PixelsPerCent);

        DrawRows(context, viewport, bounds, palette);
        DrawNotes(context, viewport, _ghosts, _ghostMaxLength, palette.Ghost, ghost: true);
        DrawNotes(context, viewport, _notes, _noteMaxLength, palette.Note, ghost: false);
        DrawPlayhead(context, viewport, bounds, PlayheadTicks, palette);
    }

    private void DrawRows(DrawingContext context, in RollViewport viewport, Rect bounds, PianoRollPalette palette)
    {
        (int low, int high) = PianoRollGeometry.VisibleNoteRange(viewport);
        double rowHeight = viewport.RowHeight;
        if (rowHeight <= 0)
        {
            return;
        }

        bool labelsFit = rowHeight >= 7;

        for (int note = low; note <= high; note++)
        {
            double y = viewport.YForCents(note * 100.0) - (rowHeight / 2.0);
            if (y > bounds.Height || y + rowHeight < 0)
            {
                continue;
            }

            int pitchClass = note % 12;
            context.FillRectangle(
                IsBlackKey[pitchClass] ? palette.BlackRow : palette.WhiteRow,
                new Rect(0, y, bounds.Width, rowHeight));

            // A line on every semitone at small zoom is noise; the octave boundary is the one that
            // helps you read pitch, so C gets the brighter line and the only label.
            bool isC = pitchClass == 0;
            context.DrawLine(isC ? palette.Octave : palette.Grid, new Point(0, y), new Point(bounds.Width, y));

            if (isC && labelsFit)
            {
                context.DrawText(LabelFor(note, palette), new Point(3, y + 1));
            }
        }
    }

    /// <summary>
    /// Key labels are the one thing the render path cannot build allocation-free, so they are cached
    /// by note number and only ever created for C rows. The cache is cleared whenever the palette
    /// changes (see <see cref="OnActualThemeVariantChanged"/>), since the brush is baked into each
    /// cached <see cref="FormattedText"/> at construction time.
    /// </summary>
    private FormattedText LabelFor(int note, PianoRollPalette palette)
    {
        if (_labelCache.TryGetValue(note, out FormattedText? cached))
        {
            return cached;
        }

        FormattedText text = new(
            $"C{(note / 12) - 1}",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            9,
            palette.OctaveLabel);

        _labelCache[note] = text;
        return text;
    }

    private void DrawNotes(
        DrawingContext context,
        in RollViewport viewport,
        RollNote[] notes,
        long maxLength,
        IBrush brush,
        bool ghost)
    {
        if (notes.Length == 0)
        {
            return;
        }

        int count = PianoRollGeometry.Cull(notes, maxLength, viewport, _quadBuffer);
        double radius = ghost ? 0 : 1.5;

        for (int i = 0; i < count; i++)
        {
            NoteQuad quad = _quadBuffer[i];
            Rect rect = new(quad.X, quad.Y, quad.Width, quad.Height);

            if (radius > 0 && quad.Width > 4)
            {
                context.DrawRectangle(brush, null, rect, radius, radius);
            }
            else
            {
                context.FillRectangle(brush, rect);
            }
        }
    }

    private static void DrawPlayhead(
        DrawingContext context,
        in RollViewport viewport,
        Rect bounds,
        double playheadTicks,
        PianoRollPalette palette)
    {
        // Negative means "not playing". A separate bool would be one more thing to keep in sync.
        if (playheadTicks < 0)
        {
            return;
        }

        double x = viewport.XForTick(playheadTicks);
        if (x < 0 || x > bounds.Width)
        {
            return;
        }

        context.DrawLine(palette.Playhead, new Point(x, 0), new Point(x, bounds.Height));
    }
}
