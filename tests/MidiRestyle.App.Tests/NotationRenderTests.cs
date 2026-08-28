using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using MidiRestyle.App.Controls;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Drives both notation views through a real headless render pass.
/// </summary>
/// <remarks>
/// Every render runs on the single Avalonia thread owned by <see cref="AvaloniaRenderFixture"/>.
/// The geometry tests beside this one check the arithmetic, which is where the interesting bugs
/// live - but they cannot catch a renderer that throws on a rest, on a null spelling, or on a score
/// scrolled past its last measure. Those only surface when something actually draws, and on screen
/// they surface as a blank pane with an exception behind it. Hence a pass over a deliberately nasty
/// score: two staves, a tie across a barline, a triplet, a double accidental, a quarter-tone with a
/// residual, a metre change, and rests of several lengths.
/// </remarks>
public class NotationRenderTests
{
    /// <summary>
    /// Every render runs on this one thread.
    /// </summary>
    /// <remarks>
    /// Two constraints force it. Avalonia's setup may run only once per process, and its objects
    /// have thread affinity - so with xunit running test methods across a pool, a second thread
    /// touching the same control fails with "a different thread owns it". A lock alone does not fix
    /// that: it serialises access without making it the <i>same</i> thread. So the framework is
    /// initialised on one long-lived thread and all drawing is marshalled onto it, which is exactly
    /// what a real UI thread is.
    /// </remarks>
    private static readonly Scale Slendro = new(
        "t.slendro", "Slendro", "Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

    /// <summary>A real gamelan tuning name, and long enough to overflow a narrow pane's header.</summary>
    private static readonly Scale LongNamed = new(
        "t.slendro.long", "Slendro (Kyahi Kanyut Mesem, Mangkunegaran, Surakarta)", "Gamelan",
        "Southeast Asia", [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

    private static NotationEntry Note(
        long start, int letter, int octave, double alter, double residual,
        NoteValue value, int dots = 0, TieState tie = TieState.None, int staff = 1) => new()
        {
            Note = new SpelledNote(letter, octave, alter, residual),
            SoundingPitch = Pitch.FromMidi(60 + letter),
            Duration = new NotatedDuration(value, dots),
            StartTicks = start,
            DurationTicks = 480,
            Staff = staff,
            Tie = tie,
        };

    private static NotationEntry Rest(long start, NoteValue value) => new()
    {
        Duration = new NotatedDuration(value),
        StartTicks = start,
        DurationTicks = 480,
    };

    /// <summary>A score chosen to hit every branch a renderer has.</summary>
    private static NotationScore Sample()
    {
        NotationMeasure first = new()
        {
            Number = 1,
            StartTicks = 0,
            LengthTicks = 1920,
            BeatsPerMeasure = 4,
            BeatUnit = 4,
            TimeSignatureChanged = true,
            Entries =
            [
                Note(0, 0, 4, 0, 0, NoteValue.Quarter),
                Note(480, 2, 4, -0.5, -15, NoteValue.Eighth),      // quarter-tone with a residual
                Note(720, 3, 4, 1, 0, NoteValue.Sixteenth, dots: 1),
                Rest(960, NoteValue.Quarter),
                Note(1440, 6, 5, 2, 0, NoteValue.Half, tie: TieState.Start),
            ],
        };

        NotationMeasure second = new()
        {
            Number = 2,
            StartTicks = 1920,
            LengthTicks = 1440,
            BeatsPerMeasure = 3,
            BeatUnit = 4,
            TimeSignatureChanged = true,
            Entries =
            [
                Note(1920, 6, 5, 2, 0, NoteValue.Half, tie: TieState.Stop),
                Note(2400, 0, 2, -2, 0, NoteValue.ThirtySecond, staff: 2),
                Rest(2640, NoteValue.Eighth),
                new NotationEntry
                {
                    Note = new SpelledNote(4, 3, -1.5, 22),
                    SoundingPitch = Pitch.FromMidi(55),
                    Duration = new NotatedDuration(NoteValue.Eighth, 0, Tuplet.Triplet),
                    StartTicks = 2880,
                    DurationTicks = 160,
                    Staff = 2,
                },
                new NotationEntry
                {
                    Note = new SpelledNote(5, 3, 0.5, 0),
                    SoundingPitch = Pitch.FromMidi(57),
                    Duration = new NotatedDuration(NoteValue.Eighth, 0, Tuplet.Triplet),
                    StartTicks = 3040,
                    DurationTicks = 160,
                    Staff = 2,
                },
                Rest(3200, NoteValue.Whole),
            ],
        };

        return new NotationScore
        {
            Divisions = 480,
            Title = "Fixture",
            ScaleName = "Slendro",
            Parts =
            [
                new NotationPart
                {
                    Id = "P1",
                    Name = "Piano",
                    TrackIndex = 0,
                    Channel = 0,
                    StaffCount = 2,
                    Clefs = [Clef.Treble, Clef.Bass],
                    Measures = [first, second],
                    ProgramNumber = 0,
                },
                new NotationPart
                {
                    Id = "P2",
                    Name = "Bass",
                    TrackIndex = 1,
                    Channel = 1,
                    StaffCount = 1,
                    Clefs = [Clef.Bass],
                    Measures = [first, second],
                },
            ],
        };
    }

    /// <summary>Lays a control out and draws it, returning whatever the render threw, if anything.</summary>
    private static void Draw(Func<Control> makeView, Action<Control>? between = null) =>
        DrawAt(900, 500, makeView, between);

    /// <summary>The same, at a chosen pane size.</summary>
    private static void DrawAt(
        int width, int height, Func<Control> makeView, Action<Control>? between = null) =>
        AvaloniaRenderFixture.Run(() =>
        {
            // The control is built on the render thread too - constructing an Avalonia control off
            // it is the same affinity violation as drawing one.
            Control view = makeView();

            view.Measure(new Size(width, height));
            view.Arrange(new Rect(0, 0, width, height));

            RenderTargetBitmap bitmap = new(new PixelSize(width, height), new Vector(96, 96));

            using var context = bitmap.CreateDrawingContext();
            view.Render(context);

            if (between is not null)
            {
                between(view);
                view.Render(context);
            }
        });

    [Fact]
    public void TheStaffDrawsARichScoreWithoutThrowing() =>
        Draw(() => new StaffView { Score = Sample(), PlayheadTicks = 900, Zoom = 1.4 });

    [Fact]
    public void TheStaffSurvivesBeingScrolledPastItsLastSystem() =>
        Draw(() => new StaffView { Score = Sample() }, v => ((StaffView)v).ScrollY = 99_999);

    [Fact]
    public void TheStaffDrawsNothingRatherThanThrowingWithNoScore() =>
        Draw(() => new StaffView { Score = null });

    [Fact]
    public void TheStaffHidesThePlayheadWhenItIsNegative() =>
        Draw(() => new StaffView { Score = Sample(), PlayheadTicks = -1 });

    [Fact]
    public void TheDegreeViewDrawsWithoutThrowing() =>
        Draw(() => new DegreeView
        {
            Score = Sample(),
            Scale = Slendro,
            Tonic = Pitch.FromMidi(60),
            PlayheadTicks = 900,
        });

    [Fact]
    public void TheDegreeViewSurvivesANullScale() =>
        // Reachable in the real app for a moment after a file loads and before a scale is chosen.
        Draw(() => new DegreeView { Score = Sample(), Scale = null });

    [Fact]
    public void TheDegreeViewDrawsNothingRatherThanThrowingWithNoScore() =>
        Draw(() => new DegreeView { Score = null, Scale = Slendro });

    [Fact]
    public void TheDegreeViewDrawsTheScaleAtRestWithNoPlayhead() =>
        // The wheel has no scroll or zoom of its own - it is one octave, always fully in view - so
        // the axis a zoom sweep used to cover is the playhead instead.
        Draw(() => new DegreeView { Score = Sample(), Scale = Slendro, PlayheadTicks = -1 });

    [Theory]
    [InlineData(0L)]
    [InlineData(700L)]
    [InlineData(2400L)]
    [InlineData(99_999L)]
    public void TheDegreeViewSurvivesEveryPlayheadPosition(long playhead) =>
        Draw(() => new DegreeView { Score = Sample(), Scale = Slendro, PlayheadTicks = playhead });

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(3.0)]
    public void TheStaffSurvivesEveryZoomLevel(double zoom) =>
        Draw(() => new StaffView { Score = Sample(), Zoom = zoom });

    /// <summary>
    /// A scale name too long for the pane must be elided, not clipped mid-word or drawn over the
    /// wheel. The fix lives in <c>DegreeView.DrawHeader</c>; the geometry layer cannot cover it,
    /// because measuring text needs Avalonia.
    /// </summary>
    [Theory]
    [InlineData(140)]
    [InlineData(220)]
    [InlineData(360)]
    [InlineData(900)]
    public void TheWheelElidesAScaleNameTooLongForItsPane(int width) =>
        DrawAt(width, 260, () => new DegreeView
        {
            Score = Sample(),
            Scale = LongNamed,
            Tonic = Pitch.FromMidi(60),
        });

    /// <summary>The elision caches on (scale, width), so a resize must re-fit rather than reuse.</summary>
    [Fact]
    public void TheWheelRefitsItsHeaderWhenThePaneNarrows() =>
        DrawAt(600, 260, () => new DegreeView { Score = Sample(), Scale = LongNamed },
            v => ((DegreeView)v).Arrange(new Rect(0, 0, 180, 260)));
}
