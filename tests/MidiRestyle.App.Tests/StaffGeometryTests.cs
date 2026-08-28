using MidiRestyle.App.Controls;
using MidiRestyle.Core.Notation;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Tests for the staff view's layout arithmetic.
/// </summary>
/// <remarks>
/// Pure maths, so no headless Avalonia fixture is needed - which is the entire reason
/// <see cref="StaffGeometry"/> exists as a separate type from <c>StaffView</c>. Nothing here
/// constructs a control, a drawing context or a window.
/// <para>
/// Staff placement is worth testing rather than eyeballing because its failures are plausible: an
/// off-by-one in a clef's reference line transposes the whole score by a third and still looks like
/// music.
/// </para>
/// </remarks>
public class StaffGeometryTests
{
    /// <summary>Zoom 1, so a staff space is 8 px and one diatonic step is 4 px.</summary>
    private static readonly StaffMetrics Metrics = StaffMetrics.ForZoom(1.0);

    /// <summary>An arbitrary non-zero staff top, so a bug that ignores it cannot pass.</summary>
    private const double StaffTop = 100.0;

    private const double Space = StaffMetrics.BaseStaffSpace;
    private const double Step = Space / 2;

    /// <summary>Staff position for a scientific-notation letter and octave, as the model computes it.</summary>
    private static int Index(int letter, int octave) => new SpelledNote(letter, octave, 0, 0).DiatonicIndex;

    private static double Y(int letter, int octave, Clef clef) =>
        StaffGeometry.YForDiatonicIndex(Index(letter, octave), clef, StaffTop, Metrics);

    // Letter indices, 0..6 = C..B.
    private const int C = 0;
    private const int D = 1;
    private const int E = 2;
    private const int F = 3;
    private const int G = 4;
    private const int A = 5;
    private const int B = 6;

    // --- the model's own assumption ---------------------------------------------------------------

    /// <summary>
    /// The whole vertical layout keys off <see cref="SpelledNote.DiatonicIndex"/> being
    /// <c>octave * 7 + letter</c>. If that ever changes, every expectation below is wrong, so it is
    /// asserted once rather than assumed silently.
    /// </summary>
    [Fact]
    public void DiatonicIndexIsOctaveTimesSevenPlusLetter()
    {
        Index(E, 4).Should().Be(30, "E4 is the treble bottom line");
        Index(F, 5).Should().Be(38, "F5 is the treble top line");
        Index(G, 2).Should().Be(18, "G2 is the bass bottom line");
        Index(A, 3).Should().Be(26, "A3 is the bass top line");
    }

    // --- notes on lines -----------------------------------------------------------------------------

    [Fact]
    public void TrebleBottomLineIsTheFifthLine()
    {
        Y(E, 4, Clef.Treble).Should().Be(StaffTop + (4 * Space));
    }

    [Fact]
    public void TrebleTopLineIsTheStaffTop()
    {
        Y(F, 5, Clef.Treble).Should().Be(StaffTop);
    }

    [Fact]
    public void TrebleMiddleLineIsTwoSpacesDown()
    {
        Y(B, 4, Clef.Treble).Should().Be(StaffTop + (2 * Space));
        StaffGeometry.MiddleLineIndex(Clef.Treble).Should().Be(Index(B, 4));
    }

    [Fact]
    public void BassLinesSitOnTheirOwnReferencePitches()
    {
        Y(G, 2, Clef.Bass).Should().Be(StaffTop + (4 * Space), "G2 is the bass bottom line");
        Y(A, 3, Clef.Bass).Should().Be(StaffTop, "A3 is the bass top line");
        Y(D, 3, Clef.Bass).Should().Be(StaffTop + (2 * Space), "D3 is the bass middle line");
    }

    // --- notes in spaces ----------------------------------------------------------------------------

    /// <summary>The four treble spaces spell F-A-C-E from the bottom, which is the point of the mnemonic.</summary>
    [Fact]
    public void TrebleSpacesFallHalfwayBetweenTheirLines()
    {
        Y(F, 4, Clef.Treble).Should().Be(StaffTop + (3.5 * Space));
        Y(A, 4, Clef.Treble).Should().Be(StaffTop + (2.5 * Space));
        Y(C, 5, Clef.Treble).Should().Be(StaffTop + (1.5 * Space));
        Y(E, 5, Clef.Treble).Should().Be(StaffTop + (0.5 * Space));
    }

    [Fact]
    public void LinesAndSpacesAreDistinguishedForDotPlacement()
    {
        StaffGeometry.IsOnLine(Index(B, 4), Clef.Treble).Should().BeTrue("B4 is the middle line");
        StaffGeometry.IsOnLine(Index(C, 5), Clef.Treble).Should().BeFalse("C5 is a space");
        StaffGeometry.IsOnLine(Index(D, 3), Clef.Bass).Should().BeTrue("D3 is the bass middle line");
    }

    /// <summary>
    /// Accidentals do not move a note: C-sharp, C and C-flat all sit on the same line. Working in
    /// MIDI note numbers instead of staff positions is the classic way to get this wrong.
    /// </summary>
    [Fact]
    public void AlterationDoesNotChangeStaffPosition()
    {
        SpelledNote natural = new(C, 5, 0, 0);
        SpelledNote sharp = new(C, 5, 1, 0);
        SpelledNote flat = new(C, 5, -1, 0);

        double y = StaffGeometry.YForDiatonicIndex(natural.DiatonicIndex, Clef.Treble, StaffTop, Metrics);

        StaffGeometry.YForDiatonicIndex(sharp.DiatonicIndex, Clef.Treble, StaffTop, Metrics).Should().Be(y);
        StaffGeometry.YForDiatonicIndex(flat.DiatonicIndex, Clef.Treble, StaffTop, Metrics).Should().Be(y);
    }

