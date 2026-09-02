using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Controls;

/// <summary>One complete set of drawing resources for <see cref="DegreeLadder"/>.</summary>
internal sealed class DegreeLadderPalette
{
    /// <summary>The octave the degrees are measured along.</summary>
    public required Pen Axis { get; init; }

    /// <summary>The twelve equal-tempered semitones, ticked under the axis.</summary>
    public required Pen TwelveTetTick { get; init; }

    /// <summary>The stem joining a degree to the axis, so the eye can see which point it is over.</summary>
    public required Pen Stem { get; init; }

    /// <summary>A degree that sits on the semitone grid.</summary>
    public required IBrush OnGrid { get; init; }

    /// <summary>A degree pitch bend has to carry, in the same amber the wheel marks deviation in.</summary>
    public required IBrush OffGrid { get; init; }

    /// <summary>The stem of a degree off the grid, so the deviating ones read as a set.</summary>
    public required Pen OffGridStem { get; init; }

    public required IBrush Caption { get; init; }
}

/// <summary>
/// The two palettes the ladder can render in, built once - see <c>PianoRollPalettes</c> for why the
/// selection lives apart from the control.
/// </summary>
internal static class DegreeLadderPalettes
{
    public static readonly DegreeLadderPalette Dark = new()
    {
        Axis = new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), 1),
        TwelveTetTick = new Pen(new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)), 1),
        Stem = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0x8F, 0x86, 0xE8)), 1.5),
        OnGrid = new SolidColorBrush(Color.FromRgb(0x8F, 0x86, 0xE8)),
        OffGrid = new SolidColorBrush(Color.FromRgb(0xE0, 0xA8, 0x50)),
        OffGridStem = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xE0, 0xA8, 0x50)), 1.5),
        Caption = new SolidColorBrush(Color.FromArgb(0xAA, 0xAA, 0xAA, 0xB4)),
    };

    public static readonly DegreeLadderPalette Light = new()
    {
        Axis = new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)), 1),
        TwelveTetTick = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)), 1),
        Stem = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0x53, 0x48, 0xC4)), 1.5),
        OnGrid = new SolidColorBrush(Color.FromRgb(0x53, 0x48, 0xC4)),
        OffGrid = new SolidColorBrush(Color.FromRgb(0xA0, 0x66, 0x10)),
        OffGridStem = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xA0, 0x66, 0x10)), 1.5),
        Caption = new SolidColorBrush(Color.FromArgb(0xAA, 0x40, 0x40, 0x48)),
    };

    public static DegreeLadderPalette For(ThemeVariant variant) =>
        variant == ThemeVariant.Light ? Light : Dark;
}

/// <summary>
/// One octave drawn as a straight line, with the scale's degrees standing on it at their true cents.
/// </summary>
/// <remarks>
/// <para>
/// The same fact the degree wheel makes on a circle, made small enough to sit under the scale list:
/// the twelve equal-tempered semitones are ticked along the axis, each degree stands where it
/// actually falls, and a degree that misses its tick is drawn in amber. Browsing the list, that is
/// the shape of the scale and the size of the pitch bend it will need, both without a click.
/// </para>
/// <para>
/// A ladder rather than a second wheel because the space available is a wide, short strip - a wheel
/// there would be thumbnail-sized and unreadable, while a line loses only the octave wrap, which the
/// ladder does not need to show. It reads left to right, so an ascending scale ascends.
/// </para>
/// </remarks>
public sealed class DegreeLadder : Control
{
    private DegreeLadderPalette _palette = DegreeLadderPalettes.Dark;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        _palette = DegreeLadderPalettes.For(ActualThemeVariant);
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _palette = DegreeLadderPalettes.For(ActualThemeVariant);

        // Every FormattedText bakes its brush in at construction, so a stale one keeps drawing the
        // previous theme - the same trap PianoRoll's label cache documents.
        _emptyText = null;
        InvalidateVisual();
    }

    private static readonly Typeface Typeface = new("Inter");

    /// <summary>Room either end so a marker at 0 or 1200 cents is not clipped by the control edge.</summary>
    private const double EdgePadding = 10;

    private const double MarkerRadius = 3.5;
    private const double TickLength = 4;

    /// <summary>
    /// A degree this far off the grid is drawn as deviating. Below it the offset is rounding noise
    /// rather than a tuning anyone chose - the same threshold the wheel uses for its whiskers.
    /// </summary>
    private const double DeviationThresholdCents = 2.0;

    private FormattedText? _emptyText;

    public static readonly StyledProperty<Scale?> ScaleProperty =
        AvaloniaProperty.Register<DegreeLadder, Scale?>(nameof(Scale));

    static DegreeLadder() => AffectsRender<DegreeLadder>(ScaleProperty);

    public Scale? Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        DegreeLadderPalette palette = _palette;
        Rect bounds = new(Bounds.Size);

        if (bounds.Width <= EdgePadding * 2 || bounds.Height <= 0)
        {
            return;
        }

        if (Scale is not { } scale)
        {
            DrawEmptyState(context, bounds, palette);
            return;
        }

        // The axis sits low in the control because everything else stands on it: the stems and their
        // markers all rise, so the space above the line is the space that has to be reserved.
        double axisY = bounds.Height - TickLength - 2;
        double left = EdgePadding;
        double span = bounds.Width - (EdgePadding * 2);
        double top = MarkerRadius + 2;

        context.DrawLine(palette.Axis, new Point(left, axisY), new Point(left + span, axisY));

        for (int i = 0; i <= MidiRounding.SemitonesPerOctave; i++)
        {
            double x = left + (i / (double)MidiRounding.SemitonesPerOctave * span);
            context.DrawLine(
                palette.TwelveTetTick, new Point(x, axisY), new Point(x, axisY + TickLength));
        }

        for (int i = 0; i < scale.DegreeCount; i++)
        {
            double cents = scale.DegreeCents[i];
            bool deviates = Math.Abs(scale.DegreeOffsets[i]) >= DeviationThresholdCents;
            double x = left + (cents / MidiRounding.CentsPerOctave * span);

            context.DrawLine(
                deviates ? palette.OffGridStem : palette.Stem,
                new Point(x, axisY),
                new Point(x, top));

            context.DrawEllipse(
                deviates ? palette.OffGrid : palette.OnGrid,
                null,
                new Point(x, top),
                MarkerRadius,
                MarkerRadius);
        }
    }

    private void DrawEmptyState(DrawingContext context, Rect bounds, DegreeLadderPalette palette)
    {
        _emptyText ??= new FormattedText(
            "Choose a scale",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            11,
            palette.Caption);

        context.DrawText(
            _emptyText,
            new Point(
                Math.Max(0, (bounds.Width - _emptyText.Width) / 2.0),
                Math.Max(0, (bounds.Height - _emptyText.Height) / 2.0)));
    }
}
