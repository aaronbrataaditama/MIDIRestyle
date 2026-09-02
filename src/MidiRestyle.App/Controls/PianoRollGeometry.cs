namespace MidiRestyle.App.Controls;

/// <summary>A note flattened for drawing: ticks and cents, nothing else.</summary>
/// <remarks>
/// Deliberately not the domain <c>Note</c>. The roll redraws on every scroll, zoom and settings
/// change, so the render path must touch a flat, sorted, cache-friendly array rather than walking
/// per-track collections and reading computed properties. Cents rather than a note number is what
/// lets a microtonal note draw visibly <em>between</em> two semitone rows, which is the whole point
/// of showing the transform.
/// </remarks>
public readonly record struct RollNote(long StartTicks, long LengthTicks, double Cents, byte Velocity)
{
    public long EndTicks => StartTicks + LengthTicks;
}

/// <summary>A note resolved to pixels, ready to hand to the drawing context.</summary>
public readonly record struct NoteQuad(double X, double Y, double Width, double Height, byte Velocity);

/// <summary>What part of the piece is on screen, and at what scale.</summary>
/// <param name="ScrollTicks">Tick at the left edge.</param>
/// <param name="TopCents">Cents at the top edge. Larger is higher in pitch, so y grows downward.</param>
/// <param name="Width">Viewport width in pixels.</param>
/// <param name="Height">Viewport height in pixels.</param>
/// <param name="PixelsPerTick">Horizontal zoom.</param>
/// <param name="PixelsPerCent">Vertical zoom. A semitone row is 100x this.</param>
/// <param name="GutterWidth">
/// Width of the keyboard column down the left edge. Zero for a roll drawn without one.
/// </param>
/// <param name="RulerHeight">
/// Height of the bar-number strip across the top. Zero for a roll drawn without one.
/// </param>
public readonly record struct RollViewport(
    double ScrollTicks,
    double TopCents,
    double Width,
    double Height,
    double PixelsPerTick,
    double PixelsPerCent,
    double GutterWidth = 0,
    double RulerHeight = 0)
{
    /// <summary>
    /// Width of the notes themselves, once the keyboard has taken its column.
    /// </summary>
    /// <remarks>
    /// Every horizontal question - what tick is at the right edge, how much music is on screen -
    /// is asked of this rather than of <see cref="Width"/>. Measuring the viewport as the whole
    /// control while drawing the notes inset by the gutter puts the last <see cref="GutterWidth"/>
    /// pixels of music off the right-hand edge, which reads as the file being truncated.
    /// </remarks>
    public double NoteAreaWidth => Math.Max(0, Width - GutterWidth);

    /// <summary>Height of the note grid, once the bar ruler has taken its strip.</summary>
    public double NoteAreaHeight => Math.Max(0, Height - RulerHeight);

    /// <summary>Tick at the right edge.</summary>
    public double EndTicks => ScrollTicks + (PixelsPerTick > 0 ? NoteAreaWidth / PixelsPerTick : 0);

    /// <summary>Cents at the bottom edge. Lower than <see cref="TopCents"/>.</summary>
    public double BottomCents =>
        TopCents - (PixelsPerCent > 0 ? NoteAreaHeight / PixelsPerCent : 0);

    /// <summary>Height of one semitone row, in pixels.</summary>
    public double RowHeight => PixelsPerCent * 100.0;

    public double XForTick(double tick) => GutterWidth + ((tick - ScrollTicks) * PixelsPerTick);

    public double YForCents(double cents) => RulerHeight + ((TopCents - cents) * PixelsPerCent);
}

