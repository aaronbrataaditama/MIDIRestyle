using MidiRestyle.App.Controls;
using MidiRestyle.Core.Notation;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Tests for the wrapped-page layout engine behind the staff view.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StaffPageLayout"/> is pure arithmetic over a <see cref="NotationScore"/> and a
/// <see cref="StaffMetrics"/>, which is the whole reason it is a separate type from
/// <c>StaffView</c>: system breaking, justification and the tick-to-page mapping are exactly the
/// parts a reader notices when they are wrong, and none of them needs a window to check.
/// </para>
/// <para>
/// Two rules govern the fixtures here. Nothing asserts a width the implementation computed - that
/// would only restate the code - so the assertions are structural: every measure lands on exactly
/// one system, measures tile their system without gaps or overlaps, a justified system's last
/// barline meets the right margin. And several fixtures place their onsets deliberately
/// <em>off</em> the tick grid, because a fixture that sits on exact boundaries is the one input
/// class that cannot fail - a lesson this repository learned the hard way in the notation builder.
/// </para>
/// </remarks>
public class StaffPageLayoutTests
{
    private static readonly StaffMetrics Metrics = StaffMetrics.ForZoom(1.0);

    /// <summary>Contiguity is exact by construction, so the epsilon only absorbs summation error.</summary>
    private const double Tight = 1e-6;

    // --- fixtures ------------------------------------------------------------------------------

    private static double FirstIndentOf(StaffMetrics metrics, bool grand) =>
        StaffGeometry.ComputeIndent(42, grand, reserveTime: true, metrics).MusicX;

    private static double LaterIndentOf(StaffMetrics metrics, bool grand) =>
        StaffGeometry.ComputeIndent(22, grand, reserveTime: false, metrics).MusicX;

    /// <summary>Lays a score out exactly the way <c>StaffView</c> does, indents included.</summary>
    private static StaffPageLayout Lay(
        NotationScore? score, double pageWidth, StaffMetrics? metrics = null, bool grand = false)
    {
        StaffMetrics m = metrics ?? Metrics;
        return StaffPageLayout.Build(score, m, pageWidth, FirstIndentOf(m, grand), LaterIndentOf(m, grand));
    }

    /// <summary>
    /// A score of <paramref name="measureCount"/> identical measures.
    /// </summary>
    /// <param name="jitter">
    /// When non-zero, onsets are pushed off the tick grid by up to this many ticks. Every fixture
    /// landing on exact boundaries is the input class that cannot fail; see the class remarks.
    /// </param>
    private static NotationScore MakeScore(
        int measureCount,
        int divisions = 480,
        int beats = 4,
        int unit = 4,
        int notesPerMeasure = 4,
        int staffCount = 1,
        int partCount = 1,
        int jitter = 0,
        int signatureChangeAt = -1,
        bool accidentals = false)
    {
        long length = (long)divisions * beats * 4 / unit;
        List<NotationPart> parts = [];

        for (int p = 0; p < partCount; p++)
        {
            List<NotationMeasure> measures = [];

            for (int i = 0; i < measureCount; i++)
            {
                long start = i * length;
                List<NotationEntry> entries = [];

                for (int n = 0; n < notesPerMeasure; n++)
                {
                    long onset = start + (n * length / notesPerMeasure);

                    if (jitter > 0)
                    {
                        onset += ((i * 7) + (n * 3) + p) % jitter;
                    }

                    entries.Add(new NotationEntry
                    {
                        Note = new SpelledNote(
                            (n + i) % 7,
                            4 + (n % 2),
                            accidentals && n % 2 == 0 ? 1 : 0,
                            0),
                        Duration = new NotatedDuration(NoteValue.Quarter),
                        StartTicks = Math.Clamp(onset, start, start + length - 1),
                        DurationTicks = length / Math.Max(1, notesPerMeasure),
                        Staff = staffCount >= 2 && n % 2 == 1 ? 2 : 1,
                    });
                }

                measures.Add(new NotationMeasure
                {
                    Number = i + 1,
                    StartTicks = start,
                    LengthTicks = length,
                    BeatsPerMeasure = beats,
                    BeatUnit = unit,
                    TimeSignatureChanged = i == 0 || i == signatureChangeAt,
                    Entries = entries,
                });
            }

            parts.Add(new NotationPart
            {
                Id = $"P{p + 1}",
                Name = p == 0 ? "Piano" : $"Part {p + 1}",
                TrackIndex = p,
                Channel = p,
                StaffCount = staffCount,
                Clefs = staffCount >= 2 ? [Clef.Treble, Clef.Bass] : [Clef.Treble],
                Measures = measures,
            });
        }

        return new NotationScore { Divisions = divisions, Parts = parts };
    }

    private static double NaturalWidth(StaffPageLayout layout, int system)
    {
        MeasureRange range = layout.MeasuresIn(system);
        double total = 0;

        for (int i = range.First; i < range.EndExclusive; i++)
        {
            total += layout.IdealMeasureWidth(i);
        }

        return total;
    }

    // --- system breaking -------------------------------------------------------------------------

