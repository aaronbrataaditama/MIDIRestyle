using MidiRestyle.Core.Notation;

namespace MidiRestyle.App.Controls;

/// <summary>Which way a stem points from its notehead.</summary>
public enum StemDirection
{
    Up,
    Down,
}

/// <summary>
/// Every length the staff view draws with, derived once from the zoom factor.
/// </summary>
/// <remarks>
/// <para>
/// Engraving is traditionally expressed in <em>staff spaces</em> - the distance between two adjacent
/// staff lines - because every other dimension on a stave is a fixed multiple of it. Keeping that
/// convention here means the whole renderer scales by changing one number, and it means the
/// hand-authored glyph paths (clefs, accidentals, rests, flags) are written once in space units and
/// never need a second set of magic numbers for a different zoom.
/// </para>
/// <para>
/// A record struct rather than a class so it can be passed <c>in</c> through the render path without
/// allocating; <see cref="StaffView"/> rebuilds it only when <c>Zoom</c> actually changes.
/// </para>
/// </remarks>
public readonly record struct StaffMetrics(double StaffSpace, double PixelsPerQuarter)
{
    /// <summary>Staff space at <c>Zoom == 1</c>, i.e. a 32 px stave - about what a screen wants.</summary>
    public const double BaseStaffSpace = 8.0;

    /// <summary>Horizontal space one quarter note gets at <c>Zoom == 1</c>.</summary>
    public const double BaseQuarterWidth = 42.0;

    public const double MinZoom = 0.35;
    public const double MaxZoom = 5.0;

    /// <summary>
    /// Metrics for a zoom factor, clamped and NaN-proofed.
    /// </summary>
    /// <remarks>
    /// The clamp is not defensive padding: <c>Zoom</c> is a bound styled property, so a slider or a
    /// binding error can hand this a zero or a NaN, and either one turns every subsequent division
    /// into an infinity that silently draws nothing at all.
    /// </remarks>
    public static StaffMetrics ForZoom(double zoom)
    {
        double clamped = double.IsFinite(zoom) ? Math.Clamp(zoom, MinZoom, MaxZoom) : 1.0;
        return new StaffMetrics(BaseStaffSpace * clamped, BaseQuarterWidth * clamped);
    }

    /// <summary>One diatonic step - a line to the next space - is half a staff space.</summary>
    public double StepHeight => StaffSpace / 2.0;

    /// <summary>Top line to bottom line: four spaces, never five.</summary>
    public double StaffHeight => StaffSpace * 4.0;

    /// <summary>Gap between the two staves of a grand staff, bottom line to top line.</summary>
    public double GrandStaffGap => StaffSpace * 7.0;

    /// <summary>Gap between one part's last staff and the next part's first.</summary>
    public double PartGap => StaffSpace * 7.0;

    // --- the page ---------------------------------------------------------------------------------

    /// <summary>
    /// Paper margin above the first system, which also holds the title block.
    /// </summary>
    /// <remarks>
    /// Deep enough for a title and a subtitle above <see cref="SystemHeadroom"/>, because a page of
    /// music that begins hard against the top edge does not look like a page of music.
    /// </remarks>
    public double PageMarginTop => StaffSpace * 11.0;

    /// <summary>Paper margin below the last system, so the page does not end on a barline.</summary>
    public double PageMarginBottom => StaffSpace * 6.0;

    /// <summary>Paper margin to the left of a system's part name.</summary>
    public double PageMarginLeft => StaffSpace * 1.6;

    /// <summary>Paper margin to the right of a system's final barline.</summary>
    public double PageMarginRight => StaffSpace * 2.4;

    /// <summary>
    /// White space between one system's lowest staff line and the next system's highest.
    /// </summary>
    /// <remarks>
    /// Generous on purpose: <see cref="SystemHeadroom"/> and <see cref="SystemFootroom"/> both eat
    /// into it, since ledger lines, tuplet brackets and residual-cent figures all stand outside the
    /// stave. Too small a gap and the high notes of one system collide with the low notes of the one
    /// above, which is the single most common way a wrapped score stops being readable.
    /// </remarks>
    public double SystemGap => StaffSpace * 11.0;

    /// <summary>How far above its top staff line a system may actually draw.</summary>
    public double SystemHeadroom => StaffSpace * 5.0;

    /// <summary>How far below its bottom staff line a system may actually draw.</summary>
    public double SystemFootroom => StaffSpace * 3.0;

    // --- the left indent of a system ---------------------------------------------------------------

    /// <summary>Blank space between a system's part name and its brace.</summary>
    public double NameGap => StaffSpace * 0.9;

    /// <summary>Width the brace of a grand staff occupies.</summary>
    public double BraceWidth => StaffSpace * 1.7;

    /// <summary>Width reserved for the clef, which every system carries.</summary>
    public double ClefWidth => StaffSpace * 3.4;

    /// <summary>
    /// Width reserved for the key signature, which this renderer deliberately never draws.
    /// </summary>
    /// <remarks>
    /// A restyled maqam or pentatonic is not a major or minor key, so no key signature is correct and
    /// the score is written with explicit accidentals instead - the same decision
    /// <c>MusicXmlExporter</c> records as <c>&lt;fifths&gt;0&lt;/fifths&gt;</c>. The <em>slot</em> is
    /// still reserved, because the order clef, key, time is what a reader's eye expects at the head of
    /// a system, and reserving it means a key signature can later be drawn here without every other
    /// measurement moving.
    /// </remarks>
    public double KeySignatureWidth => StaffSpace * 1.1;

    /// <summary>Width reserved for the time signature at the head of a system.</summary>
    public double TimeSignatureWidth => StaffSpace * 2.8;

    /// <summary>Blank space between the indent block and the first measure's first note column.</summary>
    public double IndentTrailGap => StaffSpace * 1.2;

    // --- horizontal spacing inside a measure --------------------------------------------------------

    /// <summary>A measure never gets narrower than this however few ticks it holds.</summary>
    public double MinMeasureWidth => StaffSpace * 6.5;

    /// <summary>Blank space inside a measure before the first note.</summary>
    public double MeasureLeadPadding => StaffSpace * 1.1;

    /// <summary>Blank space inside a measure after the last note, before the barline.</summary>
    public double MeasureTrailPadding => StaffSpace * 0.8;

    /// <summary>
    /// The narrowest a note column may be, whatever its duration.
    /// </summary>
    /// <remarks>
    /// A notehead is 1.32 spaces wide, so anything under about two spaces has consecutive sixteenths
    /// overlapping each other. This floor is what stops a dense bar collapsing into a smear.
    /// </remarks>
    public double MinColumnWidth => StaffSpace * 2.4;

    /// <summary>Extra width a column gets when something in it carries an accidental.</summary>
    public double AccidentalColumnWidth => StaffSpace * 1.35;

    /// <summary>Total vertical extent of a part's staves, excluding the gap after it.</summary>
    public double PartHeight(int staffCount) =>
        staffCount >= 2 ? (StaffHeight * 2) + GrandStaffGap : StaffHeight;

    /// <summary>Y of the top line of a part's <paramref name="staff"/> (1-based).</summary>
    public double StaffTop(double partTop, int staff) =>
        staff >= 2 ? partTop + StaffHeight + GrandStaffGap : partTop;
}

/// <summary>
/// The straight line a beam group's primary beam lies on: an anchor point and a slope.
/// </summary>
/// <remarks>
/// <para>
/// A beam is a line, not a set of per-note heights, and saying so in the type is what keeps the
/// group honest. Every stem in the group is cut at this line, so they cannot disagree about where
/// the beam is, and a hook or a secondary beam is the same line shifted by a fixed offset.
/// </para>
/// <para>
/// <see cref="Y0"/> is the Y at <see cref="X0"/> of the primary beam's <em>stem-side</em> edge - the
/// edge the stems end on - not its centre. Everything else is measured from there, which is why a
/// secondary level is a clean multiple of one offset rather than a special case.
/// </para>
/// </remarks>
public readonly record struct BeamLine(double X0, double Y0, double Slope, StemDirection Direction)
{
    /// <summary>Y of the primary beam's stem-side edge at <paramref name="x"/>.</summary>
    public double YAt(double x) => Y0 + (Slope * (x - X0));
}

