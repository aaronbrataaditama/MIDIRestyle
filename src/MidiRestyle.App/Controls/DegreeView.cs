using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Controls;

/// <summary>
/// One complete set of drawing resources for <see cref="DegreeView"/>, mirroring the split
/// <c>PianoRollPalette</c> uses: everything is built once, in <see cref="DegreeViewPalettes"/>, and
/// selecting a palette is just pointing at one of the two already-built instances.
/// </summary>
internal sealed class DegreeViewPalette
{
    public required IBrush Background { get; init; }

    /// <summary>Fills the middle of the wheel so the centre readout stays clear of the spokes.</summary>
    public required IBrush Hub { get; init; }

    /// <summary>The octave ring itself - one full turn is 1200 cents.</summary>
    public required Pen Ring { get; init; }

    /// <summary>The twelve equal-tempered reference marks every degree is read against.</summary>
    public required Pen TwelveTetTick { get; init; }

    /// <summary>The arc from a 12-TET tick round to where the degree actually falls.</summary>
    public required Pen Deviation { get; init; }

    /// <summary>The spoke from the hub out to a degree's marker.</summary>
    public required Pen Spoke { get; init; }

    public required IBrush DegreeMarker { get; init; }

    /// <summary>Drawn in the background colour, so overlapping markers stay countable.</summary>
    public required Pen MarkerEdge { get; init; }

    public required IBrush DegreeLabel { get; init; }

    /// <summary>The twelve semitone names ringing the wheel.</summary>
    public required IBrush TickLabel { get; init; }

    public required IBrush CentsLabel { get; init; }

    /// <summary>The cents figure of a degree that is genuinely off the 12-TET grid.</summary>
    public required IBrush CentsAccent { get; init; }

    /// <summary>A sounding pitch that is not one of the scale's own degrees.</summary>
    public required Pen MutedEdge { get; init; }

    public required IBrush Sounding { get; init; }
    public required Pen SoundingSpoke { get; init; }

    public required IBrush Title { get; init; }
    public required IBrush Subtitle { get; init; }
    public required IBrush Caption { get; init; }

    /// <summary>The tonic's own name, printed large in the middle.</summary>
    public required IBrush CentreName { get; init; }
}

/// <summary>
/// The two palettes <see cref="DegreeView"/> can render in, and the pure selection logic between
/// them - see <c>PianoRollPalettes</c> for why this lives apart from the control.
/// </summary>
internal static class DegreeViewPalettes
{
    public static readonly DegreeViewPalette Dark = new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D)),
        Hub = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D)),
        Ring = new Pen(new SolidColorBrush(Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF)), 1.25),
        TwelveTetTick = new Pen(new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF)), 1),
        Deviation = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0xA8, 0x50)), 3.5),
        Spoke = new Pen(new SolidColorBrush(Color.FromArgb(0xBB, 0x8F, 0x86, 0xE8)), 1.5),
        DegreeMarker = new SolidColorBrush(Color.FromRgb(0x8F, 0x86, 0xE8)),
        MarkerEdge = new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D)), 1.5),
        DegreeLabel = new SolidColorBrush(Color.FromRgb(0xA9, 0xA1, 0xF0)),
        TickLabel = new SolidColorBrush(Color.FromArgb(0x99, 0xAA, 0xAA, 0xB4)),
        CentsLabel = new SolidColorBrush(Color.FromArgb(0x88, 0xAA, 0xAA, 0xB4)),
        CentsAccent = new SolidColorBrush(Color.FromRgb(0xE0, 0xA8, 0x50)),
        MutedEdge = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0x8A, 0x8A, 0x96)), 1.5),
        Sounding = new SolidColorBrush(Color.FromRgb(0xF2, 0x72, 0x72)),
        SoundingSpoke = new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0x72, 0x72)), 2.5),
        Title = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEC)),
        Subtitle = new SolidColorBrush(Color.FromArgb(0xCC, 0xAA, 0xAA, 0xB4)),
        Caption = new SolidColorBrush(Color.FromArgb(0x8C, 0xAA, 0xAA, 0xB4)),
        CentreName = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEC)),
    };

    /// <summary>
    /// A light counterpart built on the same rules the piano roll's light palette follows: darken
    /// what was light on a dark ground so it still reads as solid, and flip translucent white lines
    /// to translucent black so they do not vanish against a light background.
    /// </summary>
    public static readonly DegreeViewPalette Light = new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xF0)),
        Hub = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xF0)),
        Ring = new Pen(new SolidColorBrush(Color.FromArgb(0x5C, 0x00, 0x00, 0x00)), 1.25),
        TwelveTetTick = new Pen(new SolidColorBrush(Color.FromArgb(0x77, 0x00, 0x00, 0x00)), 1),
        Deviation = new Pen(new SolidColorBrush(Color.FromRgb(0xA0, 0x66, 0x10)), 3.5),
        Spoke = new Pen(new SolidColorBrush(Color.FromArgb(0xBB, 0x53, 0x48, 0xC4)), 1.5),
        DegreeMarker = new SolidColorBrush(Color.FromRgb(0x53, 0x48, 0xC4)),
        MarkerEdge = new Pen(new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xF0)), 1.5),
        DegreeLabel = new SolidColorBrush(Color.FromRgb(0x44, 0x3B, 0xA8)),
        TickLabel = new SolidColorBrush(Color.FromArgb(0xAA, 0x40, 0x40, 0x48)),
        CentsLabel = new SolidColorBrush(Color.FromArgb(0x99, 0x40, 0x40, 0x48)),
        CentsAccent = new SolidColorBrush(Color.FromRgb(0xA0, 0x66, 0x10)),
        MutedEdge = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0x55, 0x5A, 0x66)), 1.5),
        Sounding = new SolidColorBrush(Color.FromRgb(0xD1, 0x3F, 0x3F)),
        SoundingSpoke = new Pen(new SolidColorBrush(Color.FromRgb(0xD1, 0x3F, 0x3F)), 2.5),
        Title = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)),
        Subtitle = new SolidColorBrush(Color.FromArgb(0xCC, 0x40, 0x40, 0x48)),
        Caption = new SolidColorBrush(Color.FromArgb(0x99, 0x40, 0x40, 0x48)),
        CentreName = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)),
    };

    /// <summary>Picks a palette for an already-resolved theme variant - see <c>PianoRollPalettes.For</c>.</summary>
    public static DegreeViewPalette For(ThemeVariant variant) =>
        variant == ThemeVariant.Light ? Light : Dark;
}

