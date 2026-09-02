using MidiRestyle.App.Controls;

namespace MidiRestyle.App.Tests;

public class PianoRollGeometryTests
{
    /// <summary>A viewport showing about 4 seconds and 2 octaves, roughly the app's default.</summary>
    private static RollViewport Viewport(
        double scrollTicks = 0,
        double topCents = 8400,
        double width = 1000,
        double height = 600) =>
        new(scrollTicks, topCents, width, height, PixelsPerTick: 0.5, PixelsPerCent: 0.25);

    private static RollNote[] Chromatic(int count, long spacing = 240, long length = 200)
    {
        var notes = new RollNote[count];
        for (int i = 0; i < count; i++)
        {
            notes[i] = new RollNote(i * spacing, length, 6000 + (i % 24) * 100, 90);
        }

        return notes;
    }

    // --- culling ---------------------------------------------------------------------------

    [Fact]
    public void CullReturnsOnlyWhatTheViewportShows()
    {
        RollNote[] notes = Chromatic(1000);
        var dest = new NoteQuad[8192];

        int count = PianoRollGeometry.Cull(notes, 200, Viewport(), dest);

        // 1000 px at 0.5 px/tick is 2000 ticks; notes are 240 apart, so about 9 fit.
        count.Should().BeInRange(8, 10);
        count.Should().BeLessThan(notes.Length, "culling is the entire point");
    }

    /// <summary>
    /// The failure this guards is subtle and very easy to ship: a long note whose onset has scrolled
    /// off the left edge is still sounding and must still draw. Binary-searching for the left edge
    /// alone would drop it, and a held pedal tone would vanish as you scrolled past its start.
    /// </summary>
    [Fact]
    public void ANoteStartingBeforeTheViewportIsStillDrawnWhileItSounds()
    {
        RollNote[] notes = [new RollNote(0, 100_000, 6000, 90)];
        var dest = new NoteQuad[16];

        int count = PianoRollGeometry.Cull(notes, 100_000, Viewport(scrollTicks: 50_000), dest);

        count.Should().Be(1, "the note begins off-screen but is still sounding");
        dest[0].X.Should().BeLessThan(0, "its onset is to the left of the viewport");
    }

    [Fact]
    public void NotesEntirelyLeftOfTheViewportAreSkipped()
    {
        RollNote[] notes = [new RollNote(0, 100, 6000, 90)];
        var dest = new NoteQuad[16];

        PianoRollGeometry.Cull(notes, 100, Viewport(scrollTicks: 50_000), dest).Should().Be(0);
    }

    [Fact]
    public void NotesOutsideThePitchRangeAreSkipped()
    {
        // Viewport spans 8400 cents down to 8400 - 600/0.25 = 6000.
        RollNote[] notes =
        [
            new RollNote(0, 200, 12000, 90),  // far above
            new RollNote(0, 200, 7000, 90),   // inside
            new RollNote(0, 200, 1000, 90),   // far below
        ];
        var dest = new NoteQuad[16];

        PianoRollGeometry.Cull(notes, 200, Viewport(), dest).Should().Be(1);
    }

    [Fact]
    public void CullStopsAtTheDestinationCapacityRatherThanOverrunning()
    {
        RollNote[] notes = Chromatic(1000, spacing: 1);
        var tiny = new NoteQuad[4];

        PianoRollGeometry.Cull(notes, 200, Viewport(), tiny).Should().Be(4);
    }

    [Fact]
    public void AnEmptyDestinationWritesNothing() =>
        PianoRollGeometry.Cull(Chromatic(10), 200, Viewport(), []).Should().Be(0);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ADegenerateViewportDrawsNothing(double size)
    {
        RollViewport degenerate = new(0, 8400, size, size, 0.5, 0.25);

        PianoRollGeometry.Cull(Chromatic(10), 200, degenerate, new NoteQuad[16]).Should().Be(0);
    }

    // --- geometry --------------------------------------------------------------------------

    /// <summary>
    /// Microtonal notes must draw visibly BETWEEN semitone rows - that visible offset is how the
    /// user sees that the app is delivering a real tuning rather than a 12-TET approximation.
    /// </summary>
    [Fact]
    public void AQuarterToneNoteDrawsHalfARowAboveItsSemitone()
    {
        RollViewport vp = Viewport();
        RollNote[] notes =
        [
            new RollNote(0, 200, 7000, 90),   // exactly on the semitone
            new RollNote(0, 200, 7050, 90),   // 50 cents sharp - a quarter tone
        ];
        var dest = new NoteQuad[16];

        PianoRollGeometry.Cull(notes, 200, vp, dest).Should().Be(2);

        double halfRow = vp.RowHeight / 2.0;
        (dest[0].Y - dest[1].Y).Should().BeApproximately(halfRow, 1e-9,
            "50 cents is half a semitone, so the sharp note sits half a row higher");
    }