/// <summary>A half-open run of measure indices - the measures one system holds.</summary>
public readonly record struct MeasureRange(int First, int Count)
{
    public static MeasureRange Empty => new(0, 0);

    public int EndExclusive => First + Count;

    public bool IsEmpty => Count <= 0;
}

/// <summary>A half-open run of system indices, as returned by the vertical culling pass.</summary>
public readonly record struct SystemRange(int First, int Count)
{
    public static SystemRange Empty => new(0, 0);

    public int EndExclusive => First + Count;

    public bool IsEmpty => Count <= 0;
}

/// <summary>
/// The X of everything in a system's left indent, in the order a reader expects to meet it.
/// </summary>
/// <remarks>
/// <para>
/// The order is fixed by convention and by the reference the user cited: part name, brace, the
/// barline that joins the staves, then <b>clef, key signature, time signature</b>. Every one of those
/// belongs at the head of every system, which is exactly what distinguishes a page of music from a
/// strip of it.
/// </para>
/// <para>
/// <see cref="KeyX"/> names a slot this renderer never fills - see
/// <see cref="StaffMetrics.KeySignatureWidth"/> for why the space is reserved anyway.
/// </para>
/// </remarks>
public readonly record struct StaffIndent(
    double NameX,
    double BraceX,
    double SystemBarlineX,
    double ClefX,
    double KeyX,
    double TimeX,
    double MusicX);

/// <summary>
/// Which accidentals are already in force in the measure being drawn, per staff position.
/// </summary>
/// <remarks>
/// <para>
/// A written accidental holds for the rest of its measure, at that letter <em>and that octave</em>,
/// and every later note on that position is written plain. Drawing the sign on every note instead -
/// which is what naively rendering <see cref="SpelledNote.AccidentalSymbol"/> does - produces a page
/// so cluttered that the melody stops being legible, and it is wrong notation besides.
/// </para>
/// <para>
/// The key is <see cref="SpelledNote.DiatonicIndex"/> (octave x 7 + letter), so C-sharp 4 and
/// C-sharp 5 are tracked separately - that is the actual rule, not a refinement of it. The score
/// model carries no key signature, so the state every measure starts in is "everything natural",
/// which also means a natural sign is correctly emitted for a note that follows a sharp on the same
/// position.
/// </para>
/// <para>
/// Mutable and reused across measures via <see cref="Reset"/> rather than reallocated, because the
/// render path may walk hundreds of measures per frame.
/// </para>
/// </remarks>
public sealed class MeasureAccidentals
{
    /// <summary>
    /// How close two alterations must be to count as the same sign. Alterations are quantised to
    /// halves of a semitone, so anything below a quarter of that is float noise.
    /// </summary>
    private const double AlterEpsilon = 0.01;

    private readonly Dictionary<int, double> _inForce = [];

    /// <summary>Clears the state. Called at every barline.</summary>
    public void Reset() => _inForce.Clear();

    /// <summary>
    /// Whether <paramref name="note"/> needs its accidental drawn, recording it as in force if so.
    /// </summary>
    public bool NeedsAccidental(SpelledNote note) => NeedsAccidental(note.DiatonicIndex, note.Alter);

    /// <summary>
    /// Whether a note at <paramref name="diatonicIndex"/> altered by <paramref name="alter"/> needs
    /// its accidental drawn. Not a pure query: a <c>true</c> answer records the alteration as now in
    /// force, which is what makes the second occurrence answer <c>false</c>.
    /// </summary>
    public bool NeedsAccidental(int diatonicIndex, double alter)
    {
        double current = _inForce.TryGetValue(diatonicIndex, out double existing) ? existing : 0.0;

        if (Math.Abs(current - alter) < AlterEpsilon)
        {
            return false;
        }

        _inForce[diatonicIndex] = alter;
        return true;
    }
}

/// <summary>
/// The staff view's layout arithmetic, kept free of Avalonia so it can be tested headlessly.
/// </summary>
/// <remarks>
/// <para>
/// Same split as <see cref="PianoRollGeometry"/> and for the same reason: staff placement is the part
/// of a notation renderer that is actually easy to get wrong (an off-by-one in a clef's reference
/// line silently transposes the whole score by a third, and it looks plausible), so it lives where a
/// test can call it without a window, a drawing context or an initialised Avalonia runtime.
/// <see cref="StaffView"/> then holds only drawing code.
/// </para>
/// <para>
/// The vertical model is <see cref="SpelledNote.DiatonicIndex"/> throughout - octave x 7 + letter -
/// because a staff position <em>is</em> a letter and an octave. C-sharp, C and C-flat all sit on the
/// same line; only the accidental differs. Working in MIDI note numbers here would be the classic
/// bug.
/// </para>
/// </remarks>
public static class StaffGeometry
{
    /// <summary>Letters per octave. Named so the arithmetic below reads as notation, not magic.</summary>
    public const int DiatonicStepsPerOctave = 7;

    /// <summary>A stave is five lines, so eight diatonic steps from bottom line to top.</summary>
    public const int StepsBottomToTopLine = 8;

    // --- clef reference positions ---------------------------------------------------------------

    /// <summary>
    /// Diatonic index of the bottom line: E4 in treble, G2 in bass.
    /// </summary>
    /// <remarks>
    /// These two constants anchor the entire vertical layout, so they are written out rather than
    /// derived: E4 is <c>4 * 7 + 2 = 30</c> and G2 is <c>2 * 7 + 4 = 18</c>. Everything else - top
    /// line, middle line, ledger counts, stem direction - falls out of them.
    /// </remarks>
    public static int BottomLineIndex(Clef clef) => clef == Clef.Bass ? 18 : 30;

    /// <summary>Diatonic index of the top line: F5 in treble, A3 in bass.</summary>
    public static int TopLineIndex(Clef clef) => BottomLineIndex(clef) + StepsBottomToTopLine;

    /// <summary>Diatonic index of the middle line: B4 in treble, D3 in bass.</summary>
    public static int MiddleLineIndex(Clef clef) => BottomLineIndex(clef) + 4;

    /// <summary>
    /// The line the clef glyph is drawn around: the G line (second from the bottom) in treble, the F
    /// line (second from the top) in bass. This is the definition of the clef, not decoration.
    /// </summary>
    public static int ClefReferenceIndex(Clef clef) =>
        clef == Clef.Bass ? BottomLineIndex(clef) + 6 : BottomLineIndex(clef) + 2;

    // --- vertical placement ---------------------------------------------------------------------

    /// <summary>
    /// Y of a diatonic staff position, given the Y of the stave's top line.
    /// </summary>
    /// <remarks>
    /// Y grows downward and pitch grows upward, hence the subtraction. A position one step above the
    /// top line is half a space higher on screen; there is no separate case for lines and spaces,
    /// which is the whole reason the diatonic index is the right coordinate to work in.
    /// </remarks>
    public static double YForDiatonicIndex(int diatonicIndex, Clef clef, double staffTopY, in StaffMetrics metrics) =>
        staffTopY + ((TopLineIndex(clef) - diatonicIndex) * metrics.StepHeight);

    /// <summary>Y of one of the five staff lines, <paramref name="line"/> 0 being the top.</summary>
    public static double YForStaffLine(int line, double staffTopY, in StaffMetrics metrics) =>
        staffTopY + (line * metrics.StaffSpace);

    /// <summary>True when the position sits on a line rather than in a space.</summary>
    /// <remarks>Used to decide where an augmentation dot goes: a dot never sits on a line.</remarks>
    public static bool IsOnLine(int diatonicIndex, Clef clef) =>
        (TopLineIndex(clef) - diatonicIndex) % 2 == 0;

    // --- ledger lines -----------------------------------------------------------------------------

