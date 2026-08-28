using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="ScaleDegreeMapper"/> re-emits each note at its source degree index in the target
/// scale. Three failure modes dominate this file and each was reproduced during planning: integer
/// division truncating toward zero for notes below the tonic, <c>%</c> keeping the sign of the
/// dividend and indexing <c>DegreeCents[-1]</c>, and unclamped output overflowing the MIDI range
/// because degree mapping multiplies a piece's range by <c>n_target / n_source</c>.
/// <para>
/// Every expected value in the golden tests was computed by hand from the formula, never generated
/// from the implementation.
/// </para>
/// </summary>
public class ScaleDegreeMapperTests
{
    private const string Fixture = "Test fixture, cents from CLAUDE.md";

    private static readonly Scale CMajor = new(
        "test.cmajor", "C major", "Western", "Europe",
        [0, 200, 400, 500, 700, 900, 1100], Fixture);

    private static readonly Scale Gong = new(
        "test.gong", "Gong pentatonic", "Chinese", "East Asia",
        [0, 200, 400, 700, 900], Fixture);

    private static readonly Scale Hijaz = new(
        "test.hijaz", "Hijaz", "Maqam", "Middle East",
        [0, 100, 400, 500, 700, 800, 1000], Fixture);

    private static readonly Scale Rast = new(
        "test.rast", "Maqam Rast", "Maqam", "Middle East",
        [0, 200, 350, 500, 700, 900, 1050], Fixture);

