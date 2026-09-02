using System.Globalization;
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
internal sealed class PianoRollPalette
{
    public required IBrush Background { get; init; }
    public required IBrush WhiteRow { get; init; }
    public required IBrush BlackRow { get; init; }
    public required IBrush Ghost { get; init; }
    public required IBrush Note { get; init; }
    public required IBrush OctaveLabel { get; init; }
    public required Pen Grid { get; init; }
    public required Pen Octave { get; init; }
    public required Pen Playhead { get; init; }

    // --- the keyboard gutter ------------------------------------------------------------------

    /// <summary>The face of a white key. Far lighter than <see cref="WhiteRow"/>, which is a stripe.</summary>
    public required IBrush KeyWhite { get; init; }

    /// <summary>The face of a black key, drawn over the white one behind it.</summary>
    public required IBrush KeyBlack { get; init; }

    /// <summary>A key currently sounding, in the same hue as the solid notes it belongs to.</summary>
    public required IBrush KeyLit { get; init; }

    /// <summary>The hairline between two keys, and the edge where the keyboard meets the grid.</summary>
    public required Pen KeyEdge { get; init; }

    /// <summary>The octave name printed on each C key.</summary>
    public required IBrush KeyLabel { get; init; }

    // --- the bar ruler ------------------------------------------------------------------------

    public required IBrush RulerBackground { get; init; }

    public required IBrush BarLabel { get; init; }