    /// <summary>
    /// How many ledger lines a note needs above the stave.
    /// </summary>
    /// <remarks>
    /// Integer division is exactly right here and the halving is not an approximation: ledger lines
    /// continue the stave's own spacing, so they exist only at even step offsets. A note in the space
    /// immediately above the top line is two steps from the nearest ledger line and needs none.
    /// </remarks>
    public static int LedgerLinesAbove(int diatonicIndex, Clef clef) =>
        Math.Max(0, (diatonicIndex - TopLineIndex(clef)) / 2);

    /// <summary>How many ledger lines a note needs below the stave.</summary>
    public static int LedgerLinesBelow(int diatonicIndex, Clef clef) =>
        Math.Max(0, (BottomLineIndex(clef) - diatonicIndex) / 2);

    /// <summary>Diatonic index of the <paramref name="n"/>th ledger line above the stave (1-based).</summary>
    public static int LedgerIndexAbove(Clef clef, int n) => TopLineIndex(clef) + (n * 2);

    /// <summary>Diatonic index of the <paramref name="n"/>th ledger line below the stave (1-based).</summary>
    public static int LedgerIndexBelow(Clef clef, int n) => BottomLineIndex(clef) - (n * 2);

    // --- stems ------------------------------------------------------------------------------------

    /// <summary>
    /// Which way a note's stem points.
    /// </summary>
    /// <remarks>
    /// The convention is that a note below the middle line stems up and one above stems down, so the
    /// stem always points back toward the stave. A note exactly <em>on</em> the middle line is the
    /// ambiguous case and takes a down stem, which is the near-universal engraving default.
    /// </remarks>
    public static StemDirection StemDirectionFor(int diatonicIndex, Clef clef) =>
        diatonicIndex >= MiddleLineIndex(clef) ? StemDirection.Down : StemDirection.Up;

    /// <summary>Whether a written value carries a stem at all.</summary>
    /// <remarks>
    /// A breve and a whole note are stemless; everything shorter has one. Excluding only the breve -
    /// which is what "the longest value has no stem" naively gives - puts a stem on every whole note
    /// on the page, and a whole note with a stem reads as a half note. That is the same off-by-one
    /// as <see cref="NoteValueExtensions.IsHollow"/>, which correctly stops at <c>Half</c>.
    /// </remarks>
    public static bool HasStem(NoteValue value) => value > NoteValue.Whole;

    /// <summary>Nominal stem length, in staff spaces. An octave's worth, the engraving standard.</summary>
    public const double NominalStemSpaces = 3.5;

    /// <summary>
    /// Where a stem ends, given the notehead's Y.
    /// </summary>
    /// <remarks>
    /// A stem is nominally 3.5 spaces, but a note far outside the stave gets a longer one so the stem
    /// still reaches the middle line - otherwise a run of high ledger notes has stems dangling in
    /// mid-air with no visual connection to the stave.
    /// </remarks>
    public static double StemEndY(
        int diatonicIndex, Clef clef, double staffTopY, in StaffMetrics metrics, StemDirection direction)
    {
        double headY = YForDiatonicIndex(diatonicIndex, clef, staffTopY, metrics);
        double middleY = YForStaffLine(2, staffTopY, metrics);
        double nominal = metrics.StaffSpace * NominalStemSpaces;

        return direction == StemDirection.Up
            ? Math.Min(headY - nominal, middleY)
            : Math.Max(headY + nominal, middleY);
    }

    /// <summary>Half a notehead's width, in staff spaces.</summary>
    /// <remarks>
    /// The notehead is an ellipse 1.32 spaces across, so anything placed beside one has to clear
    /// 0.66 spaces from its centre. Named here rather than repeated as a literal because both the
    /// stem side and the accidental gutter are measured from it.
    /// </remarks>
    public const double NoteheadHalfWidthSpaces = 0.66;

    /// <summary>Clear air between an accidental's right edge and its notehead, in staff spaces.</summary>
    public const double AccidentalGapSpaces = 0.24;

    /// <summary>
    /// X of the <em>right edge</em> of the accidental belonging to a notehead centred at
    /// <paramref name="noteheadX"/>.
    /// </summary>
    /// <remarks>
    /// An accidental is written clear of its notehead, not on top of it. Placing each glyph from its
    /// right edge rather than from an arbitrary anchor is what makes that true for all ten of them:
    /// a double flat is more than twice the width of a natural, so a single shared offset either
    /// leaves the narrow signs adrift or drives the wide ones straight through the notehead - which
    /// is exactly what it did.
    /// </remarks>
    public static double AccidentalRightEdge(double noteheadX, in StaffMetrics metrics) =>
        noteheadX - (metrics.StaffSpace * (NoteheadHalfWidthSpaces + AccidentalGapSpaces));

    /// <summary>How far to the side of a notehead's centre its stem stands, in staff spaces.</summary>
    /// <remarks>Half a notehead: the stem touches the head's edge, it does not bisect it.</remarks>
    public const double StemSideSpaces = 0.60;

    /// <summary>X of a stem, given the X of its notehead's centre.</summary>
    /// <remarks>
    /// Up on the right, down on the left. A beam spans stem to stem rather than head to head, so
    /// every beam calculation has to go through this - using notehead X instead shifts the whole
    /// beam sideways by a notehead and leaves the outer stems poking past its ends.
    /// </remarks>
    public static double StemX(double noteheadX, StemDirection direction, in StaffMetrics metrics) =>
        direction == StemDirection.Up
            ? noteheadX + (metrics.StaffSpace * StemSideSpaces)
            : noteheadX - (metrics.StaffSpace * StemSideSpaces);

    // --- beams ---------------------------------------------------------------------------------------

    /// <summary>Beam thickness, in staff spaces. Half a space is the engraving standard.</summary>
    public const double BeamThicknessSpaces = 0.5;

    /// <summary>White space between two adjacent beam levels, in staff spaces.</summary>
    public const double BeamGapSpaces = 0.32;

    /// <summary>
    /// Steepest a beam may lean, as rise over run.
    /// </summary>
    /// <remarks>
    /// Without a cap, a group whose first and last notes are an octave apart draws a beam at close to
    /// 45 degrees, which reads as a drawing error rather than as music. Real engraving caps the rise
    /// of a beam at about a quarter of its run and lets the stems take up the difference; so does
    /// this. The cap is what makes the sloped beam safe to have at all - a flat beam would always be
    /// readable, so the slope is only worth drawing if it can never be ugly.
    /// </remarks>
    public const double MaxBeamSlope = 0.25;

    /// <summary>
    /// Total rise a beam may have across the whole group, in staff spaces, whatever the slope cap
    /// allows. A wide group at the slope limit would otherwise climb clear of the stave.
    /// </summary>
    public const double MaxBeamRiseSpaces = 2.0;

    /// <summary>Length of a hook stub, in staff spaces - about half a notehead.</summary>
    public const double BeamHookSpaces = 0.66;

    /// <summary>Beam thickness in pixels, floored so it stays visible when zoomed right out.</summary>
    public static double BeamThickness(in StaffMetrics metrics) =>
        Math.Max(1.0, metrics.StaffSpace * BeamThicknessSpaces);

    /// <summary>Distance from one beam level's edge to the next level's matching edge.</summary>
    public static double BeamLevelPitch(in StaffMetrics metrics) =>
        BeamThickness(metrics) + Math.Max(0.6, metrics.StaffSpace * BeamGapSpaces);

    /// <summary>
    /// Signed Y offset of a beam <paramref name="level"/> (1-based) from the primary beam.
    /// </summary>
    /// <remarks>
    /// Levels stack <em>away</em> from the noteheads, so the sign follows the stem direction: an up
    /// stem's beams climb (negative Y), a down stem's descend. Stacking them inward instead would
    /// walk a 64th note's fourth beam straight through its own notehead.
    /// </remarks>
    public static double BeamLevelOffset(int level, StemDirection direction, in StaffMetrics metrics) =>
        (Math.Max(1, level) - 1) * BeamLevelPitch(metrics) * (direction == StemDirection.Up ? -1 : 1);