/// <summary>
/// The scale wheel: one octave drawn as a ring, every degree placed at its true cents angle.
/// </summary>
/// <remarks>
/// <para>
/// This is what the app shows in place of a staff whenever the restyled scale is
/// <see cref="Scale.Notatable"/> <c>== false</c> - Gamelan Slendro, Maqam Rast, Thai 7-equal, and
/// the other families no Western spelling exists for. A staff cannot express them and a row of
/// cipher numerals does not show what makes them different; a ring can. 360 degrees of arc is 1200
/// cents, twelve ticks mark the equal-tempered semitones and carry their note names, and each degree
/// sits at its own cents angle - so the gap between a maqam's neutral third and the 12-TET third it
/// is not is visible without reading a single number.
/// </para>
/// <para>
/// <b>Every degree keeps a spoke and a number at all times</b>, and what is sounding is shown by
/// recolouring them rather than by adding anything. The version this replaced drew a bare ring at
/// rest and grew spokes, haloes, octave rings and a fading trail as notes arrived, and the user's
/// verdict on it was that it was confusing: the parts that were standing information and the parts
/// that were momentary looked alike, and there was no still frame to learn the control from. A wheel
/// whose furniture never moves has one.
/// </para>
/// <para>
/// The cost of that is octave: a bass note and a melody note on the same degree now light the same
/// marker, where the previous version plotted them at different radii. That distinction was the
/// single biggest source of the clutter - it put dots across the middle of the wheel where the eye
/// had no reason to associate them with anything on the rim - and the piano roll shows octave far
/// better than a ring ever did.
/// </para>
/// <para>
/// A custom <see cref="Control"/> overriding <see cref="Render"/>, not a panel of per-note elements,
/// matching <c>PianoRoll</c>'s approach. The cull here is structural rather than spatial: only the
/// notes sounding at the playhead are ever consulted, found through <see cref="DegreeWheelIndex"/>'s
/// binary search rather than a walk over a 20,000-note score. All layout maths lives in
/// <see cref="DegreeGeometry"/> so it is testable without a window.
/// </para>
/// </remarks>
public sealed class DegreeView : Control
{
    // --- theme -----------------------------------------------------------------------------

    private DegreeViewPalette _palette = DegreeViewPalettes.Dark;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        _palette = DegreeViewPalettes.For(ActualThemeVariant);
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _palette = DegreeViewPalettes.For(ActualThemeVariant);

