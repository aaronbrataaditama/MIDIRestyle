using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Controls;

/// <summary>A point in control space, kept as a plain pair so the geometry stays Avalonia-free.</summary>
public readonly record struct WheelPoint(double X, double Y);

/// <summary>
/// One sounding span on the wheel: an absolute pitch in cents, and the ticks it occupies.
/// </summary>
/// <remarks>
/// Deliberately holds cents rather than a <see cref="DegreeReading"/>. The reading depends on the
/// scale and the tonic, both of which change while the score does not - the scale list is arrow-key
/// browsable - so baking a reading into the index would invalidate it on every keystroke. Reading a
/// handful of sounding notes per frame is free; rebuilding a 20,000-entry index per frame is not.
/// </remarks>
public readonly record struct WheelNote(long StartTicks, long EndTicks, double Cents);

/// <summary>
/// Where the wheel sits inside the control, and the radii everything on it is placed at.
/// </summary>
/// <remarks>
/// All the fractions are of <see cref="Radius"/>, so the whole control scales with the pane and
/// nothing has to be re-tuned when the splitter moves.
/// </remarks>
public readonly record struct WheelLayout(double CenterX, double CenterY, double Radius)
{
    /// <summary>The scale ring itself - degree markers, 12-TET ticks and deviation arcs.</summary>
    public double RingRadius => Radius * DegreeGeometry.RingRadiusFraction;

    /// <summary>Where a degree's number sits: on its own spoke, just inside the ring.</summary>
    public double NumberRadius => Radius * DegreeGeometry.NumberRadiusFraction;

    /// <summary>Where the twelve equal-tempered note names sit, just outside the ring.</summary>
    public double LetterRadius => Radius * DegreeGeometry.LetterRadiusFraction;

    /// <summary>Where a degree's cents reading sits, outside the note names.</summary>
    public double CentsRadius => Radius * DegreeGeometry.CentsRadiusFraction;

    /// <summary>Radius kept clear in the middle, so a spoke never runs under the centre readout.</summary>
    public double HubRadius => Radius * DegreeGeometry.HubFraction;

    /// <summary>False when the pane is too small to draw anything legible in.</summary>
    public bool IsUsable => Radius >= DegreeGeometry.MinimumUsableRadius;
}

/// <summary>
/// Pure layout maths for <see cref="DegreeView"/>'s scale wheel: cents to angle, angle to point,
/// the radii everything is placed at, the twelve reference names, and which notes sound at a tick.
/// </summary>
/// <remarks>
/// <para>
/// The wheel is one octave: 360 degrees of arc is exactly 1200 cents, degree 1 at twelve o'clock,
/// angles increasing clockwise. Every scale degree is placed at its <em>true</em> cents angle, never
/// at an evenly-spaced slot - that single decision is the point of the control. Slendro's
/// <c>[0, 240, 480, 720, 960]</c> comes out as five almost-even markers; Maqam Rast's
/// <c>[0, 200, 350, 500, 700, 900, 1050]</c> comes out visibly uneven, its neutral degrees sitting
/// between the 12-TET reference ticks. No other view in the app shows that.
/// </para>
/// <para>
/// Kept free of Avalonia entirely, the same split <c>PianoRollGeometry</c> uses - so the layout
/// rules are testable headlessly, without a window or a running <see cref="Avalonia.Application"/>.
/// <see cref="DegreeView"/> itself holds only drawing code.
/// </para>
/// </remarks>
public static class DegreeGeometry
{
    // --- proportions ---------------------------------------------------------------------------

    /// <summary>Degrees of arc in one full turn, which is one octave.</summary>
    public const double DegreesPerTurn = 360.0;

    public const double HubFraction = 0.17;
    public const double NumberRadiusFraction = 0.665;
    public const double RingRadiusFraction = 0.76;
    public const double LetterRadiusFraction = 0.865;
    public const double CentsRadiusFraction = 0.965;

    /// <summary>Below this the wheel is not worth drawing - the ring would be thinner than its own markers.</summary>
    public const double MinimumUsableRadius = 30.0;

    /// <summary>How many 12-TET reference ticks the ring carries: one per equal-tempered semitone.</summary>
    public const int TwelveTetTickCount = MidiRounding.SemitonesPerOctave;

    // --- cents to angle ------------------------------------------------------------------------

