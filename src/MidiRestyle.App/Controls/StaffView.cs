using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using MidiRestyle.Core.Notation;

namespace MidiRestyle.App.Controls;

/// <summary>One complete set of drawing colours for the staff view.</summary>
/// <remarks>
/// Built exactly twice, in <see cref="StaffPalettes"/>, never per frame - the same arrangement
/// <see cref="PianoRollPalette"/> uses, and for the same reason.
/// </remarks>
internal sealed class StaffPalette(
    IBrush background,
    IBrush ink,
    IBrush muted,
    IBrush faint,
    IBrush playhead)
{
    /// <summary>The page.</summary>
    public IBrush Background { get; } = background;

    /// <summary>Everything a copyist would draw in black: lines, notes, clefs, accidentals.</summary>
    public IBrush Ink { get; } = ink;

    /// <summary>Part names, tuplet numbers, the cents annotations - present but subordinate.</summary>
    public IBrush Muted { get; } = muted;

    /// <summary>Ledger lines and ties, a shade lighter than the ink so the notes stay dominant.</summary>
    public IBrush Faint { get; } = faint;

    public IBrush Playhead { get; } = playhead;
}

/// <summary>
/// The two palettes the staff can render in, plus the selection rule.
/// </summary>
/// <remarks>
/// A score is read as ink on paper, so the light palette is the "real" one and the dark palette
/// inverts it rather than the other way round. The dark ink is deliberately not pure white: a stave
/// of five hairlines at full white on near-black shimmers, so both the lines and the noteheads sit a
/// step in from the extremes.
/// </remarks>
internal static class StaffPalettes
{
    public static readonly StaffPalette Dark = new(
        background: new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D)),
        ink: new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xEA)),
        muted: new SolidColorBrush(Color.FromArgb(0xBB, 0xA6, 0xA6, 0xB2)),
        faint: new SolidColorBrush(Color.FromArgb(0x88, 0xC0, 0xC0, 0xCA)),
        playhead: new SolidColorBrush(Color.FromRgb(0xE8, 0x6A, 0x5C)));

    public static readonly StaffPalette Light = new(
        background: new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFA)),
        ink: new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x20)),
        muted: new SolidColorBrush(Color.FromArgb(0xCC, 0x4A, 0x4A, 0x54)),
        faint: new SolidColorBrush(Color.FromArgb(0x99, 0x2E, 0x2E, 0x36)),
        playhead: new SolidColorBrush(Color.FromRgb(0xD1, 0x48, 0x3A)));

    /// <summary>
    /// Picks a palette for an already-resolved theme variant. Anything that is not exactly
    /// <see cref="ThemeVariant.Light"/> falls back to <see cref="Dark"/>, matching
    /// <see cref="PianoRollPalettes.For"/>.
    /// </summary>
    public static StaffPalette For(ThemeVariant variant) => variant == ThemeVariant.Light ? Light : Dark;
}

/// <summary>
/// Every pen, brush and glyph path the staff draws with, built once per (palette, zoom) pair.
/// </summary>
/// <remarks>
/// <para>
/// Music glyphs are the awkward part of a notation renderer. The obvious approach - Unicode's
/// Musical Symbols block (U+1D11E and friends) - depends on a font that happens to contain the
/// astral-plane range being installed, and when it is not, every clef and double accidental renders
/// as a tofu box. Nothing in this app's dependency set ships such a font, so relying on one would
/// mean shipping a score that is legible on the developer's machine and broken on the user's.
/// </para>
/// <para>
/// So every glyph here is a hand-authored vector path, written in <em>staff spaces</em> so that one
/// set of numbers serves every zoom, and built into a cached <see cref="Geometry"/> positioned at its
/// own origin. Drawing one is then a translate and a <see cref="DrawingContext.DrawGeometry"/>, which
/// allocates nothing per frame. Rebuilt only when the theme or the zoom actually changes.
/// </para>
/// </remarks>
internal sealed class StaffResources
{
    public StaffPalette Palette { get; }

    public StaffMetrics Metrics { get; }

    public StaffResources(StaffPalette palette, StaffMetrics metrics)
    {
        Palette = palette;
        Metrics = metrics;

        double s = metrics.StaffSpace;
        IBrush ink = palette.Ink;
        IBrush faint = palette.Faint;

        StaffLine = new Pen(faint, Math.Max(0.6, s * 0.085));
        Ledger = new Pen(ink, Math.Max(0.7, s * 0.10));
        Barline = new Pen(ink, Math.Max(0.8, s * 0.11));
        ThickBarline = new Pen(ink, Math.Max(2.0, s * 0.42));
        Stem = new Pen(ink, Math.Max(0.8, s * 0.11));

        // A beam is stroked, not filled, so its thickness lives in the pen. Flat caps because a beam
        // ends squarely at its stem; a round cap would push it half a thickness past the outer stems.
        Beam = new Pen(ink, StaffGeometry.BeamThickness(metrics), lineCap: PenLineCap.Flat);
        NoteheadOutline = new Pen(ink, Math.Max(1.0, s * 0.20));
        Tie = new Pen(palette.Muted, Math.Max(0.8, s * 0.10));
        Bracket = new Pen(palette.Muted, Math.Max(0.7, s * 0.08));
        Brace = new Pen(ink, Math.Max(1.2, s * 0.20));
        ClefStroke = new Pen(ink, Math.Max(1.0, s * 0.19), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        AccidentalThin = new Pen(ink, Math.Max(0.8, s * 0.11));
        AccidentalThick = new Pen(ink, Math.Max(1.4, s * 0.26));
        RestStroke = new Pen(ink, Math.Max(1.0, s * 0.155), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        Playhead = new Pen(palette.Playhead, Math.Max(1.0, s * 0.16));

        TrebleClef = StaffGlyphs.TrebleClef(s);
        BassClef = StaffGlyphs.BassClef(s);

        // Just clear of the clef's own right edge, so the two dots never touch the hook whatever the
        // outline's exact extent turns out to be.
        BassClefDotX = BassClef.Bounds.Right + (s * 0.34);
        Notehead = BuildNotehead(s);
        FlagUp = BuildFlag(s, up: true);
        FlagDown = BuildFlag(s, up: false);
        FlatLoop = BuildFlatLoop(s, mirrored: false);
        FlatLoopMirrored = BuildFlatLoop(s, mirrored: true);
        DoubleSharp = BuildDoubleSharp(s);
        QuarterRest = StaffGlyphs.QuarterRest(s);
        RestHook = BuildRestHook(s);

        TimeSignatureFontSize = s * 2.55;
        PartNameFontSize = Math.Max(9.0, s * 1.5);
        AnnotationFontSize = Math.Max(7.0, s * 1.05);
        TupletFontSize = Math.Max(8.0, s * 1.25);
        MeasureNumberFontSize = Math.Max(7.5, s * 1.1);
        TitleFontSize = Math.Max(13.0, s * 2.4);
        SubtitleFontSize = Math.Max(9.0, s * 1.45);
    }

    public Pen StaffLine { get; }
    public Pen Ledger { get; }
    public Pen Barline { get; }
    public Pen ThickBarline { get; }
    public Pen Stem { get; }
    public Pen Beam { get; }
    public Pen NoteheadOutline { get; }
    public Pen Tie { get; }
    public Pen Bracket { get; }
    public Pen Brace { get; }
    public Pen ClefStroke { get; }
    public Pen AccidentalThin { get; }
    public Pen AccidentalThick { get; }
    public Pen RestStroke { get; }
    public Pen Playhead { get; }

    public double BassClefDotX { get; }

    public Geometry TrebleClef { get; }
    public Geometry BassClef { get; }
    public Geometry Notehead { get; }
    public Geometry FlagUp { get; }
    public Geometry FlagDown { get; }
    public Geometry FlatLoop { get; }
    public Geometry FlatLoopMirrored { get; }
    public Geometry DoubleSharp { get; }
    public Geometry QuarterRest { get; }
    public Geometry RestHook { get; }

    public double TimeSignatureFontSize { get; }
    public double PartNameFontSize { get; }
    public double AnnotationFontSize { get; }
    public double TupletFontSize { get; }
    public double MeasureNumberFontSize { get; }
    public double TitleFontSize { get; }
    public double SubtitleFontSize { get; }

    private static StreamGeometry Build(Action<StreamGeometryContext> draw)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            draw(context);
        }

        return geometry;
    }

    /// <summary>
    /// The G clef, as one continuous stroke, drawn from the tail below the stave upward.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stroked approximation rather than the filled two-contour outline a real music font uses,
    /// but it follows the actual construction of the glyph rather than a doodle that resembles it:
    /// the tail hook, the leaning stem, the crook over the top, back down across the stem, round the
    /// lower bowl and inward to a spiral whose centre sits exactly on the G line at the origin. Those
    /// two self-crossings are what the eye identifies a treble clef by - an earlier version had the
    /// bowl too small and the spiral off the line, and it read as a squiggle.
    /// </para>
    /// <para>
    /// The whole point of authoring it is that it needs no font to be installed; nothing in this
    /// app's dependency set ships the Unicode musical symbols block.
    /// </para>
    /// </remarks>
    private static StreamGeometry BuildTrebleClef(double s) => Build(c =>
    {
        Point P(double x, double y) => new(x * s, y * s);

        c.BeginFigure(P(-0.62, 2.92), isFilled: false);
        c.CubicBezierTo(P(-0.16, 3.24), P(0.26, 2.94), P(0.30, 2.42));
        c.CubicBezierTo(P(0.36, 1.10), P(0.44, -0.60), P(0.52, -2.02));
        c.CubicBezierTo(P(0.58, -3.02), P(0.62, -3.54), P(0.30, -3.94));
        c.CubicBezierTo(P(-0.14, -4.46), P(-0.82, -4.00), P(-0.74, -3.34));
        c.CubicBezierTo(P(-0.66, -2.74), P(-0.20, -2.28), P(0.20, -1.88));
        c.CubicBezierTo(P(0.76, -1.32), P(1.22, -0.84), P(1.16, -0.14));
        c.CubicBezierTo(P(1.10, 0.62), P(0.44, 1.16), P(-0.26, 1.10));
        c.CubicBezierTo(P(-1.02, 1.04), P(-1.36, 0.44), P(-1.16, -0.10));
        c.CubicBezierTo(P(-0.98, -0.58), P(-0.42, -0.76), P(-0.04, -0.48));
        c.CubicBezierTo(P(0.28, -0.24), P(0.30, 0.10), P(0.00, 0.14));
        c.EndFigure(isClosed: false);
    });