    [Theory]
    [InlineData(480, 320)]
    [InlineData(480, 700)]
    [InlineData(480, 1600)]
    [InlineData(120, 700)]
    [InlineData(120, 1600)]
    public void EveryMeasureLandsOnExactlyOneSystemInOrder(int divisions, double pageWidth)
    {
        StaffPageLayout layout = Lay(MakeScore(37, divisions: divisions, jitter: 13), pageWidth);

        layout.MeasureCount.Should().Be(37, "the layout covers the score's own measure grid");
        layout.SystemCount.Should().BeGreaterThan(0);

        List<int> seen = [];
        for (int s = 0; s < layout.SystemCount; s++)
        {
            MeasureRange range = layout.MeasuresIn(s);
            range.IsEmpty.Should().BeFalse("an empty system is a wasted line of the page");

            for (int i = range.First; i < range.EndExclusive; i++)
            {
                seen.Add(i);
                layout.SystemOf(i).Should().Be(s, "SystemOf must agree with MeasuresIn");
            }
        }

        seen.Should().Equal(
            [.. Enumerable.Range(0, 37)],
            "every measure is placed exactly once, in order, with none lost or duplicated");
    }

    [Fact]
    public void SystemOfIsNonDecreasingAcrossTheScore()
    {
        StaffPageLayout layout = Lay(MakeScore(60, jitter: 11), 640);

        for (int i = 1; i < layout.MeasureCount; i++)
        {
            layout.SystemOf(i).Should().BeGreaterThanOrEqualTo(
                layout.SystemOf(i - 1), "music runs down the page, never back up it");
            (layout.SystemOf(i) - layout.SystemOf(i - 1)).Should().BeLessThanOrEqualTo(
                1, "a break moves to the next system, never skips one");
        }
    }

    [Fact]
    public void ASystemHoldsAsManyMeasuresAsItsWidthAllows()
    {
        StaffPageLayout layout = Lay(MakeScore(40), 760);

        layout.SystemCount.Should().BeGreaterThan(2, "the fixture is meant to wrap several times");

        for (int s = 0; s < layout.SystemCount; s++)
        {
            MeasureRange range = layout.MeasuresIn(s);
            double natural = NaturalWidth(layout, s);

            if (range.Count > 1)
            {
                natural.Should().BeLessThanOrEqualTo(
                    layout.SystemMusicWidth(s) + Tight,
                    "a system is never packed past the width it was measured against");
            }

            if (range.EndExclusive < layout.MeasureCount)
            {
                (natural + layout.IdealMeasureWidth(range.EndExclusive)).Should().BeGreaterThan(
                    layout.SystemMusicWidth(s),
                    "the break happened because the next measure would not fit, not early");
            }
        }
    }

    [Fact]
    public void TheFirstSystemIsIndentedFurtherThanTheRest()
    {
        StaffPageLayout layout = Lay(MakeScore(30), 700);

        layout.SystemCount.Should().BeGreaterThan(1);
        layout.SystemMusicX(0).Should().Be(
            FirstIndentOf(Metrics, false), "the first system carries full part names");
        layout.SystemMusicX(1).Should().Be(
            LaterIndentOf(Metrics, false), "later systems abbreviate and start nearer the margin");
        layout.SystemMusicX(1).Should().BeLessThan(layout.SystemMusicX(0));
    }

    // --- justification ---------------------------------------------------------------------------

    [Theory]
    [InlineData(480, 700)]
    [InlineData(480, 1100)]
    [InlineData(120, 700)]
    public void EveryFullSystemsLastBarlineMeetsTheRightMargin(int divisions, double pageWidth)
    {
        StaffPageLayout layout = Lay(MakeScore(45, divisions: divisions, jitter: 9), pageWidth);

        layout.SystemCount.Should().BeGreaterThan(2);

        for (int s = 0; s < layout.SystemCount - 1; s++)
        {
            layout.SystemMusicRight(s).Should().BeApproximately(
                pageWidth - Metrics.PageMarginRight,
                Tight,
                "a justified system fills the page exactly - a short one is the mark of a naive engraver");
        }
    }

    [Theory]
    [InlineData(480, 700)]
    [InlineData(120, 520)]
    public void MeasuresTileTheirSystemWithNoGapAndNoOverlap(int divisions, double pageWidth)
    {
        StaffPageLayout layout = Lay(MakeScore(29, divisions: divisions, jitter: 17), pageWidth);

        for (int s = 0; s < layout.SystemCount; s++)
        {
            MeasureRange range = layout.MeasuresIn(s);

            layout.MeasureX(range.First).Should().BeApproximately(
                layout.SystemMusicX(s), Tight, "a system's first measure starts at its indent");

            for (int i = range.First; i < range.EndExclusive - 1; i++)
            {
                layout.MeasureWidth(i).Should().BeGreaterThan(0);
                (layout.MeasureX(i) + layout.MeasureWidth(i)).Should().BeApproximately(
                    layout.MeasureX(i + 1),
                    Tight,
                    "one measure's barline is the next measure's left edge");
            }
        }
    }

    [Fact]
    public void OneStretchFactorIsAppliedToEveryMeasureOfASystem()
    {
        StaffPageLayout layout = Lay(MakeScore(33, notesPerMeasure: 3, jitter: 5), 820);

        for (int s = 0; s < layout.SystemCount; s++)
        {
            MeasureRange range = layout.MeasuresIn(s);
            double stretch = layout.MeasureWidth(range.First) / layout.IdealMeasureWidth(range.First);

            for (int i = range.First; i < range.EndExclusive; i++)
            {
                (layout.MeasureWidth(i) / layout.IdealMeasureWidth(i)).Should().BeApproximately(
                    stretch,
                    1e-9,
                    "stretching some measures of a system more than others would destroy the spacing");
            }
        }
    }

    [Fact]
    public void AMeasureIsNeverNarrowerThanTheFloorBeforeJustification()
    {
        // One sixteenth-note bar: its content is worth about 34 px, which is under the floor.
        StaffPageLayout layout = Lay(MakeScore(6, beats: 1, unit: 16, notesPerMeasure: 1), 900);

        for (int i = 0; i < layout.MeasureCount; i++)
        {
            layout.IdealMeasureWidth(i).Should().BeGreaterThanOrEqualTo(
                Metrics.MinMeasureWidth,
                "an almost-empty bar still needs room for its clef-side padding and barline");
        }
    }