    private static readonly Scale Slendro = new(
        "test.slendro", "Slendro (equal-step)", "Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], Fixture, notatable: false);

    /// <summary>The smallest scale <see cref="Scale.MinDegrees"/> permits.</summary>
    private static readonly Scale Tritone = new(
        "test.tritone", "Two-degree tritone", "Synthetic", "None",
        [0, 600], Fixture, notatable: false);

    private const int MiddleC = 60;

    private static ScaleDegreeMapper MapperTo(
        Scale target,
        Scale? source = null,
        MappingOptions? options = null,
        int sourceTonic = MiddleC,
        int targetTonic = MiddleC) =>
        new(new MappingContext(
            target,
            Pitch.FromMidi(targetTonic),
            source ?? CMajor,
            Pitch.FromMidi(sourceTonic),
            options));

    private static double MappedCents(ScaleDegreeMapper mapper, int midiNote)
    {
        MappingResult result = mapper.Map(Pitch.FromMidi(midiNote));
        result.IsMapped.Should().BeTrue("MIDI {0} should map, not drop", midiNote);
        return result.Pitch.Cents;
    }

    // ---------------------------------------------------------------------------------------
    // Golden tests. C major on middle C into each target on middle C.
    //
    //   d = sourceOctave * 7 + sourceDegree, so C4..C5 give d = 0..7
    //   cents = 6000 + floor(d / n) * 1200 + target.DegreeCents[((d % n) + n) % n]
    // ---------------------------------------------------------------------------------------

    /// <summary>Gong [0,200,400,700,900], n=5. d=0..7 gives i=0,1,2,3,4,0,1,2 and oct=0,0,0,0,0,1,1,1.</summary>
    [Theory]
    [InlineData(60, 6000)] // d=0  -> 6000 + 0    + 0
    [InlineData(62, 6200)] // d=1  -> 6000 + 0    + 200
    [InlineData(64, 6400)] // d=2  -> 6000 + 0    + 400
    [InlineData(65, 6700)] // d=3  -> 6000 + 0    + 700
    [InlineData(67, 6900)] // d=4  -> 6000 + 0    + 900
    [InlineData(69, 7200)] // d=5  -> 6000 + 1200 + 0
    [InlineData(71, 7400)] // d=6  -> 6000 + 1200 + 200
    [InlineData(72, 7600)] // d=7  -> 6000 + 1200 + 400
    public void CMajorIntoGong(int sourceMidi, double expectedCents) =>
        MappedCents(MapperTo(Gong), sourceMidi).Should().BeApproximately(expectedCents, 1e-9);

    /// <summary>Hijaz [0,100,400,500,700,800,1000], n=7. Same degree count, so no wraparound until d=7.</summary>
    [Theory]
    [InlineData(60, 6000)] // d=0
    [InlineData(62, 6100)] // d=1
    [InlineData(64, 6400)] // d=2
    [InlineData(65, 6500)] // d=3
    [InlineData(67, 6700)] // d=4
    [InlineData(69, 6800)] // d=5
    [InlineData(71, 7000)] // d=6
    [InlineData(72, 7200)] // d=7 -> 6000 + 1200 + 0
    public void CMajorIntoHijaz(int sourceMidi, double expectedCents) =>
        MappedCents(MapperTo(Hijaz), sourceMidi).Should().BeApproximately(expectedCents, 1e-9);

    /// <summary>Rast [0,200,350,500,700,900,1050], n=7. The two neutral degrees land off the 12-TET grid.</summary>
    [Theory]
    [InlineData(60, 6000)]
    [InlineData(62, 6200)]
    [InlineData(64, 6350)] // the neutral third
    [InlineData(65, 6500)]
    [InlineData(67, 6700)]
    [InlineData(69, 6900)]
    [InlineData(71, 7050)] // the neutral seventh
    [InlineData(72, 7200)]
    public void CMajorIntoRast(int sourceMidi, double expectedCents) =>
        MappedCents(MapperTo(Rast), sourceMidi).Should().BeApproximately(expectedCents, 1e-9);

    /// <summary>Slendro [0,240,480,720,960], n=5. Five equal-ish steps, so 7 into 5 wraps.</summary>
    [Theory]
    [InlineData(60, 6000)] // d=0  -> 6000 + 0    + 0
    [InlineData(62, 6240)] // d=1  -> 6000 + 0    + 240
    [InlineData(64, 6480)] // d=2  -> 6000 + 0    + 480
    [InlineData(65, 6720)] // d=3  -> 6000 + 0    + 720
    [InlineData(67, 6960)] // d=4  -> 6000 + 0    + 960
    [InlineData(69, 7200)] // d=5  -> 6000 + 1200 + 0
    [InlineData(71, 7440)] // d=6  -> 6000 + 1200 + 240
    [InlineData(72, 7680)] // d=7  -> 6000 + 1200 + 480
    public void CMajorIntoSlendro(int sourceMidi, double expectedCents) =>
        MappedCents(MapperTo(Slendro), sourceMidi).Should().BeApproximately(expectedCents, 1e-9);

    /// <summary>
    /// Both of Rast's neutral degrees must round the same way. Under banker's rounding 63.5 goes to
    /// the even 64 and 70.5 to the even 70, so one inflection produces two different offsets - and
    /// the allocator then asks for three channels instead of two.
    /// </summary>
    [Theory]
    [InlineData(64, 64, -50.0)] // 6350 cents
    [InlineData(71, 71, -50.0)] // 7050 cents
    public void RastNeutralDegreesBothRoundAwayFromZeroAndSoShareOneOffset(
        int sourceMidi, int expectedNote, double expectedBend)
    {
        Pitch mapped = MapperTo(Rast).Map(Pitch.FromMidi(sourceMidi)).Pitch;

        mapped.MidiNote.Should().Be(expectedNote);
        mapped.BendCents.Should().BeApproximately(expectedBend, 1e-9);
    }

    // ---------------------------------------------------------------------------------------
    // Notes below the tonic: the floor-division and positive-modulo regression.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// G3 is a fourth below middle C, so d = -1 * 7 + 4 = -3. In Gong that is floor(-3/5) = -1 and
    /// ((-3 % 5) + 5) % 5 = 2. Plain integer division gives octave 0 and plain <c>%</c> gives index
    /// -3, which throws. Any bass line hits this on its first note.
    /// </summary>
    [Fact]
    public void AFourthBelowTheTonicMapsCorrectlyInEveryTarget()
    {
        // d = -3 throughout.
        MappedCents(MapperTo(Gong), 55).Should().BeApproximately(5200, 1e-9);    // oct -1, i 2 -> 400
        MappedCents(MapperTo(Hijaz), 55).Should().BeApproximately(5500, 1e-9);   // oct -1, i 4 -> 700
        MappedCents(MapperTo(Rast), 55).Should().BeApproximately(5500, 1e-9);    // oct -1, i 4 -> 700
        MappedCents(MapperTo(Slendro), 55).Should().BeApproximately(5280, 1e-9); // oct -1, i 2 -> 480
    }

    /// <summary>
    /// C2 is two octaves below the tonic: d = -2 * 7 + 0 = -14. In Gong, floor(-14/5) = -3 (not -2,
    /// which truncation gives) and ((-14 % 5) + 5) % 5 = 1 (not -4).
    /// </summary>
    [Fact]
    public void TwoOctavesBelowTheTonicMapsCorrectly()
    {
        MappedCents(MapperTo(Gong), 36).Should().BeApproximately(2600, 1e-9);    // 6000 - 3600 + 200
        MappedCents(MapperTo(Slendro), 36).Should().BeApproximately(2640, 1e-9); // 6000 - 3600 + 240
        MappedCents(MapperTo(CMajor), 36).Should().BeApproximately(3600, 1e-9);  // 6000 - 2400 + 0
    }

    [Fact]
    public void NegativeDegreeIndicesNeverThrow()
    {
        ScaleDegreeMapper mapper = MapperTo(Gong);

        for (int midi = 0; midi < MiddleC; midi++)
        {
            Action map = () => mapper.Map(Pitch.FromMidi(midi));
            map.Should().NotThrow("MIDI {0} is below the tonic", midi);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Identity and contour.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The cheapest available proof that the decomposition and the re-emission agree about what a
    /// degree index means: the same scale and the same tonic on both sides must be a no-op, in every
    /// octave above and below the tonic.
    /// </summary>
    [Fact]
    public void MappingAScaleOntoItselfWithTheSameTonicIsTheIdentity()
    {
        ScaleDegreeMapper mapper = MapperTo(CMajor);
        int[] pitchClasses = [0, 2, 4, 5, 7, 9, 11];

        for (int midi = 0; midi <= Pitch.MaxMidiNote; midi++)
        {
            if (!pitchClasses.Contains(midi % 12))
            {
                continue;
            }

            MappedCents(mapper, midi).Should().BeApproximately(midi * 100.0, 1e-9);
        }
    }

    [Fact]
    public void MappingAPentatonicOntoItselfIsAlsoTheIdentity()
    {
        ScaleDegreeMapper mapper = MapperTo(Gong, source: Gong);
        int[] pitchClasses = [0, 2, 4, 7, 9];

        for (int midi = 0; midi <= Pitch.MaxMidiNote; midi++)
        {
            if (pitchClasses.Contains(midi % 12))
            {
                MappedCents(mapper, midi).Should().BeApproximately(midi * 100.0, 1e-9);
            }
        }
    }

    /// <summary>Contour is the whole reason this strategy exists, and 7 into 5 is where it is tested.</summary>
    [Fact]
    public void SevenIntoFiveKeepsAnAscendingLineAscending()
    {
        ScaleDegreeMapper mapper = MapperTo(Gong);
        int[] ascending = [48, 50, 52, 53, 55, 57, 59, 60, 62, 64, 65, 67, 69, 71, 72];

        double[] mapped = [.. ascending.Select(m => MappedCents(mapper, m))];

        mapped.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void SevenIntoFiveWidensTheRangeByExactlyTheDegreeRatio()
    {
        ScaleDegreeMapper mapper = MapperTo(Slendro);

        // One source octave (7 degrees) becomes 7/5 of a target octave.
        double span = MappedCents(mapper, 72) - MappedCents(mapper, 60);

        span.Should().BeApproximately(1680, 1e-9); // 7680 - 6000, i.e. 1.4 x 1200
    }

    // ---------------------------------------------------------------------------------------
    // Non-scale notes.
    // ---------------------------------------------------------------------------------------

    private static MappingOptions With(NonScaleNotePolicy policy) =>
        MappingOptions.Default with { NonScaleNotes = policy };

    [Fact]
    public void SnapToNearestSourceDegreeIsTheDefault() =>
        MappingOptions.Default.NonScaleNotes.Should().Be(NonScaleNotePolicy.SnapToNearestSourceDegree);

    /// <summary>C sharp is 100 cents from both C and D, and a tie takes the higher degree.</summary>
    [Fact]
    public void SnapToNearestSourceDegreeResolvesATieUpward() =>
        MappedCents(MapperTo(Gong, options: With(NonScaleNotePolicy.SnapToNearestSourceDegree)), 61)
            .Should().BeApproximately(6200, 1e-9); // snapped to d=1, Gong degree 1

    [Theory]
    [InlineData(6120, 6200)] // 120 cents in: nearer D (80) than C (120) -> d=1 -> Gong 200
    [InlineData(6080, 6000)] // 80 cents in:  nearer C (80) than D (120) -> d=0 -> Gong 0
    [InlineData(7180, 7600)] // 1180 cents in: nearer the octave (20) than B (80) -> d=7 -> oct 1, i 2
    public void SnapToNearestSourceDegreePicksTheGenuinelyNearestDegree(
        double sourceCents, double expectedCents)
    {
        ScaleDegreeMapper mapper =
            MapperTo(Gong, options: With(NonScaleNotePolicy.SnapToNearestSourceDegree));

        mapper.Map(new Pitch(sourceCents)).Pitch.Cents.Should().BeApproximately(expectedCents, 1e-9);
    }

    [Fact]
    public void PassThroughLeavesANonScaleNoteWhereItWas()
    {
        MappingResult result =
            MapperTo(Gong, options: With(NonScaleNotePolicy.PassThrough)).Map(Pitch.FromMidi(61));

        result.IsMapped.Should().BeTrue();
        result.Pitch.Cents.Should().BeApproximately(6100, 1e-9);
    }

    [Fact]
    public void PassThroughStillMapsNotesThatAreInTheSourceScale() =>
        MappedCents(MapperTo(Gong, options: With(NonScaleNotePolicy.PassThrough)), 65)
            .Should().BeApproximately(6700, 1e-9);

    [Fact]
    public void DropReportsTheCauseRatherThanThrowing()
    {
        MappingResult result =
            MapperTo(Gong, options: With(NonScaleNotePolicy.Drop)).Map(Pitch.FromMidi(61));

        result.IsMapped.Should().BeFalse();
        result.Drop.Should().Be(DropCause.NotInSourceScale);
    }

    [Fact]
    public void DropCountsEveryChromaticNoteInAnOctaveAndKeepsEverySevenScaleNote()
    {
        ScaleDegreeMapper mapper = MapperTo(Gong, options: With(NonScaleNotePolicy.Drop));

        int dropped = Enumerable.Range(60, 12).Count(m => !mapper.Map(Pitch.FromMidi(m)).IsMapped);

        dropped.Should().Be(5); // the five black keys of C major
    }

    // ---------------------------------------------------------------------------------------
    // Range policy.
    // ---------------------------------------------------------------------------------------

    private static MappingOptions With(RangePolicy policy) =>
        MappingOptions.Default with { Range = policy };

    [Fact]
    public void ShiftIntoRangeIsTheDefault() =>
        MappingOptions.Default.Range.Should().Be(RangePolicy.ShiftIntoRange);

    /// <summary>
    /// The plan's worked example: MIDI 21..108 into Slendro lands at 4.80..127.20. Worth pinning,
    /// because it is <em>almost</em> an overflow - both ends survive away-from-zero rounding, as
    /// MIDI 5 and MIDI 127. The plan calls this case an overflow; it is not, quite. See the next
    /// two tests for the cases that genuinely are.
    /// </summary>
    [Fact]
    public void FullPianoRangeIntoSlendroOnTheSameTonicLandsExactlyOnTheEdgesWithoutOverflowing()
    {
        ScaleDegreeMapper mapper = MapperTo(Slendro, options: With(RangePolicy.Drop));

        MappedCents(mapper, 21).Should().BeApproximately(480, 1e-9);     // 4.80 semitones -> MIDI 5
        MappedCents(mapper, 108).Should().BeApproximately(12720, 1e-9);  // 127.20         -> MIDI 127

        new Pitch(480).MidiNote.Should().Be(5);
        new Pitch(12720).MidiNote.Should().Be(127);
    }

    [Fact]
    public void FullPianoRangeIntoSlendroKeepsEveryNoteInsideMidiRangeUnderShiftIntoRange()
    {
        ScaleDegreeMapper mapper = MapperTo(Slendro, options: With(RangePolicy.ShiftIntoRange));

        for (int midi = 21; midi <= 108; midi++)
        {
            MappingResult result = mapper.Map(Pitch.FromMidi(midi));

            result.IsMapped.Should().BeTrue("MIDI {0} should be shifted, not dropped", midi);
            result.Pitch.MidiNote.Should()
                .BeInRange(Pitch.MinMidiNote, Pitch.MaxMidiNote, "MIDI {0} must fit", midi);
        }
    }

    /// <summary>
    /// Move the target tonic up one whole tone and the same piano range does overflow: MIDI 108
    /// reaches 12920 cents, which is MIDI 129, and <c>(SevenBitNumber)129</c> throws at export.
    /// </summary>
    [Fact]
    public void FullPianoRangeIntoSlendroOverflowsOnADTonicAndDropReportsIt()
    {
        ScaleDegreeMapper mapper =
            MapperTo(Slendro, options: With(RangePolicy.Drop), targetTonic: 62);

        int dropped = Enumerable.Range(21, 88).Count(m => !mapper.Map(Pitch.FromMidi(m)).IsMapped);

        dropped.Should().BePositive();
        mapper.Map(Pitch.FromMidi(108)).Drop.Should().Be(DropCause.OutOfRange);

        // And the same input under the default policy fits.
        MapperTo(Slendro, options: With(RangePolicy.ShiftIntoRange), targetTonic: 62)
            .Map(Pitch.FromMidi(108)).Pitch.MidiNote
            .Should().BeInRange(Pitch.MinMidiNote, Pitch.MaxMidiNote);
    }

    [Fact]
    public void TheWholeMidiRangeIntoSlendroDropsAtBothEndsUnderDrop()
    {
        ScaleDegreeMapper mapper = MapperTo(Slendro, options: With(RangePolicy.Drop));

        int dropped = Enumerable.Range(0, 128).Count(m => !mapper.Map(Pitch.FromMidi(m)).IsMapped);

        dropped.Should().BePositive();
        mapper.Map(Pitch.FromMidi(0)).Drop.Should().Be(DropCause.OutOfRange);
        mapper.Map(Pitch.FromMidi(127)).Drop.Should().Be(DropCause.OutOfRange);
    }

    // 7 -> 5, C major into Slendro, both on middle C. Hand-computed:
    //   MIDI 0:   d = -35 -> oct -7, i 0 -> 6000 - 8400 +   0 = -2400
    //   MIDI 1:   snaps up to d = -34 -> oct -7, i 1 -> 6000 - 8400 + 240 = -2160
    //   MIDI 126: snaps up to d =  39 -> oct  7, i 4 -> 6000 + 8400 + 960 = 15360
    //   MIDI 127: d = 39, same as 126
    [Theory]
    [InlineData(0, -2400)]
    [InlineData(1, -2160)]
    [InlineData(126, 15360)]
    [InlineData(127, 15360)]
    public void SevenIntoFiveAtTheMidiExtremesLandsOutsideTheRangeBeforeAnyPolicyRuns(
        int sourceMidi, double unclampedCents)
    {
        // PassThrough is irrelevant here; what matters is that the raw formula overflows, which is
        // exactly why RangePolicy exists. FoldOctave is used to observe the value indirectly.
        ScaleDegreeMapper dropping = MapperTo(Slendro, options: With(RangePolicy.Drop));

        dropping.Map(Pitch.FromMidi(sourceMidi)).Drop.Should().Be(DropCause.OutOfRange);

        new Pitch(unclampedCents).IsInMidiRange.Should().BeFalse();
    }

    // The same four notes under ShiftIntoRange, shifted by whole octaves until they fit:
    //   -2400 -> -1200 -> 0          (MIDI 0)
    //   -2160 -> -960  -> 240        (MIDI 2)
    //   15360 -> 14160 -> 12960 -> 11760   (MIDI 118; 12960 is 129.6, still too high)
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 240)]
    [InlineData(126, 11760)]
    [InlineData(127, 11760)]
    public void SevenIntoFiveAtTheMidiExtremesShiftsIntoRangeByWholeOctaves(
        int sourceMidi, double expectedCents)
    {
        ScaleDegreeMapper mapper = MapperTo(Slendro, options: With(RangePolicy.ShiftIntoRange));
        MappingResult result = mapper.Map(Pitch.FromMidi(sourceMidi));

        result.IsMapped.Should().BeTrue();
        result.Pitch.Cents.Should().BeApproximately(expectedCents, 1e-9);
        result.Pitch.MidiNote.Should().BeInRange(Pitch.MinMidiNote, Pitch.MaxMidiNote);
    }

    // FoldOctave reflects off 0 and 12700 cents rather than shifting:
    //   -2400 -> 2400   |  -2160 -> 2160  |  15360 -> 12700 - (15360 - 12700) = 10040
    [Theory]
    [InlineData(0, 2400)]
    [InlineData(1, 2160)]
    [InlineData(126, 10040)]
    [InlineData(127, 10040)]
    public void SevenIntoFiveAtTheMidiExtremesFoldsBackIntoRange(
        int sourceMidi, double expectedCents)
    {
        ScaleDegreeMapper mapper = MapperTo(Slendro, options: With(RangePolicy.FoldOctave));
        MappingResult result = mapper.Map(Pitch.FromMidi(sourceMidi));

        result.IsMapped.Should().BeTrue();
        result.Pitch.Cents.Should().BeApproximately(expectedCents, 1e-9);
        result.Pitch.MidiNote.Should().BeInRange(Pitch.MinMidiNote, Pitch.MaxMidiNote);
    }

    // 5 -> 7 compresses instead of widening, so the extremes stay comfortably inside the range.
    // Gong source into Hijaz target, both on middle C:
    //   MIDI 0:   d = -25 -> oct -4, i 3 -> 6000 - 4800 + 500 =  1700 (MIDI 17)
    //   MIDI 1:   snaps up to d = -24 -> oct -4, i 4 -> 6000 - 4800 + 700 = 1900 (MIDI 19)
    //   MIDI 126: snaps to d = 28 -> oct 4, i 0 -> 6000 + 4800 + 0 = 10800 (MIDI 108)
    //   MIDI 127: d = 28, same
    [Theory]
    [InlineData(0, 1700)]
    [InlineData(1, 1900)]
    [InlineData(126, 10800)]
    [InlineData(127, 10800)]
    public void FiveIntoSevenAtTheMidiExtremesNeedsNoRangePolicyAtAll(
        int sourceMidi, double expectedCents)
    {
        foreach (RangePolicy policy in Enum.GetValues<RangePolicy>())
        {
            ScaleDegreeMapper mapper = MapperTo(Hijaz, source: Gong, options: With(policy));
            MappingResult result = mapper.Map(Pitch.FromMidi(sourceMidi));

            result.IsMapped.Should().BeTrue("{0} should not need to intervene", policy);
            result.Pitch.Cents.Should().BeApproximately(expectedCents, 1e-9);
        }
    }

    [Fact]
    public void FiveIntoSevenKeepsTheWholeMidiRangeInRangeUnderEveryPolicy()
    {
        foreach (RangePolicy policy in Enum.GetValues<RangePolicy>())
        {
            ScaleDegreeMapper mapper = MapperTo(Hijaz, source: Gong, options: With(policy));

            for (int midi = 0; midi <= Pitch.MaxMidiNote; midi++)
            {
                MappingResult result = mapper.Map(Pitch.FromMidi(midi));

                result.IsMapped.Should().BeTrue("{0} on MIDI {1}", policy, midi);
                result.Pitch.MidiNote.Should().BeInRange(Pitch.MinMidiNote, Pitch.MaxMidiNote);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Degenerate but legal scales.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Two degrees is <see cref="Scale.MinDegrees"/>. The modulo has the least room to hide a sign
    /// bug here, and the octave multiplier is at its most aggressive: one source octave becomes 3.5
    /// target octaves.
    /// </summary>
    [Theory]
    [InlineData(60, 6000)]  // d=0  -> oct 0, i 0
    [InlineData(62, 6600)]  // d=1  -> oct 0, i 1
    [InlineData(64, 7200)]  // d=2  -> oct 1, i 0
    [InlineData(65, 7800)]  // d=3  -> oct 1, i 1
    [InlineData(55, 4200)]  // d=-3 -> oct -2 (floor(-1.5)), i 1 -> 6000 - 2400 + 600
    [InlineData(53, 3600)]  // d=-4 -> oct -2,                i 0 -> 6000 - 2400 + 0
    public void ATwoDegreeTargetScaleBehaves(int sourceMidi, double expectedCents) =>
        MappedCents(MapperTo(Tritone, options: With(RangePolicy.Drop)), sourceMidi)
            .Should().BeApproximately(expectedCents, 1e-9);

    [Fact]
    public void ATwoDegreeTargetScaleStillKeepsAscendingLinesAscending()
    {
        ScaleDegreeMapper mapper = MapperTo(Tritone, options: With(RangePolicy.Drop));
        int[] ascending = [55, 57, 59, 60, 62, 64, 65];

        double[] mapped = [.. ascending.Select(m => mapper.Map(Pitch.FromMidi(m)).Pitch.Cents)];

        mapped.Should().BeInAscendingOrder();
    }

    [Fact]
    public void ATwoDegreeSourceScaleBehaves()
    {
        // Source [0, 600] into C major: d = 0, 1, 2, ... every 600 cents.
        ScaleDegreeMapper mapper = MapperTo(CMajor, source: Tritone);

        MappedCents(mapper, 60).Should().BeApproximately(6000, 1e-9); // d=0  -> i 0
        MappedCents(mapper, 66).Should().BeApproximately(6200, 1e-9); // d=1  -> i 1
        MappedCents(mapper, 72).Should().BeApproximately(6400, 1e-9); // d=2  -> i 2
        MappedCents(mapper, 54).Should().BeApproximately(5900, 1e-9); // d=-1 -> oct -1, i 6 -> 1100
    }

    // ---------------------------------------------------------------------------------------
    // Wiring.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TheMapperDeclaresThatItUsesTheSourceScale()
    {
        ScaleDegreeMapper mapper = MapperTo(Gong);

        mapper.Strategy.Should().Be(MappingStrategy.ScaleDegree);
        mapper.UsesSourceScale.Should().BeTrue();
    }

    [Fact]
    public void ConstructingWithoutASourceScaleFailsAtConstructionRatherThanPerNote()
    {
        var context = new MappingContext(Gong, Pitch.FromMidi(MiddleC));

        Action construct = () => _ = new ScaleDegreeMapper(context);

        construct.Should().Throw<ArgumentException>().WithMessage("*source scale*");
    }

    [Fact]
    public void TheContextBuildsAScaleDegreeMapperByDefault()
    {
        var context = new MappingContext(Gong, Pitch.FromMidi(MiddleC), CMajor, Pitch.FromMidi(MiddleC));

        context.CreateMapper().Should().BeOfType<ScaleDegreeMapper>();
    }

    [Fact]
    public void ADifferentSourceTonicShiftsTheDegreeIndices()
    {
        // G major source on G, into Gong on C. G4 (67) is source degree 0, so it maps to the target
        // tonic 6000 - the register change is the strategy working, not a bug.
        ScaleDegreeMapper mapper = MapperTo(Gong, sourceTonic: 67, targetTonic: MiddleC);

        MappedCents(mapper, 67).Should().BeApproximately(6000, 1e-9);
        MappedCents(mapper, 69).Should().BeApproximately(6200, 1e-9); // d=1
        MappedCents(mapper, 60).Should().BeApproximately(5000, 1e-9); // d=-4 -> oct -1, i 1 -> 200
    }
}