    /// <summary>The F clef's sweep, from the head above the F line down to the tail below the stave.</summary>
    /// <remarks>The head blob and the two dots straddling the F line are drawn as filled ellipses.</remarks>
    private static StreamGeometry BuildBassClef(double s) => Build(c =>
    {
        Point P(double x, double y) => new(x * s, y * s);

        c.BeginFigure(P(-0.78, 0.55), isFilled: false);
        c.CubicBezierTo(P(-0.96, -0.42), P(-0.06, -0.98), P(0.50, -0.40));
        c.CubicBezierTo(P(1.04, 0.16), P(0.86, 1.14), P(0.34, 1.80));
        c.CubicBezierTo(P(-0.10, 2.34), P(-0.82, 2.72), P(-1.32, 2.88));
        c.EndFigure(isClosed: false);
    });

    /// <summary>
    /// The notehead: an ellipse tilted up to the right, the shape every notehead since the 17th
    /// century has had. Drawn filled or stroked depending on the note value.
    /// </summary>
    private static Geometry BuildNotehead(double s) =>
        new EllipseGeometry(new Rect(-0.66 * s, -0.48 * s, 1.32 * s, 0.96 * s))
        {
            // Avalonia's y axis points down, so a visually anticlockwise tilt is a negative angle.
            Transform = new RotateTransform(-21),
        };

    /// <summary>One flag, anchored at the stem tip, filled.</summary>
    private static StreamGeometry BuildFlag(double s, bool up) => Build(c =>
    {
        double sign = up ? 1 : -1;
        Point P(double x, double y) => new(x * s, y * sign * s);

        c.BeginFigure(P(0, 0), isFilled: true);
        c.CubicBezierTo(P(0.95, 0.32), P(0.86, 1.05), P(0.26, 1.72));
        c.CubicBezierTo(P(0.70, 0.98), P(0.60, 0.52), P(0.02, 0.56));
        c.EndFigure(isClosed: true);
    });

    /// <summary>
    /// The bowl of a flat, drawn from the stem out and back. Mirrored, it is the half-flat, which is
    /// exactly how the quarter-tone accidental is defined - a reversed flat, not a new shape.
    /// </summary>
    private static StreamGeometry BuildFlatLoop(double s, bool mirrored) => Build(c =>
    {
        double sign = mirrored ? -1 : 1;
        Point P(double x, double y) => new(x * sign * s, y * s);

        c.BeginFigure(P(0, -1.55), isFilled: false);
        c.LineTo(P(0, 0.52));
        c.CubicBezierTo(P(0.62, -0.05), P(0.78, 0.42), P(0.30, 0.72));
        c.CubicBezierTo(P(0.16, 0.83), P(0.06, 0.72), P(0, 0.52));
        c.EndFigure(isClosed: false);
    });

    /// <summary>The double sharp: a squat filled cross, the shape of the U+1D12A glyph.</summary>
    private static StreamGeometry BuildDoubleSharp(double s) => Build(c =>
    {
        Point P(double x, double y) => new(x * s, y * s);

        // A four-armed cross with concave sides, drawn as one closed contour.
        c.BeginFigure(P(-0.42, -0.42), isFilled: true);
        c.LineTo(P(-0.12, -0.30));
        c.LineTo(P(0.00, -0.14));
        c.LineTo(P(0.12, -0.30));
        c.LineTo(P(0.42, -0.42));
        c.LineTo(P(0.30, -0.12));
        c.LineTo(P(0.14, 0.00));
        c.LineTo(P(0.30, 0.12));
        c.LineTo(P(0.42, 0.42));
        c.LineTo(P(0.12, 0.30));
        c.LineTo(P(0.00, 0.14));
        c.LineTo(P(-0.12, 0.30));
        c.LineTo(P(-0.42, 0.42));
        c.LineTo(P(-0.30, 0.12));
        c.LineTo(P(-0.14, 0.00));
        c.LineTo(P(-0.30, -0.12));
        c.EndFigure(isClosed: true);
    });

    /// <summary>The quarter rest's zigzag, centred on the middle line.</summary>
    private static StreamGeometry BuildQuarterRest(double s) => Build(c =>
    {
        Point P(double x, double y) => new(x * s, y * s);

        c.BeginFigure(P(-0.28, -1.28), isFilled: false);
        c.LineTo(P(0.26, -0.48));
        c.LineTo(P(-0.24, 0.16));
        c.LineTo(P(0.30, 0.88));
        c.CubicBezierTo(P(-0.16, 0.60), P(-0.38, 1.05), P(0.02, 1.38));
        c.EndFigure(isClosed: false);
    });

    /// <summary>
    /// The hook that joins a rest's blob to the top of its stem, as a filled sliver.
    /// </summary>
    /// <remarks>
    /// Traced from <c>Music-eighthrest.svg</c> and converted into staff spaces about the glyph's
    /// origin, which is why the numbers are not round ones. It was a bare two-point stroke until
    /// 2026-08-28, which is part of why an eighth rest read as a plain "7".
    /// </remarks>
    private static StreamGeometry BuildRestHook(double s) => Build(c =>
    {
        Point P(double x, double y) => new(x * s, y * s);

        c.BeginFigure(P(-0.493, -0.283), isFilled: false);
        c.CubicBezierTo(P(-0.266, -0.267), P(0.161, -0.325), P(0.238, -0.721));
        c.CubicBezierTo(P(0.140, -0.566), P(0.136, -0.265), P(-0.434, -0.397));
        c.EndFigure(isClosed: false);
    });
}

/// <summary>
/// The staff-notation view: a horizontally scrolling system per part, drawn straight to a
/// <see cref="DrawingContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// A custom <see cref="Control"/> overriding <see cref="Render"/>, <b>not</b> a panel of per-glyph
/// elements - the same rule the piano roll follows, and it matters more here, since one bar of music
/// is a dozen visual pieces (notehead, stem, flags, dots, accidental, ledger lines) and a dense file
/// would run to hundreds of thousands of them.
/// </para>
/// <para>
/// The layout is one continuous system per part rather than wrapped systems down a page. That choice
/// follows from the control's job: it sits beside a piano roll showing the same music, scrolls with
/// the same transport, and is scrubbed against a playhead. A wrapped page would need the playhead to
/// jump between systems and would make <see cref="ScrollMeasures"/> meaningless. The clef, part name,
/// brace and prevailing time signature therefore live in a fixed left gutter that the music scrolls
/// underneath, so they are readable at every scroll position instead of only at the start of the
/// piece.
/// </para>
/// <para>
/// All layout arithmetic lives in <see cref="StaffGeometry"/>; this class holds drawing only.
/// </para>
/// </remarks>
public sealed class StaffView : Control
{
    // --- properties ------------------------------------------------------------------------------

    public static readonly StyledProperty<NotationScore?> ScoreProperty =
        AvaloniaProperty.Register<StaffView, NotationScore?>(nameof(Score));

    public static readonly StyledProperty<double> ScrollYProperty =
        AvaloniaProperty.Register<StaffView, double>(nameof(ScrollY));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<StaffView, double>(nameof(Zoom), 1.0);

    public static readonly StyledProperty<long> PlayheadTicksProperty =
        AvaloniaProperty.Register<StaffView, long>(nameof(PlayheadTicks), -1);

    static StaffView() =>
        AffectsRender<StaffView>(
            ScoreProperty,
            ScrollYProperty,
            ZoomProperty,
            PlayheadTicksProperty);

    public StaffView() =>
        // The page is taller than the control by design, so without this the systems below the fold
        // paint over whatever sits under the view.
        ClipToBounds = true;

    /// <summary>The score to draw. Null or empty draws a neutral placeholder, never an exception.</summary>
    public NotationScore? Score
    {
        get => GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    /// <summary>
    /// Vertical scroll position in pixels from the top of the page.
    /// </summary>
    /// <remarks>
    /// Pixels rather than systems, because a system is not a stable unit: rewrapping at a different
    /// width or zoom changes how many bars a system holds, so "system 7" means somewhere else
    /// afterwards. The page height is reported through <see cref="ContentHeight"/> so the host can
    /// size a scrollbar in the same units.
    /// </remarks>
    public double ScrollY
    {
        get => GetValue(ScrollYProperty);
        set => SetValue(ScrollYProperty, value);
    }

    /// <summary>Staff size, 1.0 being the default.</summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Playhead position in absolute ticks. Negative hides it.</summary>
    public long PlayheadTicks
    {
        get => GetValue(PlayheadTicksProperty);
        set => SetValue(PlayheadTicksProperty, value);
    }

    /// <summary>How many measures the score has.</summary>
    public int MeasureCount => Score?.MeasureCount ?? 0;

    /// <summary>Total laid-out page height in pixels at the current zoom and width.</summary>
    public double ContentHeight => EnsureLayout().ContentHeight;

    /// <summary>How many systems the score wrapped into at the current zoom and width.</summary>
    public int SystemCount => EnsureLayout().SystemCount;

    /// <summary>
    /// The tick under a point in this control's own coordinates, for click-to-seek.
    /// </summary>
    /// <remarks>
    /// The control's y is a viewport y and the layout's is a page y, so the scroll offset has to go
    /// in before the question is asked - otherwise every click lands on whichever system happens to
    /// be at the top of the page.
    /// </remarks>
    public bool TryTickAt(Point point, out long tick)
    {
        tick = 0;

        StaffPageLayout layout = EnsureLayout();
        return !layout.IsEmpty && layout.TryTickAt(point.X, point.Y + ScrollY, out tick);
    }

    /// <summary>
    /// Scrolls so the playhead's system is comfortably visible, reporting whether it moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same contract as <c>PianoRoll.FollowPlayhead</c> - the host calls both from one 60 Hz timer
    /// - extended to the vertical page: following now means moving <em>down to the system the playhead
    /// is in</em>, not just sliding sideways within one. The decision itself is
    /// <see cref="StaffPageLayout.FollowPlayhead"/>, so it can be tested without a window.
    /// </para>
    /// <para>
    /// Returning <c>false</c> when nothing was needed is the load-bearing half of the contract: called
    /// sixty times a second, a follow that always scrolled would fight the reader's own scrolling.
    /// </para>
    /// </remarks>
    public bool FollowPlayhead()
    {
        StaffPageLayout layout = EnsureLayout();
        double height = Bounds.Height;

        if (layout.IsEmpty
            || height <= 0
            || !layout.FollowPlayhead(PlayheadTicks, ScrollY, height, out double target))
        {
            return false;
        }

        ScrollY = target;
        return true;
    }

    // --- theme -----------------------------------------------------------------------------------

    private StaffPalette _palette = StaffPalettes.Dark;
    private StaffResources? _resources;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        _palette = StaffPalettes.For(ActualThemeVariant);
        _resources = null;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _palette = StaffPalettes.For(ActualThemeVariant);

        // Both caches bake a brush in at construction time - pens take theirs in the constructor,
        // and so does FormattedText - so a stale cache would keep drawing in the old theme.
        _resources = null;
        _textCache.Clear();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ZoomProperty)
        {
            _resources = null;
            _textCache.Clear();
            _layout = null;
        }
        else if (change.Property == ScoreProperty)
        {
            _layout = null;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // Width decides where the systems break, so a resize rewraps the page. Height does not, but
        // it is cheap to rebuild and the alternative is a second cache key that buys nothing.
        if (e.WidthChanged)
        {
            _layout = null;
        }
    }