    /// <summary>Length of a hook stub in pixels.</summary>
    public static double BeamHookWidth(in StaffMetrics metrics) => metrics.StaffSpace * BeamHookSpaces;

    /// <summary>
    /// The one stem direction a whole beam group takes.
    /// </summary>
    /// <remarks>
    /// Mixed stems inside a beam are not a style choice, they are wrong - the beam has to join the
    /// stem ends, and stems on opposite sides have no common end to join. The conventional rule is
    /// that the note furthest from the middle line decides for everybody, because that is the note
    /// whose stem would be most absurd if it went the other way. Ties, and a group sitting squarely
    /// on the middle line, take a down stem - the same default
    /// <see cref="StemDirectionFor"/> uses for a single note.
    /// </remarks>
    public static StemDirection GroupStemDirection(ReadOnlySpan<int> diatonicIndices, Clef clef) =>
        GroupStemDirection(diatonicIndices, diatonicIndices, clef);

    /// <summary>
    /// The same rule over a group whose entries may be chords, each given as a lowest and a highest
    /// staff position.
    /// </summary>
    /// <remarks>
    /// A chord's <em>extent</em> decides, not its first-written note. The model hangs the beam on the
    /// chord's timed head and leaves its other members' <c>Beams</c> empty, so a renderer that reads
    /// one position per chord is reading whichever note the builder happened to write first - and a
    /// group of triads then leans on the accident of voicing order rather than on where the music
    /// sits. The two spans are paired by index and may be the same span for a group of single notes.
    /// </remarks>
    public static StemDirection GroupStemDirection(
        ReadOnlySpan<int> lowIndices, ReadOnlySpan<int> highIndices, Clef clef)
    {
        int middle = MiddleLineIndex(clef);
        int above = 0;
        int below = 0;
        int count = Math.Min(lowIndices.Length, highIndices.Length);

        for (int i = 0; i < count; i++)
        {
            above = Math.Max(above, Math.Max(lowIndices[i], highIndices[i]) - middle);
            below = Math.Max(below, middle - Math.Min(lowIndices[i], highIndices[i]));
        }

        return above >= below ? StemDirection.Down : StemDirection.Up;
    }

    /// <summary>
    /// Which of a chord's two extreme noteheads its stem <em>ends</em> at - the one nearest the beam.
    /// </summary>
    /// <remarks>
    /// A chord has one stem, and its length is measured from the notehead closest to the stem's tip:
    /// an up-stemmed C-E-G reaches its nominal length above the <em>G</em>, not above the C. Measuring
    /// from the far head instead leaves a chord's beam sitting on top of its own highest notehead.
    /// This is also the head whose pitch the beam's contour should follow.
    /// </remarks>
    public static int BeamSideIndex(int lowIndex, int highIndex, StemDirection direction) =>
        direction == StemDirection.Up ? Math.Max(lowIndex, highIndex) : Math.Min(lowIndex, highIndex);

    /// <summary>
    /// Which of a chord's two extreme noteheads its stem <em>starts</em> at - the one furthest from
    /// the beam, so the single stem spans the whole chord and every member hangs off it.
    /// </summary>
    public static int StemFootIndex(int lowIndex, int highIndex, StemDirection direction) =>
        direction == StemDirection.Up ? Math.Min(lowIndex, highIndex) : Math.Max(lowIndex, highIndex);

    /// <summary>
    /// The line a group's primary beam lies on, given each note's stem X and staff position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two steps, and the order matters. The slope comes from the first and last noteheads - the
    /// contour a reader actually sees - then is clamped twice, once as a gradient and once as a
    /// total rise. Only then is the line slid bodily along Y until it clears every note's nominal
    /// stem end: <c>min</c> for an up stem, <c>max</c> for a down one.
    /// </para>
    /// <para>
    /// That second step is what guarantees no stem in the group is ever shorter than
    /// <see cref="NominalStemSpaces"/>, including the middle notes nobody thought about. Deriving
    /// the line from the outer notes alone is the classic beaming bug: an inner note higher than
    /// both of them ends up with a beam cutting through its notehead.
    /// </para>
    /// </remarks>
    public static BeamLine ComputeBeamLine(
        ReadOnlySpan<double> stemXs,
        ReadOnlySpan<int> diatonicIndices,
        Clef clef,
        double staffTopY,
        in StaffMetrics metrics,
        StemDirection direction)
    {
        if (stemXs.Length == 0 || stemXs.Length != diatonicIndices.Length)
        {
            return new BeamLine(0, 0, 0, direction);
        }

        double x0 = stemXs[0];
        double run = stemXs[^1] - x0;
        double slope = 0;

        if (run > 0.001)
        {
            double firstY = YForDiatonicIndex(diatonicIndices[0], clef, staffTopY, metrics);
            double lastY = YForDiatonicIndex(diatonicIndices[^1], clef, staffTopY, metrics);

            slope = Math.Clamp((lastY - firstY) / run, -MaxBeamSlope, MaxBeamSlope);

            double maxRise = metrics.StaffSpace * MaxBeamRiseSpaces;
            double rise = slope * run;
            if (Math.Abs(rise) > maxRise)
            {
                slope = Math.CopySign(maxRise / run, slope);
            }
        }

        double anchor = direction == StemDirection.Up ? double.MaxValue : double.MinValue;

        for (int i = 0; i < stemXs.Length; i++)
        {
            double ideal = StemEndY(diatonicIndices[i], clef, staffTopY, metrics, direction);
            double candidate = ideal - (slope * (stemXs[i] - x0));

            anchor = direction == StemDirection.Up
                ? Math.Min(anchor, candidate)
                : Math.Max(anchor, candidate);
        }

        return new BeamLine(x0, anchor, slope, direction);
    }

    /// <summary>
    /// Where a beamed note's stem ends: on the outermost beam level that note itself carries.
    /// </summary>
    /// <remarks>
    /// Per note rather than per group, so the sixteenth of a dotted-eighth-plus-sixteenth pair runs
    /// out to its second beam while its neighbour stops at the first. Stopping every stem at the
    /// primary would leave the secondary beams and hooks floating clear of their stems.
    /// </remarks>
    public static double BeamStemEndY(in BeamLine line, double stemX, int levels, in StaffMetrics metrics) =>
        line.YAt(stemX) + BeamLevelOffset(levels, line.Direction, metrics);

    /// <summary>
    /// Y of the centre of beam <paramref name="level"/> at <paramref name="x"/>.
    /// </summary>
    /// <remarks>
    /// The centre, not the edge, because the renderer strokes a beam as a thick line rather than
    /// filling a quadrilateral - a stroked line needs no per-frame geometry, and at the slope cap
    /// the difference between a mitred end and a vertical one is well under a pixel.
    /// </remarks>
    public static double BeamCentreY(in BeamLine line, double x, int level, in StaffMetrics metrics) =>
        BeamStemEndY(line, x, level, metrics)
        + (BeamThickness(metrics) / 2 * (line.Direction == StemDirection.Up ? 1 : -1));

    /// <summary>
    /// Whether a full beam segment is drawn between two adjacent notes at one level.
    /// </summary>
    /// <remarks>
    /// Deliberately stricter than "neither is <see cref="BeamState.None"/>". Two sixteenth pairs
    /// inside one eighth-note group give <c>End</c> immediately followed by <c>Begin</c> at level 2,
    /// and joining those would beam across the very gap the model went to the trouble of describing.
    /// </remarks>
    public static bool BeamsJoin(BeamState left, BeamState right) =>
        left is BeamState.Begin or BeamState.Continue
        && right is BeamState.Continue or BeamState.End;