    // --- ledger lines --------------------------------------------------------------------------------

    [Fact]
    public void MiddleCNeedsOneLedgerLineBelowTheTreble()
    {
        int middleC = Index(C, 4);

        StaffGeometry.LedgerLinesBelow(middleC, Clef.Treble).Should().Be(1);
        StaffGeometry.LedgerLinesAbove(middleC, Clef.Treble).Should().Be(0);
        StaffGeometry.YForDiatonicIndex(middleC, Clef.Treble, StaffTop, Metrics)
            .Should().Be(StaffTop + (5 * Space), "one space below the bottom line");
    }

    [Fact]
    public void MiddleCNeedsOneLedgerLineAboveTheBass()
    {
        int middleC = Index(C, 4);

        StaffGeometry.LedgerLinesAbove(middleC, Clef.Bass).Should().Be(1);
        StaffGeometry.LedgerLinesBelow(middleC, Clef.Bass).Should().Be(0);
    }

    /// <summary>
    /// A note in the space immediately outside the stave needs no ledger line - it is only two steps
    /// from the last staff line, and ledger lines continue the stave's own spacing. Rounding this up
    /// instead of down draws a line through every high note.
    /// </summary>
    [Fact]
    public void TheSpaceJustOutsideTheStaveNeedsNoLedgerLine()
    {
        StaffGeometry.LedgerLinesAbove(Index(G, 5), Clef.Treble).Should().Be(0, "G5 sits above the top line");
        StaffGeometry.LedgerLinesBelow(Index(D, 4), Clef.Treble).Should().Be(0, "D4 sits below the bottom line");
    }

    [Fact]
    public void LedgerLinesAccumulateWithDistance()
    {
        StaffGeometry.LedgerLinesAbove(Index(A, 5), Clef.Treble).Should().Be(1);
        StaffGeometry.LedgerLinesAbove(Index(C, 6), Clef.Treble).Should().Be(2);
        StaffGeometry.LedgerLinesAbove(Index(E, 6), Clef.Treble).Should().Be(3);

        StaffGeometry.LedgerLinesBelow(Index(C, 4), Clef.Treble).Should().Be(1);
        StaffGeometry.LedgerLinesBelow(Index(A, 3), Clef.Treble).Should().Be(2);
        StaffGeometry.LedgerLinesBelow(Index(F, 3), Clef.Treble).Should().Be(3);
    }

    [Fact]
    public void LedgerLinePositionsContinueTheStaveSpacing()
    {
        double first = StaffGeometry.YForDiatonicIndex(
            StaffGeometry.LedgerIndexBelow(Clef.Treble, 1), Clef.Treble, StaffTop, Metrics);
        double second = StaffGeometry.YForDiatonicIndex(
            StaffGeometry.LedgerIndexBelow(Clef.Treble, 2), Clef.Treble, StaffTop, Metrics);

        first.Should().Be(StaffTop + (5 * Space));
        (second - first).Should().Be(Space, "ledger lines are one staff space apart, like the stave itself");
    }

    // --- stems ----------------------------------------------------------------------------------------

    /// <summary>
    /// The flip is at the middle line, and the note <em>on</em> the middle line stems down - the
    /// near-universal engraving default for the ambiguous case.
    /// </summary>
    [Fact]
    public void StemDirectionFlipsAtTheMiddleLine()
    {
        StaffGeometry.StemDirectionFor(Index(A, 4), Clef.Treble).Should().Be(StemDirection.Up,
            "A4 is below the middle line");
        StaffGeometry.StemDirectionFor(Index(B, 4), Clef.Treble).Should().Be(StemDirection.Down,
            "a note on the middle line takes a down stem");
        StaffGeometry.StemDirectionFor(Index(C, 5), Clef.Treble).Should().Be(StemDirection.Down,
            "C5 is above the middle line");
    }

    [Fact]
    public void StemDirectionUsesTheClefsOwnMiddleLine()
    {
        int d3 = Index(D, 3);

        StaffGeometry.StemDirectionFor(d3, Clef.Bass).Should().Be(StemDirection.Down,
            "D3 is the bass middle line");
        StaffGeometry.StemDirectionFor(d3, Clef.Treble).Should().Be(StemDirection.Up,
            "the same pitch is far below the treble middle line");
    }

    [Fact]
    public void ANoteNearTheMiddleLineGetsTheNominalStemLength()
    {
        int e4 = Index(E, 4);
        double head = StaffGeometry.YForDiatonicIndex(e4, Clef.Treble, StaffTop, Metrics);

        double end = StaffGeometry.StemEndY(e4, Clef.Treble, StaffTop, Metrics, StemDirection.Up);

        (head - end).Should().Be(Space * StaffGeometry.NominalStemSpaces);
    }

    /// <summary>
    /// A note far outside the stave gets a longer stem so it still reaches the middle line; a
    /// fixed-length stem would leave a run of ledger notes with stems dangling clear of the stave.
    /// </summary>
    [Fact]
    public void AFarLedgerNoteGetsAStemReachingTheMiddleLine()
    {
        int c3 = Index(C, 3);
        double middleLineY = StaffGeometry.YForStaffLine(2, StaffTop, Metrics);

        double end = StaffGeometry.StemEndY(c3, Clef.Treble, StaffTop, Metrics, StemDirection.Up);

        end.Should().Be(middleLineY);
        double head = StaffGeometry.YForDiatonicIndex(c3, Clef.Treble, StaffTop, Metrics);
        (head - end).Should().BeGreaterThan(Space * StaffGeometry.NominalStemSpaces);
    }