    // --- the ragged last system --------------------------------------------------------------------

    /// <summary>
    /// Both sides of <see cref="StaffPageLayout.RaggedLastThreshold"/>, swept rather than guessed.
    /// </summary>
    /// <remarks>
    /// The measure count that lands the last system just under or just over the threshold depends on
    /// the ideal width, which depends on the spacing exponent - so pinning one count would pin the
    /// spacing model too, and break for the wrong reason the next time it is tuned. Sweeping the
    /// counts and asserting the rule that applies to each covers both branches and stays honest; the
    /// tallies at the end are what stop it passing vacuously.
    /// </remarks>
    [Fact]
    public void AShortLastSystemStaysRaggedAndAFullOneJustifies()
    {
        int ragged = 0;
        int justified = 0;

        for (int count = 1; count <= 40; count++)
        {
            StaffPageLayout layout = Lay(MakeScore(count), 700);
            int last = layout.SystemCount - 1;

            double natural = NaturalWidth(layout, last);
            double line = layout.SystemMusicWidth(last);
            double left = layout.SystemMusicX(last);

            if (natural < line * StaffPageLayout.RaggedLastThreshold)
            {
                ragged++;
                layout.SystemMusicRight(last).Should().BeApproximately(
                    left + natural,
                    Tight,
                    "a short final system keeps its natural width - two bars stretched across a page is the classic naive-engraver look");
            }
            else
            {
                justified++;
                layout.SystemMusicRight(last).Should().BeApproximately(
                    left + line,
                    Tight,
                    "a nearly-full final system is justified like any other");
            }
        }

        ragged.Should().BeGreaterThan(0, "the sweep must actually reach the ragged branch");
        justified.Should().BeGreaterThan(0, "the sweep must actually reach the justified branch");
    }

    [Fact]
    public void OnlyTheLastSystemIsEverLeftRagged()
    {
        StaffPageLayout layout = Lay(MakeScore(41, jitter: 3), 660);

        layout.SystemCount.Should().BeGreaterThan(2);

        for (int s = 0; s < layout.SystemCount - 1; s++)
        {
            NaturalWidth(layout, s).Should().BeLessThan(
                layout.SystemMusicWidth(s) * StaffPageLayout.RaggedLastThreshold * 4,
                "sanity: the fixture's systems are genuinely full");

            layout.SystemMusicRight(s).Should().BeApproximately(
                layout.SystemMusicX(s) + layout.SystemMusicWidth(s),
                Tight,
                "the ragged rule applies to the last system alone");
        }
    }

    // --- the minimum stretch -----------------------------------------------------------------------

    [Fact]
    public void ASingleMeasureTooWideForThePageIsSqueezedButNotPastTheFloor()
    {
        // Sixteen onsets in one bar against a page barely wider than the indent: the natural width is
        // several times what the line can hold, so the squeeze bottoms out on the floor.
        StaffPageLayout layout = Lay(MakeScore(1, notesPerMeasure: 16), 240);

        layout.SystemCount.Should().Be(1);
        layout.MeasuresIn(0).Count.Should().Be(1, "there is nowhere narrower to put the measure");

        double ideal = layout.IdealMeasureWidth(0);
        ideal.Should().BeGreaterThan(
            layout.SystemMusicWidth(0) / StaffPageLayout.MinStretch,
            "the fixture must actually be wide enough to hit the floor");

        layout.MeasureWidth(0).Should().BeApproximately(
            ideal * StaffPageLayout.MinStretch,
            Tight,
            "squeezing past the floor would collapse the noteheads into each other");

        layout.MeasureX(0).Should().BeApproximately(
            layout.SystemMusicX(0), Tight, "the squeezed measure still starts at the indent");
    }

    [Fact]
    public void APageNarrowerThanOneMeasureStillLaysOut()
    {
        StaffPageLayout layout = Lay(MakeScore(4, notesPerMeasure: 8), 60);

        layout.IsEmpty.Should().BeFalse("a hopeless width is still not an error");
        layout.MeasureCount.Should().Be(4);
        layout.SystemCount.Should().Be(4, "nothing fits beside anything, so every measure is its own system");
        layout.SystemMusicWidth(0).Should().BeGreaterThanOrEqualTo(
            Metrics.MinMeasureWidth, "the line width has its own floor so the page never goes negative");

        for (int i = 0; i < layout.MeasureCount; i++)
        {
            layout.MeasureWidth(i).Should().BeGreaterThan(0);
        }
    }

    // --- content height ------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(60)]
    public void ContentHeightIsMarginsPlusSystemsPlusTheGapsBetweenThem(int measureCount)
    {
        StaffPageLayout layout = Lay(MakeScore(measureCount), 700);
        int systems = layout.SystemCount;

        layout.ContentHeight.Should().BeApproximately(
            Metrics.PageMarginTop
            + (systems * layout.SystemBlockHeight)
            + ((systems - 1) * Metrics.SystemGap)
            + Metrics.PageMarginBottom,
            Tight,
            "the page is margins, systems and the white space between them - nothing else");

        layout.SystemTop(0).Should().Be(Metrics.PageMarginTop);

        for (int s = 1; s < systems; s++)
        {
            (layout.SystemTop(s) - layout.SystemTop(s - 1)).Should().BeApproximately(
                layout.SystemBlockHeight + Metrics.SystemGap,
                Tight,
                "systems are evenly pitched down the page");
        }
    }