    /// <summary>
    /// The clock angle of a cents value, in degrees: 0 at twelve o'clock, increasing clockwise, so
    /// 600 cents is straight down and 1200 wraps back to the top.
    /// </summary>
    /// <remarks>
    /// Wraps into a single octave first, using floor-style arithmetic rather than <c>%</c> - C#
    /// keeps the sign of the dividend, so a note below the tonic (routine in any bass line) would
    /// come back negative and place itself anticlockwise of the top instead of just under it.
    /// </remarks>
    public static double AngleDegreesAt(double cents)
    {
        double wrapped = cents - (Math.Floor(cents / MidiRounding.CentsPerOctave) * MidiRounding.CentsPerOctave);

        // Floor arithmetic on a value a hair under a whole octave can land exactly on 1200 after the
        // subtraction; normalise it back to the top rather than reporting a full turn.
        if (wrapped >= MidiRounding.CentsPerOctave)
        {
            wrapped -= MidiRounding.CentsPerOctave;
        }

        return wrapped / MidiRounding.CentsPerOctave * DegreesPerTurn;
    }

    /// <summary>The point at a given clock angle and radius, measured from twelve o'clock clockwise.</summary>
    /// <remarks>
    /// Screen Y grows downward, which is what makes a clockwise sweep the natural one here: rotating
    /// the standard mathematical basis by a quarter turn gives <c>(sin, -cos)</c>, and no sign flip
    /// is needed anywhere else.
    /// </remarks>
    public static WheelPoint PointAtAngle(in WheelLayout layout, double angleDegrees, double radius)
    {
        double radians = angleDegrees / DegreesPerTurn * 2.0 * Math.PI;
        return new WheelPoint(
            layout.CenterX + (Math.Sin(radians) * radius),
            layout.CenterY - (Math.Cos(radians) * radius));
    }

    /// <summary>The point a cents value maps to at a given radius.</summary>
    public static WheelPoint PointAtCents(in WheelLayout layout, double cents, double radius) =>
        PointAtAngle(layout, AngleDegreesAt(cents), radius);

    /// <summary>
    /// The nearest 12-TET semitone to a cents value, in cents - the reference tick a degree's
    /// deviation whisker is drawn back to.
    /// </summary>
    public static double NearestTwelveTetCents(double cents) => MidiRounding.ToNearestSemitoneCents(cents);

    // --- the 12-TET reference names -----------------------------------------------------------

    /// <summary>
    /// The twelve semitone names, sharp-spelled, indexed by pitch class.
    /// </summary>
    /// <remarks>
    /// Sharps throughout, deliberately. These name the equal-tempered <em>grid</em> the scale is read
    /// against, not the scale's own degrees - those carry their own spelling in
    /// <c>Scale.Spelling</c>, which knows whether a maqam wants D flat or C sharp. A reference grid
    /// that respelled itself per scale would be a second, disagreeing opinion about the same twelve
    /// pitches.
    /// </remarks>
    public static readonly string[] PitchClassNames =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>
    /// The note name at one of the wheel's twelve reference ticks.
    /// </summary>
    /// <remarks>
    /// The wheel's twelve o'clock is the tonic, not C, so tick zero is whatever the tonic is and the
    /// names run round from there. Positive modulo, because a tonic pitch class plus a tick index
    /// wraps past B and C# minus a semitone is B - both routine, and both negative under C#'s <c>%</c>.
    /// </remarks>
    public static string NameAtTick(int tonicPitchClass, int tickIndex)
    {
        int pitchClass = tonicPitchClass + tickIndex;
        return PitchClassNames[
            (((pitchClass % MidiRounding.SemitonesPerOctave) + MidiRounding.SemitonesPerOctave)
                % MidiRounding.SemitonesPerOctave)];
    }

    // --- the wheel's place in the control --------------------------------------------------------

    /// <summary>
    /// Centres the largest wheel that fits between the header at the top and the caption at the
    /// bottom. Returns a layout whose <see cref="WheelLayout.IsUsable"/> is false when the pane is
    /// too small, rather than a negative radius the caller has to remember to check for.
    /// </summary>
    public static WheelLayout LayoutFor(
        double width, double height, double topInset, double bottomInset, double padding)
    {
        double usableTop = topInset + padding;
        double usableHeight = height - topInset - bottomInset - (padding * 2);
        double usableWidth = width - (padding * 2);

        return new WheelLayout(
            width / 2.0,
            usableTop + (usableHeight / 2.0),
            Math.Min(usableWidth, usableHeight) / 2.0);
    }
}