    // --- accidentals in force --------------------------------------------------------------------------

    [Fact]
    public void AnUnalteredNoteNeedsNoAccidental()
    {
        MeasureAccidentals state = new();

        state.NeedsAccidental(new SpelledNote(C, 4, 0, 0)).Should().BeFalse(
            "the score carries no key signature, so a measure starts with everything natural");
    }

    [Fact]
    public void TheSameAlteredNoteTwiceInAMeasureDrawsOneAccidental()
    {
        MeasureAccidentals state = new();
        SpelledNote fSharp4 = new(F, 4, 1, 0);

        state.NeedsAccidental(fSharp4).Should().BeTrue("the first F sharp needs its sign");
        state.NeedsAccidental(fSharp4).Should().BeFalse("the sign is still in force");
        state.NeedsAccidental(fSharp4).Should().BeFalse("and stays in force for the rest of the measure");
    }

    /// <summary>
    /// The rule is per letter <em>and</em> octave, not per letter. F-sharp 4 does not cancel the sign
    /// on F-sharp 5; a renderer that keyed on the letter alone would silently drop it.
    /// </summary>
    [Fact]
    public void ADifferentOctaveNeedsItsOwnAccidental()
    {
        MeasureAccidentals state = new();

        state.NeedsAccidental(new SpelledNote(F, 4, 1, 0)).Should().BeTrue();
        state.NeedsAccidental(new SpelledNote(F, 5, 1, 0)).Should().BeTrue("a different octave is a different position");
        state.NeedsAccidental(new SpelledNote(F, 5, 1, 0)).Should().BeFalse();
        state.NeedsAccidental(new SpelledNote(F, 4, 1, 0)).Should().BeFalse("the lower F sharp is still in force");
    }

    [Fact]
    public void ADifferentLetterNeedsItsOwnAccidental()
    {
        MeasureAccidentals state = new();

        state.NeedsAccidental(new SpelledNote(F, 4, 1, 0)).Should().BeTrue();
        state.NeedsAccidental(new SpelledNote(C, 4, 1, 0)).Should().BeTrue("C sharp is a different position");
    }

    /// <summary>Cancelling a sharp needs a natural sign, which is the same difference test running backwards.</summary>
    [Fact]
    public void ReturningToNaturalAfterASharpNeedsANaturalSign()
    {
        MeasureAccidentals state = new();

        state.NeedsAccidental(new SpelledNote(F, 4, 1, 0)).Should().BeTrue();
        state.NeedsAccidental(new SpelledNote(F, 4, 0, 0)).Should().BeTrue("a natural must cancel the sharp");
        state.NeedsAccidental(new SpelledNote(F, 4, 0, 0)).Should().BeFalse("the natural is now in force");
    }

    [Fact]
    public void ChangingTheAlterationWithinAMeasureNeedsTheNewSign()
    {
        MeasureAccidentals state = new();

        state.NeedsAccidental(new SpelledNote(E, 4, -1, 0)).Should().BeTrue("E flat");
        state.NeedsAccidental(new SpelledNote(E, 4, -0.5, 0)).Should().BeTrue("E half-flat is a different sign");
        state.NeedsAccidental(new SpelledNote(E, 4, -0.5, 0)).Should().BeFalse();
    }

    [Fact]
    public void TheNextMeasureResetsEverything()
    {
        MeasureAccidentals state = new();
        SpelledNote fSharp4 = new(F, 4, 1, 0);

        state.NeedsAccidental(fSharp4).Should().BeTrue();
        state.NeedsAccidental(fSharp4).Should().BeFalse();

        state.Reset();

        state.NeedsAccidental(fSharp4).Should().BeTrue("a barline cancels every accidental in force");
    }

    // --- measure layout and culling ----------------------------------------------------------------------

    private const int Divisions = 480;

    /// <summary>A run of plain 4/4 measures, each 1920 ticks and so 168 px wide at zoom 1.</summary>
    private static NotationMeasure[] CommonTimeMeasures(int count)
    {
        NotationMeasure[] measures = new NotationMeasure[count];
        for (int i = 0; i < count; i++)
        {
            measures[i] = new NotationMeasure
            {
                Number = i + 1,
                StartTicks = i * 1920L,
                LengthTicks = 1920,
                BeatsPerMeasure = 4,
                BeatUnit = 4,
                Entries = [],
            };
        }

        return measures;
    }

    private const double CommonTimeWidth = 4 * StaffMetrics.BaseQuarterWidth;

    /// <summary>
    /// A pickup bar or a 1/8 bar must still be wide enough to hold notes, so the proportional width
    /// has a floor.
    /// </summary>
    /// <summary>
    /// A fractional scroll cannot be a multiplication, because measures differ in width: 2.5 means
    /// half way through measure 2, and how many pixels that is depends on measure 2.
    /// </summary>
    /// <summary>
    /// The first visible measure is the one <em>containing</em> the left edge, not the first one
    /// starting after it - otherwise the partly-scrolled measure at the left of the screen is blank.
    /// </summary>
    /// <summary>
    /// A wider zoom must not change which bar you are looking at - that is the reason the scroll
    /// position is in measures rather than pixels.
    /// </summary>
    /// <summary>
    /// The AEU comma case. MusicXML cannot carry a residual, so the on-screen score is the only place
    /// it can be shown - but showing a two-cent rounding artefact on every note would be noise.
    /// </summary>
    [Fact]
    public void OnlyAnAudibleResidualIsAnnotated()
    {
        StaffGeometry.ShouldShowResidual(0).Should().BeFalse();
        StaffGeometry.ShouldShowResidual(2.5).Should().BeFalse("below the threshold");
        StaffGeometry.ShouldShowResidual(-15).Should().BeTrue();
        StaffGeometry.ShouldShowResidual(15).Should().BeTrue();
        StaffGeometry.ShouldShowResidual(double.NaN).Should().BeFalse();
    }