    [Fact]
    public void ANarrowerPageWrapsIntoMoreSystemsAndIsTaller()
    {
        NotationScore score = MakeScore(40, jitter: 7);

        StaffPageLayout wide = Lay(score, 1400);
        StaffPageLayout narrow = Lay(score, 600);

        narrow.SystemCount.Should().BeGreaterThan(wide.SystemCount);
        narrow.ContentHeight.Should().BeGreaterThan(wide.ContentHeight);
        narrow.MeasureCount.Should().Be(wide.MeasureCount, "wrapping never loses a measure");
    }

    [Fact]
    public void SystemBlockHeightFollowsTheParts()
    {
        StaffPageLayout single = Lay(MakeScore(4), 900);
        StaffPageLayout grand = Lay(MakeScore(4, staffCount: 2), 900, grand: true);
        StaffPageLayout duet = Lay(MakeScore(4, partCount: 2), 900);

        single.SystemBlockHeight.Should().BeApproximately(Metrics.StaffHeight, Tight);
        grand.SystemBlockHeight.Should().BeApproximately(
            (Metrics.StaffHeight * 2) + Metrics.GrandStaffGap,
            Tight,
            "a grand staff is two staves and the brace gap between them");
        duet.SystemBlockHeight.Should().BeApproximately(
            (Metrics.StaffHeight * 2) + Metrics.PartGap,
            Tight,
            "two single-staff parts are separated by the part gap, not the grand-staff gap");
    }

    [Fact]
    public void ASystemsDrawnExtentIncludesItsHeadroomAndFootroom()
    {
        StaffPageLayout layout = Lay(MakeScore(20), 640);

        for (int s = 0; s < layout.SystemCount; s++)
        {
            layout.SystemBlockTop(s).Should().BeApproximately(
                layout.SystemTop(s) - Metrics.SystemHeadroom, Tight);
            layout.SystemBlockBottom(s).Should().BeApproximately(
                layout.SystemTop(s) + layout.SystemBlockHeight + Metrics.SystemFootroom, Tight);
        }

        for (int s = 1; s < layout.SystemCount; s++)
        {
            layout.SystemBlockTop(s).Should().BeGreaterThan(
                layout.SystemBlockBottom(s - 1),
                "a ledger line under one system must not reach into the one below");
        }
    }

    // --- inline time signatures ------------------------------------------------------------------

    [Fact]
    public void TheFirstMeasureNeverPrintsItsOwnTimeSignature()
    {
        StaffPageLayout layout = Lay(MakeScore(6), 900);

        layout.PrintsTimeSignature(0).Should().BeFalse(
            "measure 1's signature belongs in the indent at the head of the first system");
    }

    [Fact]
    public void OnlyAMeasureThatChangesTheSignaturePrintsOne()
    {
        StaffPageLayout layout = Lay(MakeScore(8, signatureChangeAt: 4), 900);

        for (int i = 0; i < layout.MeasureCount; i++)
        {
            layout.PrintsTimeSignature(i).Should().Be(
                i == 4, $"measure {i + 1} prints a signature only if the metre changed there");
        }
    }

    [Fact]
    public void AMeasureThatPrintsASignatureIsWiderThanOneThatDoesNot()
    {
        StaffPageLayout layout = Lay(MakeScore(8, signatureChangeAt: 4), 900);

        layout.IdealMeasureWidth(4).Should().BeApproximately(
            layout.IdealMeasureWidth(3) + Metrics.TimeSignatureWidth,
            Tight,
            "the inline signature is drawn before the first note, so the bar has to hold it");
    }

    [Fact]
    public void CompoundTimeLaysOutJustAsSimpleTimeDoes()
    {
        StaffPageLayout layout = Lay(MakeScore(24, beats: 6, unit: 8, notesPerMeasure: 6, jitter: 5), 700);

        layout.MeasureCount.Should().Be(24);
        layout.MeasureLengthTicks(0).Should().Be(1440, "6/8 at 480 PPQN is three quarters per bar");

        for (int i = 1; i < layout.MeasureCount; i++)
        {
            layout.MeasureStartTicks(i).Should().Be(
                layout.MeasureStartTicks(i - 1) + layout.MeasureLengthTicks(i - 1),
                "measures are contiguous in time as well as on the page");
        }
    }

    // --- the empty page -----------------------------------------------------------------------------

