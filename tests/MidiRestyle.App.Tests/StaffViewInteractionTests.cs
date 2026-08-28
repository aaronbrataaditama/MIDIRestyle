using Avalonia;
using Avalonia.Controls;
using MidiRestyle.App.Controls;
using MidiRestyle.Core.Notation;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The staff view's own behaviour: the page height it reports to the host's scrollbar, and the
/// playhead follow the host calls sixty times a second.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic these two rest on is <see cref="StaffPageLayout"/>'s and is tested headlessly
/// beside this file. What is left here is the part that genuinely needs a laid-out control: that
/// the view builds its page from its own <c>Bounds</c> before anything has drawn, that a host
/// asking for <c>ContentHeight</c> gets a real answer rather than the zero that would leave its
/// scrollbar dead, and that <c>FollowPlayhead</c> writes a scroll position inside the page.
/// </para>
/// <para>
/// Every one of them runs on <see cref="AvaloniaRenderFixture"/>'s single thread: Avalonia objects
/// have thread affinity, and constructing a control off that thread is the same violation as
/// drawing one.
/// </para>
/// </remarks>
public class StaffViewInteractionTests
{
    private const double Width = 700;
    private const double Height = 400;

    private static NotationScore Score(int measureCount)
    {
        List<NotationMeasure> measures = [];

        for (int i = 0; i < measureCount; i++)
        {
            long start = i * 1920L;
            List<NotationEntry> entries = [];

            for (int n = 0; n < 4; n++)
            {
                entries.Add(new NotationEntry
                {
                    // Deliberately off the beat: a fixture on exact boundaries is the one input
                    // class that cannot fail.
                    Note = new SpelledNote((n + i) % 7, 4, 0, 0),
                    Duration = new NotatedDuration(NoteValue.Quarter),
                    StartTicks = start + (n * 480) + ((i * 5) + (n * 3)) % 11,
                    DurationTicks = 480,
                });
            }

            measures.Add(new NotationMeasure
            {
                Number = i + 1,
                StartTicks = start,
                LengthTicks = 1920,
                BeatsPerMeasure = 4,
                BeatUnit = 4,
                TimeSignatureChanged = i == 0,
                Entries = entries,
            });
        }

        return new NotationScore
        {
            Divisions = 480,
            Title = "Interaction",
            Parts =
            [
                new NotationPart
                {
                    Id = "P1", Name = "Flute", TrackIndex = 0, Channel = 0,
                    StaffCount = 1, Clefs = [Clef.Treble], Measures = measures,
                },
            ],
        };
    }

    /// <summary>Builds a laid-out view on the render thread and hands it to <paramref name="assert"/>.</summary>
    private static void WithView(
        Action<StaffView> assert, int measures = 40, double width = Width, double height = Height) =>
        AvaloniaRenderFixture.Run(() =>
        {
            StaffView view = new() { Score = Score(measures) };
            view.Measure(new Size(width, height));
            view.Arrange(new Rect(0, 0, width, height));
            assert(view);
        });

    // --- what the host's scrollbar reads ------------------------------------------------------------

    [Fact]
    public void ThePageHeightIsAnsweredBeforeAnythingHasDrawn() =>
        WithView(view =>
        {
            view.ContentHeight.Should().BeGreaterThan(
                Height, "a host sizing a scrollbar on the first pass must not be told zero");
            view.SystemCount.Should().BeGreaterThan(1, "40 bars do not fit on one 700 px system");
            view.MeasureCount.Should().Be(40);
        });

    [Fact]
    public void ANarrowerViewIsATallerPage()
    {
        double wide = 0;
        double narrow = 0;

        WithView(v => wide = v.ContentHeight, width: 1400);
        WithView(v => narrow = v.ContentHeight, width: 500);

        narrow.Should().BeGreaterThan(wide, "fewer bars to the line means more lines");
    }

    [Fact]
    public void AZoomOutsideItsRangeStillGivesARealPage() =>
        WithView(view =>
        {
            foreach (double zoom in new[] { 0.0, -4.0, double.NaN, 1e9 })
            {
                view.Zoom = zoom;

                double height = view.ContentHeight;
                double.IsFinite(height).Should().BeTrue($"zoom {zoom} must not produce an infinity");
                height.Should().BeGreaterThan(0);
            }
        });

    [Fact]
    public void AnEmptyViewHasNoPageAndFollowsNothing() =>
        AvaloniaRenderFixture.Run(() =>
        {
            StaffView view = new() { Score = null };
            view.Measure(new Size(Width, Height));
            view.Arrange(new Rect(0, 0, Width, Height));

            view.ContentHeight.Should().Be(0);
            view.SystemCount.Should().Be(0);
            view.MeasureCount.Should().Be(0);
            view.FollowPlayhead().Should().BeFalse();
        });