    // --- beams ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A beam has to join stem <em>ends</em>, so stems on opposite sides of their noteheads have no
    /// common end to join and the group cannot be drawn at all. Both pairs below are mixed when each
    /// note is asked on its own, which is exactly the case the group rule exists for.
    /// </summary>
    [Fact]
    public void AGroupTakesOneStemDirectionEvenWhenItsNotesDisagree()
    {
        int c4 = Index(C, 4);
        int g5 = Index(G, 5);

        StaffGeometry.StemDirectionFor(c4, Clef.Treble).Should().Be(StemDirection.Up);
        StaffGeometry.StemDirectionFor(g5, Clef.Treble).Should().Be(StemDirection.Down, "the two disagree");

        StaffGeometry.GroupStemDirection([c4, g5], Clef.Treble).Should()
            .Be(StemDirection.Up, "C4 is six steps below the middle line and G5 only five above it");

        StaffGeometry.GroupStemDirection([Index(D, 4), Index(A, 5)], Clef.Treble).Should()
            .Be(StemDirection.Down, "now the highest note is the further from the middle line");
    }

    /// <summary>
    /// A group balanced either side of the middle line takes a down stem, the same default a single
    /// note sitting on that line takes. Any rule would do here; agreeing with the single-note rule is
    /// what stops a group and a lone note beside it leaning opposite ways.
    /// </summary>
    [Fact]
    public void ABalancedGroupStemsDownLikeASingleNoteOnTheMiddleLine()
    {
        StaffGeometry.GroupStemDirection([Index(C, 4), Index(A, 5)], Clef.Treble).Should()
            .Be(StemDirection.Down, "six steps below and six above");

        StaffGeometry.GroupStemDirection([Index(B, 4)], Clef.Treble).Should().Be(StemDirection.Down);
        StaffGeometry.GroupStemDirection([], Clef.Treble).Should().Be(StemDirection.Down, "no notes, no crash");
    }

    /// <summary>The clef moves the middle line, so it has to move the group's decision with it.</summary>
    [Fact]
    public void TheGroupDirectionFollowsTheClef()
    {
        // A3 and C4 are high in the bass stave but low in the treble - the same two notes, opposite
        // answers, which is the whole point of asking per clef.
        int a3 = Index(A, 3);
        int c4 = Index(C, 4);

        StaffGeometry.GroupStemDirection([a3, c4], Clef.Bass).Should().Be(StemDirection.Down);
        StaffGeometry.GroupStemDirection([a3, c4], Clef.Treble).Should().Be(StemDirection.Up);
    }

    /// <summary>
    /// A chord's whole extent decides the group's direction, not the one note the model hangs the
    /// beam on. These two triads sit mostly above the middle line and must stem down, but their
    /// lowest notes - the entries that carry the beam - are both below it and would say otherwise.
    /// </summary>
    [Fact]
    public void AChordsExtentDecidesTheGroupsStemDirection()
    {
        // A4-C5-A5 and G4-B4-G5.
        int[] lows = [Index(A, 4), Index(G, 4)];
        int[] highs = [Index(A, 5), Index(G, 5)];

        StaffGeometry.GroupStemDirection(lows, Clef.Treble).Should()
            .Be(StemDirection.Up, "the beam-carrying notes alone read as a low group");

        StaffGeometry.GroupStemDirection(lows, highs, Clef.Treble).Should()
            .Be(StemDirection.Down, "but the chords reach a sixth higher than that");
    }

    /// <summary>
    /// The chord overload has to agree with the single-note one where they overlap, or a group of
    /// plain notes and a group of one-note "chords" would lean different ways.
    /// </summary>
    [Fact]
    public void TheChordOverloadReducesToTheSingleNoteRule()
    {
        int[] indices = [Index(C, 4), Index(G, 5)];

        StaffGeometry.GroupStemDirection(indices, indices, Clef.Treble).Should()
            .Be(StaffGeometry.GroupStemDirection(indices, Clef.Treble));

        StaffGeometry.GroupStemDirection([Index(B, 4)], [Index(B, 4)], Clef.Treble).Should()
            .Be(StaffGeometry.StemDirectionFor(Index(B, 4), Clef.Treble));
    }

    /// <summary>
    /// One chord, one stem, spanning the lot: it ends at the notehead nearest the beam and starts at
    /// the one furthest from it, so every member of the chord hangs off the same line.
    /// </summary>
    [Fact]
    public void AChordsStemRunsFromItsFarHeadToItsNearOne()
    {
        int low = Index(C, 4);
        int high = Index(G, 4);

        StaffGeometry.BeamSideIndex(low, high, StemDirection.Up).Should().Be(high);
        StaffGeometry.StemFootIndex(low, high, StemDirection.Up).Should().Be(low);

        StaffGeometry.BeamSideIndex(low, high, StemDirection.Down).Should().Be(low);
        StaffGeometry.StemFootIndex(low, high, StemDirection.Down).Should().Be(high);

        // Order-agnostic, so a caller that has not sorted its pair cannot invert the stem.
        StaffGeometry.BeamSideIndex(high, low, StemDirection.Up).Should().Be(high);
        StaffGeometry.StemFootIndex(high, low, StemDirection.Down).Should().Be(high);

        // A single note is the degenerate chord, and both ends are the same note.
        StaffGeometry.BeamSideIndex(low, low, StemDirection.Up).Should().Be(low);
        StaffGeometry.StemFootIndex(low, low, StemDirection.Up).Should().Be(low);
    }