    // --- cached layout ---------------------------------------------------------------------------

    /// <summary>
    /// The wrapped page, rebuilt only when the score, the zoom or the width actually changes.
    /// </summary>
    /// <remarks>
    /// Everything expensive lives here: measure spacing, system breaking, justification. Rebuilding
    /// it per frame would make scrolling cost O(measures) per redraw for no reason, since none of it
    /// depends on the scroll position.
    /// </remarks>
    private StaffPageLayout? _layout;

    private NotationScore? _layoutScore;
    private double _layoutWidth = -1;

    /// <summary>The indent of the first system, which names its parts in full.</summary>
    private StaffIndent _firstIndent;

    /// <summary>The indent of every later system, which uses the abbreviated names.</summary>
    private StaffIndent _laterIndent;

    /// <summary>Full and abbreviated part names, one entry per part, resolved with the layout.</summary>
    private string[] _partNames = [];
    private string[] _partAbbreviations = [];

    private StaffResources EnsureResources()
    {
        StaffMetrics metrics = StaffMetrics.ForZoom(Zoom);
        if (_resources is null || _resources.Palette != _palette || _resources.Metrics != metrics)
        {
            _resources = new StaffResources(_palette, metrics);
            _textCache.Clear();
        }

        return _resources;
    }

    /// <summary>
    /// The current page layout, building it if the score, zoom or width has moved under it.
    /// </summary>
    /// <remarks>
    /// Public read-only members call this too, so a host asking for <see cref="ContentHeight"/> before
    /// the first frame gets a real answer rather than zero - which would leave its scrollbar dead
    /// until something else forced a redraw.
    /// </remarks>
    private StaffPageLayout EnsureLayout()
    {
        NotationScore? score = Score;
        double width = Bounds.Width;

        if (_layout is { } cached
            && ReferenceEquals(_layoutScore, score)
            && Math.Abs(_layoutWidth - width) < 0.5)
        {
            return cached;
        }

        StaffResources resources = EnsureResources();
        StaffMetrics metrics = resources.Metrics;

        ResolvePartNames(score);

        double fullWidth = 0;
        double shortWidth = 0;
        bool grandStaff = false;

        for (int i = 0; i < _partNames.Length; i++)
        {
            fullWidth = Math.Max(fullWidth, Text(TextRole.PartName, _partNames[i], resources).Width);
            shortWidth = Math.Max(
                shortWidth, Text(TextRole.PartName, _partAbbreviations[i], resources).Width);
        }

        if (score is not null)
        {
            foreach (NotationPart part in score.Parts)
            {
                grandStaff |= part.IsGrandStaff;
            }
        }

        _firstIndent = StaffGeometry.ComputeIndent(fullWidth, grandStaff, reserveTime: true, metrics);
        _laterIndent = StaffGeometry.ComputeIndent(shortWidth, grandStaff, reserveTime: false, metrics);

        _layout = StaffPageLayout.Build(
            score, metrics, width, _firstIndent.MusicX, _laterIndent.MusicX);
        _layoutScore = score;
        _layoutWidth = width;

        return _layout;
    }

    /// <summary>
    /// Fills the full and abbreviated name of every part.
    /// </summary>
    /// <remarks>
    /// Abbreviating after the first system is the convention, but only while the abbreviations still
    /// tell the parts apart: two staves both labelled "Pno." are worse than two spelled out in full.
    /// So a collision drops the whole score back to full names rather than guessing harder.
    /// </remarks>
    private void ResolvePartNames(NotationScore? score)
    {
        int count = score?.Parts.Count ?? 0;

        if (_partNames.Length != count)
        {
            _partNames = new string[count];
            _partAbbreviations = new string[count];
        }

        if (score is null)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            _partNames[i] = score.Parts[i].Name ?? string.Empty;
            _partAbbreviations[i] = Abbreviate(_partNames[i]);
        }

        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if (!string.Equals(_partAbbreviations[i], _partAbbreviations[j], StringComparison.Ordinal))
                {
                    continue;
                }