    [Fact]
    public void TheEmptyPageAnswersEveryQueryWithoutThrowing()
    {
        StaffPageLayout empty = StaffPageLayout.Empty;

        empty.IsEmpty.Should().BeTrue();
        empty.MeasureCount.Should().Be(0);
        empty.SystemCount.Should().Be(0);
        empty.ContentHeight.Should().Be(0);
        empty.MaxScrollY(500).Should().Be(0);
        empty.ClampScrollY(9999, 500).Should().Be(0);
        empty.VisibleSystems(0, 500).IsEmpty.Should().BeTrue();
        empty.MeasureIndexForTick(0).Should().Be(-1);
        empty.TryLocate(0, out _, out _).Should().BeFalse();
        empty.FollowPlayhead(0, 0, 500, out double to).Should().BeFalse();
        to.Should().Be(0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-100.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnImpossiblePageWidthGivesTheEmptyPage(double pageWidth)
    {
        Lay(MakeScore(8), pageWidth).IsEmpty.Should().BeTrue(
            "a width of zero or NaN arrives from a binding before the first arrange, not from a bug");
    }

    [Fact]
    public void ANullScoreGivesTheEmptyPage() =>
        Lay(null, 900).IsEmpty.Should().BeTrue();

    [Fact]
    public void AScoreWithNoPartsGivesTheEmptyPage() =>
        Lay(new NotationScore { Divisions = 480, Parts = [] }, 900).IsEmpty.Should().BeTrue();

    [Fact]
    public void AScoreWhosePartsHaveNoMeasuresGivesTheEmptyPage() =>
        Lay(MakeScore(0), 900).IsEmpty.Should().BeTrue();

    // --- rebuilding ------------------------------------------------------------------------------

    [Fact]
    public void ThePageRecordsTheWidthItWasBuiltFor()
    {
        Lay(MakeScore(10), 733.5).PageWidth.Should().Be(733.5);
        StaffPageLayout.Empty.PageWidth.Should().Be(0);
    }

    [Fact]
    public void RebuildingWithIdenticalInputGivesAnIdenticalPage()
    {
        NotationScore score = MakeScore(31, jitter: 13, accidentals: true);

        StaffPageLayout a = Lay(score, 690);
        StaffPageLayout b = Lay(score, 690);

        a.SystemCount.Should().Be(b.SystemCount);
        a.ContentHeight.Should().Be(b.ContentHeight);

        for (int i = 0; i < a.MeasureCount; i++)
        {
            a.SystemOf(i).Should().Be(b.SystemOf(i));
            a.MeasureX(i).Should().Be(b.MeasureX(i));
            a.MeasureWidth(i).Should().Be(b.MeasureWidth(i));
            a.XForTick(i, a.MeasureStartTicks(i) + 37).Should().Be(
                b.XForTick(i, b.MeasureStartTicks(i) + 37));
        }
    }

    [Fact]
    public void AChangeOfPageWidthRewrapsTheSameScore()
    {
        NotationScore score = MakeScore(24);

        StaffPageLayout a = Lay(score, 1500);
        StaffPageLayout b = Lay(score, 500);

        a.SystemCount.Should().NotBe(b.SystemCount, "the break points depend on the width");
        a.MeasureCount.Should().Be(b.MeasureCount);
    }

    [Fact]
    public void AChangeOfZoomRewrapsTheSameScore()
    {
        NotationScore score = MakeScore(24);

        StaffPageLayout small = Lay(score, 900, StaffMetrics.ForZoom(0.6));
        StaffPageLayout large = Lay(score, 900, StaffMetrics.ForZoom(2.0));

        large.SystemCount.Should().BeGreaterThan(
            small.SystemCount, "bigger staves mean fewer bars to the line");
        large.ContentHeight.Should().BeGreaterThan(small.ContentHeight);
    }

    // --- ticks to the page ----------------------------------------------------------------------------

    [Theory]
    [InlineData(480)]
    [InlineData(120)]
    public void EveryTickInTheScoreResolvesToItsOwnMeasure(int divisions)
    {
        StaffPageLayout layout = Lay(MakeScore(12, divisions: divisions, jitter: 11), 700);

        for (int i = 0; i < layout.MeasureCount; i++)
        {
            long start = layout.MeasureStartTicks(i);
            long length = layout.MeasureLengthTicks(i);

            layout.MeasureIndexForTick(start).Should().Be(i);
            layout.MeasureIndexForTick(start + (length / 2) + 1).Should().Be(i);
            layout.MeasureIndexForTick(start + length - 1).Should().Be(i);
        }

        layout.MeasureIndexForTick(-1).Should().Be(-1, "before the score there is no measure");

        long end = layout.MeasureStartTicks(layout.MeasureCount - 1)
            + layout.MeasureLengthTicks(layout.MeasureCount - 1);
        layout.MeasureIndexForTick(end).Should().Be(-1, "the tick one past the end is past the end");
    }

    [Theory]
    [InlineData(480)]
    [InlineData(120)]
    public void XAdvancesMonotonicallyAcrossAMeasureAndStaysInsideIt(int divisions)
    {
        StaffPageLayout layout = Lay(MakeScore(9, divisions: divisions, jitter: 7, accidentals: true), 780);

        for (int i = 0; i < layout.MeasureCount; i++)
        {
            long start = layout.MeasureStartTicks(i);
            long length = layout.MeasureLengthTicks(i);
            double previous = double.NegativeInfinity;

            for (long t = start; t < start + length; t += Math.Max(1, length / 24))
            {
                double x = layout.XForTick(i, t);

                x.Should().BeGreaterThanOrEqualTo(previous, "the playhead never runs backwards");
                x.Should().BeGreaterThanOrEqualTo(layout.MeasureX(i) - Tight);
                x.Should().BeLessThanOrEqualTo(
                    layout.MeasureX(i) + layout.MeasureWidth(i) + Tight,
                    "a tick inside a measure is drawn inside that measure");

                previous = x;
            }
        }
    }

    [Fact]
    public void TryLocateReportsTheSystemThePlayheadIsOn()
    {
        StaffPageLayout layout = Lay(MakeScore(30, jitter: 5), 620);

        for (int i = 0; i < layout.MeasureCount; i++)
        {
            long tick = layout.MeasureStartTicks(i) + 13;

            layout.TryLocate(tick, out int system, out double x).Should().BeTrue();
            system.Should().Be(layout.SystemOf(i));
            x.Should().BeApproximately(layout.XForTick(i, tick), Tight);
        }

        layout.TryLocate(-5, out _, out _).Should().BeFalse();
        layout.TryLocate(long.MaxValue, out _, out _).Should().BeFalse();
    }

    /// <summary>
    /// The two staves of a grand staff, and the several parts of a score, share one column grid.
    /// </summary>
    /// <remarks>
    /// Spacing each part independently is what makes a score look like unrelated strips of music
    /// stacked up: a bass note sounding with a treble note has to be drawn directly beneath it. The
    /// test is that a note only the second part plays still moves the column grid.
    /// </remarks>
    [Fact]
    public void ColumnsAreSharedAcrossEveryPartOfTheScore()
    {
        NotationScore sparse = MakeScore(1, notesPerMeasure: 1, partCount: 2);
        NotationScore dense = MakeScore(1, notesPerMeasure: 1, partCount: 2);

        // Give part 2 an extra onset halfway through the bar that part 1 does not have.
        NotationPart second = dense.Parts[1];
        NotationMeasure measure = second.Measures[0];
        dense = dense with
        {
            Parts =
            [
                dense.Parts[0],
                second with
                {
                    Measures =
                    [
                        measure with
                        {
                            Entries =
                            [
                                .. measure.Entries,
                                new NotationEntry
                                {
                                    Note = new SpelledNote(2, 3, 0, 0),
                                    Duration = new NotatedDuration(NoteValue.Half),
                                    StartTicks = 960,
                                    DurationTicks = 960,
                                    Staff = 1,
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        StaffPageLayout without = Lay(sparse, 900);
        StaffPageLayout with = Lay(dense, 900);

        with.IdealMeasureWidth(0).Should().BeGreaterThan(
            without.IdealMeasureWidth(0),
            "a note in the lower part claims its own column in the shared grid");

        with.XForTick(0, 960).Should().BeGreaterThan(
            with.XForTick(0, 0), "the extra onset is placed after the downbeat, not on it");
    }

    // --- scrolling ---------------------------------------------------------------------------------

    [Fact]
    public void MaxScrollStopsAtTheBottomOfThePage()
    {
        StaffPageLayout layout = Lay(MakeScore(40), 620);

        layout.MaxScrollY(400).Should().BeApproximately(layout.ContentHeight - 400, Tight);
        layout.MaxScrollY(layout.ContentHeight + 500).Should().Be(
            0, "a page shorter than its viewport does not scroll at all");
    }

    [Theory]
    [InlineData(-50.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(double.NaN, 0.0)]
    public void ClampScrollRejectsPositionsAboveThePage(double requested, double expected)
    {
        StaffPageLayout layout = Lay(MakeScore(40), 620);
        layout.ClampScrollY(requested, 400).Should().Be(expected);
    }

    [Fact]
    public void ClampScrollSurvivesBeingSetFarPastTheEnd()
    {
        StaffPageLayout layout = Lay(MakeScore(40), 620);

        layout.ClampScrollY(1e12, 400).Should().Be(layout.MaxScrollY(400));

        // A non-finite offset is a binding fault rather than a scroll position, so it goes to the top
        // of the page with the NaN case rather than to the bottom. No scrollbar can produce one.
        layout.ClampScrollY(double.PositiveInfinity, 400).Should().Be(0);
    }

    /// <summary>
    /// Culling is arithmetic rather than a search, so it is worth checking against the definition it
    /// is supposed to be a shortcut for: exactly the systems whose drawn extent meets the viewport.
    /// </summary>
    [Theory]
    [InlineData(0.0, 400.0)]
    [InlineData(137.0, 400.0)]
    [InlineData(900.0, 250.0)]
    [InlineData(1e9, 400.0)]
    public void VisibleSystemsAreExactlyThoseTheViewportTouches(double scrollY, double viewport)
    {
        StaffPageLayout layout = Lay(MakeScore(60), 620);
        SystemRange range = layout.VisibleSystems(scrollY, viewport);

        for (int s = 0; s < layout.SystemCount; s++)
        {
            bool intersects = layout.SystemBlockBottom(s) >= scrollY
                && layout.SystemBlockTop(s) <= scrollY + viewport;
            bool included = s >= range.First && s < range.EndExclusive;

            included.Should().Be(
                intersects,
                $"system {s} is drawn exactly when its extent, ledger headroom included, meets the viewport");
        }
    }

    [Fact]
    public void NothingIsVisibleInAViewportOfNoHeight()
    {
        StaffPageLayout layout = Lay(MakeScore(20), 620);
        layout.VisibleSystems(0, 0).IsEmpty.Should().BeTrue();
        layout.VisibleSystems(0, -10).IsEmpty.Should().BeTrue();
    }

    // --- following the playhead ---------------------------------------------------------------------

    [Fact]
    public void FollowingMovesToTheSystemThePlayheadIsIn()
    {
        StaffPageLayout layout = Lay(MakeScore(60), 620);
        const double viewport = 400;

        int measure = layout.MeasureCount - 5;
        int system = layout.SystemOf(measure);
        system.Should().BeGreaterThan(2, "the fixture must actually need scrolling");

        layout.FollowPlayhead(layout.MeasureStartTicks(measure), 0, viewport, out double to)
            .Should().BeTrue("the playhead is far below the first screenful");

        to.Should().BeApproximately(
            Math.Clamp(
                layout.SystemBlockTop(system) - (viewport * 0.10), 0, layout.MaxScrollY(viewport)),
            Tight,
            "the system lands a tenth of the way down, leaving headroom to read what is coming");
    }

    [Fact]
    public void FollowingDoesNothingWhenThePlayheadIsAlreadyComfortablyVisible()
    {
        StaffPageLayout layout = Lay(MakeScore(60), 620);
        const double viewport = 600;
        const double scrollY = 300;

        // A measure whose system's whole drawn extent already sits inside the comfortable band.
        int measure = 6;
        int system = layout.SystemOf(measure);
        layout.SystemBlockTop(system).Should().BeGreaterThanOrEqualTo(scrollY + (viewport * 0.10));
        layout.SystemBlockBottom(system).Should().BeLessThanOrEqualTo(scrollY + (viewport * 0.85));

        layout.FollowPlayhead(layout.MeasureStartTicks(measure), scrollY, viewport, out double to)
            .Should().BeFalse("leaving a reader's own scroll position alone is half the contract");
        to.Should().Be(scrollY);
    }

    [Theory]
    [InlineData(400.0)]
    [InlineData(120.0)]
    [InlineData(1200.0)]
    public void FollowingSettlesAfterOneMoveAndNeverThrashes(double viewport)
    {
        StaffPageLayout layout = Lay(MakeScore(60), 620);

        for (int measure = 0; measure < layout.MeasureCount; measure += 3)
        {
            long tick = layout.MeasureStartTicks(measure) + 17;
            double scroll = 0;

            // Sixty calls a second means the second call is the one that matters: if it still
            // reports movement, the page fights every drag the reader makes.
            for (int step = 0; step < 4; step++)
            {
                if (!layout.FollowPlayhead(tick, scroll, viewport, out double to))
                {
                    break;
                }

                step.Should().Be(0, $"following settled on the first move for measure {measure + 1}");
                scroll = to;
            }
        }
    }

    [Fact]
    public void FollowingIgnoresATickThatIsNotInTheScore()
    {
        StaffPageLayout layout = Lay(MakeScore(20), 620);

        layout.FollowPlayhead(-1, 100, 400, out double a).Should().BeFalse("a negative tick hides the playhead");
        a.Should().Be(100);

        layout.FollowPlayhead(long.MaxValue, 100, 400, out double b).Should().BeFalse();
        b.Should().Be(100);

        layout.FollowPlayhead(0, double.NaN, 400, out _).Should().BeFalse("NaN is a binding fault, not a scroll");
        layout.FollowPlayhead(0, 0, 0, out _).Should().BeFalse("nothing is visible in no viewport");
    }

    [Fact]
    public void FollowingNeverScrollsPastTheEndOfThePage()
    {
        StaffPageLayout layout = Lay(MakeScore(60), 620);
        const double viewport = 400;

        long last = layout.MeasureStartTicks(layout.MeasureCount - 1);

        layout.FollowPlayhead(last, 0, viewport, out double to).Should().BeTrue();
        to.Should().BeLessThanOrEqualTo(layout.MaxScrollY(viewport));
        to.Should().BeGreaterThanOrEqualTo(0);
    }

    // --- edges ----------------------------------------------------------------------------------------

    [Fact]
    public void AOneMeasureScoreIsOneRaggedSystem()
    {
        StaffPageLayout layout = Lay(MakeScore(1), 900);

        layout.MeasureCount.Should().Be(1);
        layout.SystemCount.Should().Be(1);
        layout.SystemOf(0).Should().Be(0);
        layout.MeasuresIn(0).Should().Be(new MeasureRange(0, 1));
        layout.MeasureWidth(0).Should().BeApproximately(
            layout.IdealMeasureWidth(0), Tight, "one short bar is never stretched across the page");
        layout.SystemMusicRight(0).Should().BeLessThan(900 - Metrics.PageMarginRight);
    }

    [Fact]
    public void ALongScoreLaysOutEveryMeasure()
    {
        StaffPageLayout layout = Lay(MakeScore(400, jitter: 23), 900);

        layout.MeasureCount.Should().Be(400);
        layout.SystemCount.Should().BeGreaterThan(40);
        layout.SystemOf(399).Should().Be(layout.SystemCount - 1);
        layout.ContentHeight.Should().BeGreaterThan(layout.SystemCount * layout.SystemBlockHeight);
    }

    [Fact]
    public void AGrandStaffPartLaysOutLikeAnyOther()
    {
        StaffPageLayout layout = Lay(MakeScore(24, staffCount: 2, jitter: 9), 760, grand: true);

        layout.MeasureCount.Should().Be(24);
        layout.SystemCount.Should().BeGreaterThan(1);

        for (int s = 0; s < layout.SystemCount; s++)
        {
            MeasureRange range = layout.MeasuresIn(s);
            layout.MeasureX(range.First).Should().BeApproximately(layout.SystemMusicX(s), Tight);
        }

        layout.SystemMusicX(0).Should().BeGreaterThan(
            FirstIndentOf(Metrics, false), "the brace claims its own column in the indent");
    }

    [Fact]
    public void APartThatStopsEarlyDoesNotTruncateThePage()
    {
        NotationScore full = MakeScore(12, partCount: 2);
        NotationScore ragged = full with
        {
            Parts = [full.Parts[0], full.Parts[1] with { Measures = [.. full.Parts[1].Measures.Take(3)] }],
        };

        StaffPageLayout layout = Lay(ragged, 700);

        layout.MeasureCount.Should().Be(12, "the longest part sets the page's measure grid");
        layout.SystemOf(11).Should().Be(layout.SystemCount - 1);
    }

    // --- the page back to ticks, for click-to-seek -------------------------------------------------

    /// <summary>
    /// Clicking where the playhead is drawn seeks to the tick the playhead was drawn for.
    /// </summary>
    /// <remarks>
    /// The round trip is the whole contract, and it has to go through the column interpolation both
    /// ways: a bar's columns are spaced by duration weight, so an inverse that divided the bar
    /// proportionally by time would land short of a whole note and past a run of sixteenths. Ticks
    /// before the measure's first column are excluded because they are genuinely not invertible -
    /// <see cref="StaffPageLayout.XForTick"/> maps all of them onto the first column's x, so the
    /// mapping is many-to-one there by design.
    /// </remarks>
    [Theory]
    [InlineData(480)]
    [InlineData(120)]
    [InlineData(96)]
    public void AClickRoundTripsBackToTheTickThePlayheadWasDrawnFor(int divisions)
    {
        StaffPageLayout layout = Lay(MakeScore(9, divisions: divisions, jitter: 7, accidentals: true), 780);

        for (int i = 0; i < layout.MeasureCount; i++)
        {
            long start = layout.MeasureStartTicks(i);
            long length = layout.MeasureLengthTicks(i);

            // Everything at or left of the measure's left edge collapses onto its first column.
            long firstColumn = layout.TickForX(i, layout.MeasureX(i));

            for (long t = firstColumn; t < start + length; t += Math.Max(1, length / 24))
            {
                long back = layout.TickForX(i, layout.XForTick(i, t));

                Math.Abs(back - t).Should().BeLessThanOrEqualTo(
                    2, $"measure {i} tick {t} should survive the trip to the page and back");
            }
        }
    }

    [Fact]
    public void TickForXNeverRunsBackwardsAcrossAMeasure()
    {
        StaffPageLayout layout = Lay(MakeScore(6, jitter: 5), 700);

        for (int i = 0; i < layout.MeasureCount; i++)
        {
            double left = layout.MeasureX(i);
            double width = layout.MeasureWidth(i);
            long previous = long.MinValue;

            for (int step = 0; step <= 40; step++)
            {
                long tick = layout.TickForX(i, left + (width * step / 40.0));

                tick.Should().BeGreaterThanOrEqualTo(previous);
                tick.Should().BeGreaterThanOrEqualTo(layout.MeasureStartTicks(i));
                tick.Should().BeLessThanOrEqualTo(
                    layout.MeasureStartTicks(i) + layout.MeasureLengthTicks(i),
                    "a click inside a measure belongs to that measure");

                previous = tick;
            }
        }
    }

    /// <summary>
    /// A click resolves against the system it landed on, not merely its x.
    /// </summary>
    /// <remarks>
    /// This is the one that matters on a wrapped page: the same x appears once per system, so an
    /// implementation that ignored y would seek into the first bar of the piece wherever you clicked
    /// on the last line. The fixture is deliberately several systems deep for that reason.
    /// </remarks>
    [Fact]
    public void AClickResolvesAgainstTheSystemItLandedOn()
    {
        StaffPageLayout layout = Lay(MakeScore(30, jitter: 5), 620);
        layout.SystemCount.Should().BeGreaterThan(3, "the fixture must actually wrap to test this");

        for (int i = 0; i < layout.MeasureCount; i++)
        {
            long tick = layout.MeasureStartTicks(i) + 13;

            layout.TryLocate(tick, out int system, out double x).Should().BeTrue();

            double y = layout.SystemTop(system) + (Metrics.StaffHeight / 2);
            layout.TryTickAt(x, y, out long hit).Should().BeTrue();

            Math.Abs(hit - tick).Should().BeLessThanOrEqualTo(
                2, $"measure {i} sits on system {system} and should be found there");
        }
    }

    [Fact]
    public void TheSameXOnDifferentSystemsGivesDifferentTicks()
    {
        StaffPageLayout layout = Lay(MakeScore(30, jitter: 5), 620);

        double x = layout.SystemMusicX(0) + (layout.SystemMusicWidth(0) / 2);

        layout.TryTickAt(x, layout.SystemTop(0) + (Metrics.StaffHeight / 2), out long first).Should().BeTrue();
        layout.TryTickAt(x, layout.SystemTop(2) + (Metrics.StaffHeight / 2), out long third).Should().BeTrue();

        third.Should().BeGreaterThan(
            first, "the third system is later music - ignoring y would return the same tick twice");
    }

    [Fact]
    public void AClickInTheIndentGivesTheFirstMeasureOfThatSystem()
    {
        StaffPageLayout layout = Lay(MakeScore(30, jitter: 5), 620);

        for (int system = 0; system < layout.SystemCount; system++)
        {
            MeasureRange range = layout.MeasuresIn(system);
            double y = layout.SystemTop(system) + (Metrics.StaffHeight / 2);

            // Hard against the left margin: on the brace, the part name or the clef.
            layout.TryTickAt(0, y, out long tick).Should().BeTrue();

            tick.Should().BeGreaterThanOrEqualTo(layout.MeasureStartTicks(range.First));
            tick.Should().BeLessThan(
                layout.MeasureStartTicks(range.First) + layout.MeasureLengthTicks(range.First),
                "clicking the clef seeks to the start of the line, never off it");
        }
    }

    [Fact]
    public void AClickAboveOrBelowThePageClampsOntoIt()
    {
        StaffPageLayout layout = Lay(MakeScore(12, jitter: 5), 620);
        double x = layout.SystemMusicX(0) + 10;

        layout.TryTickAt(x, -5000, out long above).Should().BeTrue();
        layout.TryTickAt(x, layout.ContentHeight + 5000, out long below).Should().BeTrue();

        layout.TryLocate(above, out int aboveSystem, out _).Should().BeTrue();
        layout.TryLocate(below, out int belowSystem, out _).Should().BeTrue();

        aboveSystem.Should().Be(0);
        belowSystem.Should().Be(layout.SystemCount - 1);
    }

    [Fact]
    public void AnEmptyLayoutHasNoTickAnywhere() =>
        StaffPageLayout.Empty.TryTickAt(10, 10, out _).Should().BeFalse();
}