    /// <summary>
    /// A chord's stem length is measured from the head <em>nearest</em> the beam - an up-stemmed
    /// C-E-G reaches its nominal length above the G, not above the C. Measuring from the far head
    /// would drop the beam onto the chord's own top notehead.
    /// </summary>
    [Fact]
    public void AChordsBeamClearsItsNearestHeadAndItsStemStillSpansTheChord()
    {
        double[] xs = [100, 160];

        // C4-E4-G4 then E4-G4-C5: two triads, rising.
        int[] lows = [Index(C, 4), Index(E, 4)];
        int[] highs = [Index(G, 4), Index(C, 5)];

        StemDirection direction = StaffGeometry.GroupStemDirection(lows, highs, Clef.Treble);
        direction.Should().Be(StemDirection.Up);

        int[] beamSide =
        [
            StaffGeometry.BeamSideIndex(lows[0], highs[0], direction),
            StaffGeometry.BeamSideIndex(lows[1], highs[1], direction),
        ];

        beamSide.Should().Equal([highs[0], highs[1]], "an up stem faces the top of its chord");

        BeamLine line = StaffGeometry.ComputeBeamLine(
            xs, beamSide, Clef.Treble, StaffTop, Metrics, direction);

        line.YAt(100).Should().BeApproximately(96, 1e-9);
        line.YAt(160).Should().BeApproximately(84, 1e-9);

        double nominal = Metrics.StaffSpace * StaffGeometry.NominalStemSpaces;

        for (int i = 0; i < xs.Length; i++)
        {
            double nearY = StaffGeometry.YForDiatonicIndex(beamSide[i], Clef.Treble, StaffTop, Metrics);
            double footY = StaffGeometry.YForDiatonicIndex(
                StaffGeometry.StemFootIndex(lows[i], highs[i], direction), Clef.Treble, StaffTop, Metrics);
            double beamY = StaffGeometry.BeamStemEndY(line, xs[i], levels: 1, Metrics);

            (nearY - beamY).Should().BeGreaterThanOrEqualTo(
                nominal - 1e-9, "the beam clears the head it faces by a full stem");

            (footY - beamY).Should().BeGreaterThan(
                nearY - beamY, "and the drawn stem is longer still, because it spans the chord");
        }
    }

    /// <summary>
    /// The beam-facing head is also the one whose pitch the contour follows. Two chords with the same
    /// bottom note but different tops must not give a flat beam when the group stems up.
    /// </summary>
    [Fact]
    public void ThePitchContourFollowsTheBeamFacingHead()
    {
        int[] lows = [Index(C, 4), Index(C, 4)];
        int[] highs = [Index(E, 4), Index(C, 5)];

        int[] beamSide =
        [
            StaffGeometry.BeamSideIndex(lows[0], highs[0], StemDirection.Up),
            StaffGeometry.BeamSideIndex(lows[1], highs[1], StemDirection.Up),
        ];

        BeamLine sloped = StaffGeometry.ComputeBeamLine(
            [100, 200], beamSide, Clef.Treble, StaffTop, Metrics, StemDirection.Up);

        sloped.Slope.Should().BeLessThan(0, "the tops rise, so the beam must rise with them");

        BeamLine flat = StaffGeometry.ComputeBeamLine(
            [100, 200], lows, Clef.Treble, StaffTop, Metrics, StemDirection.Up);

        flat.Slope.Should().Be(0, "reading the bottom notes instead would have lost the contour");
    }

    /// <summary>
    /// The beam is a line, so a note in the middle of the group sits exactly where the line says -
    /// not where its own pitch would put it. Ascending E4-G4-B4 at even spacing is the simplest case
    /// where a per-note height and a real line differ.
    /// </summary>
    [Fact]
    public void ABeamLineInterpolatesAcrossASlopedGroup()
    {
        double[] xs = [100, 140, 180];
        int[] indices = [Index(E, 4), Index(G, 4), Index(B, 4)];

        BeamLine line = StaffGeometry.ComputeBeamLine(
            xs, indices, Clef.Treble, StaffTop, Metrics, StemDirection.Up);

        line.Slope.Should().BeApproximately(-0.2, 1e-9, "the group rises four steps over 80 px");

        line.YAt(100).Should().BeApproximately(104, 1e-9);
        line.YAt(180).Should().BeApproximately(88, 1e-9);
        line.YAt(140).Should().BeApproximately(96, 1e-9, "the middle note sits on the line, not beside it");
        line.YAt(140).Should().BeApproximately((line.YAt(100) + line.YAt(180)) / 2, 1e-9);
    }

    /// <summary>A group that neither rises nor falls gets a flat beam, and no float noise in the slope.</summary>
    [Fact]
    public void ALevelGroupGetsAFlatBeam()
    {
        double[] xs = [100, 140, 180];
        int[] indices = [Index(E, 4), Index(E, 4), Index(E, 4)];

        BeamLine line = StaffGeometry.ComputeBeamLine(
            xs, indices, Clef.Treble, StaffTop, Metrics, StemDirection.Up);

        line.Slope.Should().Be(0);
        line.YAt(100).Should().Be(line.YAt(180));
    }