                Array.Copy(_partNames, _partAbbreviations, count);
                return;
            }
        }
    }

    /// <summary>The short label a part is given from its second system on.</summary>
    private static string Abbreviate(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length <= 7)
        {
            return trimmed;
        }

        int space = trimmed.LastIndexOf(' ');
        if (space > 0 && space + 1 < trimmed.Length)
        {
            // Instrument names put the noun last - "Acoustic Grand Piano" abbreviates to "Piano" -
            // which reads far better than initials do.
            string tail = trimmed[(space + 1)..];
            if (tail.Length is >= 3 and <= 9 && char.IsLetter(tail[0]))
            {
                return tail;
            }
        }

        return string.Concat(trimmed.AsSpan(0, 5), ".");
    }

    // --- text ------------------------------------------------------------------------------------

    private static readonly Typeface LabelTypeface = new("Inter");

    /// <summary>
    /// The face the time-signature numerals are set in.
    /// </summary>
    /// <remarks>
    /// Bold, because engraved metre numerals are heavy - they have to hold their own against five
    /// staff lines running straight through them. At regular weight they read as a page number that
    /// wandered onto the stave.
    /// </remarks>
    private static readonly Typeface NumeralTypeface = new("Inter", weight: FontWeight.Bold);

    /// <summary>
    /// Which colour and size a cached <see cref="FormattedText"/> was built for. Baked into the key
    /// because <see cref="FormattedText"/> takes its brush and size at construction, not at draw time.
    /// </summary>
    private enum TextRole
    {
        TimeSignature,
        PartName,
        Annotation,
        Tuplet,
        MeasureNumber,
        Title,
        Subtitle,
        Placeholder,
    }

    private readonly Dictionary<(TextRole Role, string Text), FormattedText> _textCache = [];

    private FormattedText Text(TextRole role, string text, StaffResources resources)
    {
        if (_textCache.TryGetValue((role, text), out FormattedText? cached))
        {
            return cached;
        }

        (double size, IBrush brush) = role switch
        {
            TextRole.TimeSignature => (resources.TimeSignatureFontSize, resources.Palette.Ink),
            TextRole.PartName => (resources.PartNameFontSize, resources.Palette.Muted),
            TextRole.Annotation => (resources.AnnotationFontSize, resources.Palette.Muted),
            TextRole.Tuplet => (resources.TupletFontSize, resources.Palette.Muted),
            TextRole.MeasureNumber => (resources.MeasureNumberFontSize, resources.Palette.Muted),
            TextRole.Title => (resources.TitleFontSize, resources.Palette.Ink),
            TextRole.Subtitle => (resources.SubtitleFontSize, resources.Palette.Muted),
            _ => (13.0, resources.Palette.Muted),
        };

        Typeface typeface = role is TextRole.TimeSignature or TextRole.Title
            ? NumeralTypeface
            : LabelTypeface;

        FormattedText formatted = new(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);

        _textCache[(role, text)] = formatted;
        return formatted;
    }

    // --- input -----------------------------------------------------------------------------------

    /// <summary>
    /// Wheel scrolls the page down, Ctrl+wheel zooms. Nothing here is required by the host - the two
    /// properties are bindable - but a score you cannot scroll with the wheel reads as broken.
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            double factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
            Zoom = Math.Clamp(Zoom * factor, StaffMetrics.MinZoom, StaffMetrics.MaxZoom);
        }
        else
        {
            StaffPageLayout layout = EnsureLayout();
            double step = EnsureResources().Metrics.StaffSpace * 6.0;
            ScrollY = layout.ClampScrollY(ScrollY - (e.Delta.Y * step), Bounds.Height);
        }

        e.Handled = true;
    }

    // --- render ----------------------------------------------------------------------------------

    private readonly MeasureAccidentals[] _accidentals = [new MeasureAccidentals(), new MeasureAccidentals()];

    /// <summary>
    /// What identifies the two ends of one tie: the same written pitch, in the same voice, of the
    /// same staff of the same part.
    /// </summary>
    /// <remarks>
    /// The part is in the key because a page holds several parts and every one of them numbers its
    /// staves and voices from 1 - so without it, a tie in the piano's staff 1 voice 1 pairs happily
    /// with an unrelated note in the flute's.
    /// </remarks>
    private readonly record struct TieKey(int Part, int Staff, int Voice, int Index);

    /// <summary>A tie's open end: where it starts, and which way that note's stem pointed.</summary>
    private readonly record struct TiePoint(double X, double Y, StemDirection Stem);

    private readonly Dictionary<TieKey, TiePoint> _tieStarts = [];

    /// <summary>Ties whose start was on the system just drawn, so this system's end is a stub.</summary>
    private readonly HashSet<TieKey> _tieIncoming = [];

    /// <summary>Ties still open when a system ended, handed to the next system as incoming.</summary>
    private readonly HashSet<TieKey> _tieCarried = [];

    public override void Render(DrawingContext context)
    {
        StaffResources resources = EnsureResources();
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(resources.Palette.Background, bounds);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        NotationScore? score = Score;
        if (score is null || score.IsEmpty)
        {
            DrawPlaceholder(context, bounds, resources);
            return;
        }

        StaffPageLayout layout = EnsureLayout();
        if (layout.IsEmpty)
        {
            DrawPlaceholder(context, bounds, resources);
            return;
        }

        double scrollY = layout.ClampScrollY(ScrollY, bounds.Height);
        SystemRange visible = layout.VisibleSystems(scrollY, bounds.Height);

        _beamCount = 0;
        _beamClosed = false;
        _chordBeamed = false;

        for (int s = visible.First; s < visible.EndExclusive; s++)
        {
            DrawSystem(context, resources, score, layout, s, scrollY);
        }

        DrawTitleBlock(context, resources, score, bounds, scrollY);
    }

    /// <summary>
    /// The title and, under it, the scale the music was restyled into.
    /// </summary>
    /// <remarks>
    /// Drawn in the page's top margin and scrolled with it, so it behaves like the head of a printed
    /// page rather than a banner pinned to the window. The subtitle names the scale because that is
    /// the one thing a reader of a restyled score cannot work out from the notes: with
    /// <c>&lt;fifths&gt;0&lt;/fifths&gt;</c> and explicit accidentals there is no key signature to
    /// tell them what they are looking at.
    /// </remarks>
    private void DrawTitleBlock(
        DrawingContext context, StaffResources resources, NotationScore score, Rect bounds, double scrollY)
    {
        double s = resources.Metrics.StaffSpace;
        double y = (s * 1.4) - scrollY;

        if (y + (s * 6) < 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(score.Title))
        {
            FormattedText title = Text(TextRole.Title, score.Title, resources);
            context.DrawText(title, new Point((bounds.Width - title.Width) / 2, y));
            y += title.Height + (s * 0.15);
        }

        if (!string.IsNullOrWhiteSpace(score.ScaleName))
        {
            FormattedText subtitle = Text(TextRole.Subtitle, score.ScaleName, resources);
            context.DrawText(subtitle, new Point((bounds.Width - subtitle.Width) / 2, y));
        }
    }

    private void DrawPlaceholder(DrawingContext context, Rect bounds, StaffResources resources)
    {
        FormattedText text = Text(TextRole.Placeholder, "No score to display", resources);
        context.DrawText(
            text,
            new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2));
    }

    // --- one system ---------------------------------------------------------------------------------

    /// <summary>
    /// Draws one system: its indent, its staves, its measures, and the playhead if it falls here.
    /// </summary>
    /// <remarks>
    /// The unit of a page is the system, not the measure, because everything that makes a page read as
    /// music is a property of the system - the clef and signature at its head, the barline joining its
    /// staves, the justification that makes its last barline meet the right margin.
    /// </remarks>
    private void DrawSystem(
        DrawingContext context,
        StaffResources resources,
        NotationScore score,
        StaffPageLayout layout,
        int system,
        double scrollY)
    {
        StaffMetrics metrics = resources.Metrics;
        StaffIndent indent = system == 0 ? _firstIndent : _laterIndent;

        double systemTop = layout.SystemTop(system) - scrollY;
        double systemBottom = systemTop + layout.SystemBlockHeight;
        double musicRight = layout.SystemMusicRight(system);
        MeasureRange measures = layout.MeasuresIn(system);

        // Ties are paired within a system; one crossing a system break gets a stub at each end
        // instead, which is how a real score shows a tie that runs off the line.
        BeginSystemTies();

        // The one vertical that joins every staff of the system, which is what says "read these
        // together" - a page of unjoined staves reads as unrelated strips.
        context.DrawLine(
            resources.Barline,
            new Point(indent.SystemBarlineX, systemTop),
            new Point(indent.SystemBarlineX, systemBottom));

        double partTop = systemTop;

        for (int p = 0; p < score.Parts.Count; p++)
        {
            NotationPart part = score.Parts[p];
            DrawSystemPart(
                context, resources, layout, part, p, system, indent, partTop, musicRight, measures);
            partTop += metrics.PartHeight(part.StaffCount) + metrics.PartGap;
        }

        EndSystemTies(context, resources);
        DrawMeasureNumber(context, resources, score, measures, layout, systemTop);
        DrawPlayhead(context, resources, layout, system, systemTop);
    }

    /// <summary>
    /// The bar number above the first measure of a system.
    /// </summary>
    /// <remarks>
    /// Only where a system begins, and never on bar 1, which is the printed convention: the number is
    /// there so a player can be told "from bar 34", and on every bar it would be clutter. It is also
    /// the clearest signal on the page that this is a wrapped score rather than a strip - each system
    /// announces where in the piece it picks up.
    /// </remarks>
    private void DrawMeasureNumber(
        DrawingContext context,
        StaffResources resources,
        NotationScore score,
        MeasureRange measures,
        StaffPageLayout layout,
        double systemTop)
    {
        if (measures.IsEmpty || measures.First == 0)
        {
            return;
        }

        int number = measures.First + 1;
        foreach (NotationPart part in score.Parts)
        {
            if (measures.First < part.Measures.Count)
            {
                number = part.Measures[measures.First].Number;
                break;
            }
        }

        FormattedText text = Text(
            TextRole.MeasureNumber, number.ToString(CultureInfo.InvariantCulture), resources);

        context.DrawText(
            text,
            new Point(
                layout.MeasureX(measures.First),
                systemTop - (resources.Metrics.StaffSpace * 1.4) - text.Height));
    }

    private void DrawSystemPart(
        DrawingContext context,
        StaffResources resources,
        StaffPageLayout layout,
        NotationPart part,
        int partIndex,
        int system,
        in StaffIndent indent,
        double partTop,
        double musicRight,
        MeasureRange measures)
    {
        StaffMetrics metrics = resources.Metrics;
        double partBottom = partTop + metrics.PartHeight(part.StaffCount);

        for (int staff = 1; staff <= part.StaffCount; staff++)
        {
            double staffTopY = metrics.StaffTop(partTop, staff);
            DrawStaffLines(context, resources, staffTopY, indent.SystemBarlineX, musicRight);

            Clef clef = staff - 1 < part.Clefs.Count ? part.Clefs[staff - 1] : Clef.Treble;

            // Clef, then the reserved key-signature slot, then time - the order every system's head
            // is read in. Nothing is drawn at indent.KeyX: see StaffMetrics.KeySignatureWidth.
            DrawClef(context, resources, clef, indent.ClefX + (metrics.ClefWidth / 2), staffTopY);

            // Printed at the head of the first system only; a later change is printed inline at the
            // measure that changes, which is where a reader looks for it.
            if (system == 0 && !measures.IsEmpty)
            {
                (int beats, int unit) = SignatureAt(part, measures.First);
                DrawTimeSignature(context, resources, beats, unit, indent.TimeX, staffTopY);
            }
        }

        if (part.IsGrandStaff)
        {
            DrawBrace(context, resources, indent.BraceX, partTop, partBottom);
        }

        DrawPartName(
            context,
            resources,
            system == 0 || partIndex >= _partAbbreviations.Length
                ? part.Name
                : _partAbbreviations[partIndex],
            indent.NameX,
            partTop,
            partBottom);

        int lastMeasure = layout.MeasureCount - 1;

        for (int i = measures.First; i < measures.EndExclusive; i++)
        {
            double measureX = layout.MeasureX(i);
            double measureWidth = layout.MeasureWidth(i);

            DrawBarline(
                context, resources, measureX + measureWidth, partTop, partBottom, i == lastMeasure);

            if (i >= part.Measures.Count)
            {
                continue;
            }

            NotationMeasure measure = part.Measures[i];

            if (layout.PrintsTimeSignature(i))
            {
                for (int staff = 1; staff <= part.StaffCount; staff++)
                {
                    DrawTimeSignature(
                        context, resources, measure.BeatsPerMeasure, measure.BeatUnit,
                        measureX + (metrics.StaffSpace * 0.4), metrics.StaffTop(partTop, staff));
                }
            }

            DrawMeasureEntries(context, resources, layout, part, partIndex, measure, i, partTop);
        }
    }

    private static void DrawStaffLines(
        DrawingContext context, StaffResources resources, double staffTopY, double x0, double x1)
    {
        for (int line = 0; line < 5; line++)
        {
            double y = StaffGeometry.YForStaffLine(line, staffTopY, resources.Metrics);
            context.DrawLine(resources.StaffLine, new Point(x0, y), new Point(x1, y));
        }
    }

    private static void DrawBarline(
        DrawingContext context, StaffResources resources, double x, double top, double bottom, bool final)
    {
        if (!final)
        {
            context.DrawLine(resources.Barline, new Point(x, top), new Point(x, bottom));
            return;
        }

        double gap = resources.Metrics.StaffSpace * 0.45;
        context.DrawLine(resources.Barline, new Point(x - gap, top), new Point(x - gap, bottom));
        context.DrawLine(resources.ThickBarline, new Point(x, top), new Point(x, bottom));
    }

    private void DrawMeasureEntries(
        DrawingContext context,
        StaffResources resources,
        StaffPageLayout layout,
        NotationPart part,
        int partIndex,
        NotationMeasure measure,
        int measureIndex,
        double partTop)
    {
        StaffMetrics metrics = resources.Metrics;

        foreach (MeasureAccidentals state in _accidentals)
        {
            state.Reset();
        }

        // Tuplet bracket run, flushed when the ratio, the staff or the measure changes. Consecutive
        // entries sharing a ratio are one tuplet; that is what the ratio means.
        _chordBeamed = false;

        Tuplet runTuplet = Tuplet.None;
        int runStaff = 0;
        double runStartX = 0;
        double runEndX = 0;
        double runStaffTop = 0;

        foreach (NotationEntry entry in measure.Entries)
        {
            int staff = Math.Clamp(entry.Staff, 1, Math.Max(1, part.StaffCount));
            Clef clef = staff - 1 < part.Clefs.Count ? part.Clefs[staff - 1] : Clef.Treble;
            double staffTopY = metrics.StaffTop(partTop, staff);

            // Placed on the measure's own note columns, not proportionally across its ticks - which
            // is what stops consecutive sixteenths overlapping and leaves a whole note room to breathe.
            double x = layout.XForTick(measureIndex, entry.StartTicks);

            if (entry.Note is { } note)
            {
                // A chord's timed head arrives first and its members immediately after, so the head's
                // answer is still current when they are reached. That ordering is what lets a member -
                // whose own Beams list is empty by contract - know it belongs to a beamed chord
                // without the model having to say so on every member.
                if (!entry.IsChordMember)
                {
                    FlushChordStem(context, resources);
                    _chordBeamed = entry.IsBeamed;
                }

                // Never drawn here for a chordable note: the stem belongs to the whole chord, and
                // the chord is not complete until an entry arrives that is not one of its members.
                DrawNote(context, resources, entry, note, clef, partIndex, staff, staffTopY, x);

                if (entry.IsChordMember)
                {
                    // Never a group boundary. It widens its chord's column, in the open beam group
                    // for a beamed chord and in the pending single stem for an unbeamed one.
                    if (_chordBeamed)
                    {
                        ExtendBeamChord(note.DiatonicIndex);
                    }
                    else
                    {
                        ExtendChordStem(note.DiatonicIndex);
                    }
                }
                else if (entry.IsBeamed)
                {
                    AppendToBeamGroup(context, resources, entry, note.DiatonicIndex, clef, staff, staffTopY, x);
                }
                else
                {
                    FlushBeamGroup(context, resources);
                    BeginChordStem(entry, note.DiatonicIndex, clef, staffTopY, x);
                }
            }
            else
            {
                // A rest is never beamed, so it closes whatever group was open.
                _chordBeamed = false;
                FlushChordStem(context, resources);
                FlushBeamGroup(context, resources);
                DrawRest(context, resources, entry.Duration, staffTopY, x);
            }

            Tuplet tuplet = entry.Duration.EffectiveTuplet;
            bool sameRun = !tuplet.IsNone && tuplet == runTuplet && staff == runStaff;

            if (!sameRun)
            {
                FlushTuplet(context, resources, runTuplet, runStartX, runEndX, runStaffTop);
                runTuplet = tuplet;
                runStaff = staff;
                runStartX = x;
                runStaffTop = staffTopY;
            }

            runEndX = x;
        }

        FlushTuplet(context, resources, runTuplet, runStartX, runEndX, runStaffTop);
        FlushChordStem(context, resources);

        // Beams do not cross barlines, so a group still open at the end of a measure is one the model
        // left dangling. Flushing here also keeps the buffer from carrying state into the next
        // measure, where the entry order restarts at staff 1 voice 1 and the X positions jump back.
        FlushBeamGroup(context, resources);
    }

    // --- the stem of an unbeamed chord --------------------------------------------------------------

    /// <summary>
    /// The unbeamed note or chord whose stem has not been drawn yet, and its vertical extent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A chord has <em>one</em> stem, and it spans the chord. That cannot be drawn when the timed
    /// head is reached, because <see cref="NotationEntry.IsChordMember"/> entries arrive afterwards
    /// and every one of them can widen the chord - the same reason a beam group cannot be flushed on
    /// seeing its <see cref="BeamState.End"/>.
    /// </para>
    /// <para>
    /// Drawing a stem per member instead is not merely untidy. Direction is decided per notehead, so
    /// a chord straddling the middle line gets stems pointing both ways out of one column; and a
    /// flagged chord gets a flag per member. Both were visible on screen before this existed. The
    /// beamed case already had its own path through <see cref="AppendToBeamGroup"/>; this is the
    /// unbeamed half of the same rule.
    /// </para>
    /// </remarks>
    private NotationEntry? _chordStemEntry;
    private int _chordStemLow;
    private int _chordStemHigh;
    private Clef _chordStemClef = Clef.Treble;
    private double _chordStemStaffTopY;
    private double _chordStemX;

    private void BeginChordStem(
        NotationEntry entry, int diatonicIndex, Clef clef, double staffTopY, double x)
    {
        _chordStemEntry = entry;
        _chordStemLow = diatonicIndex;
        _chordStemHigh = diatonicIndex;
        _chordStemClef = clef;
        _chordStemStaffTopY = staffTopY;
        _chordStemX = x;
    }

    private void ExtendChordStem(int diatonicIndex)
    {
        if (_chordStemEntry is null)
        {
            return;
        }

        _chordStemLow = Math.Min(_chordStemLow, diatonicIndex);
        _chordStemHigh = Math.Max(_chordStemHigh, diatonicIndex);
    }

    private void FlushChordStem(DrawingContext context, StaffResources resources)
    {
        if (_chordStemEntry is not { } entry)
        {
            return;
        }

        _chordStemEntry = null;

        DrawStemAndFlags(
            context, resources, entry, _chordStemLow, _chordStemHigh,
            _chordStemClef, _chordStemStaffTopY, _chordStemX);
    }

    private void DrawNote(
        DrawingContext context,
        StaffResources resources,
        NotationEntry entry,
        SpelledNote note,
        Clef clef,
        int partIndex,
        int staff,
        double staffTopY,
        double x)
    {
        StaffMetrics metrics = resources.Metrics;
        int index = note.DiatonicIndex;
        double y = StaffGeometry.YForDiatonicIndex(index, clef, staffTopY, metrics);
        double s = metrics.StaffSpace;

        DrawLedgerLines(context, resources, index, clef, staffTopY, x);

        // Accidentals are per staff: the tracker for staff 2 must not silence a sign on staff 1,
        // which can share a diatonic position around middle C on a grand staff.
        MeasureAccidentals state = _accidentals[Math.Clamp(staff, 1, _accidentals.Length) - 1];
        if (state.NeedsAccidental(note))
        {
            DrawAccidental(context, resources, note, x, y);
        }

        Geometry head = resources.Notehead;
        using (context.PushTransform(Matrix.CreateTranslation(x, y)))
        {
            if (entry.Duration.Value.IsHollow())
            {
                context.DrawGeometry(null, resources.NoteheadOutline, head);
            }
            else
            {
                context.DrawGeometry(resources.Palette.Ink, null, head);
            }
        }

        // No stem here, ever. A stem belongs to a whole column - a beamed group draws its own in
        // FlushBeamGroup, and an unbeamed note or chord in FlushChordStem - and neither length nor
        // direction is known until every notehead of that column has been read. Drawing one per
        // notehead is what gave a chord a stem per member, pointing both ways out of one column.
        DrawDots(context, resources, entry.Duration.Dots, index, clef, x, y);
        DrawResidual(context, resources, note, x, y);
        TrackTie(context, resources, entry, clef, partIndex, staff, index, x, y);
    }

    private static void DrawLedgerLines(
        DrawingContext context, StaffResources resources, int index, Clef clef, double staffTopY, double x)
    {
        double half = resources.Metrics.StaffSpace * 0.95;

        int above = StaffGeometry.LedgerLinesAbove(index, clef);
        for (int n = 1; n <= above; n++)
        {
            double y = StaffGeometry.YForDiatonicIndex(
                StaffGeometry.LedgerIndexAbove(clef, n), clef, staffTopY, resources.Metrics);
            context.DrawLine(resources.Ledger, new Point(x - half, y), new Point(x + half, y));
        }

        int below = StaffGeometry.LedgerLinesBelow(index, clef);
        for (int n = 1; n <= below; n++)
        {
            double y = StaffGeometry.YForDiatonicIndex(
                StaffGeometry.LedgerIndexBelow(clef, n), clef, staffTopY, resources.Metrics);
            context.DrawLine(resources.Ledger, new Point(x - half, y), new Point(x + half, y));
        }
    }

    /// <summary>
    /// The stem, and a flag per beam level, for a note that is <em>not</em> beamed.
    /// </summary>
    /// <remarks>
    /// A flag and a beam are alternatives, never both: <see cref="NotationEntry.Beams"/> is empty
    /// exactly when the note stands alone, and a note that carries beams is drawn by
    /// <see cref="FlushBeamGroup"/> instead. The grouping itself is <see cref="NotationBuilder"/>'s
    /// decision, not this renderer's - a beam is a statement about where the beat falls, so two
    /// places deciding it would eventually disagree with the exported file.
    /// </remarks>
    private static void DrawStemAndFlags(
        DrawingContext context,
        StaffResources resources,
        NotationEntry entry,
        int lowIndex,
        int highIndex,
        Clef clef,
        double staffTopY,
        double x)
    {
        // Guarded here rather than at the call sites, so no future caller can put a stem on a whole
        // note - which reads as a half note and is the classic version of this mistake.
        if (!StaffGeometry.HasStem(entry.Duration.Value))
        {
            return;
        }

        StaffMetrics metrics = resources.Metrics;

        // Given as a range so one chord gets one stem spanning it. The two are equal for a single
        // note, and the rule then reduces exactly to StemDirectionFor.
        StemDirection direction = StaffGeometry.GroupStemDirection([lowIndex], [highIndex], clef);

        int endIndex = StaffGeometry.BeamSideIndex(lowIndex, highIndex, direction);
        int footIndex = StaffGeometry.StemFootIndex(lowIndex, highIndex, direction);

        double endY = StaffGeometry.StemEndY(endIndex, clef, staffTopY, metrics, direction);
        double footY = StaffGeometry.YForDiatonicIndex(footIndex, clef, staffTopY, metrics);

        // The stem meets the notehead at its side, not its centre: up on the right, down on the left.
        double stemX = StaffGeometry.StemX(x, direction, metrics);
        double startY = StemFootY(footY, direction, metrics);

        context.DrawLine(resources.Stem, new Point(stemX, startY), new Point(stemX, endY));

        int flags = entry.Duration.Value.FlagCount();
        if (flags <= 0)
        {
            return;
        }

        Geometry flag = direction == StemDirection.Up ? resources.FlagUp : resources.FlagDown;
        double step = metrics.StaffSpace * 0.82 * (direction == StemDirection.Up ? 1 : -1);

        for (int i = 0; i < flags; i++)
        {
            using (context.PushTransform(Matrix.CreateTranslation(stemX, endY + (i * step))))
            {
                context.DrawGeometry(resources.Palette.Ink, null, flag);
            }
        }
    }

    /// <summary>
    /// Where a stem meets its notehead: just inside the head's edge, so the two overlap rather than
    /// leaving a hairline of background between them at fractional zooms.
    /// </summary>
    private static double StemFootY(double noteheadY, StemDirection direction, in StaffMetrics metrics) =>
        noteheadY + (direction == StemDirection.Up ? -metrics.StaffSpace * 0.12 : metrics.StaffSpace * 0.12);

    // --- beams ---------------------------------------------------------------------------------------

    /// <summary>
    /// The beam group being accumulated, as parallel scratch arrays.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A beam cannot be drawn note by note - its slope, its height and every stem length in it depend
    /// on the whole group - so the group has to be collected before any of it is drawn. Collecting it
    /// into a <c>List</c> would allocate per group per frame, which for a dense score is thousands of
    /// allocations a redraw; these are fields, grown by doubling on the rare group longer than the
    /// current capacity and reused for every group thereafter.
    /// </para>
    /// <para>
    /// Instance fields rather than statics, unlike the brace scratch, because a beam group is
    /// per-control state that survives across several draw calls within one <see cref="Render"/>.
    /// </para>
    /// </remarks>
    private NotationEntry[] _beamEntries = new NotationEntry[16];
    private double[] _beamStemXs = new double[16];

    /// <summary>
    /// Each column's vertical extent: one position for a single note, the chord's outermost two for a
    /// chord. Two arrays rather than one because a chord's stem has to span it, its direction has to
    /// be decided from it, and the beam's contour has to follow whichever end faces the beam.
    /// </summary>
    private int[] _beamLowIndices = new int[16];
    private int[] _beamHighIndices = new int[16];

    /// <summary>
    /// Scratch for the beam-facing position of each column, filled at flush time once the group's
    /// direction is known. A field so the flush can hand <see cref="StaffGeometry.ComputeBeamLine"/> a
    /// span without allocating one per group per frame.
    /// </summary>
    private int[] _beamSideIndices = new int[16];

    private int _beamCount;

    /// <summary>
    /// Whether the open group has seen its level-1 <see cref="BeamState.End"/>.
    /// </summary>
    /// <remarks>
    /// The group cannot be drawn the instant its last note arrives, because that note may be a chord
    /// whose remaining members have not been read yet - and they still have to widen its column. So
    /// the end is recorded and the drawing deferred to the next entry that is not a chord member.
    /// </remarks>
    private bool _beamClosed;

    /// <summary>
    /// Whether the chord currently being read is beamed - i.e. whether its timed head carried beams.
    /// </summary>
    /// <remarks>
    /// The one piece of state that lets a chord member behave correctly without a model change.
    /// <see cref="NotationEntry.Beams"/> is empty on a member by contract, so a member is otherwise
    /// indistinguishable from a note that is genuinely unbeamed.
    /// </remarks>
    private bool _chordBeamed;

    /// <summary>The staff the open group belongs to; a change of staff or voice ends it.</summary>
    private Clef _beamClef = Clef.Treble;
    private double _beamStaffTopY;
    private int _beamStaff;
    private int _beamVoice;

    /// <summary>Widens the open group's last column to take in one more note of the same chord.</summary>
    private void ExtendBeamChord(int diatonicIndex)
    {
        if (_beamCount == 0)
        {
            return;
        }

        int last = _beamCount - 1;
        _beamLowIndices[last] = Math.Min(_beamLowIndices[last], diatonicIndex);
        _beamHighIndices[last] = Math.Max(_beamHighIndices[last], diatonicIndex);
    }

    /// <summary>
    /// Adds one beamed note to the open group, starting a new one where the model says to.
    /// </summary>
    /// <remarks>
    /// A group ends on <see cref="BeamState.End"/> at level 1, but it also has to end on anything the
    /// model cannot have meant to continue through - a fresh <see cref="BeamState.Begin"/>, or a jump
    /// to another staff or voice, which happens at every group boundary inside a measure because the
    /// entry list is ordered by staff then voice rather than by time.
    /// </remarks>
    private void AppendToBeamGroup(
        DrawingContext context,
        StaffResources resources,
        NotationEntry entry,
        int diatonicIndex,
        Clef clef,
        int staff,
        double staffTopY,
        double x)
    {
        BeamState level1 = entry.Beams[0];

        if (_beamClosed
            || level1 == BeamState.Begin
            || _beamCount == 0
            || staff != _beamStaff
            || entry.Voice != _beamVoice)
        {
            FlushBeamGroup(context, resources);
            _beamClef = clef;
            _beamStaff = staff;
            _beamVoice = entry.Voice;
            _beamStaffTopY = staffTopY;
        }

        if (_beamCount == _beamEntries.Length)
        {
            int grown = _beamEntries.Length * 2;
            Array.Resize(ref _beamEntries, grown);
            Array.Resize(ref _beamStemXs, grown);
            Array.Resize(ref _beamLowIndices, grown);
            Array.Resize(ref _beamHighIndices, grown);
            Array.Resize(ref _beamSideIndices, grown);
        }

        _beamEntries[_beamCount] = entry;

        // The notehead X for now: the stem side is not known until the group's direction is, and that
        // is a property of the group, not of this note. FlushBeamGroup converts them in place.
        _beamStemXs[_beamCount] = x;
        _beamLowIndices[_beamCount] = diatonicIndex;
        _beamHighIndices[_beamCount] = diatonicIndex;
        _beamCount++;

        // Recorded, not acted on: this note may be a chord whose members are still to come.
        _beamClosed = level1 == BeamState.End;
    }

    /// <summary>
    /// Draws the accumulated group - one uniform stem direction, one beam line, a segment per level
    /// and a stub per hook - and empties the buffer.
    /// </summary>
    private void FlushBeamGroup(DrawingContext context, StaffResources resources)
    {
        int count = _beamCount;
        _beamCount = 0;
        _beamClosed = false;

        if (count == 0)
        {
            return;
        }

        StaffMetrics metrics = resources.Metrics;

        if (count == 1)
        {
            // A lone Begin or End: the model said "beamed" and then gave it nobody to beam to. A beam
            // to nowhere is worse than a flag, so it falls back to the flag it would have had - and if
            // that lone entry is a chord, to the one stem spanning it.
            NotationEntry only = _beamEntries[0];
            DrawStemAndFlags(
                context, resources, only, _beamLowIndices[0], _beamHighIndices[0],
                _beamClef, _beamStaffTopY, _beamStemXs[0]);
            _beamEntries[0] = null!;
            return;
        }

        ReadOnlySpan<int> lows = _beamLowIndices.AsSpan(0, count);
        ReadOnlySpan<int> highs = _beamHighIndices.AsSpan(0, count);
        StemDirection direction = StaffGeometry.GroupStemDirection(lows, highs, _beamClef);

        for (int i = 0; i < count; i++)
        {
            _beamStemXs[i] = StaffGeometry.StemX(_beamStemXs[i], direction, metrics);
            _beamSideIndices[i] = StaffGeometry.BeamSideIndex(lows[i], highs[i], direction);
        }

        ReadOnlySpan<double> stemXs = _beamStemXs.AsSpan(0, count);

        // The beam-facing head of each column, so a chord's contour follows the note nearest the beam
        // and the line clears that note rather than the one at the far end of the stem.
        BeamLine line = StaffGeometry.ComputeBeamLine(
            stemXs, _beamSideIndices.AsSpan(0, count), _beamClef, _beamStaffTopY, metrics, direction);

        int maxLevel = 1;
        for (int i = 0; i < count; i++)
        {
            int levels = Math.Max(1, _beamEntries[i].Beams.Count);
            maxLevel = Math.Max(maxLevel, levels);

            int footIndex = StaffGeometry.StemFootIndex(lows[i], highs[i], direction);
            double footY = StaffGeometry.YForDiatonicIndex(footIndex, _beamClef, _beamStaffTopY, metrics);
            double endY = StaffGeometry.BeamStemEndY(line, stemXs[i], levels, metrics);

            context.DrawLine(
                resources.Stem,
                new Point(stemXs[i], StemFootY(footY, direction, metrics)),
                new Point(stemXs[i], endY));
        }

        for (int level = 1; level <= maxLevel; level++)
        {
            DrawBeamLevel(context, resources, line, count, level);
        }

        Array.Clear(_beamEntries, 0, count);
    }

    /// <summary>One beam level across the group: its full segments, then its hooks.</summary>
    private void DrawBeamLevel(
        DrawingContext context, StaffResources resources, in BeamLine line, int count, int level)
    {
        StaffMetrics metrics = resources.Metrics;

        for (int i = 0; i < count; i++)
        {
            BeamState state = StateAt(_beamEntries[i], level);
            if (state == BeamState.None)
            {
                continue;
            }

            double stemX = _beamStemXs[i];

            if (state is BeamState.ForwardHook or BeamState.BackwardHook)
            {
                bool forward = state == BeamState.ForwardHook;
                double? neighbour = forward
                    ? (i + 1 < count ? _beamStemXs[i + 1] : null)
                    : (i > 0 ? _beamStemXs[i - 1] : null);

                DrawBeamSegment(
                    context, resources, line, level,
                    stemX, StaffGeometry.BeamHookEndX(stemX, neighbour, forward, metrics));
                continue;
            }

            if (i + 1 < count && StaffGeometry.BeamsJoin(state, StateAt(_beamEntries[i + 1], level)))
            {
                DrawBeamSegment(context, resources, line, level, stemX, _beamStemXs[i + 1]);
            }
        }
    }

    /// <summary>This entry's role at <paramref name="level"/>, or <c>None</c> if it has no such level.</summary>
    private static BeamState StateAt(NotationEntry entry, int level) =>
        level <= entry.Beams.Count ? entry.Beams[level - 1] : BeamState.None;

    private static void DrawBeamSegment(
        DrawingContext context, StaffResources resources, in BeamLine line, int level, double x0, double x1)
    {
        StaffMetrics metrics = resources.Metrics;
        context.DrawLine(
            resources.Beam,
            new Point(x0, StaffGeometry.BeamCentreY(line, x0, level, metrics)),
            new Point(x1, StaffGeometry.BeamCentreY(line, x1, level, metrics)));
    }

    private static void DrawDots(
        DrawingContext context, StaffResources resources, int dots, int index, Clef clef, double x, double y)
    {
        if (dots <= 0)
        {
            return;
        }

        double s = resources.Metrics.StaffSpace;

        // A dot never sits on a staff line; a note on a line has its dots in the space above.
        double dotY = StaffGeometry.IsOnLine(index, clef) ? y - (s * 0.5) : y;

        for (int i = 0; i < dots; i++)
        {
            context.DrawEllipse(
                resources.Palette.Ink, null,
                new Point(x + (s * (1.05 + (i * 0.45))), dotY), s * 0.13, s * 0.13);
        }
    }

    private void DrawResidual(
        DrawingContext context, StaffResources resources, SpelledNote note, double x, double y)
    {
        if (!StaffGeometry.ShouldShowResidual(note.ResidualCents))
        {
            return;
        }

        // The AEU comma case: MusicXML has no way to say "and 15 cents besides", so the screen is
        // the only place the reader is told the written note approximates what will sound.
        int cents = (int)Math.Round(note.ResidualCents, MidpointRounding.AwayFromZero);
        string label = cents > 0
            ? string.Create(CultureInfo.InvariantCulture, $"+{cents}¢")
            : string.Create(CultureInfo.InvariantCulture, $"{cents}¢");

        FormattedText text = Text(TextRole.Annotation, label, resources);
        context.DrawText(
            text, new Point(x + (resources.Metrics.StaffSpace * 0.8), y - (resources.Metrics.StaffSpace * 1.5)));
    }

    /// <summary>
    /// Pairs a tie's ends and draws the arc between them.
    /// </summary>
    /// <remarks>
    /// Tied notes are by definition the same written pitch, so both ends share a Y and the arc is a
    /// symmetric bulge. That is what lets it be drawn as a sampled polyline with cached pens instead
    /// of a per-frame <see cref="StreamGeometry"/> - the render path allocates nothing.
    /// </remarks>
    private void TrackTie(
        DrawingContext context,
        StaffResources resources,
        NotationEntry entry,
        Clef clef,
        int partIndex,
        int staff,
        int index,
        double x,
        double y)
    {
        TieKey key = new(partIndex, staff, entry.Voice, index);
        StemDirection stem = StaffGeometry.StemDirectionFor(index, clef);

        if (entry.Tie is TieState.Stop or TieState.Continue)
        {
            if (_tieStarts.Remove(key, out TiePoint start))
            {
                // A tie bows away from the stem, so it takes the same side rule the stem does.
                DrawTieArc(context, resources, new Point(start.X, start.Y), new Point(x, y), start.Stem);
            }
            else if (_tieIncoming.Remove(key))
            {
                // The other end is on the previous system, so this end is written as a stub running
                // back off the left of the note - the standard way a score shows a tie that wrapped.
                DrawTieStub(context, resources, x, y, stem, forward: false);
            }
        }

        if (entry.Tie is TieState.Start or TieState.Continue)
        {
            _tieStarts[key] = new TiePoint(x, y, stem);
        }
    }

    /// <summary>Arms the incoming half of every tie that ran off the end of the previous system.</summary>
    private void BeginSystemTies()
    {
        _tieStarts.Clear();
        _tieIncoming.Clear();

        foreach (TieKey key in _tieCarried)
        {
            _tieIncoming.Add(key);
        }

        _tieCarried.Clear();
    }

    /// <summary>
    /// Writes a stub for every tie still open at the end of a system and remembers it for the next.
    /// </summary>
    /// <remarks>
    /// Beams never cross a barline and systems only ever break at one, so a tie is the single thing
    /// that can genuinely span a system break. Left unhandled it would either vanish or - worse - be
    /// drawn as an arc from one system to the next, straight across the page.
    /// </remarks>
    private void EndSystemTies(DrawingContext context, StaffResources resources)
    {
        foreach ((TieKey key, TiePoint point) in _tieStarts)
        {
            DrawTieStub(context, resources, point.X, point.Y, point.Stem, forward: true);
            _tieCarried.Add(key);
        }

        _tieStarts.Clear();
    }

    /// <summary>Half a tie: the short curve that says the other end is on another line.</summary>
    private static void DrawTieStub(
        DrawingContext context, StaffResources resources, double x, double y, StemDirection stem, bool forward)
    {
        double s = resources.Metrics.StaffSpace;
        double sign = forward ? 1 : -1;
        double bulge = (stem == StemDirection.Up ? 1 : -1) * s * 0.7;

        Point from = new(x + (sign * s * 0.55), y + (stem == StemDirection.Up ? s * 0.55 : -s * 0.55));
        Point to = new(from.X + (sign * s * 1.6), from.Y + (bulge * 0.9));
        Point control = new(from.X + (sign * s * 0.9), from.Y + bulge);

        const int Segments = 6;
        Point previous = from;
        for (int i = 1; i <= Segments; i++)
        {
            double t = (double)i / Segments;
            double inverse = 1 - t;
            Point next = new(
                (inverse * inverse * from.X) + (2 * inverse * t * control.X) + (t * t * to.X),
                (inverse * inverse * from.Y) + (2 * inverse * t * control.Y) + (t * t * to.Y));

            context.DrawLine(resources.Tie, previous, next);
            previous = next;
        }
    }

    private static void DrawTieArc(
        DrawingContext context, StaffResources resources, Point from, Point to, StemDirection stem)
    {
        double s = resources.Metrics.StaffSpace;
        double width = to.X - from.X;
        if (width <= s * 0.2)
        {
            return;
        }

        // Bow away from the stem so the arc and the stem do not overlap.
        double bulge = (stem == StemDirection.Up ? 1 : -1) * s * 0.85;
        double y = from.Y + (stem == StemDirection.Up ? s * 0.55 : -s * 0.55);

        Point control = new(from.X + (width / 2), y + bulge);
        Point start = new(from.X + (s * 0.55), y);
        Point end = new(to.X - (s * 0.55), y);

        if (end.X <= start.X)
        {
            return;
        }

        const int Segments = 10;
        Point previous = start;
        for (int i = 1; i <= Segments; i++)
        {
            double t = (double)i / Segments;
            double inverse = 1 - t;
            Point next = new(
                (inverse * inverse * start.X) + (2 * inverse * t * control.X) + (t * t * end.X),
                (inverse * inverse * start.Y) + (2 * inverse * t * control.Y) + (t * t * end.Y));

            context.DrawLine(resources.Tie, previous, next);
            previous = next;
        }
    }

    private void FlushTuplet(
        DrawingContext context,
        StaffResources resources,
        Tuplet tuplet,
        double startX,
        double endX,
        double staffTopY)
    {
        if (tuplet.IsNone || endX <= startX)
        {
            return;
        }

        StaffMetrics metrics = resources.Metrics;
        double s = metrics.StaffSpace;
        double y = staffTopY - (s * 1.8);
        double left = startX - (s * 0.5);
        double right = endX + (s * 0.5);

        FormattedText label = Text(
            TextRole.Tuplet, tuplet.ActualNotes.ToString(CultureInfo.InvariantCulture), resources);

        double centre = (left + right) / 2;
        double halfGap = (label.Width / 2) + (s * 0.35);

        context.DrawLine(resources.Bracket, new Point(left, y), new Point(centre - halfGap, y));
        context.DrawLine(resources.Bracket, new Point(centre + halfGap, y), new Point(right, y));
        context.DrawLine(resources.Bracket, new Point(left, y), new Point(left, y + (s * 0.5)));
        context.DrawLine(resources.Bracket, new Point(right, y), new Point(right, y + (s * 0.5)));
        context.DrawText(label, new Point(centre - (label.Width / 2), y - (label.Height / 2)));
    }

    // --- rests -------------------------------------------------------------------------------------

    private static void DrawRest(
        DrawingContext context, StaffResources resources, NotatedDuration duration, double staffTopY, double x)
    {
        StaffMetrics metrics = resources.Metrics;
        double s = metrics.StaffSpace;
        IBrush ink = resources.Palette.Ink;

        double line1 = StaffGeometry.YForStaffLine(1, staffTopY, metrics);
        double middle = StaffGeometry.YForStaffLine(2, staffTopY, metrics);

        switch (duration.Value)
        {
            case NoteValue.Breve:
                // A full-space block between the second and third lines, with the side strokes that
                // distinguish it from a whole rest.
                context.FillRectangle(ink, new Rect(x - (s * 0.34), line1, s * 0.68, s));
                context.DrawLine(resources.RestStroke, new Point(x - (s * 0.34), line1), new Point(x - (s * 0.34), middle));
                context.DrawLine(resources.RestStroke, new Point(x + (s * 0.34), line1), new Point(x + (s * 0.34), middle));
                break;

            // The two are the same block; what tells them apart is that a whole rest hangs below its
            // line and a half rest sits on top of one. Getting that backwards is the classic error.
            case NoteValue.Whole:
                context.FillRectangle(ink, new Rect(x - (s * 0.55), line1, s * 1.1, s * 0.5));
                break;

            case NoteValue.Half:
                context.FillRectangle(ink, new Rect(x - (s * 0.55), middle - (s * 0.5), s * 1.1, s * 0.5));
                break;

            case NoteValue.Quarter:
                using (context.PushTransform(Matrix.CreateTranslation(x, middle)))
                {
                    context.DrawGeometry(resources.Palette.Ink, null, resources.QuarterRest);
                }

                break;

            default:
                DrawFlaggedRest(context, resources, duration.Value.FlagCount(), x, middle);
                break;
        }

        DrawRestDots(context, resources, duration.Dots, x, middle);
    }

    /// <summary>An eighth rest and shorter: a slanted stem with one blob-and-hook per beam level.</summary>
    /// <remarks>
    /// The proportions are measured off <c>Music-eighthrest.svg</c> - a staff space of 24.02 units,
    /// its middle line at y = 51.19, the glyph centred on x = 105.2 - which is why they are these
    /// numbers and not round ones. Until 2026-08-28 the blob was drawn well under its proper size
    /// against a hook that was a two-point stroke, so the glyph read as a bare "7" with no blob at
    /// all: the single most obviously wrong symbol on the page.
    /// </remarks>
    private static void DrawFlaggedRest(
        DrawingContext context, StaffResources resources, int hooks, double x, double middleY)
    {
        double s = resources.Metrics.StaffSpace;
        int count = Math.Max(1, hooks);

        // One beam level further down. The blobs step along the stem's own slope rather than
        // dropping vertically, so a sixteenth rest's pair sits on the stem instead of beside it.
        const double StepX = -0.204;
        const double StepY = 0.72;

        double extra = count - 1;

        context.DrawLine(
            resources.RestStroke,
            new Point(x + (s * 0.244), middleY - (s * 0.736)),
            new Point(x + (s * (-0.243 + (StepX * extra))), middleY + (s * (0.987 + (StepY * extra)))));

        for (int i = 0; i < count; i++)
        {
            double dx = x + (s * StepX * i);
            double dy = middleY + (s * StepY * i);

            context.DrawEllipse(
                resources.Palette.Ink, null,
                new Point(dx - (s * 0.475), dy - (s * 0.531)), s * 0.291, s * 0.291);

            // Stroked, not filled: the source draws this as a stroke that runs out to the stem and
            // back, so the two passes give the hook its body. Filling the sliver between them
            // instead leaves a wisp barely a pixel wide at ordinary zoom.
            using (context.PushTransform(Matrix.CreateTranslation(dx, dy)))
            {
                context.DrawGeometry(null, resources.RestStroke, resources.RestHook);
            }
        }
    }

    private static void DrawRestDots(
        DrawingContext context, StaffResources resources, int dots, double x, double middleY)
    {
        double s = resources.Metrics.StaffSpace;
        for (int i = 0; i < dots; i++)
        {
            context.DrawEllipse(
                resources.Palette.Ink, null,
                new Point(x + (s * (0.85 + (i * 0.45))), middleY - (s * 0.5)), s * 0.13, s * 0.13);
        }
    }

    // --- accidentals -------------------------------------------------------------------------------

    /// <summary>Width of a sharp's crossbars, in staff spaces; a sesqui-sharp needs a third upright.</summary>
    private const double SharpWidthSpaces = 0.78;
    private const double SesquiSharpWidthSpaces = 1.0;

    /// <summary>How far a natural's right upright stands from the anchor <see cref="DrawNatural"/> takes.</summary>
    private const double NaturalWidthSpaces = 0.62;

    /// <summary>How far a flat's bowl reaches from its own upright.</summary>
    private const double FlatBowlSpaces = 0.78;

    /// <summary>Half the width of the double-sharp cross.</summary>
    private const double DoubleSharpHalfSpaces = 0.42;

    /// <summary>Centre-to-centre spacing of the two signs in a double or sesqui accidental.</summary>
    private const double PairedAccidentalSpaces = 0.66;

    /// <summary>
    /// Draws the accidental for <paramref name="note"/> before the notehead centred at
    /// <paramref name="x"/>, vertically centred on <paramref name="y"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every branch places its glyph from <see cref="StaffGeometry.AccidentalRightEdge"/> - the one
    /// X the sign's right-hand extent must not pass - rather than from an offset chosen per shape.
    /// A single shared anchor cannot work, because these signs differ by more than a factor of two
    /// in width: measured against the notehead, the old fixed anchor left a double flat overlapping
    /// its own head by two thirds of a space, and the natural, sharp and half-flat by a third.
    /// </para>
    /// <para>
    /// Dispatches on <see cref="SpelledNote.Alter"/> rather than on
    /// <see cref="SpelledNote.AccidentalSymbol"/>, because the alteration is the thing that has a
    /// shape - the symbol string is a label. Every value the model can produce is covered:
    /// <c>DegreeSpelling</c> quantises to halves of a semitone within +/-2, which is the ten cases
    /// below. Anything outside that falls back to drawing the symbol as text, which is the only
    /// place a font is involved at all.
    /// </remarks>
    private void DrawAccidental(
        DrawingContext context, StaffResources resources, SpelledNote note, double x, double y)
    {
        double s = resources.Metrics.StaffSpace;
        double right = StaffGeometry.AccidentalRightEdge(x, resources.Metrics);

        switch (note.Alter)
        {
            case 0:
                DrawNatural(context, resources, right - (s * NaturalWidthSpaces), y, s);
                break;
            case 1:
                DrawSharp(context, resources, right - (s * SharpWidthSpaces), y, s, verticals: 2);
                break;
            case 0.5:
                DrawSharp(context, resources, right - (s * SharpWidthSpaces), y, s, verticals: 1);
                break;
            case 1.5:
                DrawSharp(context, resources, right - (s * SesquiSharpWidthSpaces), y, s, verticals: 3);
                break;
            case 2:
                using (context.PushTransform(
                    Matrix.CreateTranslation(right - (s * DoubleSharpHalfSpaces), y)))
                {
                    context.DrawGeometry(resources.Palette.Ink, null, resources.DoubleSharp);
                }

                break;
            case -1:
                DrawFlat(context, resources, right - (s * FlatBowlSpaces), y, mirrored: false);
                break;
            case -0.5:
                // Mirrored, so its bowl reaches left of the anchor and the anchor is the right edge.
                DrawFlat(context, resources, right, y, mirrored: true);
                break;
            case -1.5:
                DrawFlat(
                    context, resources,
                    right - (s * (FlatBowlSpaces + PairedAccidentalSpaces)), y, mirrored: false);
                DrawFlat(context, resources, right, y, mirrored: true);
                break;
            case -2:
                DrawFlat(
                    context, resources,
                    right - (s * (FlatBowlSpaces + PairedAccidentalSpaces)), y, mirrored: false);
                DrawFlat(context, resources, right - (s * FlatBowlSpaces), y, mirrored: false);
                break;
            default:
                FormattedText text = Text(TextRole.Annotation, note.AccidentalSymbol, resources);
                context.DrawText(text, new Point(right - text.Width, y - (s * 0.8)));
                break;
        }
    }

    /// <summary>
    /// A sharp: two rising crossbars with one, two or three uprights through them. One upright is
    /// the half-sharp and three is the sesqui-sharp; the crossbars are the same in all three, which
    /// is exactly how the quarter-tone accidentals are defined.
    /// </summary>
    private static void DrawSharp(
        DrawingContext context, StaffResources resources, double x, double y, double s, int verticals)
    {
        double width = s * (verticals == 3 ? 1.0 : 0.78);
        double left = x;
        double right = x + width;

        context.DrawLine(resources.AccidentalThick, new Point(left, y - (s * 0.14)), new Point(right, y - (s * 0.34)));
        context.DrawLine(resources.AccidentalThick, new Point(left, y + (s * 0.44)), new Point(right, y + (s * 0.24)));

        for (int i = 0; i < verticals; i++)
        {
            double t = verticals == 1 ? 0.5 : (double)(i + 1) / (verticals + 1);
            double vx = left + (width * t);
            context.DrawLine(
                resources.AccidentalThin, new Point(vx, y - (s * 0.92)), new Point(vx, y + (s * 0.92)));
        }
    }

    /// <summary>A natural: two offset uprights joined by two crossbars.</summary>
    private static void DrawNatural(
        DrawingContext context, StaffResources resources, double x, double y, double s)
    {
        double left = x + (s * 0.12);
        double right = x + (s * 0.62);

        context.DrawLine(resources.AccidentalThin, new Point(left, y - (s * 0.95)), new Point(left, y + (s * 0.48)));
        context.DrawLine(resources.AccidentalThin, new Point(right, y - (s * 0.48)), new Point(right, y + (s * 0.95)));
        context.DrawLine(resources.AccidentalThick, new Point(left, y - (s * 0.24)), new Point(right, y - (s * 0.44)));
        context.DrawLine(resources.AccidentalThick, new Point(left, y + (s * 0.44)), new Point(right, y + (s * 0.24)));
    }

    /// <summary>A flat, or - mirrored - the reversed flat that is the half-flat.</summary>
    private static void DrawFlat(
        DrawingContext context, StaffResources resources, double x, double y, bool mirrored)
    {
        Geometry loop = mirrored ? resources.FlatLoopMirrored : resources.FlatLoop;
        using (context.PushTransform(Matrix.CreateTranslation(x, y)))
        {
            context.DrawGeometry(null, resources.AccidentalThin, loop);
        }
    }

    // --- the head of a system -------------------------------------------------------------------

    /// <summary>
    /// The time signature in force at <paramref name="measureIndex"/>.
    /// </summary>
    /// <remarks>
    /// Read from the measure itself rather than tracked forward from the start: every measure
    /// carries its own beats and beat unit, and <c>TimeSignatureChanged</c> only says whether it
    /// would be <em>printed</em> there.
    /// </remarks>
    private static (int Beats, int Unit) SignatureAt(NotationPart part, int measureIndex)
    {
        if (part.Measures.Count == 0)
        {
            return (MeasureGrid.DefaultNumerator, MeasureGrid.DefaultDenominator);
        }

        NotationMeasure measure = part.Measures[Math.Clamp(measureIndex, 0, part.Measures.Count - 1)];
        return (measure.BeatsPerMeasure, measure.BeatUnit);
    }

    private static void DrawClef(
        DrawingContext context, StaffResources resources, Clef clef, double x, double staffTopY)
    {
        StaffMetrics metrics = resources.Metrics;
        double s = metrics.StaffSpace;
        double referenceY = StaffGeometry.YForDiatonicIndex(
            StaffGeometry.ClefReferenceIndex(clef), clef, staffTopY, metrics);

        // Filled, not stroked. These are real glyph outlines - the closed contours already carry
        // the calligraphic thick-and-thin, and stroking one would outline that shape rather than
        // ink it.
        using (context.PushTransform(Matrix.CreateTranslation(x, referenceY)))
        {
            context.DrawGeometry(
                resources.Palette.Ink, null, clef == Clef.Bass ? resources.BassClef : resources.TrebleClef);
        }

        if (clef != Clef.Bass)
        {
            return;
        }

        // The F clef is a hook plus two dots straddling the F line; without the dots it is just a
        // curve. Exactly half a space either side - the source SVG places its own a little off, and
        // this is the one part of the glyph that is trivially placeable correctly.
        double dotX = x + resources.BassClefDotX;
        context.DrawEllipse(
            resources.Palette.Ink, null, new Point(dotX, referenceY - (s * 0.5)), s * 0.15, s * 0.15);
        context.DrawEllipse(
            resources.Palette.Ink, null, new Point(dotX, referenceY + (s * 0.5)), s * 0.15, s * 0.15);
    }

    private void DrawTimeSignature(
        DrawingContext context, StaffResources resources, int beats, int unit, double x, double staffTopY)
    {
        if (beats <= 0 || unit <= 0)
        {
            return;
        }

        StaffMetrics metrics = resources.Metrics;
        FormattedText top = Text(
            TextRole.TimeSignature, beats.ToString(CultureInfo.InvariantCulture), resources);
        FormattedText bottom = Text(
            TextRole.TimeSignature, unit.ToString(CultureInfo.InvariantCulture), resources);

        // Numerator centred in the upper half of the stave, denominator in the lower half - the
        // numerals straddle the middle line rather than sitting on it.
        double centre = x + (Math.Max(top.Width, bottom.Width) / 2);
        double upperY = StaffGeometry.YForStaffLine(1, staffTopY, metrics);
        double lowerY = StaffGeometry.YForStaffLine(3, staffTopY, metrics);

        context.DrawText(top, new Point(centre - (top.Width / 2), upperY - (top.Height / 2)));
        context.DrawText(bottom, new Point(centre - (bottom.Width / 2), lowerY - (bottom.Height / 2)));
    }

    /// <summary>
    /// Scratch storage for <see cref="DrawBrace"/>'s two control-point runs, reused across calls
    /// rather than allocated per grand-staff part per frame. Safe because rendering happens on one
    /// owned thread at a time (the Avalonia UI thread in the app, the single render thread in
    /// <c>AvaloniaRenderFixture</c> in tests) and <see cref="DrawBezierPolyline"/> only reads a run
    /// before this method's next call overwrites it - never holds one past its own return.
    /// </summary>
    private static readonly Point[] BraceUpperScratch = new Point[4];
    private static readonly Point[] BraceLowerScratch = new Point[4];

    /// <summary>The brace that binds a grand staff, as two mirrored bows.</summary>
    private static void DrawBrace(
        DrawingContext context, StaffResources resources, double x, double top, double bottom)
    {
        double w = resources.Metrics.StaffSpace * 0.85;
        double height = bottom - top;
        double middle = top + (height / 2);

        BraceUpperScratch[0] = new Point(x + w, top);
        BraceUpperScratch[1] = new Point(x - (w * 0.2), top + (height * 0.16));
        BraceUpperScratch[2] = new Point(x + w, top + (height * 0.30));
        BraceUpperScratch[3] = new Point(x + (w * 0.15), middle);

        BraceLowerScratch[0] = new Point(x + (w * 0.15), middle);
        BraceLowerScratch[1] = new Point(x + w, bottom - (height * 0.30));
        BraceLowerScratch[2] = new Point(x - (w * 0.2), bottom - (height * 0.16));
        BraceLowerScratch[3] = new Point(x + w, bottom);

        DrawBezierPolyline(context, resources.Brace, BraceUpperScratch);
        DrawBezierPolyline(context, resources.Brace, BraceLowerScratch);
    }

    /// <summary>
    /// Strokes a cubic Bezier as a short polyline.
    /// </summary>
    /// <remarks>
    /// The brace's height depends on the part's staff count, so it cannot be a geometry cached with
    /// the rest of the glyphs; sampling it avoids building one per frame instead.
    /// </remarks>
    private static void DrawBezierPolyline(DrawingContext context, Pen pen, Point[] control)
    {
        const int Segments = 12;
        Point previous = control[0];

        for (int i = 1; i <= Segments; i++)
        {
            double t = (double)i / Segments;
            double u = 1 - t;
            double a = u * u * u;
            double b = 3 * u * u * t;
            double c = 3 * u * t * t;
            double d = t * t * t;

            Point next = new(
                (a * control[0].X) + (b * control[1].X) + (c * control[2].X) + (d * control[3].X),
                (a * control[0].Y) + (b * control[1].Y) + (c * control[2].Y) + (d * control[3].Y));

            context.DrawLine(pen, previous, next);
            previous = next;
        }
    }

    /// <summary>
    /// The part's name, vertically centred on its staves, in the column the indent reserved for it.
    /// </summary>
    /// <remarks>
    /// <paramref name="x"/> comes from <see cref="StaffGeometry.ComputeIndent"/>, which sized the
    /// column from this very text. The old code drew at a hard-coded x of 2 with nothing reserving
    /// room, so a name of any length ran under the brace and off the left edge.
    /// </remarks>
    private void DrawPartName(
        DrawingContext context, StaffResources resources, string name, double x, double top, double bottom)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        FormattedText text = Text(TextRole.PartName, name, resources);
        context.DrawText(text, new Point(x, ((top + bottom) / 2) - (text.Height / 2)));
    }

    // --- playhead -------------------------------------------------------------------------------------

    /// <summary>
    /// The playhead, as a short vertical inside the one system that holds the current tick.
    /// </summary>
    /// <remarks>
    /// Bounded to its own system, not run down the whole page. On a wrapped page a full-height line
    /// would put a red stripe through four unrelated bars of music and say nothing about which of them
    /// is sounding.
    /// </remarks>
    private void DrawPlayhead(
        DrawingContext context,
        StaffResources resources,
        StaffPageLayout layout,
        int system,
        double systemTop)
    {
        long ticks = PlayheadTicks;
        if (ticks < 0 || !layout.TryLocate(ticks, out int at, out double x) || at != system)
        {
            return;
        }

        StaffMetrics metrics = resources.Metrics;
        context.DrawLine(
            resources.Playhead,
            new Point(x, systemTop - metrics.SystemHeadroom),
            new Point(x, systemTop + layout.SystemBlockHeight + metrics.SystemFootroom));
    }
}