    /// <summary>
    /// Where a hook stub ends, given its own stem X and its neighbour's.
    /// </summary>
    /// <remarks>
    /// Shortened when the neighbour is close, so a hook on a narrow measure cannot reach across and
    /// touch the next stem - which would read as a full beam and say the opposite of what a hook
    /// means. <paramref name="neighbourX"/> is null when there is no neighbour on that side.
    /// </remarks>
    public static double BeamHookEndX(
        double stemX, double? neighbourX, bool forward, in StaffMetrics metrics)
    {
        double width = BeamHookWidth(metrics);

        if (neighbourX is { } other)
        {
            double available = Math.Abs(other - stemX) * 0.5;
            if (available > 0)
            {
                width = Math.Min(width, available);
            }
        }

        return forward ? stemX + width : stemX - width;
    }

    // --- horizontal layout -------------------------------------------------------------------------

    /// <summary>
    /// How sharply a note's width follows its duration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Engraving does not space notes in proportion to their length: a whole note is not four times
    /// the width of a quarter, because the reader's eye needs roughly constant information density,
    /// not constant time density. The standard answer is a fractional power of the duration, and the
    /// usual range is 0.5 to 0.6. At 0.6 a quarter is 1.00 units, an eighth 0.66, a sixteenth 0.43 and
    /// a whole 2.30 - long notes get more room than short ones, but nothing like proportionally more.
    /// </para>
    /// <para>
    /// This replaced strict tick-proportional placement, under which a sixteenth got 10.5 px at zoom 1
    /// - narrower than the 10.6 px notehead it has to hold - so runs of sixteenths overlapped while
    /// whole notes left a quarter of the bar empty.
    /// </para>
    /// </remarks>
    public const double SpacingExponent = 0.6;

    /// <summary>
    /// Width of one note column, from its own onset to the next.
    /// </summary>
    /// <remarks>
    /// The floor is not cosmetic: without it a 32nd-note run is spaced more narrowly than its own
    /// noteheads. The accidental allowance is added rather than maxed in, because an accidental sits
    /// <em>beside</em> the notehead and needs its own room whatever the note's duration.
    /// </remarks>
    public static double ColumnWidth(double quarters, bool hasAccidental, in StaffMetrics metrics)
    {
        double span = quarters > 0 && double.IsFinite(quarters)
            ? metrics.PixelsPerQuarter * Math.Pow(quarters, SpacingExponent)
            : 0;

        return Math.Max(metrics.MinColumnWidth, span)
            + (hasAccidental ? metrics.AccidentalColumnWidth : 0);
    }

    /// <summary>
    /// Where everything in a system's left indent sits, given the width its part names need.
    /// </summary>
    /// <remarks>
    /// The name width is passed in rather than measured here because measuring text needs Avalonia and
    /// this class must stay headless. It is what fixes the reported fault that the part name overlapped
    /// the brace and ran off the left edge: the name now <em>owns</em> a column, and every later item
    /// is placed after it rather than at a fixed offset that assumed the name would fit.
    /// </remarks>
    public static StaffIndent ComputeIndent(
        double nameWidth, bool grandStaff, bool reserveTime, in StaffMetrics metrics)
    {
        double x = metrics.PageMarginLeft;
        double nameX = x;

        if (nameWidth > 0)
        {
            x += nameWidth + metrics.NameGap;
        }

        double braceX = x;
        if (grandStaff)
        {
            x += metrics.BraceWidth;
        }

        double barlineX = x;
        x += metrics.StaffSpace * 0.7;

        double clefX = x;
        x += metrics.ClefWidth;

        // Always reserved, never drawn: see StaffMetrics.KeySignatureWidth.
        double keyX = x;
        x += metrics.KeySignatureWidth;

        // Reserved only where a signature is actually printed - the head of the first system. Later
        // systems repeat the clef, which is the convention, but not the metre, so holding the room
        // open for it would push every one of them in for nothing.
        double timeX = x;
        if (reserveTime)
        {
            x += metrics.TimeSignatureWidth;
        }

        return new StaffIndent(nameX, braceX, barlineX, clefX, keyX, timeX, x + metrics.IndentTrailGap);
    }

    /// <summary>Index of the measure containing <paramref name="tick"/>, or -1.</summary>
    public static int MeasureIndexForTick(IReadOnlyList<NotationMeasure> measures, long tick)
    {
        ArgumentNullException.ThrowIfNull(measures);

        if (measures.Count == 0 || tick < measures[0].StartTicks)
        {
            return -1;
        }

        int low = 0;
        int high = measures.Count - 1;
        int found = -1;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            if (measures[mid].StartTicks <= tick)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        // Measures are contiguous, so the last one starting at or before the tick contains it -
        // unless the tick is past the end of the score entirely, which is the one case to reject.
        return found >= 0 && tick < measures[found].StartTicks + measures[found].LengthTicks
            ? found
            : -1;
    }

    // --- misc ---------------------------------------------------------------------------------------

    /// <summary>
    /// Below this a residual is not worth annotating - it is inside the tuning error of the
    /// instrument, let alone the reader's ear.
    /// </summary>
    public const double ResidualCentsThreshold = 5.0;

    /// <summary>
    /// Whether a note's leftover cents deserve the small comma figure beside its notehead.
    /// </summary>
    /// <remarks>
    /// This is the AEU comma case. MusicXML cannot carry it - there is no element for "and 15 cents
    /// besides" - so the on-screen score is the only place the reader can be told the written note is
    /// an approximation of what will sound.
    /// </remarks>
    public static bool ShouldShowResidual(double residualCents) =>
        double.IsFinite(residualCents) && Math.Abs(residualCents) >= ResidualCentsThreshold;
}

/// <summary>
/// A whole score broken into systems and laid out down a page, computed once per
/// (score, zoom, width) and read - never rebuilt - by the render path.
/// </summary>
/// <remarks>
/// <para>
/// This is the type that turns the staff view from a strip into a page. Real music wraps: measures
/// run left to right, break at a barline, and continue on the next system down. The earlier design
/// was one endless horizontal system, which is easier to scroll against a transport and is not what
/// sheet music looks like.
/// </para>
/// <para>
/// Everything here is pure arithmetic over <see cref="NotationScore"/> and <see cref="StaffMetrics"/>,
/// so the page break rule, the justification rule, the tick-to-position mapping and the follow rule
/// can all be tested without a window. <see cref="StaffView"/> holds only drawing.
/// </para>
/// <para>
/// The internal storage is flat arrays rather than a tree of per-measure objects. A 900-bar file has
/// tens of thousands of note columns, and the render path touches only the handful of systems on
/// screen; flat arrays keep the whole layout in a few allocations made once, and let culling be
/// arithmetic rather than a walk.
/// </para>
/// </remarks>
public sealed class StaffPageLayout
{
    /// <summary>The fraction of a system's natural width below which the last system is left ragged.</summary>
    /// <remarks>
    /// Justifying every system including the last is what makes a two-bar final line sprawl across
    /// the page - the classic sign of a naive engraver. Real practice leaves a short last system at
    /// its natural width, so the music simply stops. The threshold is where "short" begins.
    /// </remarks>
    public const double RaggedLastThreshold = 0.65;

    /// <summary>How far a system may be squeezed when a single measure will not fit the page.</summary>
    public const double MinStretch = 0.35;

    private readonly StaffMetrics _metrics;

    // Per measure.
    private readonly double[] _idealWidth;
    private readonly double[] _width;
    private readonly double[] _x;
    private readonly int[] _system;
    private readonly long[] _startTicks;
    private readonly long[] _lengthTicks;
    private readonly bool[] _printsTime;

    // Note columns, flattened: measure i owns _columnTick[_columnStart[i].._columnStart[i + 1]].
    private readonly int[] _columnStart;
    private readonly long[] _columnTick;
    private readonly double[] _columnX;

    // Per system.
    private readonly int[] _systemFirst;
    private readonly double[] _systemMusicX;
    private readonly double[] _systemMusicWidth;