    // --- following the playhead ---------------------------------------------------------------------

    [Fact]
    public void FollowingMovesToThePlayheadAndThenLeavesTheReaderAlone() =>
        WithView(view =>
        {
            view.PlayheadTicks = 1920L * 35;

            view.FollowPlayhead().Should().BeTrue("the playhead is far below the first screenful");

            view.ScrollY.Should().BeGreaterThan(0);
            view.ScrollY.Should().BeLessThanOrEqualTo(
                view.ContentHeight - Height, "following never scrolls past the end of the page");

            view.FollowPlayhead().Should().BeFalse(
                "called sixty times a second, a follow that always scrolled would fight every drag");
        });

    [Fact]
    public void FollowingBringsAScrollSetFarPastTheEndBackOntoThePage() =>
        WithView(view =>
        {
            view.ScrollY = 99_999;
            view.PlayheadTicks = 0;

            view.FollowPlayhead().Should().BeTrue();
            view.ScrollY.Should().BeInRange(0, view.ContentHeight - Height);
        });

    [Fact]
    public void FollowingIgnoresAHiddenPlayhead() =>
        WithView(view =>
        {
            view.ScrollY = 250;
            view.PlayheadTicks = -1;

            view.FollowPlayhead().Should().BeFalse("a negative tick means no playhead at all");
            view.ScrollY.Should().Be(250);
        });

    [Fact]
    public void FollowingIgnoresATickPastTheLastBar() =>
        WithView(view =>
        {
            view.ScrollY = 250;
            view.PlayheadTicks = long.MaxValue;

            view.FollowPlayhead().Should().BeFalse();
            view.ScrollY.Should().Be(250);
        });

    /// <summary>
    /// Walked bar by bar, following must settle every time rather than oscillating.
    /// </summary>
    /// <remarks>
    /// This is the shape a real playback takes: the tick advances continuously and the host calls
    /// <c>FollowPlayhead</c> on a timer. A rule that reported movement on every call would make the
    /// page shiver for the whole piece, which no value assertion would ever catch.
    /// </remarks>
    [Fact]
    public void FollowingSettlesAtEveryBarOfThePiece() =>
        WithView(view =>
        {
            for (int measure = 0; measure < 40; measure++)
            {
                view.PlayheadTicks = (measure * 1920L) + 37;

                if (view.FollowPlayhead())
                {
                    view.FollowPlayhead().Should().BeFalse(
                        $"following settled after one move at bar {measure + 1}");
                }

                view.ScrollY.Should().BeInRange(0, view.ContentHeight - Height);
            }
        });

    // --- click-to-seek ------------------------------------------------------------------------------

    /// <summary>
    /// A click is answered in the control's own coordinates, which means through the scroll offset.
    /// </summary>
    /// <remarks>
    /// The arithmetic behind this is <see cref="StaffPageLayout.TryTickAt"/>'s and is tested beside
    /// it. What only a laid-out control can catch is the offset itself: the control is handed a
    /// viewport y and the page is addressed in page y, so a view that forgot to add
    /// <c>ScrollY</c> would answer every click with whatever is at the top of the page - correct on
    /// an unscrolled first screen, and wrong everywhere after that.
    /// </remarks>
    [Fact]
    public void AClickIsReadThroughTheScrollOffset() =>
        WithView(view =>
        {
            Point point = new(Width / 2, Height / 2);

            view.ScrollY = 0;
            view.TryTickAt(point, out long atTop).Should().BeTrue();

            view.ScrollY = view.ContentHeight / 2;
            view.TryTickAt(point, out long scrolled).Should().BeTrue();

            scrolled.Should().BeGreaterThan(
                atTop, "the same point on a scrolled page is later music");
        });

    [Fact]
    public void EveryPointOnThePageAnswersWithATickInsideThePiece() =>
        WithView(view =>
        {
            long last = (40 * 1920L) - 1;

            for (double y = 4; y < Height; y += 37)
            {
                for (double x = 0; x < Width; x += 53)
                {
                    view.TryTickAt(new Point(x, y), out long tick).Should().BeTrue(
                        $"({x}, {y}) is on the page");

                    tick.Should().BeInRange(0, last + 1);
                }
            }
        });

    [Fact]
    public void AViewWithNoScoreHasNoTickToClick() =>
        AvaloniaRenderFixture.Run(() =>
        {
            StaffView view = new() { Score = null };
            view.Measure(new Size(Width, Height));
            view.Arrange(new Rect(0, 0, Width, Height));

            view.TryTickAt(new Point(10, 10), out _).Should().BeFalse();
        });
}