/// <summary>
/// Every sounding span in a score, indexed so "what is sounding at this tick" is a binary search
/// rather than a walk over the whole file.
/// </summary>
/// <remarks>
/// <para>
/// Built once per <see cref="NotationScore"/> and reused for every frame; a 20,000-note file is
/// scanned once, not sixty times a second. The lookup mirrors <c>PianoRollGeometry.Cull</c>'s trick:
/// the notes are sorted by start tick, so the search only has to walk back as far as the longest
/// note in the file could reach.
/// </para>
/// <para>
/// A tied continuation counts as sounding. A note split across a barline is one note being held, so
/// the wheel keeps its degree lit right through the barline rather than blinking at it.
/// </para>
/// </remarks>
public sealed class DegreeWheelIndex
{
    /// <summary>The index of a score with nothing in it - or of no score at all.</summary>
    public static readonly DegreeWheelIndex Empty = new([], 0);

    private readonly WheelNote[] _notes;
    private readonly long _maxDurationTicks;

    private DegreeWheelIndex(WheelNote[] notes, long maxDurationTicks)
    {
        _notes = notes;
        _maxDurationTicks = maxDurationTicks;
    }

    /// <summary>How many sounding spans the score contributed, across every part.</summary>
    public int Count => _notes.Length;

    /// <summary>The longest single span in the file, which bounds how far a lookup has to walk back.</summary>
    public long MaxDurationTicks => _maxDurationTicks;

    /// <summary>Indexes a score. A null or empty score gives <see cref="Empty"/> rather than throwing.</summary>
    public static DegreeWheelIndex Build(NotationScore? score)
    {
        if (score is null || score.Parts.Count == 0)
        {
            return Empty;
        }

        List<WheelNote> notes = [];
        long maxDuration = 0;

        foreach (NotationPart part in score.Parts)
        {
            foreach (NotationMeasure measure in part.Measures)
            {
                foreach (NotationEntry entry in measure.Entries)
                {
                    if (entry.IsRest || entry.SoundingPitch is not { } pitch || entry.DurationTicks <= 0)
                    {
                        continue;
                    }

                    notes.Add(new WheelNote(entry.StartTicks, entry.EndTicks, pitch.Cents));

                    if (entry.DurationTicks > maxDuration)
                    {
                        maxDuration = entry.DurationTicks;
                    }
                }
            }
        }

        if (notes.Count == 0)
        {
            return Empty;
        }

        WheelNote[] sortedNotes = [.. notes];

        // Parts are concatenated, so the merged list restarts at tick zero on every part boundary.
        // The search below assumes a single ascending run.
        Array.Sort(sortedNotes, static (a, b) => a.StartTicks.CompareTo(b.StartTicks));

        return new DegreeWheelIndex(sortedNotes, maxDuration);
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with the notes sounding at <paramref name="tick"/>, in
    /// ascending start order, and returns how many were written.
    /// </summary>
    /// <remarks>
    /// A note sounds over <c>[StartTicks, EndTicks)</c>: half-open, so a note ending exactly where
    /// the next begins does not light both. Writes at most <c>buffer.Length</c> - a chord of forty
    /// notes is drawn as the first however-many the caller has room for, never an overrun.
    /// </remarks>
    public int Sounding(long tick, Span<WheelNote> buffer)
    {
        if (_notes.Length == 0 || buffer.Length == 0 || tick < 0)
        {
            return 0;
        }

        int upper = FirstStartingAfter(_notes, tick);
        long floor = tick - _maxDurationTicks;
        int count = 0;

        for (int i = upper - 1; i >= 0; i--)
        {
            if (_notes[i].StartTicks < floor)
            {
                break;
            }

            if (_notes[i].EndTicks > tick && count < buffer.Length)
            {
                buffer[count++] = _notes[i];
            }
        }

        buffer[..count].Reverse();
        return count;
    }

    /// <summary>Smallest index whose start tick is strictly past <paramref name="tick"/>, or the count.</summary>
    private static int FirstStartingAfter(WheelNote[] notes, long tick)
    {
        int low = 0;
        int high = notes.Length;

        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (notes[mid].StartTicks <= tick)
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
}