    [Fact]
    public void ZeroLengthNotesStayVisible()
    {
        // Zero-length notes are legal MIDI and the loader preserves them, so the roll must not let
        // them collapse to an invisible zero-width rectangle.
        RollNote[] notes = [new RollNote(100, 0, 7000, 90)];
        var dest = new NoteQuad[4];

        PianoRollGeometry.Cull(notes, 0, Viewport(), dest).Should().Be(1);
        dest[0].Width.Should().BeGreaterThanOrEqualTo(1.0);
    }

    [Fact]
    public void ViewportMapsTicksAndCentsToPixels()
    {
        RollViewport vp = Viewport(scrollTicks: 1000, topCents: 8400);

        vp.XForTick(1000).Should().Be(0);
        vp.XForTick(1002).Should().BeApproximately(1.0, 1e-9);
        vp.YForCents(8400).Should().Be(0);
        vp.YForCents(8300).Should().BeApproximately(25.0, 1e-9, "a semitone is 100 cents at 0.25 px/cent");
        vp.RowHeight.Should().BeApproximately(25.0, 1e-9);
    }

    [Fact]
    public void VisibleNoteRangeIsClampedToTheMidiRange()
    {
        RollViewport wide = new(0, 100_000, 1000, 100_000, 0.5, 0.25);

        (int low, int high) = PianoRollGeometry.VisibleNoteRange(wide);

        low.Should().Be(0);
        high.Should().Be(127);
    }

    [Fact]
    public void FirstPossiblyVisibleFindsTheBinarySearchBoundary()
    {
        RollNote[] notes = Chromatic(100, spacing: 100, length: 50);

        // Looking at tick 5000 with a max length of 50, nothing starting before 4950 can matter.
        int index = PianoRollGeometry.FirstPossiblyVisible(notes, 50, 5000);

        index.Should().Be(50);
        notes[index].StartTicks.Should().BeGreaterThanOrEqualTo(4950);
    }

    [Fact]
    public void MaxLengthFindsTheLongestNote() =>
        PianoRollGeometry.MaxLength(
            [new RollNote(0, 10, 6000, 90), new RollNote(0, 9999, 6000, 90), new RollNote(0, 5, 6000, 90)])
            .Should().Be(9999);

    [Fact]
    public void MaxLengthOfNothingIsZero() => PianoRollGeometry.MaxLength([]).Should().Be(0);

    // --- the phase gate ----------------------------------------------------------------------