    private StaffPageLayout(
        in StaffMetrics metrics,
        double pageWidth,
        double[] idealWidth,
        double[] width,
        double[] x,
        int[] system,
        long[] startTicks,
        long[] lengthTicks,
        bool[] printsTime,
        int[] columnStart,
        long[] columnTick,
        double[] columnX,
        int[] systemFirst,
        double[] systemMusicX,
        double[] systemMusicWidth,
        double blockHeight)
    {
        _metrics = metrics;
        PageWidth = pageWidth;
        _idealWidth = idealWidth;
        _width = width;
        _x = x;
        _system = system;
        _startTicks = startTicks;
        _lengthTicks = lengthTicks;
        _printsTime = printsTime;
        _columnStart = columnStart;
        _columnTick = columnTick;
        _columnX = columnX;
        _systemFirst = systemFirst;
        _systemMusicX = systemMusicX;
        _systemMusicWidth = systemMusicWidth;
        SystemBlockHeight = blockHeight;

        ContentHeight = SystemCount == 0
            ? 0
            : metrics.PageMarginTop
              + (SystemCount * blockHeight)
              + ((SystemCount - 1) * metrics.SystemGap)
              + metrics.PageMarginBottom;
    }

    /// <summary>An empty page, for a null or empty score.</summary>
    public static StaffPageLayout Empty { get; } = new(
        StaffMetrics.ForZoom(1.0), 0, [], [], [], [], [], [], [], [0], [], [], [0], [], [], 0);

    /// <summary>The width the page was laid out for. A change of width forces a rebuild.</summary>
    public double PageWidth { get; }

    /// <summary>Top staff line to bottom staff line of one system, across every part.</summary>
    public double SystemBlockHeight { get; }

    /// <summary>Total laid-out page height in pixels, margins included.</summary>
    public double ContentHeight { get; }

    public int MeasureCount => _idealWidth.Length;

    public int SystemCount => _systemFirst.Length - 1;

    public bool IsEmpty => MeasureCount == 0 || SystemCount == 0;

    // --- measures --------------------------------------------------------------------------------

    /// <summary>Which system a measure was placed on.</summary>
    public int SystemOf(int measure) => _system[measure];

    /// <summary>Page X of a measure's left edge, after justification.</summary>
    public double MeasureX(int measure) => _x[measure];

    /// <summary>A measure's justified width. Its barline stands at <c>MeasureX + MeasureWidth</c>.</summary>
    public double MeasureWidth(int measure) => _width[measure];

    /// <summary>A measure's width before its system was justified, for tests and diagnostics.</summary>
    public double IdealMeasureWidth(int measure) => _idealWidth[measure];

    /// <summary>Whether this measure prints its own time signature inline, before its first note.</summary>
    /// <remarks>
    /// True where the signature changes, except in the very first measure - whose signature belongs
    /// in the indent at the head of the first system, per the convention that the head of a system
    /// carries clef, then key, then time.
    /// </remarks>
    public bool PrintsTimeSignature(int measure) => _printsTime[measure];

    public long MeasureStartTicks(int measure) => _startTicks[measure];

    public long MeasureLengthTicks(int measure) => _lengthTicks[measure];

    // --- systems ---------------------------------------------------------------------------------

    /// <summary>The measures one system holds.</summary>
    public MeasureRange MeasuresIn(int system) =>
        new(_systemFirst[system], _systemFirst[system + 1] - _systemFirst[system]);

    /// <summary>Page Y of the top staff line of a system's first part.</summary>
    public double SystemTop(int system) =>
        _metrics.PageMarginTop + (system * (SystemBlockHeight + _metrics.SystemGap));

    /// <summary>Page Y above which a system draws nothing - its staves plus ledger-line headroom.</summary>
    public double SystemBlockTop(int system) => SystemTop(system) - _metrics.SystemHeadroom;

    /// <summary>Page Y below which a system draws nothing.</summary>
    public double SystemBlockBottom(int system) =>
        SystemTop(system) + SystemBlockHeight + _metrics.SystemFootroom;

    /// <summary>Page X where a system's music starts, i.e. the right edge of its indent.</summary>
    public double SystemMusicX(int system) => _systemMusicX[system];

    /// <summary>How wide the music of a system is allowed to be.</summary>
    public double SystemMusicWidth(int system) => _systemMusicWidth[system];

    /// <summary>Page X of a system's rightmost barline.</summary>
    public double SystemMusicRight(int system)
    {
        MeasureRange range = MeasuresIn(system);
        return range.IsEmpty
            ? SystemMusicX(system)
            : MeasureX(range.EndExclusive - 1) + MeasureWidth(range.EndExclusive - 1);
    }

    // --- scrolling -------------------------------------------------------------------------------

    /// <summary>The furthest down the page a viewport of this height may be scrolled.</summary>
    public double MaxScrollY(double viewportHeight) => Math.Max(0, ContentHeight - viewportHeight);

    /// <summary>Clamps a scroll position, NaN included, to the page.</summary>
    /// <remarks>
    /// <c>ScrollY</c> is a bound styled property, so a scrollbar or a binding error can deliver a NaN
    /// - and a NaN scroll offsets every coordinate on the page into nothing being drawn at all, with
    /// no exception to say why.
    /// </remarks>
    public double ClampScrollY(double scrollY, double viewportHeight) =>
        double.IsFinite(scrollY) ? Math.Clamp(scrollY, 0, MaxScrollY(viewportHeight)) : 0;

    /// <summary>
    /// Which systems intersect a viewport <paramref name="viewportHeight"/> tall at a given scroll.
    /// </summary>
    /// <remarks>
    /// Systems are evenly pitched down the page, so this is arithmetic rather than a search - which is
    /// what keeps a 900-bar score costing the same per frame as a 20-bar one. The bounds use each
    /// system's <em>drawn</em> extent, headroom and footroom included, so a ledger line hanging above a
    /// system that has otherwise scrolled off the top is still drawn.
    /// </remarks>
    public SystemRange VisibleSystems(double scrollY, double viewportHeight)
    {
        if (SystemCount == 0 || viewportHeight <= 0)
        {
            return SystemRange.Empty;
        }

        double pitch = SystemBlockHeight + _metrics.SystemGap;
        if (pitch <= 0)
        {
            return new SystemRange(0, SystemCount);
        }

        double top = double.IsFinite(scrollY) ? scrollY : 0;
        double bottom = top + viewportHeight;

        double firstReal =
            (top - _metrics.PageMarginTop - SystemBlockHeight - _metrics.SystemFootroom) / pitch;
        double lastReal = (bottom - _metrics.PageMarginTop + _metrics.SystemHeadroom) / pitch;

        int first = Math.Max(0, (int)Math.Ceiling(firstReal));
        int last = Math.Min(SystemCount - 1, (int)Math.Floor(lastReal));

        return last >= first ? new SystemRange(first, last - first + 1) : SystemRange.Empty;
    }

    /// <summary>
    /// Where the scroll should go so the playhead is comfortably visible, or <c>false</c> to leave it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same comfortable-band rule as <c>PianoRoll.FollowPlayhead</c>, turned on its side: if the
    /// system the playhead is in already lies between 10% and 85% down the viewport, nothing happens
    /// and the caller is told so, which is what leaves a reader's own scrolling alone. Otherwise the
    /// page moves so that system's top sits 10% down, leaving headroom to read what is coming.
    /// </para>
    /// <para>
    /// Reporting "did nothing" matters as much as the movement. Called on a 60 Hz timer, a follow that
    /// always scrolled would fight every drag the user made.
    /// </para>
    /// </remarks>
    public bool FollowPlayhead(long tick, double scrollY, double viewportHeight, out double scrolledTo)
    {
        scrolledTo = scrollY;

        if (tick < 0 || viewportHeight <= 0 || SystemCount == 0 || !double.IsFinite(scrollY))
        {
            return false;
        }

        int measure = MeasureIndexForTick(tick);
        if (measure < 0)
        {
            return false;
        }

        int system = SystemOf(measure);
        double blockTop = SystemBlockTop(system);
        double blockBottom = SystemBlockBottom(system);

        double comfortableTop = scrollY + (viewportHeight * 0.10);
        double comfortableBottom = scrollY + (viewportHeight * 0.85);

        if (blockTop >= comfortableTop && blockBottom <= comfortableBottom)
        {
            return false;
        }

        double target = Math.Clamp(
            blockTop - (viewportHeight * 0.10), 0, MaxScrollY(viewportHeight));

        // A system taller than the comfortable band can never satisfy the test above, so without this
        // the follow would report movement on every frame for ever.
        if (Math.Abs(target - scrollY) < 0.5)
        {
            return false;
        }

        scrolledTo = target;
        return true;
    }