    /// <summary>
    /// The two clamps, and which one binds. A wide leap over a narrow span is held by the gradient
    /// cap; the same leap over a wide span passes the gradient cap and is held by the total rise
    /// instead. Without the second, a beam at the gradient limit across a whole bar climbs clear of
    /// the stave.
    /// </summary>
    [Fact]
    public void SlopeIsClampedByGradientAndByTotalRise()
    {
        int[] leap = [Index(C, 4), Index(C, 6)];

        BeamLine steep = StaffGeometry.ComputeBeamLine(
            [100, 140], leap, Clef.Treble, StaffTop, Metrics, StemDirection.Down);

        steep.Slope.Should().BeApproximately(
            -StaffGeometry.MaxBeamSlope, 1e-9, "the raw gradient is -1.4, far past the cap");

        BeamLine wide = StaffGeometry.ComputeBeamLine(
            [100, 500], leap, Clef.Treble, StaffTop, Metrics, StemDirection.Down);

        Math.Abs(wide.Slope).Should().BeLessThan(
            StaffGeometry.MaxBeamSlope, "the gradient cap does not bind over that run");

        (wide.YAt(500) - wide.YAt(100)).Should().BeApproximately(
            -Metrics.StaffSpace * StaffGeometry.MaxBeamRiseSpaces, 1e-9, "the rise cap does");
    }

    /// <summary>
    /// The classic beaming bug: take the line from the outer notes alone and an inner note higher
    /// than both ends up with the beam through its notehead. The line is slid until every stem in the
    /// group is at least its nominal length, inner notes included.
    /// </summary>
    [Fact]
    public void EveryStemInAGroupReachesTheBeam()
    {
        double[] xs = [100, 140, 180];

        // E4, C5, E4 - the middle note is five steps above its neighbours.
        int[] indices = [Index(E, 4), Index(C, 5), Index(E, 4)];

        StemDirection direction = StaffGeometry.GroupStemDirection(indices, Clef.Treble);
        direction.Should().Be(StemDirection.Up);

        BeamLine line = StaffGeometry.ComputeBeamLine(
            xs, indices, Clef.Treble, StaffTop, Metrics, direction);

        double nominal = Metrics.StaffSpace * StaffGeometry.NominalStemSpaces;

        for (int i = 0; i < xs.Length; i++)
        {
            double headY = StaffGeometry.YForDiatonicIndex(indices[i], Clef.Treble, StaffTop, Metrics);
            double beamY = StaffGeometry.BeamStemEndY(line, xs[i], levels: 1, Metrics);

            (headY - beamY).Should().BeGreaterThanOrEqualTo(
                nominal - 1e-9, "an up stem must run at least its nominal length to reach the beam");
        }
    }

    /// <summary>The same guarantee the other way up, where the extreme note is the lowest.</summary>
    [Fact]
    public void EveryStemInADownStemmedGroupReachesTheBeam()
    {
        double[] xs = [100, 150, 200, 250];
        int[] indices = [Index(D, 5), Index(F, 5), Index(A, 4), Index(G, 5)];

        StemDirection direction = StaffGeometry.GroupStemDirection(indices, Clef.Treble);
        direction.Should().Be(StemDirection.Down);

        BeamLine line = StaffGeometry.ComputeBeamLine(
            xs, indices, Clef.Treble, StaffTop, Metrics, direction);

        double nominal = Metrics.StaffSpace * StaffGeometry.NominalStemSpaces;

        for (int i = 0; i < xs.Length; i++)
        {
            double headY = StaffGeometry.YForDiatonicIndex(indices[i], Clef.Treble, StaffTop, Metrics);
            double beamY = StaffGeometry.BeamStemEndY(line, xs[i], levels: 1, Metrics);

            (beamY - headY).Should().BeGreaterThanOrEqualTo(nominal - 1e-9);
        }
    }

    /// <summary>
    /// Extra beam levels stack away from the noteheads, so the sign of the offset follows the stem
    /// direction. Stacking them inward instead walks a 64th's fourth beam through its own notehead.
    /// </summary>
    [Fact]
    public void BeamLevelsStackAwayFromTheNoteheads()
    {
        double pitch = StaffGeometry.BeamLevelPitch(Metrics);

        StaffGeometry.BeamLevelOffset(1, StemDirection.Up, Metrics).Should().Be(0, "level 1 is the beam line");
        StaffGeometry.BeamLevelOffset(1, StemDirection.Down, Metrics).Should().Be(0);

        StaffGeometry.BeamLevelOffset(2, StemDirection.Up, Metrics).Should()
            .Be(-pitch, "an up stem points at smaller Y, so away from the heads is upward");
        StaffGeometry.BeamLevelOffset(2, StemDirection.Down, Metrics).Should().Be(pitch);

        StaffGeometry.BeamLevelOffset(4, StemDirection.Up, Metrics).Should().Be(-3 * pitch);
        StaffGeometry.BeamLevelOffset(4, StemDirection.Down, Metrics).Should().Be(3 * pitch);
    }

    /// <summary>
    /// A dotted eighth plus a sixteenth: one level on the first note, two on the second. The
    /// sixteenth's stem has to run out to its own second beam or that beam floats free of it.
    /// </summary>
    [Fact]
    public void AStemRunsOutToItsOwnOutermostLevel()
    {
        BeamLine line = new(100, 90, 0, StemDirection.Up);
        double pitch = StaffGeometry.BeamLevelPitch(Metrics);

        StaffGeometry.BeamStemEndY(line, 100, levels: 1, Metrics).Should().Be(90);
        StaffGeometry.BeamStemEndY(line, 160, levels: 2, Metrics).Should().Be(90 - pitch);

        BeamLine down = new(100, 90, 0, StemDirection.Down);
        StaffGeometry.BeamStemEndY(down, 160, levels: 2, Metrics).Should().Be(90 + pitch);

        StaffGeometry.BeamStemEndY(line, 100, levels: 0, Metrics).Should()
            .Be(90, "a level count of zero still reaches the primary beam");
    }

