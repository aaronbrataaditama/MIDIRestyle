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

    /// <summary>Fills the middle of the wheel so the centre readout stays legible over trail lines.</summary>
    public required IBrush Hub { get; init; }

    /// <summary>The octave ring itself - one full turn is 1200 cents.</summary>
    public required Pen Ring { get; init; }

    /// <summary>A faint circle marking the tonic's own octave, so the radius scale has a datum.</summary>
    public required Pen OctaveGuide { get; init; }

    /// <summary>The twelve equal-tempered reference marks every degree is read against.</summary>
    public required Pen TwelveTetTick { get; init; }

    /// <summary>The whisker from a 12-TET tick to where the degree actually falls.</summary>
    public required Pen Deviation { get; init; }

    public required IBrush DegreeMarker { get; init; }
    public required IBrush TonicMarker { get; init; }

    /// <summary>Drawn in the background colour, so overlapping markers stay countable.</summary>
    public required Pen MarkerEdge { get; init; }

    public required IBrush DegreeLabel { get; init; }
    public required IBrush CentsLabel { get; init; }

    /// <summary>The cents figure of a degree that is genuinely off the 12-TET grid.</summary>
    public required IBrush CentsAccent { get; init; }

    public required IBrush Muted { get; init; }
    public required Pen MutedEdge { get; init; }

    public required IBrush Sounding { get; init; }
    public required IBrush SoundingGlow { get; init; }
    public required Pen SoundingSpoke { get; init; }
    public required Pen SoundingRing { get; init; }

    public required IBrush Tonic { get; init; }
    public required IBrush TonicGlow { get; init; }
    public required Pen TonicSpoke { get; init; }
    public required Pen TonicRing { get; init; }

    public required IBrush Title { get; init; }
    public required IBrush Subtitle { get; init; }
    public required IBrush Caption { get; init; }

    /// <summary>
    /// One brush and one pen per fade step of the trail, weakest first.
    /// </summary>
    /// <remarks>
    /// A ladder rather than a computed alpha because the render path may not allocate: a brush per
    /// trail note per frame is exactly the allocation the rule exists to prevent. See
    /// <see cref="DegreeGeometry.TrailStep"/>.
    /// </remarks>
    public required IReadOnlyList<IBrush> TrailDots { get; init; }

    public required IReadOnlyList<Pen> TrailLines { get; init; }
}

/// <summary>
/// The two palettes <see cref="DegreeView"/> can render in, and the pure selection logic between
/// them - see <c>PianoRollPalettes</c> for why this lives apart from the control.
/// </summary>
internal static class DegreeViewPalettes
{
    /// <summary>How many fade steps a trail is quantised to.</summary>
    public const int TrailSteps = 5;

    private static readonly byte[] DotAlphas = [0x26, 0x44, 0x66, 0x8C, 0xB4];
    private static readonly byte[] LineAlphas = [0x10, 0x20, 0x34, 0x4C, 0x68];