    /// <summary>The barline running the full height of the grid.</summary>
    public required Pen BarLine { get; init; }
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
    public static readonly PianoRollPalette Dark = new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D)),
        WhiteRow = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x26)),
        BlackRow = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1F)),
        Ghost = new SolidColorBrush(Color.FromArgb(0x55, 0x8A, 0x8A, 0x96)),
        Note = new SolidColorBrush(Color.FromRgb(0x5B, 0x8F, 0xF9)),
        OctaveLabel = new SolidColorBrush(Color.FromArgb(0x99, 0xAA, 0xAA, 0xB4)),
        Grid = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1),
        Octave = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1),
        Playhead = new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0x6A, 0x5C)), 1.5),
        KeyWhite = new SolidColorBrush(Color.FromRgb(0xD6, 0xD7, 0xE0)),
        KeyBlack = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x2B)),
        KeyLit = new SolidColorBrush(Color.FromRgb(0x5B, 0x8F, 0xF9)),
        KeyEdge = new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0x10, 0x10, 0x14)), 1),
        KeyLabel = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x5C)),
        RulerBackground = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x25)),
        BarLabel = new SolidColorBrush(Color.FromArgb(0xBB, 0xAA, 0xAA, 0xB4)),
        BarLine = new Pen(new SolidColorBrush(Color.FromArgb(0x4A, 0xFF, 0xFF, 0xFF)), 1),
    };

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
    public static readonly PianoRollPalette Light = new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xF0)),
        WhiteRow = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFB)),
        BlackRow = new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE7)),
        Ghost = new SolidColorBrush(Color.FromArgb(0x50, 0x55, 0x5A, 0x66)),
        Note = new SolidColorBrush(Color.FromRgb(0x2F, 0x5F, 0xD9)),
        OctaveLabel = new SolidColorBrush(Color.FromArgb(0xAA, 0x40, 0x40, 0x48)),
        Grid = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x00, 0x00)), 1),
        Octave = new Pen(new SolidColorBrush(Color.FromArgb(0x45, 0x00, 0x00, 0x00)), 1),
        Playhead = new Pen(new SolidColorBrush(Color.FromRgb(0xD1, 0x48, 0x3A)), 1.5),
        KeyWhite = new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFD)),
        KeyBlack = new SolidColorBrush(Color.FromRgb(0x3A, 0x3C, 0x46)),
        KeyLit = new SolidColorBrush(Color.FromRgb(0x2F, 0x5F, 0xD9)),
        KeyEdge = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)), 1),
        KeyLabel = new SolidColorBrush(Color.FromRgb(0x86, 0x86, 0x92)),
        RulerBackground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE8)),
        BarLabel = new SolidColorBrush(Color.FromArgb(0xCC, 0x40, 0x40, 0x48)),
        BarLine = new Pen(new SolidColorBrush(Color.FromArgb(0x3A, 0x00, 0x00, 0x00)), 1),
    };

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
        _barLabelCache.Clear();

        InvalidateVisual();
    }

    // --- immutable drawing resources, created once ------------------------------------------

    private static readonly Typeface LabelTypeface = new("Inter");

    /// <summary>Which pitch classes are black keys, for the row striping.</summary>
    private static readonly bool[] IsBlackKey =
        [false, true, false, true, false, false, true, false, true, false, true, false];

    /// <summary>
    /// Width of the keyboard column down the left edge.
    /// </summary>
    /// <remarks>
    /// Fixed rather than proportional. It has to hold an octave label at nine points and a black key
    /// that still reads as narrower than the white one behind it; below about forty pixels the label
    /// runs into the black key, and above about sixty the keyboard starts eating the music.
    /// </remarks>
    public const double GutterWidth = 46;

    /// <summary>Height of the bar-number strip across the top.</summary>
    public const double RulerHeight = 19;

    /// <summary>How far across the gutter a black key reaches. Real keyboards are about this ratio.</summary>
    private const double BlackKeyWidthFraction = 0.62;

    /// <summary>Below this row height a key label cannot be read, so none is drawn.</summary>
    private const double LabelMinimumRowHeight = 8;

    /// <summary>
    /// Below this spacing consecutive bar numbers collide, so the ruler thins them out.
    /// </summary>
    private const double BarLabelMinimumSpacing = 30;

    /// <summary>
    /// The intervals bar numbering may thin to. Musicians count bars in fours and eights, so a
    /// ruler labelling every fifth bar is harder to read than one labelling every fourth - even
    /// though the fifth fits the space slightly better.
    /// </summary>
    private static readonly int[] LabelIntervals = [1, 2, 4, 8, 16, 32, 64];

    /// <summary>Below this spacing barlines merge into a grey wash, so they are dropped entirely.</summary>
    private const double BarLineMinimumSpacing = 5;

    // --- reused per-frame buffers -------------------------------------------------------------

    private NoteQuad[] _quadBuffer = new NoteQuad[1024];
    private RollNote[] _ghosts = [];
    private RollNote[] _notes = [];
    private long[] _bars = [];
    private long _ghostMaxLength;
    private long _noteMaxLength;
    private readonly Dictionary<int, FormattedText> _labelCache = [];
    private readonly Dictionary<int, FormattedText> _barLabelCache = [];

    /// <summary>
    /// Which semitone rows are sounding at the playhead, recomputed each frame.
    /// </summary>
    /// <remarks>
    /// A fixed 128-entry array cleared per frame rather than a set built per frame: the render path
    /// may not allocate, and one row per MIDI note is the whole domain. Microtonal notes light the
    /// key they are nearest to - there is no quarter-tone key to light, and rounding here is the
    /// same rounding the note number itself gets at the output stage.
    /// </remarks>
    private readonly bool[] _litRows = new bool[128];

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

    /// <summary>
    /// Which layer the keyboard lights from: the restyled notes, or the original ghosts.
    /// </summary>
    /// <remarks>
    /// The keyboard is meant to show what you are <em>hearing</em>, so it has to follow the A/B
    /// toggle rather than always following the solid layer. Lighting the restyled keys while the
    /// original is sounding is a quiet lie of exactly the kind the A/B switch exists to prevent.
    /// </remarks>
    public static readonly StyledProperty<bool> HighlightRestyledProperty =
        AvaloniaProperty.Register<PianoRoll, bool>(nameof(HighlightRestyled));

    static PianoRoll() =>
        AffectsRender<PianoRoll>(
            ScrollTicksProperty,
            TopCentsProperty,
            PixelsPerTickProperty,
            PixelsPerCentProperty,
            PlayheadTicksProperty,
            HighlightRestyledProperty);

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

    /// <summary>Whether the keyboard lights from the restyled layer rather than the ghosts.</summary>
    public bool HighlightRestyled
    {
        get => GetValue(HighlightRestyledProperty);
        set => SetValue(HighlightRestyledProperty, value);
    }

    /// <summary>Width of the grid itself, once the keyboard has taken its column.</summary>
    private double NoteAreaWidth => Math.Max(0, Bounds.Width - GutterWidth);

    /// <summary>Height of the grid itself, once the bar ruler has taken its strip.</summary>
    private double NoteAreaHeight => Math.Max(0, Bounds.Height - RulerHeight);

    /// <summary>How many ticks the viewport spans at the current zoom.</summary>
    public double VisibleTicks =>
        PixelsPerTick > 0 && NoteAreaWidth > 0 ? NoteAreaWidth / PixelsPerTick : 0;

    /// <summary>How many cents the viewport spans at the current zoom.</summary>
    public double VisibleCents =>
        PixelsPerCent > 0 && NoteAreaHeight > 0 ? NoteAreaHeight / PixelsPerCent : 0;

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
    /// Where the barlines fall, in ticks, ascending.
    /// </summary>
    /// <remarks>
    /// Handed in rather than derived here. The barlines a roll draws must be the same ones the
    /// metadata pane counts and the transport reads out, and three implementations of "where does
    /// bar 5 start" would eventually disagree - see <c>MeasureGrid</c>'s own note on why the staff
    /// and the exporter share one.
    /// </remarks>
    public void SetBars(long[] startTicksAscending)
    {
        _bars = startTicksAscending ?? [];
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
            PixelsPerCent,
            GutterWidth,
            RulerHeight);

        // Order matters. Rows and barlines are the ground the notes sit on; the keyboard and the
        // ruler are furniture drawn over both edges, so a note scrolled hard left or a barline at
        // tick zero cannot spill into them.
        DrawRows(context, viewport, bounds, palette);
        DrawBarLines(context, viewport, bounds, palette);
        DrawNotes(context, viewport, _ghosts, _ghostMaxLength, palette.Ghost, ghost: true);
        DrawNotes(context, viewport, _notes, _noteMaxLength, palette.Note, ghost: false);
        DrawPlayhead(context, viewport, bounds, PlayheadTicks, palette);

        MarkSoundingRows(PlayheadTicks);
        DrawKeyboard(context, viewport, palette);
        DrawRuler(context, viewport, bounds, palette);
    }

    private void DrawRows(DrawingContext context, in RollViewport viewport, Rect bounds, PianoRollPalette palette)
    {
        (int low, int high) = PianoRollGeometry.VisibleNoteRange(viewport);
        double rowHeight = viewport.RowHeight;
        if (rowHeight <= 0)
        {
            return;
        }

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
            // helps you read pitch, so C gets the brighter line. The label that used to sit here has
            // moved onto the C key itself, where it names something the eye is already looking at.
            context.DrawLine(
                pitchClass == 0 ? palette.Octave : palette.Grid,
                new Point(0, y),
                new Point(bounds.Width, y));
        }
    }

    // --- the keyboard ----------------------------------------------------------------------------

    /// <summary>
    /// Flags every semitone row sounding at <paramref name="playheadTicks"/>, from whichever layer
    /// the A/B toggle says is audible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses the same binary search the culling does, so the cost is the notes overlapping one tick
    /// rather than the whole file - and clears a fixed array rather than allocating a set, since the
    /// render path may not allocate.
    /// </para>
    /// <para>
    /// The scan cannot stop at the first note that has already ended: the notes are sorted by
    /// <em>start</em>, so a short note can sit between the playhead and a long one still sounding.
    /// It runs forward to the first onset past the playhead instead.
    /// </para>
    /// </remarks>
    private void MarkSoundingRows(double playheadTicks)
    {
        Array.Clear(_litRows);

        bool restyled = HighlightRestyled && _notes.Length > 0;
        RollNote[] source = restyled ? _notes : _ghosts;
        long maxLength = restyled ? _noteMaxLength : _ghostMaxLength;

        if (playheadTicks < 0 || source.Length == 0)
        {
            return;
        }

        for (int i = PianoRollGeometry.FirstPossiblyVisible(source, maxLength, playheadTicks);
             i < source.Length && source[i].StartTicks <= playheadTicks;
             i++)
        {
            if (source[i].EndTicks <= playheadTicks)
            {
                continue;
            }

            int row = (int)Math.Round(source[i].Cents / 100.0, MidpointRounding.AwayFromZero);
            if (row is >= 0 and < 128)
            {
                _litRows[row] = true;
            }
        }
    }

    /// <summary>
    /// The keyboard down the left edge: a white key per natural, a narrower black key over it per
    /// accidental, the octave printed on every C, and whatever is sounding lit.
    /// </summary>
    /// <remarks>
    /// Every row is the same height, because a row here is a grid row and the notes beside it have
    /// to line up with it. A real keyboard's uneven white keys would put the keyboard and the grid
    /// out of register, which is a worse lie than evenly spaced naturals - and it is the convention
    /// every piano-roll editor follows, for the same reason. The white key is drawn behind the black
    /// one at full width, which is what a keyboard looks like from above.
    /// </remarks>
    private void DrawKeyboard(DrawingContext context, in RollViewport viewport, PianoRollPalette palette)
    {
        (int low, int high) = PianoRollGeometry.VisibleNoteRange(viewport);
        double rowHeight = viewport.RowHeight;
        if (rowHeight <= 0 || GutterWidth <= 0)
        {
            return;
        }

        double top = viewport.RulerHeight;
        double bottom = viewport.Height;
        bool labelsFit = rowHeight >= LabelMinimumRowHeight;
        double blackWidth = GutterWidth * BlackKeyWidthFraction;

        context.FillRectangle(palette.KeyWhite, new Rect(0, top, GutterWidth, bottom - top));

        for (int note = low; note <= high; note++)
        {
            double y = viewport.YForCents(note * 100.0) - (rowHeight / 2.0);
            if (y > bottom || y + rowHeight < top)
            {
                continue;
            }

            int pitchClass = note % 12;
            bool lit = _litRows[note];

            if (IsBlackKey[pitchClass])
            {
                context.FillRectangle(
                    lit ? palette.KeyLit : palette.KeyBlack,
                    new Rect(0, y, blackWidth, Math.Max(1, rowHeight - 1)));
                continue;
            }

            if (lit)
            {
                context.FillRectangle(palette.KeyLit, new Rect(0, y, GutterWidth, rowHeight));
            }

            // The hairline sits under every natural rather than only between two of them. Drawn only
            // where a black key is missing it would appear at E-F and B-C and nowhere else, which
            // reads as a rendering fault rather than as a keyboard.
            context.DrawLine(
                palette.KeyEdge, new Point(0, y + rowHeight), new Point(GutterWidth, y + rowHeight));

            if (pitchClass == 0 && labelsFit)
            {
                FormattedText label = LabelFor(note, palette);
                context.DrawText(
                    label,
                    new Point(
                        GutterWidth - label.Width - 3,
                        y + Math.Max(0, (rowHeight - label.Height) / 2.0)));
            }
        }

        // The keyboard is furniture, not grid: a hard edge stops the eye reading a key as a note.
        context.DrawLine(palette.KeyEdge, new Point(GutterWidth, top), new Point(GutterWidth, bottom));
    }

    // --- the bar ruler ---------------------------------------------------------------------------

    /// <summary>Index of the first barline at or after a tick.</summary>
    private static int FirstBarAtOrAfter(long[] bars, double tick)
    {
        int low = 0;
        int high = bars.Length;

        while (low < high)
        {
            int mid = low + ((high - low) >> 1);
            if (bars[mid] < tick)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    /// <summary>
    /// How far apart the barlines are on screen on average, in pixels, or zero when there is nothing
    /// to space.
    /// </summary>
    /// <remarks>
    /// Averaged over the piece rather than measured off the first bar, and the difference is not
    /// academic: a pickup bar is routinely a fraction of the bars after it - one real file opens
    /// with a single 1/8 bar and continues in 4/4 - so the first gap is eight times too small and
    /// sparsified the whole ruler to a number every fourth bar when every bar fitted comfortably.
    /// The average is still only an estimate where the metre changes mid-piece, which is why the
    /// ruler also refuses a label that would land on top of the last one it drew.
    /// </remarks>
    private double BarSpacing(in RollViewport viewport) =>
        _bars.Length < 2
            ? 0
            : (_bars[^1] - _bars[0]) / (double)(_bars.Length - 1) * viewport.PixelsPerTick;

    /// <summary>
    /// The barlines, running the height of the grid behind the notes.
    /// </summary>
    /// <remarks>
    /// Dropped entirely rather than thinned once they are closer together than a few pixels. A
    /// hundred bars across a pane is not a grid, it is a grey wash over the music - and the ruler
    /// above still says where you are.
    /// </remarks>
    private void DrawBarLines(
        DrawingContext context, in RollViewport viewport, Rect bounds, PianoRollPalette palette)
    {
        if (_bars.Length == 0 || BarSpacing(viewport) < BarLineMinimumSpacing)
        {
            return;
        }

        for (int i = FirstBarAtOrAfter(_bars, viewport.ScrollTicks); i < _bars.Length; i++)
        {
            double x = viewport.XForTick(_bars[i]);
            if (x > bounds.Width)
            {
                break;
            }

            context.DrawLine(
                palette.BarLine, new Point(x, viewport.RulerHeight), new Point(x, bounds.Height));
        }
    }

    /// <summary>The numbered strip across the top, which is what makes the barlines readable.</summary>
    private void DrawRuler(
        DrawingContext context, in RollViewport viewport, Rect bounds, PianoRollPalette palette)
    {
        context.FillRectangle(palette.RulerBackground, new Rect(0, 0, bounds.Width, RulerHeight));
        context.DrawLine(palette.KeyEdge, new Point(0, RulerHeight), new Point(bounds.Width, RulerHeight));

        if (_bars.Length == 0)
        {
            return;
        }

        int interval = LabelInterval(BarSpacing(viewport));
        double lastLabelX = double.NegativeInfinity;

        for (int i = FirstBarAtOrAfter(_bars, viewport.ScrollTicks); i < _bars.Length; i++)
        {
            double x = viewport.XForTick(_bars[i]);
            if (x > bounds.Width)
            {
                break;
            }

            // A barline scrolled behind the keyboard still exists; it just has nowhere to print.
            if (x < GutterWidth)
            {
                continue;
            }

            context.DrawLine(palette.BarLine, new Point(x, RulerHeight - 5), new Point(x, RulerHeight));

            // The interval keeps the numbering tidy - musicians read bars in fours - and the gap
            // check keeps it legible where the metre changes and the interval's estimate is wrong.
            if (interval > 0 && i % interval == 0 && x - lastLabelX >= BarLabelMinimumSpacing)
            {
                context.DrawText(BarLabelFor(i + 1, palette), new Point(x + 3, 2));
                lastLabelX = x;
            }
        }
    }

    /// <summary>
    /// How many bars apart the printed numbers should be at this spacing, or 0 when even the
    /// coarsest interval will not fit.
    /// </summary>
    /// <remarks>
    /// Thinned rather than dropped. A whole piece framed in the pane puts its bars twenty pixels
    /// apart, which is too close to number consecutively - and a ruler with no numbers at all leaves
    /// the barlines saying only "a bar happens here", which the eye already knew.
    /// </remarks>
    private static int LabelInterval(double spacing)
    {
        if (spacing <= 0)
        {
            return 0;
        }

        foreach (int interval in LabelIntervals)
        {
            if (spacing * interval >= BarLabelMinimumSpacing)
            {
                return interval;
            }
        }

        return 0;
    }

    private FormattedText BarLabelFor(int bar, PianoRollPalette palette)
    {
        if (_barLabelCache.TryGetValue(bar, out FormattedText? cached))
        {
            return cached;
        }

        FormattedText text = new(
            bar.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10,
            palette.BarLabel);

        _barLabelCache[bar] = text;
        return text;
    }

    /// <summary>
    /// Key labels are the one thing the render path cannot build allocation-free, so they are cached
    /// by note number and only ever created for C keys. The cache is cleared whenever the palette
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
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            9,
            palette.KeyLabel);

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
        if (x < viewport.GutterWidth || x > bounds.Width)
        {
            return;
        }

        context.DrawLine(
            palette.Playhead, new Point(x, viewport.RulerHeight), new Point(x, bounds.Height));
    }
}