    // --- ticks to the page -------------------------------------------------------------------------

    /// <summary>Index of the measure containing <paramref name="tick"/>, or -1.</summary>
    public int MeasureIndexForTick(long tick)
    {
        int count = MeasureCount;
        if (count == 0 || tick < _startTicks[0])
        {
            return -1;
        }

        int low = 0;
        int high = count - 1;
        int found = -1;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            if (_startTicks[mid] <= tick)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found >= 0 && tick < _startTicks[found] + _lengthTicks[found] ? found : -1;
    }

    /// <summary>
    /// Page X of an absolute tick inside a known measure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Interpolated between the measure's own note columns, not proportionally across its ticks. That
    /// is what keeps the playhead on the note that is sounding: a note's onset <em>is</em> a column
    /// boundary, so at that tick this returns exactly the X the notehead was drawn at, however
    /// unevenly the bar is spaced. Between two onsets the line slides linearly, which is the reading a
    /// scrubbing eye expects.
    /// </para>
    /// <para>
    /// Proportional placement would put the playhead ahead of a whole note and behind a run of
    /// sixteenths, since duration-weighted spacing is deliberately not linear in time.
    /// </para>
    /// </remarks>
    public double XForTick(int measure, long tick)
    {
        double left = _x[measure];
        double stretch = _idealWidth[measure] > 0 ? _width[measure] / _idealWidth[measure] : 1.0;

        int start = _columnStart[measure];
        int end = _columnStart[measure + 1];

        if (end <= start)
        {
            return left + (_metrics.MeasureLeadPadding * stretch);
        }

        // The right edge of the last column: the barline, less its trailing padding.
        double rightEdge = Math.Max(
            _columnX[end - 1], _idealWidth[measure] - _metrics.MeasureTrailPadding);

        long relative = tick - _startTicks[measure];
        if (relative <= _columnTick[start])
        {
            return left + (_columnX[start] * stretch);
        }

        for (int i = start; i < end; i++)
        {
            long from = _columnTick[i];
            long to = i + 1 < end ? _columnTick[i + 1] : _lengthTicks[measure];

            if (relative >= to)
            {
                continue;
            }

            double fromX = _columnX[i];
            double toX = i + 1 < end ? _columnX[i + 1] : rightEdge;
            double span = to - from;
            double fraction = span > 0 ? (relative - from) / span : 0;

            return left + ((fromX + (fraction * (toX - fromX))) * stretch);
        }

        return left + (rightEdge * stretch);
    }

    /// <summary>
    /// Locates an absolute tick on the page: which system it is on, and where across it.
    /// </summary>
    /// <remarks>
    /// The system matters as much as the X. A playhead on a wrapped page is a short vertical line
    /// inside one system, not a line down the whole page - drawing it full height would put a red
    /// stripe through four unrelated bars of music.
    /// </remarks>
    public bool TryLocate(long tick, out int system, out double x)
    {
        system = 0;
        x = 0;

        int measure = MeasureIndexForTick(tick);
        if (measure < 0)
        {
            return false;
        }

        system = _system[measure];
        x = XForTick(measure, tick);
        return true;
    }

    // --- the page back to ticks --------------------------------------------------------------------

    /// <summary>The system whose block contains <paramref name="y"/>, or the nearest one.</summary>
    /// <remarks>
    /// A click in the gap between two systems belongs to the one below it: the blocks already
    /// include their headroom and footroom, so the gap being split between neighbours is what the
    /// reader sees. Above the first system and below the last both clamp inward, so a click anywhere
    /// on the page resolves to something.
    /// </remarks>
    private int SystemAtY(double y)
    {
        int count = SystemCount;
        if (count == 0)
        {
            return -1;
        }

        for (int system = 0; system < count; system++)
        {
            if (y < SystemBlockBottom(system))
            {
                return system;
            }
        }

        return count - 1;
    }

    /// <summary>
    /// The absolute tick at a page X inside a known measure: the inverse of <see cref="XForTick"/>.
    /// </summary>
    /// <remarks>
    /// It has to invert the <em>same</em> column interpolation, not divide the bar proportionally by
    /// time. Clicking a notehead must seek to that note, and a bar's columns are spaced by duration
    /// weight rather than linearly - so proportional arithmetic would land short of a whole note and
    /// past a run of sixteenths, which is exactly the error <see cref="XForTick"/> exists to avoid in
    /// the other direction.
    /// </remarks>
    public long TickForX(int measure, double pageX)
    {
        long measureStart = _startTicks[measure];
        long length = _lengthTicks[measure];

        int start = _columnStart[measure];
        int end = _columnStart[measure + 1];

        if (end <= start)
        {
            return measureStart;
        }

        double stretch = _idealWidth[measure] > 0 ? _width[measure] / _idealWidth[measure] : 1.0;
        double local = stretch > 0 ? (pageX - _x[measure]) / stretch : 0;

        if (local <= _columnX[start])
        {
            return measureStart + _columnTick[start];
        }

        double rightEdge = Math.Max(
            _columnX[end - 1], _idealWidth[measure] - _metrics.MeasureTrailPadding);

        for (int i = start; i < end; i++)
        {
            long from = _columnTick[i];
            long to = i + 1 < end ? _columnTick[i + 1] : length;

            double fromX = _columnX[i];
            double toX = i + 1 < end ? _columnX[i + 1] : rightEdge;

            if (local >= toX)
            {
                continue;
            }

            double span = toX - fromX;
            double fraction = span > 0 ? (local - fromX) / span : 0;

            return measureStart + from + (long)Math.Round(fraction * (to - from));
        }

        return measureStart + length;
    }

    /// <summary>
    /// The absolute tick at a point on the page, in content coordinates - the caller adds its own
    /// scroll offset before asking.
    /// </summary>
    /// <remarks>
    /// Both axes matter, and the y is not a formality: on a wrapped page the same x appears once per
    /// system, so without resolving the system first a click on the last line would seek into the
    /// first bar of the piece.
    /// </remarks>
    public bool TryTickAt(double pageX, double pageY, out long tick)
    {
        tick = 0;

        if (IsEmpty)
        {
            return false;
        }

        int system = SystemAtY(pageY);
        if (system < 0)
        {
            return false;
        }

        MeasureRange range = MeasuresIn(system);
        if (range.IsEmpty)
        {
            return false;
        }

        // The last measure whose left edge the click is at or past; clicking left of the first - in
        // the indent, on the clef - gives that measure's start rather than nothing.
        int measure = range.First;
        for (int m = range.First + 1; m < range.EndExclusive; m++)
        {
            if (pageX >= _x[m])
            {
                measure = m;
            }
        }

        tick = TickForX(measure, pageX);
        return true;
    }

    // --- construction ------------------------------------------------------------------------------