    public static readonly DegreeViewPalette Dark = new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D)),
        Hub = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D)),
        Ring = new Pen(new SolidColorBrush(Color.FromArgb(0x5E, 0xFF, 0xFF, 0xFF)), 1.5),
        OctaveGuide = new Pen(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), 1, DashStyle.Dash),
        TwelveTetTick = new Pen(new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)), 1),
        Deviation = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0xA8, 0x50)), 2.5),
        DegreeMarker = new SolidColorBrush(Color.FromRgb(0xA8, 0xAE, 0xBC)),
        TonicMarker = new SolidColorBrush(Color.FromRgb(0xC0, 0x93, 0x48)),
        MarkerEdge = new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D)), 1.5),
        DegreeLabel = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEC)),
        CentsLabel = new SolidColorBrush(Color.FromArgb(0x99, 0xAA, 0xAA, 0xB4)),
        CentsAccent = new SolidColorBrush(Color.FromRgb(0xE0, 0xA8, 0x50)),
        Muted = new SolidColorBrush(Color.FromArgb(0x88, 0x8A, 0x8A, 0x96)),
        MutedEdge = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0x8A, 0x8A, 0x96)), 1.5),
        Sounding = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xFF)),
        SoundingGlow = new SolidColorBrush(Color.FromArgb(0x3A, 0x6F, 0xA8, 0xFF)),
        SoundingSpoke = new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0x6F, 0xA8, 0xFF)), 2),
        SoundingRing = new Pen(new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xFF)), 2.5),
        Tonic = new SolidColorBrush(Color.FromRgb(0xF5, 0xC3, 0x6B)),
        TonicGlow = new SolidColorBrush(Color.FromArgb(0x3E, 0xF5, 0xC3, 0x6B)),
        TonicSpoke = new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0xF5, 0xC3, 0x6B)), 2),
        TonicRing = new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0xC3, 0x6B)), 2.5),
        Title = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEC)),
        Subtitle = new SolidColorBrush(Color.FromArgb(0xCC, 0xAA, 0xAA, 0xB4)),
        Caption = new SolidColorBrush(Color.FromArgb(0x8C, 0xAA, 0xAA, 0xB4)),
        TrailDots = Ladder(Color.FromRgb(0x6F, 0xA8, 0xFF), DotAlphas),
        TrailLines = LadderPens(Color.FromRgb(0x6F, 0xA8, 0xFF), LineAlphas),
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
        Ring = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x00, 0x00)), 1.5),
        OctaveGuide = new Pen(new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x00, 0x00)), 1, DashStyle.Dash),
        TwelveTetTick = new Pen(new SolidColorBrush(Color.FromArgb(0x8C, 0x00, 0x00, 0x00)), 1),
        Deviation = new Pen(new SolidColorBrush(Color.FromRgb(0xA0, 0x66, 0x10)), 2.5),
        DegreeMarker = new SolidColorBrush(Color.FromRgb(0x6A, 0x6E, 0x7A)),
        TonicMarker = new SolidColorBrush(Color.FromRgb(0xB0, 0x7A, 0x22)),
        MarkerEdge = new Pen(new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xF0)), 1.5),
        DegreeLabel = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)),
        CentsLabel = new SolidColorBrush(Color.FromArgb(0xAA, 0x40, 0x40, 0x48)),
        CentsAccent = new SolidColorBrush(Color.FromRgb(0xA0, 0x66, 0x10)),
        Muted = new SolidColorBrush(Color.FromArgb(0x88, 0x55, 0x5A, 0x66)),
        MutedEdge = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0x55, 0x5A, 0x66)), 1.5),
        Sounding = new SolidColorBrush(Color.FromRgb(0x2F, 0x5F, 0xD9)),
        SoundingGlow = new SolidColorBrush(Color.FromArgb(0x33, 0x2F, 0x5F, 0xD9)),
        SoundingSpoke = new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0x2F, 0x5F, 0xD9)), 2),
        SoundingRing = new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0x5F, 0xD9)), 2.5),
        Tonic = new SolidColorBrush(Color.FromRgb(0xB2, 0x70, 0x0A)),
        TonicGlow = new SolidColorBrush(Color.FromArgb(0x33, 0xB2, 0x70, 0x0A)),
        TonicSpoke = new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0xB2, 0x70, 0x0A)), 2),
        TonicRing = new Pen(new SolidColorBrush(Color.FromRgb(0xB2, 0x70, 0x0A)), 2.5),
        Title = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)),
        Subtitle = new SolidColorBrush(Color.FromArgb(0xCC, 0x40, 0x40, 0x48)),
        Caption = new SolidColorBrush(Color.FromArgb(0x99, 0x40, 0x40, 0x48)),
        TrailDots = Ladder(Color.FromRgb(0x2F, 0x5F, 0xD9), DotAlphas),
        TrailLines = LadderPens(Color.FromRgb(0x2F, 0x5F, 0xD9), LineAlphas),
    };

    private static IReadOnlyList<IBrush> Ladder(Color color, byte[] alphas)
    {
        IBrush[] brushes = new IBrush[alphas.Length];
        for (int i = 0; i < alphas.Length; i++)
        {
            brushes[i] = new SolidColorBrush(Color.FromArgb(alphas[i], color.R, color.G, color.B));
        }

        return brushes;
    }

    private static IReadOnlyList<Pen> LadderPens(Color color, byte[] alphas)
    {
        Pen[] pens = new Pen[alphas.Length];
        for (int i = 0; i < alphas.Length; i++)
        {
            pens[i] = new Pen(new SolidColorBrush(Color.FromArgb(alphas[i], color.R, color.G, color.B)), 1.5);
        }

        return pens;
    }

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
/// cents, twelve faint ticks mark the equal-tempered semitones, and each degree sits at its own
/// cents angle - so the gap between a maqam's neutral third and the 12-TET third it is not is
/// visible without reading a single number.
/// </para>
/// <para>
/// A custom <see cref="Control"/> overriding <see cref="Render"/>, not a panel of per-note elements,
/// matching <c>PianoRoll</c>'s approach. The cull here is structural rather than spatial: only the
/// notes sounding at the playhead and the last few attacks behind it are ever drawn, found through
/// <see cref="DegreeWheelIndex"/>'s binary search rather than a walk over a 20,000-note score. All
/// layout maths lives in <see cref="DegreeGeometry"/> so it is testable without a window.
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
        _centreCache.Clear();
        _headerScale = null;
        _titleFit = null;
        _subtitleFit = null;
        _headerFitWidth = -1;
        _emptyText = null;
        _captionText = null;
        _shortCaptionText = null;

        InvalidateVisual();
    }

    // --- immutable drawing resources, created once ------------------------------------------

    private static readonly Typeface Typeface = new("Inter");

    private const double TitleFontSize = 15;
    private const double SubtitleFontSize = 11;
    private const double CaptionFontSize = 10;
    private const double DegreeLabelFontSize = 13;
    private const double CentsFontSize = 9;
    private const double CentreFontSize = 40;

    private const double EdgePadding = 12;
    private const double WheelPadding = 14;

    private const double TickHalfLength = 9;
    private const double MarkerRadius = 5.5;
    private const double TonicMarkerRadius = 7.0;
    private const double SoundingRadius = 6.0;
    private const double SoundingGlowRadius = 12.0;
    private const double TrailDotRadius = 3.0;

    /// <summary>
    /// A degree this far off the 12-TET grid gets a deviation whisker and an accented cents figure.
    /// Below it the offset is rounding noise rather than a tuning the user chose.
    /// </summary>
    private const double DeviationThresholdCents = 2.0;

    /// <summary>
    /// How many beats of history the trail spans. Long enough that the control is never blank
    /// between attacks in a slow piece; short enough that a fast passage does not smear into a mesh.
    /// </summary>
    private const int TrailBeats = 3;

    /// <summary>How many past attacks the trail keeps. Also the size of the buffer, allocated once.</summary>
    private const int TrailLength = 5;

    /// <summary>
    /// Ceiling on simultaneously drawn sounding notes. A hundred-voice cluster is unreadable on any
    /// wheel; the point of the cap is that the buffer is allocated once and never grows.
    /// </summary>
    private const int MaxSoundingNotes = 32;

    /// <summary>Below this radius the centre readout has nowhere to sit and is skipped.</summary>
    private const double CentreReadoutMinimumRadius = 118;

    // --- reused buffers and caches -------------------------------------------------------------

    private readonly WheelNote[] _soundingBuffer = new WheelNote[MaxSoundingNotes];
    private readonly WheelNote[] _trailBuffer = new WheelNote[TrailLength];

    private readonly Dictionary<int, FormattedText> _degreeLabelCache = [];
    private readonly Dictionary<(int Cents, bool Accent), FormattedText> _centsLabelCache = [];
    private readonly Dictionary<(int Degree, bool Tonic), FormattedText> _centreCache = [];

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

        DrawRing(context, layout, palette);
        DrawDegrees(context, layout, palette, scale);

        long playhead = PlayheadTicks;
        if (playhead < 0)
        {
            return;
        }

        Pitch tonic = Tonic;
        NotationScore? score = Score;
        long trailWindow = Math.Max(1, (score?.Divisions ?? 480) * TrailBeats);

        DrawTrail(context, layout, palette, scale, tonic, playhead, trailWindow);
        DrawSounding(context, layout, palette, scale, tonic, playhead);
    }

    private static Point P(in WheelPoint point) => new(point.X, point.Y);

    // --- the ring and its 12-TET reference ------------------------------------------------------

    /// <summary>
    /// The octave ring, the faint circle marking the tonic's own octave, and the twelve equal-tempered
    /// reference ticks.
    /// </summary>
    /// <remarks>
    /// The ticks are the most informative marks on the control: every degree is read against them,
    /// and the distance between a degree marker and its nearest tick <em>is</em> the scale's
    /// deviation from equal temperament, shown rather than stated.
    /// </remarks>
    private static void DrawRing(DrawingContext context, in WheelLayout layout, DegreeViewPalette palette)
    {
        Point centre = new(layout.CenterX, layout.CenterY);
        double ring = layout.RingRadius;

        context.DrawEllipse(null, palette.Ring, centre, ring, ring);
        context.DrawEllipse(
            null, palette.OctaveGuide, centre, layout.OctaveBaseRadius, layout.OctaveBaseRadius);

        double step = DegreeGeometry.DegreesPerTurn / DegreeGeometry.TwelveTetTickCount;
        for (int i = 0; i < DegreeGeometry.TwelveTetTickCount; i++)
        {
            double angle = i * step;
            context.DrawLine(
                palette.TwelveTetTick,
                P(DegreeGeometry.PointAtAngle(layout, angle, ring - TickHalfLength)),
                P(DegreeGeometry.PointAtAngle(layout, angle, ring + TickHalfLength)));
        }
    }

    // --- the scale's own degrees -----------------------------------------------------------------

    private void DrawDegrees(
        DrawingContext context, in WheelLayout layout, DegreeViewPalette palette, Scale scale)
    {
        double ring = layout.RingRadius;

        for (int i = 0; i < scale.DegreeCount; i++)
        {
            double cents = scale.DegreeCents[i];
            double offset = scale.DegreeOffsets[i];
            bool deviates = Math.Abs(offset) >= DeviationThresholdCents;

            WheelPoint at = DegreeGeometry.PointAtCents(layout, cents, ring);

            // The whisker runs from where 12-TET would have put this degree to where the scale
            // actually puts it. On Rast it is the whole story; on a major scale it never appears.
            if (deviates)
            {
                WheelPoint reference = DegreeGeometry.PointAtCents(
                    layout, DegreeGeometry.NearestTwelveTetCents(cents), ring);
                context.DrawLine(palette.Deviation, P(reference), P(at));
            }

            bool isTonic = i == 0;
            context.DrawEllipse(
                isTonic ? palette.TonicMarker : palette.DegreeMarker,
                palette.MarkerEdge,
                P(at),
                isTonic ? TonicMarkerRadius : MarkerRadius,
                isTonic ? TonicMarkerRadius : MarkerRadius);

            DrawDegreeLabel(context, layout, palette, i + 1, cents, deviates);
        }
    }

    /// <summary>
    /// The degree number with its cents value stacked underneath, both centred on the label ring.
    /// Stacked in screen-vertical rather than radially so the pair stays readable all the way round
    /// - a radial stack would run the cents figure into the ring at the top and off the edge at the
    /// bottom.
    /// </summary>
    private void DrawDegreeLabel(
        DrawingContext context,
        in WheelLayout layout,
        DegreeViewPalette palette,
        int degree,
        double cents,
        bool deviates)
    {
        WheelPoint at = DegreeGeometry.PointAtCents(layout, cents, layout.LabelRadius);

        FormattedText number = DegreeLabelFor(degree, palette);
        FormattedText centsText = CentsLabelFor(
            MidiRounding.ToNearestInt(cents), deviates, palette);

        context.DrawText(number, new Point(at.X - (number.Width / 2.0), at.Y - number.Height));
        context.DrawText(centsText, new Point(at.X - (centsText.Width / 2.0), at.Y));
    }

    // --- the trail --------------------------------------------------------------------------------

    /// <summary>
    /// The last few attacks, joined newest-to-oldest and fading out, so the wheel shows melodic
    /// motion instead of blinking one dot at a time - and is never blank between attacks.
    /// </summary>
    private void DrawTrail(
        DrawingContext context,
        in WheelLayout layout,
        DegreeViewPalette palette,
        Scale scale,
        Pitch tonic,
        long playhead,
        long windowTicks)
    {
        int count = _index.Trail(playhead, windowTicks, _trailBuffer);
        if (count == 0)
        {
            return;
        }

        Point previous = default;
        bool hasPrevious = false;

        for (int i = 0; i < count; i++)
        {
            WheelNote note = _trailBuffer[i];
            int step = DegreeGeometry.TrailStep(
                DegreeGeometry.TrailStrength(note.StartTicks, playhead, windowTicks),
                DegreeViewPalettes.TrailSteps);

            Point at = P(DegreeGeometry.PointAtCents(
                layout, note.Cents - tonic.Cents, layout.TrailRadius));

            if (hasPrevious)
            {
                context.DrawLine(palette.TrailLines[step], previous, at);
            }

            context.DrawEllipse(palette.TrailDots[step], null, at, TrailDotRadius, TrailDotRadius);

            previous = at;
            hasPrevious = true;
        }
    }

    // --- what is sounding now -----------------------------------------------------------------------

    private void DrawSounding(
        DrawingContext context,
        in WheelLayout layout,
        DegreeViewPalette palette,
        Scale scale,
        Pitch tonic,
        long playhead)
    {
        int count = _index.Sounding(playhead, _soundingBuffer);
        if (count == 0)
        {
            return;
        }

        Point centre = new(layout.CenterX, layout.CenterY);
        int soleDegree = 0;
        bool soleIsTonic = false;
        bool oneDegreeOnly = true;

        for (int i = 0; i < count; i++)
        {
            WheelNote note = _soundingBuffer[i];
            DegreeReading reading = DegreeReader.Read(new Pitch(note.Cents), scale, tonic);
            double relative = note.Cents - tonic.Cents;
            double radius = DegreeGeometry.RadiusForOctave(layout, reading.OctaveOffset);
            Point at = P(DegreeGeometry.PointAtCents(layout, relative, radius));

            // The spoke runs the whole way from the hub out to the ring, not merely to the dot.
            // That is what ties the note's own marker to the degree it is an octave or two away
            // from: without it the eye has no reason to associate a dot near the middle with a
            // haloed degree out on the rim, which was exactly how the first draft read.
            Point hub = P(DegreeGeometry.PointAtCents(layout, relative, layout.HubRadius));
            Point rim = P(DegreeGeometry.PointAtCents(layout, relative, layout.RingRadius));

            if (!reading.IsInScale)
            {
                // Not one of the scale's degrees, but it is still sounding - so it is placed at its
                // true angle and drawn hollow and muted, which reads as "off the scale" rather than
                // as an extra degree nobody authored.
                context.DrawLine(palette.MutedEdge, hub, rim);
                context.DrawEllipse(null, palette.MutedEdge, at, SoundingRadius, SoundingRadius);
                oneDegreeOnly = false;
                continue;
            }

            bool isTonic = reading.Degree == 1;
            IBrush glow = isTonic ? palette.TonicGlow : palette.SoundingGlow;
            IBrush fill = isTonic ? palette.Tonic : palette.Sounding;
            Pen spoke = isTonic ? palette.TonicSpoke : palette.SoundingSpoke;
            Pen halo = isTonic ? palette.TonicRing : palette.SoundingRing;

            context.DrawLine(spoke, hub, rim);
            context.DrawEllipse(glow, null, at, SoundingGlowRadius, SoundingGlowRadius);
            context.DrawEllipse(fill, palette.MarkerEdge, at, SoundingRadius, SoundingRadius);

            // Ring the degree's own marker as well as the octave dot: the dot says which octave, the
            // halo says which degree, and on a bass note two rings in the radius scale apart they
            // would otherwise be hard to associate.
            WheelPoint onRing = DegreeGeometry.PointAtCents(
                layout, scale.DegreeCents[reading.Degree - 1], layout.RingRadius);
            context.DrawEllipse(null, halo, P(onRing), MarkerRadius + 4, MarkerRadius + 4);

            if (soleDegree == 0)
            {
                soleDegree = reading.Degree;
                soleIsTonic = isTonic;
            }
            else if (soleDegree != reading.Degree)
            {
                oneDegreeOnly = false;
            }
        }

        if (oneDegreeOnly && soleDegree > 0 && layout.Radius >= CentreReadoutMinimumRadius)
        {
            FormattedText numeral = CentreNumeralFor(soleDegree, soleIsTonic, palette);

            // Sized to the glyph, not to the hub: a 40-point numeral is taller than the hub circle,
            // and a trail line crossing its top half is exactly what the disc is there to stop.
            double disc = Math.Max(layout.HubRadius, (numeral.Height / 2.0) + 4);
            context.DrawEllipse(palette.Hub, null, centre, disc, disc);

            context.DrawText(
                numeral,
                new Point(centre.X - (numeral.Width / 2.0), centre.Y - (numeral.Height / 2.0)));
        }
    }

    // --- header, caption and empty state ------------------------------------------------------------

    /// <summary>
    /// Draws the title and subtitle, eliding either that overruns the pane. <see cref="HeaderHeight"/>
    /// is unaffected by elision - both strings stay one line at the same font size, so the layout
    /// above them (where the wheel starts) does not shift as the pane is resized.
    /// </summary>
    private void DrawHeader(DrawingContext context, DegreeViewPalette palette, double width)
    {
        if (_titleText is null || _subtitleText is null)
        {
            return;
        }

        double available = Math.Max(0, width - (EdgePadding * 2));
        if (_titleFit is null || _headerFitWidth != available)
        {
            _titleFit = Elide(_titleString, TitleFontSize, palette.Title, available);
            _subtitleFit = Elide(_subtitleString, SubtitleFontSize, palette.Subtitle, available);
            _headerFitWidth = available;
        }

        context.DrawText(_titleFit, new Point(EdgePadding, EdgePadding * 0.5));
        context.DrawText(
            _subtitleFit!, new Point(EdgePadding, (EdgePadding * 0.5) + _titleText.Height + 1));
    }

    /// <summary>
    /// The full string if it fits, otherwise the longest prefix that does, with an ellipsis appended.
    /// A binary search over prefix length rather than trimming one character at a time - this runs
    /// once per resize, not once per frame, but it is still one <see cref="FormattedText"/> per
    /// candidate and there is no reason to make more of them than <c>log2(length)</c>.
    /// </summary>
    private static FormattedText Elide(string text, double fontSize, IBrush brush, double maxWidth)
    {
        FormattedText Build(string s) => new(
            s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, fontSize, brush);

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
            : (EdgePadding * 0.5) + _titleText.Height + 1 + _subtitleText.Height;

    private double CaptionHeight() => (_captionText?.Height ?? CaptionFontSize + 3) + EdgePadding;

    /// <summary>
    /// The standing explanation of what the wheel means, built once per theme. The user's complaint
    /// about the view this replaced was that its meaning was not readable off the screen, so the
    /// legend is part of the control rather than something to look up.
    /// </summary>
    private void EnsureCaption(DegreeViewPalette palette)
    {
        _captionText ??= new FormattedText(
            "One turn is an octave. Faint ticks are the 12 equal-tempered semitones; "
            + "distance from the centre is octave, inner lower.",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            CaptionFontSize,
            palette.Caption);

        _shortCaptionText ??= new FormattedText(
            "One turn is an octave · faint ticks are 12-TET",
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
            Typeface,
            TitleFontSize,
            palette.Title);

        string tuning = scale.IsTwelveTet
            ? "exactly 12-TET"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"worst deviation from 12-TET {scale.MaxOffsetCents:0}¢");

        _subtitleString = string.Create(
            CultureInfo.InvariantCulture, $"{scale.DegreeCount} degrees · {tuning}");
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

    private FormattedText DegreeLabelFor(int degree, DegreeViewPalette palette)
    {
        if (_degreeLabelCache.TryGetValue(degree, out FormattedText? cached))
        {
            return cached;
        }

        FormattedText created = new(
            degree.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            DegreeLabelFontSize,
            palette.DegreeLabel);

        _degreeLabelCache[degree] = created;
        return created;
    }

    private FormattedText CentsLabelFor(int cents, bool accent, DegreeViewPalette palette)
    {
        (int cents, bool accent) key = (cents, accent);
        if (_centsLabelCache.TryGetValue(key, out FormattedText? cached))
        {
            return cached;
        }

        FormattedText created = new(
            string.Create(CultureInfo.InvariantCulture, $"{cents}¢"),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            CentsFontSize,
            accent ? palette.CentsAccent : palette.CentsLabel);

        _centsLabelCache[key] = created;
        return created;
    }

    private FormattedText CentreNumeralFor(int degree, bool isTonic, DegreeViewPalette palette)
    {
        (int degree, bool isTonic) key = (degree, isTonic);
        if (_centreCache.TryGetValue(key, out FormattedText? cached))
        {
            return cached;
        }

        FormattedText created = new(
            degree.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            CentreFontSize,
            isTonic ? palette.Tonic : palette.Sounding);

        _centreCache[key] = created;
        return created;
    }
}