        // Every cached FormattedText has a brush baked in at construction time, so a stale cache
        // would keep drawing the previous theme's colours forever - see PianoRoll's identical note.
        _degreeLabelCache.Clear();
        _centsLabelCache.Clear();
        _tickLabelCache.Clear();
        _headerScale = null;
        _titleFit = null;
        _subtitleFit = null;
        _headerFitWidth = -1;
        _centreTonic = int.MinValue;
        _centreName = null;
        _centreDetail = null;
        _emptyText = null;
        _captionText = null;
        _shortCaptionText = null;

        InvalidateVisual();
    }

    // --- immutable drawing resources, created once ------------------------------------------

    private static readonly Typeface Typeface = new("Inter");
    private static readonly Typeface BoldTypeface = new("Inter", FontStyle.Normal, FontWeight.SemiBold);

    private const double TitleFontSize = 15;
    private const double SubtitleFontSize = 11;
    private const double CaptionFontSize = 10;
    private const double DegreeLabelFontSize = 13;
    private const double TickLabelFontSize = 10;
    private const double CentsFontSize = 9;
    private const double CentreNameFontSize = 22;
    private const double CentreDetailFontSize = 10;

    private const double EdgePadding = 12;
    private const double WheelPadding = 14;

    private const double TickHalfLength = 5;
    private const double MarkerRadius = 5.0;
    private const double SoundingRadius = 7.5;

    /// <summary>
    /// A degree this far off the 12-TET grid gets a deviation arc and an accented cents figure.
    /// Below it the offset is rounding noise rather than a tuning the user chose.
    /// </summary>
    private const double DeviationThresholdCents = 2.0;

    /// <summary>
    /// How many straight segments a deviation arc is drawn from.
    /// </summary>
    /// <remarks>
    /// A <c>StreamGeometry</c> would be exact and would also be one allocation per degree per frame,
    /// which is the allocation the render path is forbidden. An offset is at most 50 cents, so the
    /// widest arc spans 15 degrees of turn; six chords across that are within a fifth of a pixel of
    /// the true curve at any radius this control reaches.
    /// </remarks>
    private const int DeviationArcSegments = 6;

    /// <summary>
    /// Ceiling on simultaneously read sounding notes. A hundred-voice cluster lights every degree
    /// anyway; the point of the cap is that the buffer is allocated once and never grows.
    /// </summary>
    private const int MaxSoundingNotes = 32;

    /// <summary>Below this radius the centre readout has nowhere to sit and is skipped.</summary>
    private const double CentreReadoutMinimumRadius = 96;

    // --- reused buffers and caches -------------------------------------------------------------

    private readonly WheelNote[] _soundingBuffer = new WheelNote[MaxSoundingNotes];

    /// <summary>
    /// Which of the scale's degrees are sounding, by index. Rebuilt each frame into a fixed array
    /// rather than a set, since the render path may not allocate and a scale has at most twelve.
    /// </summary>
    private readonly bool[] _sounding = new bool[Scale.MaxDegrees];

    private readonly Dictionary<int, FormattedText> _degreeLabelCache = [];
    private readonly Dictionary<(int Tenths, bool Accent), FormattedText> _centsLabelCache = [];
    private readonly Dictionary<int, FormattedText> _tickLabelCache = [];

    private Scale? _headerScale;
    private string _titleString = "";
    private string _subtitleString = "";
    private FormattedText? _titleText;
    private FormattedText? _subtitleText;

    /// <summary>
    /// The header, re-elided to fit whenever the pane width changes - a scale name can run far
    /// longer than a narrow pane (a Javanese gamelan citation alone can run 50 characters), and
    /// unlike the caption there is no shorter alternate string to fall back to, only a truncation of
    /// the one there is. Cached against the width it was fit to, the same way the label caches are
    /// cached against the theme, so a title untouched since the last frame costs nothing.
    /// </summary>
    private double _headerFitWidth = -1;
    private FormattedText? _titleFit;
    private FormattedText? _subtitleFit;

    /// <summary>The centre readout, rebuilt only when the tonic or the degree count changes.</summary>
    private int _centreTonic = int.MinValue;
    private int _centreDegreeCount = -1;
    private FormattedText? _centreName;
    private FormattedText? _centreDetail;

    private FormattedText? _captionText;
    private FormattedText? _shortCaptionText;
    private FormattedText? _emptyText;

    private NotationScore? _indexedScore;
    private DegreeWheelIndex _index = DegreeWheelIndex.Empty;

    // --- styled properties -------------------------------------------------------------------

    public static readonly StyledProperty<NotationScore?> ScoreProperty =
        AvaloniaProperty.Register<DegreeView, NotationScore?>(nameof(Score));

    public static readonly StyledProperty<Scale?> ScaleProperty =
        AvaloniaProperty.Register<DegreeView, Scale?>(nameof(Scale));

    public static readonly StyledProperty<Pitch> TonicProperty =
        AvaloniaProperty.Register<DegreeView, Pitch>(nameof(Tonic), Pitch.FromMidi(60));

    /// <summary>Playhead position in ticks. Negative shows the scale at rest, with nothing highlighted.</summary>
    public static readonly StyledProperty<long> PlayheadTicksProperty =
        AvaloniaProperty.Register<DegreeView, long>(nameof(PlayheadTicks), -1);

    static DegreeView() =>
        AffectsRender<DegreeView>(
            ScoreProperty,
            ScaleProperty,
            TonicProperty,
            PlayheadTicksProperty);

    public NotationScore? Score
    {
        get => GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    public Scale? Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public Pitch Tonic
    {
        get => GetValue(TonicProperty);
        set => SetValue(TonicProperty, value);
    }

    public long PlayheadTicks
    {
        get => GetValue(PlayheadTicksProperty);
        set => SetValue(PlayheadTicksProperty, value);
    }

    // --- rendering -----------------------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        DegreeViewPalette palette = _palette;
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(palette.Background, bounds);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        Scale? scale = Scale;

        // Never throw: no scale yet is routine while a file is loading or before the user has
        // landed on a target, and the view must say so calmly rather than crash. A missing *score*
        // is not an empty state at all - the wheel describes the scale, so it is worth drawing on
        // its own, simply with nothing highlighted.
        if (scale is null)
        {
            DrawEmptyState(context, bounds, palette);
            return;
        }

        EnsureHeader(scale, palette);
        EnsureCaption(palette);
        EnsureIndex(Score);

        Pitch tonic = Tonic;
        EnsureCentre(tonic, scale.DegreeCount, palette);

        double headerHeight = HeaderHeight();
        double captionHeight = CaptionHeight();

        DrawHeader(context, palette, bounds.Width);
        DrawCaption(context, bounds, captionHeight);

        WheelLayout layout = DegreeGeometry.LayoutFor(
            bounds.Width, bounds.Height, headerHeight, captionHeight, WheelPadding);

        if (!layout.IsUsable)
        {
            return;
        }

        // What is sounding is read before anything is drawn, because it changes how the standing
        // furniture is coloured rather than adding marks on top of it.
        MarkSounding(scale, tonic, PlayheadTicks);

        double hub = HubRadiusFor(layout);

        DrawRing(context, layout, palette, tonic);
        DrawDegrees(context, layout, palette, scale, hub);
        DrawOutOfScaleNotes(context, layout, palette, scale, tonic, hub);
        DrawCentre(context, layout, palette, hub);
    }

    private static Point P(in WheelPoint point) => new(point.X, point.Y);

    /// <summary>
    /// How far the spokes start from the middle: the layout's hub, or wider if the centre readout
    /// needs the room.
    /// </summary>
    /// <remarks>
    /// One radius for both, so the spokes meet the readout's disc exactly. Sizing the disc to the
    /// text while leaving the spokes on the layout's hub leaves them either floating short of it or
    /// running under the words, and "tonic - 7 degrees" is wider than any fixed fraction of the
    /// radius that still leaves a wheel worth looking at.
    /// </remarks>
    private double HubRadiusFor(in WheelLayout layout)
    {
        double widest = Math.Max(_centreName?.Width ?? 0, _centreDetail?.Width ?? 0);
        return layout.Radius < CentreReadoutMinimumRadius
            ? layout.HubRadius
            : Math.Max(layout.HubRadius, (widest / 2.0) + 7);
    }

    // --- the ring and its 12-TET reference ------------------------------------------------------

    /// <summary>
    /// The octave ring, the twelve equal-tempered reference ticks, and the note name at each.
    /// </summary>
    /// <remarks>
    /// The ticks are the most informative marks on the control: every degree is read against them,
    /// and the distance between a degree marker and its nearest tick <em>is</em> the scale's
    /// deviation from equal temperament, shown rather than stated. The names make that concrete -
    /// "the second degree sits half a semitone flat of D" rather than "the second degree sits
    /// somewhere between two anonymous ticks".
    /// </remarks>
    private void DrawRing(
        DrawingContext context, in WheelLayout layout, DegreeViewPalette palette, Pitch tonic)
    {
        Point centre = new(layout.CenterX, layout.CenterY);
        double ring = layout.RingRadius;

        context.DrawEllipse(null, palette.Ring, centre, ring, ring);

        int tonicPitchClass = tonic.MidiNote % MidiRounding.SemitonesPerOctave;
        double step = DegreeGeometry.DegreesPerTurn / DegreeGeometry.TwelveTetTickCount;

        for (int i = 0; i < DegreeGeometry.TwelveTetTickCount; i++)
        {
            double angle = i * step;
            context.DrawLine(
                palette.TwelveTetTick,
                P(DegreeGeometry.PointAtAngle(layout, angle, ring - TickHalfLength)),
                P(DegreeGeometry.PointAtAngle(layout, angle, ring + TickHalfLength)));

            FormattedText name = TickLabelFor(
                DegreeGeometry.NameAtTick(tonicPitchClass, i), palette);
            WheelPoint at = DegreeGeometry.PointAtAngle(layout, angle, layout.LetterRadius);
            context.DrawText(
                name, new Point(at.X - (name.Width / 2.0), at.Y - (name.Height / 2.0)));
        }
    }

    // --- the scale's own degrees -----------------------------------------------------------------

    /// <summary>
    /// Every degree, always: a spoke from the hub, its number on that spoke, its marker on the ring,
    /// its cents reading outside, and an arc back to the 12-TET tick it misses.
    /// </summary>
    private void DrawDegrees(
        DrawingContext context,
        in WheelLayout layout,
        DegreeViewPalette palette,
        Scale scale,
        double hub)
    {
        double ring = layout.RingRadius;

        for (int i = 0; i < scale.DegreeCount; i++)
        {
            double cents = scale.DegreeCents[i];
            double offset = scale.DegreeOffsets[i];
            bool deviates = Math.Abs(offset) >= DeviationThresholdCents;
            bool sounding = i < _sounding.Length && _sounding[i];

            WheelPoint at = DegreeGeometry.PointAtCents(layout, cents, ring);

            context.DrawLine(
                sounding ? palette.SoundingSpoke : palette.Spoke,
                P(DegreeGeometry.PointAtCents(layout, cents, hub)),
                P(at));

            // The arc runs along the ring from where 12-TET would have put this degree to where the
            // scale actually puts it. On Saba it is the whole story; on a major scale it never
            // appears. Along the ring rather than straight across it, because the quantity it stands
            // for is an angle - a chord would read as a separate mark rather than as the gap.
            if (deviates)
            {
                DrawDeviationArc(context, layout, palette, cents, ring);
            }

            context.DrawEllipse(
                sounding ? palette.Sounding : palette.DegreeMarker,
                palette.MarkerEdge,
                P(at),
                sounding ? SoundingRadius : MarkerRadius,
                sounding ? SoundingRadius : MarkerRadius);

            DrawDegreeNumber(context, layout, palette, i + 1, cents, sounding);
            DrawCentsLabel(context, layout, palette, offset, cents, deviates);
        }
    }

    private static void DrawDeviationArc(
        DrawingContext context,
        in WheelLayout layout,
        DegreeViewPalette palette,
        double cents,
        double ring)
    {
        double reference = DegreeGeometry.NearestTwelveTetCents(cents);
        Point previous = P(DegreeGeometry.PointAtCents(layout, cents, ring));

        for (int step = 1; step <= DeviationArcSegments; step++)
        {
            double along = cents + ((reference - cents) * step / DeviationArcSegments);
            Point next = P(DegreeGeometry.PointAtCents(layout, along, ring));
            context.DrawLine(palette.Deviation, previous, next);
            previous = next;
        }
    }

    /// <summary>The degree's number, sitting on its own spoke just inside the ring.</summary>
    private void DrawDegreeNumber(
        DrawingContext context,
        in WheelLayout layout,
        DegreeViewPalette palette,
        int degree,
        double cents,
        bool sounding)
    {
        WheelPoint at = DegreeGeometry.PointAtCents(layout, cents, layout.NumberRadius);
        FormattedText number = DegreeLabelFor(degree, sounding, palette);
        context.DrawText(
            number, new Point(at.X - (number.Width / 2.0), at.Y - (number.Height / 2.0)));
    }

    /// <summary>
    /// The degree's distance from its nearest semitone, printed outside the note names.
    /// </summary>
    /// <remarks>
    /// The offset, not the absolute cents. Absolute cents restate the marker's own position, which
    /// the wheel already shows; the offset is the number the reader cannot see - and it is exactly
    /// what pitch bend has to carry, so it is the figure the channel budget is spent on.
    /// </remarks>
    private void DrawCentsLabel(
        DrawingContext context,
        in WheelLayout layout,
        DegreeViewPalette palette,
        double offset,
        double cents,
        bool deviates)
    {
        WheelPoint at = DegreeGeometry.PointAtCents(layout, cents, layout.CentsRadius);
        FormattedText text = CentsLabelFor(offset, deviates, palette);
        context.DrawText(text, new Point(at.X - (text.Width / 2.0), at.Y - (text.Height / 2.0)));
    }

    // --- what is sounding now -----------------------------------------------------------------------

    /// <summary>
    /// Flags which of the scale's degrees are sounding at the playhead.
    /// </summary>
    /// <remarks>
    /// Clears and refills a fixed array rather than building a set, for the same reason the piano
    /// roll's lit keys do: the render path may not allocate, and a scale has at most twelve degrees.
    /// </remarks>
    private void MarkSounding(Scale scale, Pitch tonic, long playhead)
    {
        Array.Clear(_sounding);

        if (playhead < 0)
        {
            return;
        }

        int count = _index.Sounding(playhead, _soundingBuffer);
        for (int i = 0; i < count; i++)
        {
            DegreeReading reading = DegreeReader.Read(new Pitch(_soundingBuffer[i].Cents), scale, tonic);
            if (reading.IsInScale && reading.Degree >= 1 && reading.Degree <= _sounding.Length)
            {
                _sounding[reading.Degree - 1] = true;
            }
        }
    }

    /// <summary>
    /// Anything sounding that is <em>not</em> one of the scale's degrees, drawn hollow at its true
    /// angle.
    /// </summary>
    /// <remarks>
    /// Under <c>PassThrough</c> a note outside the source scale reaches the output untouched, so a
    /// pitch that belongs to no degree is an ordinary state rather than a fault. Hollow and grey at
    /// its true angle says "sounding, off the scale"; drawing it like a degree would invent one, and
    /// not drawing it at all would leave the wheel silent while the speakers were not.
    /// </remarks>
    private void DrawOutOfScaleNotes(
        DrawingContext context,
        in WheelLayout layout,
        DegreeViewPalette palette,
        Scale scale,
        Pitch tonic,
        double hub)
    {
        long playhead = PlayheadTicks;
        if (playhead < 0)
        {
            return;
        }

        int count = _index.Sounding(playhead, _soundingBuffer);
        for (int i = 0; i < count; i++)
        {
            double cents = _soundingBuffer[i].Cents;
            DegreeReading reading = DegreeReader.Read(new Pitch(cents), scale, tonic);
            if (reading.IsInScale)
            {
                continue;
            }

            double relative = cents - tonic.Cents;
            context.DrawLine(
                palette.MutedEdge,
                P(DegreeGeometry.PointAtCents(layout, relative, hub)),
                P(DegreeGeometry.PointAtCents(layout, relative, layout.RingRadius)));

            context.DrawEllipse(
                null,
                palette.MutedEdge,
                P(DegreeGeometry.PointAtCents(layout, relative, layout.RingRadius)),
                MarkerRadius,
                MarkerRadius);
        }
    }

    // --- the centre readout ---------------------------------------------------------------------

    /// <summary>
    /// The tonic's name and the scale's size, on a disc the spokes stop at.
    /// </summary>
    /// <remarks>
    /// The tonic rather than whatever is sounding. Every angle on the wheel is measured from it, so
    /// without it printed the reader has no datum - twelve o'clock could be any pitch. It is also
    /// the one thing here that does not move, which is what makes the moving parts legible.
    /// </remarks>
    private void DrawCentre(
        DrawingContext context, in WheelLayout layout, DegreeViewPalette palette, double hub)
    {
        if (layout.Radius < CentreReadoutMinimumRadius
            || _centreName is null
            || _centreDetail is null)
        {
            return;
        }

        Point centre = new(layout.CenterX, layout.CenterY);
        context.DrawEllipse(palette.Hub, null, centre, hub, hub);

        double total = _centreName.Height + _centreDetail.Height;
        double top = centre.Y - (total / 2.0);

        context.DrawText(_centreName, new Point(centre.X - (_centreName.Width / 2.0), top));
        context.DrawText(
            _centreDetail,
            new Point(centre.X - (_centreDetail.Width / 2.0), top + _centreName.Height));
    }

    private void EnsureCentre(Pitch tonic, int degreeCount, DegreeViewPalette palette)
    {
        int midi = tonic.MidiNote;
        if (_centreTonic == midi && _centreDegreeCount == degreeCount && _centreName is not null)
        {
            return;
        }

        _centreTonic = midi;
        _centreDegreeCount = degreeCount;

        int pitchClass = ((midi % MidiRounding.SemitonesPerOctave) + MidiRounding.SemitonesPerOctave)
            % MidiRounding.SemitonesPerOctave;

        _centreName = new FormattedText(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{DegreeGeometry.PitchClassNames[pitchClass]}{(midi / MidiRounding.SemitonesPerOctave) - 1}"),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            BoldTypeface,
            CentreNameFontSize,
            palette.CentreName);

        _centreDetail = new FormattedText(
            string.Create(CultureInfo.InvariantCulture, $"tonic · {degreeCount} degrees"),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            CentreDetailFontSize,
            palette.Subtitle);
    }

    // --- header, caption and empty state ------------------------------------------------------------

    /// <summary>
    /// Draws the title and subtitle, centred, eliding either that overruns the pane.
    /// </summary>
    /// <remarks>
    /// <see cref="HeaderHeight"/> is unaffected by elision - both strings stay one line at the same
    /// font size, so the layout below them (where the wheel starts) does not shift as the pane is
    /// resized.
    /// </remarks>
    private void DrawHeader(DrawingContext context, DegreeViewPalette palette, double width)
    {
        if (_titleText is null || _subtitleText is null)
        {
            return;
        }

        double available = Math.Max(0, width - (EdgePadding * 2));
        if (_titleFit is null || _headerFitWidth != available)
        {
            _titleFit = Elide(_titleString, TitleFontSize, BoldTypeface, palette.Title, available);
            _subtitleFit = Elide(
                _subtitleString, SubtitleFontSize, Typeface, palette.Subtitle, available);
            _headerFitWidth = available;
        }

        double top = EdgePadding * 0.7;
        context.DrawText(_titleFit, new Point((width - _titleFit.Width) / 2.0, top));
        context.DrawText(
            _subtitleFit!,
            new Point((width - _subtitleFit!.Width) / 2.0, top + _titleText.Height + 3));
    }

    /// <summary>
    /// The full string if it fits, otherwise the longest prefix that does, with an ellipsis appended.
    /// A binary search over prefix length rather than trimming one character at a time - this runs
    /// once per resize, not once per frame, but it is still one <see cref="FormattedText"/> per
    /// candidate and there is no reason to make more of them than <c>log2(length)</c>.
    /// </summary>
    private static FormattedText Elide(
        string text, double fontSize, Typeface typeface, IBrush brush, double maxWidth)
    {
        FormattedText Build(string s) => new(
            s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, brush);

        FormattedText full = Build(text);
        if (full.Width <= maxWidth || text.Length <= 1)
        {
            return full;
        }

        const string Ellipsis = "…";
        string best = Ellipsis;
        int low = 0;
        int high = text.Length - 1;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            string candidate = text[..mid].TrimEnd() + Ellipsis;
            if (Build(candidate).Width <= maxWidth)
            {
                best = candidate;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return Build(best);
    }

    /// <summary>
    /// The legend, in whichever form fits. A single fixed string ran off the right-hand edge as soon
    /// as the pane was narrowed - and a legend that is cut in half is worse than none, because the
    /// reader cannot tell what was lost.
    /// </summary>
    private void DrawCaption(DrawingContext context, Rect bounds, double captionHeight)
    {
        double available = bounds.Width - (EdgePadding * 2);
        FormattedText? caption = _captionText is not null && _captionText.Width <= available
            ? _captionText
            : _shortCaptionText;

        if (caption is null || caption.Width > available)
        {
            return;
        }

        context.DrawText(
            caption,
            new Point(
                Math.Max(EdgePadding, (bounds.Width - caption.Width) / 2.0),
                bounds.Height - captionHeight));
    }

    private void DrawEmptyState(DrawingContext context, Rect bounds, DegreeViewPalette palette)
    {
        _emptyText ??= new FormattedText(
            "Choose a target scale to see its wheel",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            SubtitleFontSize + 1,
            palette.Subtitle);

        context.DrawText(
            _emptyText,
            new Point(
                Math.Max(0, (bounds.Width - _emptyText.Width) / 2.0),
                Math.Max(0, (bounds.Height - _emptyText.Height) / 2.0)));
    }

    private double HeaderHeight() =>
        _titleText is null || _subtitleText is null
            ? 0
            : (EdgePadding * 0.7) + _titleText.Height + 3 + _subtitleText.Height;

    private double CaptionHeight() => (_captionText?.Height ?? CaptionFontSize + 3) + EdgePadding;

    /// <summary>
    /// The standing explanation of what the wheel means, built once per theme. The user's complaint
    /// about the view this replaced was that its meaning was not readable off the screen, so the
    /// legend is part of the control rather than something to look up.
    /// </summary>
    private void EnsureCaption(DegreeViewPalette palette)
    {
        _captionText ??= new FormattedText(
            "Degrees sit at their true cents angle, not evenly by index - the gap between a marker "
            + "and its 12-TET tick is the deviation pitch bend has to carry.",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            CaptionFontSize,
            palette.Caption);

        _shortCaptionText ??= new FormattedText(
            "One turn is an octave · ticks are the 12-TET semitones",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            CaptionFontSize,
            palette.Caption);
    }

    /// <summary>
    /// Rebuilds the scale-dependent header only when the scale itself changes. The scale list is
    /// arrow-key browsable, so this runs on a keystroke - but never on a mere repaint or a playhead
    /// tick, which is the case that has to stay free.
    /// </summary>
    /// <remarks>
    /// The subtitle names the tradition and the region rather than restating the degree count and
    /// the deviation. Both of those are now drawn on the wheel itself - the count as the spokes, the
    /// deviation as the arcs and the cents figures - and a header that repeats what the picture says
    /// spends the one line above it on nothing. Where the scale is from does not appear anywhere
    /// else in this pane.
    /// </remarks>
    private void EnsureHeader(Scale scale, DegreeViewPalette palette)
    {
        if (ReferenceEquals(_headerScale, scale) && _titleText is not null)
        {
            return;
        }

        _headerScale = scale;

        _titleString = scale.Name;
        _titleText = new FormattedText(
            _titleString,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            BoldTypeface,
            TitleFontSize,
            palette.Title);

        _subtitleString = string.Create(
            CultureInfo.InvariantCulture, $"{scale.Tradition} · {scale.Region}");
        _subtitleText = new FormattedText(
            _subtitleString,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            SubtitleFontSize,
            palette.Subtitle);

        // The new scale's name may be a different length entirely - a stale elision built for the
        // previous scale's string must not be reused just because the pane width has not changed.
        _titleFit = null;
        _subtitleFit = null;
        _headerFitWidth = -1;
    }

    /// <summary>
    /// Re-indexes only when the score object itself changes. A reference compare per frame is the
    /// whole cost; the alternative - walking every entry to find what sounds now - is the O(all
    /// entries) per-frame scan the wheel is explicitly not allowed to do.
    /// </summary>
    private void EnsureIndex(NotationScore? score)
    {
        if (ReferenceEquals(_indexedScore, score))
        {
            return;
        }

        _indexedScore = score;
        _index = DegreeWheelIndex.Build(score);
    }

    // --- text caches, all cleared on theme change ---------------------------------------------

    private FormattedText DegreeLabelFor(int degree, bool sounding, DegreeViewPalette palette)
    {
        int key = sounding ? -degree : degree;
        if (_degreeLabelCache.TryGetValue(key, out FormattedText? cached))
        {
            return cached;
        }

        FormattedText created = new(
            degree.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            BoldTypeface,
            DegreeLabelFontSize,
            sounding ? palette.Sounding : palette.DegreeLabel);

        _degreeLabelCache[key] = created;
        return created;
    }

    private FormattedText TickLabelFor(string name, DegreeViewPalette palette)
    {
        // Keyed on the pitch class rather than the string, so the dictionary never hashes text.
        int key = Array.IndexOf(DegreeGeometry.PitchClassNames, name);
        if (_tickLabelCache.TryGetValue(key, out FormattedText? cached))
        {
            return cached;
        }

        FormattedText created = new(
            name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            TickLabelFontSize,
            palette.TickLabel);

        _tickLabelCache[key] = created;
        return created;
    }

    private FormattedText CentsLabelFor(double offset, bool accent, DegreeViewPalette palette)
    {
        // Keyed on tenths of a cent: an AEU comma scale's offsets are not whole numbers, and keying
        // on the double itself would make a cache that never hits.
        (int Tenths, bool accent) key = (MidiRounding.ToNearestInt(offset * 10), accent);
        if (_centsLabelCache.TryGetValue(key, out FormattedText? cached))
        {
            return cached;
        }

        FormattedText created = new(
            string.Create(CultureInfo.InvariantCulture, $"{key.Tenths / 10.0:0.#}¢"),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            CentsFontSize,
            accent ? palette.CentsAccent : palette.CentsLabel);

        _centsLabelCache[key] = created;
        return created;
    }
}