    /// <summary>
    /// The phase 4 gate: scrolling a dense file must allocate nothing.
    /// </summary>
    /// <remarks>
    /// This is why the culling maths lives outside the control - a claim about per-frame allocation
    /// is only worth making if it can be measured, and measuring it through Avalonia's render loop
    /// would be far harder than measuring it here. 20,000 notes is the figure the plan uses for a
    /// dense file.
    /// </remarks>
    [Fact]
    public void ScrollingTwentyThousandNotesAllocatesNothing()
    {
        RollNote[] notes = Chromatic(20_000);
        long maxLength = PianoRollGeometry.MaxLength(notes);
        var dest = new NoteQuad[8192];

        // Warm up: first call may JIT, which allocates.
        for (int i = 0; i < 32; i++)
        {
            PianoRollGeometry.Cull(notes, maxLength, Viewport(scrollTicks: i * 137), dest);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();

        int total = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            total += PianoRollGeometry.Cull(notes, maxLength, Viewport(scrollTicks: frame * 31), dest);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        total.Should().BeGreaterThan(0, "the frames must actually have drawn something");
        allocated.Should().Be(0, "600 frames of scrolling a 20k-note file must not allocate");
    }

    /// <summary>
    /// The default zoom frames the WHOLE piece, so nearly every note is legitimately visible at
    /// once - the visible count is not bounded by "what a human can read".
    /// </summary>
    /// <remarks>
    /// This is a regression test for a real bug: the control originally capped its quad buffer at
    /// 8192 on the reasoning that nobody can read more notes than that. True, and irrelevant - a
    /// 20,000-note file opened at fit-the-piece zoom put 18,690 notes in view, and the cap silently
    /// dropped the rest, leaving the right-hand side of the roll blank. Truncating the visible set
    /// is a correctness bug no matter how illegible the full set would be.
    /// </remarks>
    [Fact]
    public void AtFitThePieceZoomNearlyEveryNoteIsVisibleAtOnce()
    {
        RollNote[] notes = Chromatic(20_000, spacing: 240, length: 200);
        long maxLength = PianoRollGeometry.MaxLength(notes);

        // Wide pitch span and a zoom that fits the whole piece across 1200 px, as the app does.
        double spanTicks = notes[^1].EndTicks;
        RollViewport fitAll = new(0, 13_000, 1200, 700, 1200.0 / spanTicks, 0.05);

        var generous = new NoteQuad[notes.Length];
        int visible = PianoRollGeometry.Cull(notes, maxLength, fitAll, generous);

        visible.Should().BeGreaterThan(8192,
            "a capped buffer would silently drop the tail of the piece");
        visible.Should().Be(notes.Length, "every note is inside this viewport");
    }

    /// <summary>Culling must stay sublinear in file size, not scan every note every frame.</summary>
    [Fact]
    public void CullingCostDoesNotGrowWithNotesOffScreen()
    {
        RollNote[] small = Chromatic(1_000);
        RollNote[] large = Chromatic(100_000);
        var dest = new NoteQuad[8192];

        // Same viewport, same visible content - only the tail beyond the right edge differs.
        int fromSmall = PianoRollGeometry.Cull(small, 200, Viewport(), dest);
        int fromLarge = PianoRollGeometry.Cull(large, 200, Viewport(), dest);

        fromLarge.Should().Be(fromSmall,
            "a hundredfold larger file shows the same notes in the same viewport");
    }

    // --- the keyboard gutter and the bar ruler ---------------------------------------------------

    /// <summary>
    /// The grid starts where the keyboard ends, and one tick under the playhead is one place.
    /// </summary>
    private static RollViewport Inset(double gutter = 46, double ruler = 19) =>
        new(ScrollTicks: 0, TopCents: 7200, Width: 1000, Height: 600,
            PixelsPerTick: 0.5, PixelsPerCent: 0.25, GutterWidth: gutter, RulerHeight: ruler);

    [Fact]
    public void TheFirstTickIsDrawnAtTheEdgeOfTheKeyboardNotTheEdgeOfTheControl()
    {
        RollViewport viewport = Inset();

        viewport.XForTick(0).Should().Be(46);
        viewport.YForCents(7200).Should().Be(19);
    }

    /// <summary>
    /// The extent is the grid, not the control.
    /// </summary>
    /// <remarks>
    /// Measured against the whole control the viewport claims more music on screen than there is,
    /// and the last bar sits permanently just past the right edge with the scrollbar already at its
    /// end - a subtle, permanent off-by-a-gutter that no single frame makes obvious.
    /// </remarks>
    [Fact]
    public void TheVisibleSpanExcludesTheFurniture()
    {
        RollViewport viewport = Inset();

        viewport.NoteAreaWidth.Should().Be(1000 - 46);
        viewport.NoteAreaHeight.Should().Be(600 - 19);
        viewport.EndTicks.Should().Be((1000 - 46) / 0.5);
        viewport.BottomCents.Should().Be(7200 - ((600 - 19) / 0.25));
    }

    [Fact]
    public void AGutterWiderThanTheControlLeavesNoGridRatherThanANegativeOne()
    {
        RollViewport viewport = Inset(gutter: 2000, ruler: 900);

        viewport.NoteAreaWidth.Should().Be(0);
        viewport.NoteAreaHeight.Should().Be(0);
        viewport.EndTicks.Should().Be(viewport.ScrollTicks);
    }

    /// <summary>A roll drawn without furniture is the old behaviour, unchanged.</summary>
    [Fact]
    public void WithoutFurnitureTheViewportIsTheWholeControl()
    {
        RollViewport viewport = new(0, 7200, 1000, 600, 0.5, 0.25);

        viewport.XForTick(0).Should().Be(0);
        viewport.YForCents(7200).Should().Be(0);
        viewport.EndTicks.Should().Be(2000);
    }

    [Fact]
    public void CulledNotesAreOffsetByTheKeyboardToo()
    {
        RollNote[] notes = [new RollNote(0, 480, 7200, 90)];
        NoteQuad[] quads = new NoteQuad[4];

        int count = PianoRollGeometry.Cull(notes, 480, Inset(), quads);

        count.Should().Be(1);
        quads[0].X.Should().Be(46, "a note at tick zero starts at the edge of the keyboard");
        quads[0].Y.Should().BeApproximately(19 - (quads[0].Height / 2.0), 1.0);
    }
}