/// <summary>
/// The piano roll's culling and layout maths, kept free of Avalonia so it can be measured.
/// </summary>
/// <remarks>
/// <para>
/// Split out from the control for one reason: the phase gate is "zoom and scroll allocate nothing
/// per frame", and that is only assertable if the hot path is callable from a test without a window,
/// a render loop or a drawing context. Everything here writes into a caller-supplied
/// <see cref="Span{T}"/> and allocates nothing.
/// </para>
/// <para>
/// A dense MIDI file is tens of thousands of notes and the roll redraws on every scroll tick, so
/// culling has to be sublinear in the file and linear only in what is visible. Notes are held sorted
/// by <see cref="RollNote.StartTicks"/>, which makes the left edge a binary search; the only
/// subtlety is that a long note starting off-screen can still be visible, which is what
/// <c>maxLengthTicks</c> accounts for.
/// </para>
/// </remarks>
public static class PianoRollGeometry
{
    /// <summary>
    /// Index of the first note that could possibly intersect the viewport.
    /// </summary>
    /// <remarks>
    /// Binary search for <c>ScrollTicks - maxLengthTicks</c> rather than for <c>ScrollTicks</c>.
    /// Searching for the left edge alone would skip a long note that began before it and is still
    /// sounding - a held pedal tone would vanish the moment its onset scrolled away, which looks
    /// like a rendering bug and is very easy to ship.
    /// </remarks>
    public static int FirstPossiblyVisible(
        ReadOnlySpan<RollNote> notesByStart,
        long maxLengthTicks,
        double scrollTicks)
    {
        double cutoff = scrollTicks - maxLengthTicks;

        int lo = 0;
        int hi = notesByStart.Length;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (notesByStart[mid].StartTicks < cutoff)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the visible notes as pixel rectangles.
    /// </summary>
    /// <returns>
    /// How many quads were written. If the viewport holds more notes than
    /// <paramref name="destination"/> can take, the excess is dropped and the count is capped -
    /// the caller is expected to grow its buffer and redraw rather than to render a partial frame
    /// silently.
    /// </returns>
    public static int Cull(
        ReadOnlySpan<RollNote> notesByStart,
        long maxLengthTicks,
        in RollViewport viewport,
        Span<NoteQuad> destination)
    {
        if (destination.IsEmpty || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return 0;
        }

        double leftTick = viewport.ScrollTicks;
        double rightTick = viewport.EndTicks;
        double topCents = viewport.TopCents;
        double bottomCents = viewport.BottomCents;

        // Notes are drawn one semitone row tall, centred on their true cents. Half a row of slack
        // on each edge keeps a note that is only partly on screen from popping in and out.
        double halfRow = viewport.RowHeight / 2.0;
        double slackCents = viewport.PixelsPerCent > 0 ? halfRow / viewport.PixelsPerCent : 0;

        int written = 0;

        for (int i = FirstPossiblyVisible(notesByStart, maxLengthTicks, leftTick);
             i < notesByStart.Length;
             i++)
        {
            RollNote note = notesByStart[i];

            // Sorted by start, so once onsets pass the right edge nothing later can be visible.
            if (note.StartTicks > rightTick)
            {
                break;
            }

            if (note.EndTicks < leftTick)
            {
                continue;
            }

            if (note.Cents > topCents + slackCents || note.Cents < bottomCents - slackCents)
            {
                continue;
            }

            double x = viewport.XForTick(note.StartTicks);
            double width = note.LengthTicks * viewport.PixelsPerTick;

            // Zero-length notes are legal MIDI and must stay visible, so floor the drawn width
            // rather than letting them collapse to nothing.
            if (width < 1.0)
            {
                width = 1.0;
            }

            destination[written++] = new NoteQuad(
                x,
                viewport.YForCents(note.Cents) - halfRow,
                width,
                Math.Max(1.0, viewport.RowHeight - 1.0),
                note.Velocity);

            if (written == destination.Length)
            {
                break;
            }
        }

        return written;
    }

    /// <summary>
    /// The lowest and highest semitone row touching the viewport, for drawing the grid and keyboard.
    /// </summary>
    public static (int LowNote, int HighNote) VisibleNoteRange(in RollViewport viewport)
    {
        int low = (int)Math.Floor(viewport.BottomCents / 100.0);
        int high = (int)Math.Ceiling(viewport.TopCents / 100.0);
        return (Math.Max(0, low), Math.Min(127, high));
    }

    /// <summary>Longest note in the set, which culling needs to avoid dropping held notes.</summary>
    public static long MaxLength(ReadOnlySpan<RollNote> notes)
    {
        long max = 0;
        for (int i = 0; i < notes.Length; i++)
        {
            if (notes[i].LengthTicks > max)
            {
                max = notes[i].LengthTicks;
            }
        }

        return max;
    }
}