    /// <summary>
    /// A beam is stroked as a thick line, so its centre sits half a thickness inward from the stem
    /// ends. Inward, not outward: outward would leave every stem half a thickness short of the ink.
    /// </summary>
    [Fact]
    public void ABeamsCentreLiesInsideItsStemEnds()
    {
        double half = StaffGeometry.BeamThickness(Metrics) / 2;

        BeamLine up = new(100, 90, 0, StemDirection.Up);
        StaffGeometry.BeamCentreY(up, 100, level: 1, Metrics).Should().Be(90 + half);

        BeamLine down = new(100, 90, 0, StemDirection.Down);
        StaffGeometry.BeamCentreY(down, 100, level: 1, Metrics).Should().Be(90 - half);
    }

    /// <summary>
    /// Beam thickness and level spacing are staff-space multiples, so they follow the zoom. Baking
    /// them in as pixels would leave a beam thicker than the stave it sits on at 0.35x.
    /// </summary>
    [Fact]
    public void BeamMetricsScaleWithZoom()
    {
        StaffMetrics big = StaffMetrics.ForZoom(3.0);

        StaffGeometry.BeamThickness(big).Should().BeGreaterThan(StaffGeometry.BeamThickness(Metrics));
        StaffGeometry.BeamLevelPitch(big).Should().BeGreaterThan(StaffGeometry.BeamLevelPitch(Metrics));
        StaffGeometry.BeamHookWidth(big).Should().BeGreaterThan(StaffGeometry.BeamHookWidth(Metrics));

        StaffGeometry.BeamLevelPitch(big).Should().BeGreaterThan(
            StaffGeometry.BeamThickness(big), "levels need daylight between them");
    }

    /// <summary>
    /// Which adjacent pairs get a full beam. Stricter than "neither is None" on purpose: two
    /// sixteenth pairs inside one eighth group put an <c>End</c> next to a <c>Begin</c> at level 2,
    /// and joining those beams across the very gap the model went to the trouble of describing.
    /// </summary>
    [Fact]
    public void OnlyABeginOrContinueJoinsForwardToAContinueOrEnd()
    {
        StaffGeometry.BeamsJoin(BeamState.Begin, BeamState.End).Should().BeTrue();
        StaffGeometry.BeamsJoin(BeamState.Begin, BeamState.Continue).Should().BeTrue();
        StaffGeometry.BeamsJoin(BeamState.Continue, BeamState.End).Should().BeTrue();

        StaffGeometry.BeamsJoin(BeamState.End, BeamState.Begin).Should()
            .BeFalse("that is the gap between two sixteenth pairs, not a beam");
        StaffGeometry.BeamsJoin(BeamState.Begin, BeamState.None).Should().BeFalse();
        StaffGeometry.BeamsJoin(BeamState.None, BeamState.End).Should().BeFalse();
        StaffGeometry.BeamsJoin(BeamState.Begin, BeamState.BackwardHook).Should()
            .BeFalse("a hook is a stub drawn on its own, never half of a joined segment");
        StaffGeometry.BeamsJoin(BeamState.ForwardHook, BeamState.End).Should().BeFalse();
    }

    /// <summary>
    /// A hook points away from the note that owns it, and is shortened rather than allowed to touch a
    /// close neighbour's stem - a hook that reaches the next stem reads as a full beam, which says
    /// the opposite of what a hook means.
    /// </summary>
    [Fact]
    public void AHookPointsTheRightWayAndNeverReachesItsNeighbour()
    {
        double full = StaffGeometry.BeamHookWidth(Metrics);

        StaffGeometry.BeamHookEndX(200, neighbourX: null, forward: true, Metrics).Should().Be(200 + full);
        StaffGeometry.BeamHookEndX(200, neighbourX: null, forward: false, Metrics).Should().Be(200 - full);

        StaffGeometry.BeamHookEndX(200, neighbourX: 260, forward: true, Metrics).Should()
            .Be(200 + full, "a distant neighbour does not shorten it");

        StaffGeometry.BeamHookEndX(200, neighbourX: 204, forward: true, Metrics).Should()
            .Be(202, "half the gap, so the stub stops well short of the next stem");

        StaffGeometry.BeamHookEndX(200, neighbourX: 196, forward: false, Metrics).Should().Be(198);
    }

    /// <summary>
    /// A beam runs stem to stem, not notehead to notehead, so every beam calculation goes through the
    /// same side rule the stem itself does.
    /// </summary>
    [Fact]
    public void AStemStandsOnItsOwnSideOfTheNotehead()
    {
        double side = Space * StaffGeometry.StemSideSpaces;

        StaffGeometry.StemX(200, StemDirection.Up, Metrics).Should().Be(200 + side);
        StaffGeometry.StemX(200, StemDirection.Down, Metrics).Should().Be(200 - side);
    }