    /// <summary>
    /// Lays a score out across a page of a given width.
    /// </summary>
    /// <param name="score">The score. Null or empty gives <see cref="Empty"/>.</param>
    /// <param name="metrics">Every length, derived from the zoom.</param>
    /// <param name="pageWidth">The viewport width in pixels; the page is exactly this wide.</param>
    /// <param name="firstIndent">Width of the first system's indent, which carries full part names.</param>
    /// <param name="laterIndent">Width of every later system's indent, which carries abbreviations.</param>
    /// <remarks>
    /// <para>
    /// Two indent widths because that is the convention: the first system names its parts in full and
    /// is indented further for it, later systems use an abbreviation and start closer to the margin.
    /// Both are measured by the caller, since measuring text needs Avalonia.
    /// </para>
    /// <para>
    /// Called once per (score, zoom, width), never per frame.
    /// </para>
    /// </remarks>
    public static StaffPageLayout Build(
        NotationScore? score,
        in StaffMetrics metrics,
        double pageWidth,
        double firstIndent,
        double laterIndent)
    {
        if (score is null || score.Parts.Count == 0 || !double.IsFinite(pageWidth) || pageWidth <= 0)
        {
            return Empty;
        }

        // Every part shares one measure grid, so any part gives the same barlines; the longest is used
        // so a part that stops early cannot truncate the page.
        IReadOnlyList<NotationMeasure> reference = [];
        foreach (NotationPart part in score.Parts)
        {
            if (part.Measures.Count > reference.Count)
            {
                reference = part.Measures;
            }
        }

        int count = reference.Count;
        if (count == 0)
        {
            return Empty;
        }

        double[] idealWidth = new double[count];
        double[] width = new double[count];
        double[] x = new double[count];
        int[] system = new int[count];
        long[] startTicks = new long[count];
        long[] lengthTicks = new long[count];
        bool[] printsTime = new bool[count];
        int[] columnStart = new int[count + 1];

        List<long> columnTicks = [];
        List<double> columnXs = [];
        List<(long Tick, bool Accidental)> onsets = [];
        MeasureAccidentals[] accidentals = [new MeasureAccidentals(), new MeasureAccidentals()];

        int divisions = Math.Max(1, score.Divisions);

        for (int i = 0; i < count; i++)
        {
            NotationMeasure measure = reference[i];
            startTicks[i] = measure.StartTicks;
            lengthTicks[i] = Math.Max(1, measure.LengthTicks);
            printsTime[i] = i > 0 && measure.TimeSignatureChanged;

            columnStart[i] = columnTicks.Count;

            CollectOnsets(score, i, measure, accidentals, onsets);

            double lead = metrics.MeasureLeadPadding
                + (printsTime[i] ? metrics.TimeSignatureWidth : 0);

            // An accidental is written to the LEFT of its notehead, so the room it needs belongs to
            // the gap before that column, not after it. Charging it to the column's own width instead
            // pushes the *following* note away and leaves a hole where nothing is drawn - which is
            // exactly the ragged spacing this rewrite set out to fix.
            if (onsets.Count > 0 && onsets[0].Accidental)
            {
                lead += metrics.AccidentalColumnWidth;
            }

            double cursor = lead;

            for (int j = 0; j < onsets.Count; j++)
            {
                long tick = onsets[j].Tick;
                long next = j + 1 < onsets.Count ? onsets[j + 1].Tick : lengthTicks[i];

                columnTicks.Add(tick);
                columnXs.Add(cursor);

                cursor += StaffGeometry.ColumnWidth(
                    (double)(next - tick) / divisions,
                    j + 1 < onsets.Count && onsets[j + 1].Accidental,
                    metrics);
            }

            idealWidth[i] = Math.Max(
                metrics.MinMeasureWidth, cursor + metrics.MeasureTrailPadding);
        }

        columnStart[count] = columnTicks.Count;

        // --- break into systems ---------------------------------------------------------------------

        List<int> systemFirst = [0];
        double available = AvailableWidth(pageWidth, firstIndent, metrics);
        double used = 0;

        for (int i = 0; i < count; i++)
        {
            // The first measure of a system always fits, however wide it is: there is nowhere narrower
            // to put it, and a break before it would leave an empty system. It is squeezed instead.
            if (used > 0 && used + idealWidth[i] > available + 0.001)
            {
                systemFirst.Add(i);
                available = AvailableWidth(pageWidth, laterIndent, metrics);
                used = 0;
            }

            system[i] = systemFirst.Count - 1;
            used += idealWidth[i];
        }

        systemFirst.Add(count);

        int systemCount = systemFirst.Count - 1;
        double[] systemMusicX = new double[systemCount];
        double[] systemMusicWidth = new double[systemCount];

        // --- justify ------------------------------------------------------------------------------

        for (int s = 0; s < systemCount; s++)
        {
            double indent = s == 0 ? firstIndent : laterIndent;
            double lineWidth = AvailableWidth(pageWidth, indent, metrics);

            systemMusicX[s] = indent;
            systemMusicWidth[s] = lineWidth;

            int from = systemFirst[s];
            int to = systemFirst[s + 1];

            double natural = 0;
            for (int i = from; i < to; i++)
            {
                natural += idealWidth[i];
            }

            double stretch = 1.0;
            if (natural > 0)
            {
                bool last = s == systemCount - 1;
                stretch = last && natural < lineWidth * RaggedLastThreshold
                    ? 1.0
                    : Math.Max(MinStretch, lineWidth / natural);
            }

            double cursor = indent;
            for (int i = from; i < to; i++)
            {
                x[i] = cursor;
                width[i] = idealWidth[i] * stretch;
                cursor += width[i];
            }
        }

        double blockHeight = 0;
        for (int p = 0; p < score.Parts.Count; p++)
        {
            if (p > 0)
            {
                blockHeight += metrics.PartGap;
            }

            blockHeight += metrics.PartHeight(score.Parts[p].StaffCount);
        }

        return new StaffPageLayout(
            metrics, pageWidth, idealWidth, width, x, system, startTicks, lengthTicks, printsTime,
            columnStart, [.. columnTicks], [.. columnXs],
            [.. systemFirst], systemMusicX, systemMusicWidth, blockHeight);
    }

    private static double AvailableWidth(double pageWidth, double indent, in StaffMetrics metrics) =>
        Math.Max(metrics.MinMeasureWidth, pageWidth - indent - metrics.PageMarginRight);

    /// <summary>
    /// The distinct onsets in one measure, across every part, in time order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Across every part, and that is the point: the two staves of a grand staff and the several parts
    /// of a score share one column grid, so a note in the bass sounding with a note in the treble is
    /// drawn directly beneath it. Spacing each part independently is what makes a score look like
    /// several unrelated strips of music stacked up.
    /// </para>
    /// <para>
    /// A chord contributes one onset, since its members share a tick - which falls out of deduplication
    /// rather than needing a check for <see cref="NotationEntry.IsChordMember"/>.
    /// </para>
    /// </remarks>
    private static void CollectOnsets(
        NotationScore score,
        int index,
        NotationMeasure reference,
        MeasureAccidentals[] accidentals,
        List<(long Tick, bool Accidental)> into)
    {
        into.Clear();
        long length = Math.Max(1, reference.LengthTicks);

        foreach (NotationPart part in score.Parts)
        {
            if (index >= part.Measures.Count)
            {
                continue;
            }

            // The accidental allowance must mirror the in-force rule exactly, or the spacing lies:
            // a bar of quarter-tones would be padded on every note while the sign is printed on only
            // the first, leaving visibly ragged gaps where nothing is drawn. Same reset point, same
            // per-staff trackers and same entry order as the renderer, so the two cannot drift.
            foreach (MeasureAccidentals state in accidentals)
            {
                state.Reset();
            }

            NotationMeasure measure = part.Measures[index];
            foreach (NotationEntry entry in measure.Entries)
            {
                long tick = Math.Clamp(entry.StartTicks - measure.StartTicks, 0, length - 1);
                bool sign = entry.Note is { } note
                    && accidentals[Math.Clamp(entry.Staff, 1, accidentals.Length) - 1]
                        .NeedsAccidental(note);

                into.Add((tick, sign));
            }
        }

        if (into.Count == 0)
        {
            into.Add((0, false));
            return;
        }

        into.Sort(static (a, b) => a.Tick.CompareTo(b.Tick));

        // Merge equal ticks, keeping "something here needs an accidental" across the merge.
        int write = 0;
        for (int read = 1; read < into.Count; read++)
        {
            if (into[read].Tick == into[write].Tick)
            {
                if (into[read].Accidental)
                {
                    into[write] = (into[write].Tick, true);
                }

                continue;
            }

            write++;
            into[write] = into[read];
        }

        into.RemoveRange(write + 1, into.Count - write - 1);

        if (into[0].Tick != 0)
        {
            into.Insert(0, (0, false));
        }
    }
}