    /// <summary>A group of one - a malformed model - must not divide by a zero-length run.</summary>
    [Fact]
    public void ADegenerateGroupProducesAUsableLine()
    {
        BeamLine single = StaffGeometry.ComputeBeamLine(
            [100], [Index(E, 4)], Clef.Treble, StaffTop, Metrics, StemDirection.Up);

        single.Slope.Should().Be(0);
        double.IsFinite(single.YAt(100)).Should().BeTrue();

        BeamLine none = StaffGeometry.ComputeBeamLine(
            [], [], Clef.Treble, StaffTop, Metrics, StemDirection.Up);

        double.IsFinite(none.YAt(0)).Should().BeTrue();

        BeamLine stacked = StaffGeometry.ComputeBeamLine(
            [100, 100], [Index(E, 4), Index(G, 4)], Clef.Treble, StaffTop, Metrics, StemDirection.Up);

        stacked.Slope.Should().Be(0, "a zero-length run has no gradient to take");
        double.IsFinite(stacked.YAt(100)).Should().BeTrue();
    }

    // --- metrics -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>Zoom</c> is a bound styled property, so a slider or a binding error can deliver zero or
    /// NaN - either of which turns every later division into an infinity that draws nothing at all.
    /// </summary>
    [Fact]
    public void ZoomIsClampedAndNaNProofed()
    {
        StaffMetrics.ForZoom(0).StaffSpace.Should().Be(StaffMetrics.BaseStaffSpace * StaffMetrics.MinZoom);
        StaffMetrics.ForZoom(1000).StaffSpace.Should().Be(StaffMetrics.BaseStaffSpace * StaffMetrics.MaxZoom);
        StaffMetrics.ForZoom(double.NaN).StaffSpace.Should().Be(StaffMetrics.BaseStaffSpace);
        StaffMetrics.ForZoom(-3).StaffSpace.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AGrandStaffIsTwoStavesPlusTheGapBetweenThem()
    {
        Metrics.PartHeight(1).Should().Be(Metrics.StaffHeight);
        Metrics.PartHeight(2).Should().Be((Metrics.StaffHeight * 2) + Metrics.GrandStaffGap);

        Metrics.StaffTop(StaffTop, 1).Should().Be(StaffTop);
        Metrics.StaffTop(StaffTop, 2).Should().Be(StaffTop + Metrics.StaffHeight + Metrics.GrandStaffGap);
    }

    [Fact]
    public void OneDiatonicStepIsHalfAStaffSpace()
    {
        Metrics.StepHeight.Should().Be(Step);

        double b4 = Y(B, 4, Clef.Treble);
        double c5 = Y(C, 5, Clef.Treble);

        (b4 - c5).Should().Be(Step, "adjacent letters are one step apart, whatever their pitch distance");
    }

    // --- stems that should not exist -------------------------------------------------------------

    /// <summary>
    /// Regression: whole notes were drawn with a stem, which reads as a half note.
    /// </summary>
    /// <remarks>
    /// The renderer excluded only <see cref="NoteValue.Breve"/>, on the reasoning that the longest
    /// value is the stemless one. Two values are stemless, and a whole note is far commoner than a
    /// breve, so every held note on the page was mis-drawn. Found by looking at a rendered page, not
    /// by a test - hence this one.
    /// </remarks>
    [Theory]
    [InlineData(NoteValue.Breve, false)]
    [InlineData(NoteValue.Whole, false)]
    [InlineData(NoteValue.Half, true)]
    [InlineData(NoteValue.Quarter, true)]
    [InlineData(NoteValue.Eighth, true)]
    [InlineData(NoteValue.SixtyFourth, true)]
    public void OnlyABreveAndAWholeNoteAreStemless(NoteValue value, bool stemmed) =>
        StaffGeometry.HasStem(value).Should().Be(stemmed);

    [Fact]
    public void EveryHollowValueShorterThanAWholeStillTakesAStem()
    {
        // The two rules are deliberately off by one from each other: a half note is hollow and
        // stemmed. Asserting them together is what stops one being "tidied" to match the other.
        StaffGeometry.HasStem(NoteValue.Half).Should().BeTrue();
        NoteValue.Half.IsHollow().Should().BeTrue();
    }

    // --- accidentals clear of their noteheads ------------------------------------------------------

    /// <summary>
    /// Regression: every accidental was drawn overlapping its own notehead.
    /// </summary>
    /// <remarks>
    /// The old renderer anchored all ten signs at one fixed offset from the notehead centre. They
    /// differ by more than a factor of two in width, so the offset that suited a natural drove a
    /// double flat two thirds of a space into the head. Placing each glyph from its right edge is
    /// the fix, and this pins the edge itself: it must clear the notehead, with air to spare.
    /// </remarks>
    [Theory]
    [InlineData(0.35)]
    [InlineData(1.0)]
    [InlineData(2.5)]
    public void AnAccidentalsRightEdgeClearsItsNotehead(double zoom)
    {
        StaffMetrics metrics = StaffMetrics.ForZoom(zoom);
        const double noteheadX = 500.0;

        double right = StaffGeometry.AccidentalRightEdge(noteheadX, metrics);
        double headLeft = noteheadX - (metrics.StaffSpace * StaffGeometry.NoteheadHalfWidthSpaces);

        right.Should().BeLessThan(headLeft, "an accidental is written beside its notehead, not on it");
        (headLeft - right).Should().BeApproximately(
            metrics.StaffSpace * StaffGeometry.AccidentalGapSpaces,
            1e-9,
            "the air between the two is the gap constant and nothing else");
    }

    [Fact]
    public void TheNoteheadIsWiderThanTheStemStandsFromItsCentre() =>
        // The stem touches the head's edge from inside; the accidental clears it from outside. Both
        // are measured from the same half-width, so they cannot drift apart.
        StaffGeometry.NoteheadHalfWidthSpaces.Should().BeGreaterThan(
            StaffGeometry.StemSideSpaces, "the stem meets the notehead rather than floating beside it");
}
