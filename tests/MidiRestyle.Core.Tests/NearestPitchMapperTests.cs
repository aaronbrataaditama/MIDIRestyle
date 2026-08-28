using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="NearestPitchMapper"/> snaps each note to the nearest pitch the target scale offers,
/// in whatever octave that happens to be. It is the mirror image of the degree mapper: absolute
/// register survives and contour does not, and neither the source scale nor the detected key can
/// reach it - the constructor does not accept them.
/// </summary>
public class NearestPitchMapperTests
{
    private const string Fixture = "Test fixture, cents from CLAUDE.md";

    private static readonly Scale Gong = new(
        "test.gong", "Gong pentatonic", "Chinese", "East Asia",
        [0, 200, 400, 700, 900], Fixture);

    private static readonly Scale Slendro = new(
        "test.slendro", "Slendro (equal-step)", "Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], Fixture, notatable: false);

    private static readonly Scale Rast = new(
        "test.rast", "Maqam Rast", "Maqam", "Middle East",
        [0, 200, 350, 500, 700, 900, 1050], Fixture);

    private static readonly Scale Tritone = new(
        "test.tritone", "Two-degree tritone", "Synthetic", "None",
        [0, 600], Fixture, notatable: false);

    private const int MiddleC = 60;

    private static NearestPitchMapper MapperTo(Scale target, int tonic = MiddleC) =>
        new(target, Pitch.FromMidi(tonic));

    private static double Snap(NearestPitchMapper mapper, double cents)
    {
        MappingResult result = mapper.Map(new Pitch(cents));
        result.IsMapped.Should().BeTrue("snapping cannot drop a note");
        return result.Pitch.Cents;
    }

    // ---------------------------------------------------------------------------------------
    // Snapping.
    // ---------------------------------------------------------------------------------------

    /// <summary>Gong on middle C offers 6000, 6200, 6400, 6700, 6900, 7200 ... in this octave.</summary>
    [Theory]
    [InlineData(6000, 6000)] // exactly on a degree
    [InlineData(6200, 6200)]
    [InlineData(6300, 6400)] // 100 either way - a tie, resolved upward
    [InlineData(6500, 6400)] // 100 down beats 200 up
    [InlineData(6600, 6700)] // 100 up beats 200 down
    [InlineData(7000, 6900)]
    [InlineData(7100, 7200)]
    public void SnapsToTheNearestTargetPitch(double sourceCents, double expectedCents) =>
        Snap(MapperTo(Gong), sourceCents).Should().BeApproximately(expectedCents, 1e-9);

    /// <summary>
    /// C sharp is exactly 100 cents from both C and D. Away from zero, and every candidate is at or
    /// above 0 cents, so the tie goes upward - to D, not back down to C.
    /// </summary>
    [Fact]
    public void TiesRoundAwayFromZero()
    {
        NearestPitchMapper mapper = MapperTo(Gong);

        mapper.Map(Pitch.FromMidi(61)).Pitch.MidiNote.Should().Be(62);
        mapper.Map(Pitch.FromMidi(66)).Pitch.MidiNote.Should().Be(67);  // 6600: 6400 vs 6700
        Snap(mapper, 6550).Should().BeApproximately(6700, 1e-9);        // 150 either way
    }

    /// <summary>A tie between two microtonal candidates resolves upward for the same reason.</summary>
    [Fact]
    public void TiesRoundAwayFromZeroBetweenMicrotonalCandidates()
    {
        NearestPitchMapper mapper = MapperTo(Slendro);

        // Slendro on C: 6000, 6240, 6480, 6720, 6960, 7200. Midpoint of 6000 and 6240 is 6120.
        Snap(mapper, 6120).Should().BeApproximately(6240, 1e-9);
        Snap(mapper, 6360).Should().BeApproximately(6480, 1e-9);
    }

    // ---------------------------------------------------------------------------------------
    // Register.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The defining property. Gong's widest step is 300 cents, so no note can move more than 150
    /// cents - a note never jumps an octave the way the degree mapper routinely does.
    /// </summary>
    [Fact]
    public void RegisterIsPreservedAcrossTheWholeMidiRange()
    {
        NearestPitchMapper mapper = MapperTo(Gong);

        for (int midi = 0; midi <= Pitch.MaxMidiNote; midi++)
        {
            MappingResult result = mapper.Map(Pitch.FromMidi(midi));

            result.IsMapped.Should().BeTrue();
            Math.Abs(result.Pitch.Cents - midi * 100.0).Should()
                .BeLessThanOrEqualTo(150 + 1e-9, "MIDI {0} must stay in its own register", midi);
        }
    }

    [Fact]
    public void RegisterIsPreservedForAnEqualStepScaleToo()
    {
        NearestPitchMapper mapper = MapperTo(Slendro);

        for (int midi = 0; midi <= Pitch.MaxMidiNote; midi++)
        {
            // Slendro's step is 240 cents, so the worst move is 120.
            Math.Abs(Snap(mapper, midi * 100.0) - midi * 100.0).Should()
                .BeLessThanOrEqualTo(120 + 1e-9, "MIDI {0}", midi);
        }
    }

    /// <summary>
    /// Contour flattens rather than inverts: an ascending line comes out non-decreasing, with
    /// repeats where two source notes share a target pitch. The degree mapper is the one that keeps
    /// them distinct.
    /// </summary>
    [Fact]
    public void AnAscendingLineComesOutNonDecreasingAndCollapsesSomeNotes()
    {
        NearestPitchMapper mapper = MapperTo(Gong);
        int[] chromatic = [.. Enumerable.Range(60, 13)];

        double[] snapped = [.. chromatic.Select(m => Snap(mapper, m * 100.0))];

        snapped.Should().BeInAscendingOrder();
        snapped.Distinct().Should().HaveCountLessThan(chromatic.Length);
    }

    // ---------------------------------------------------------------------------------------
    // The candidate set.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void EveryCandidateIsInsideTheMidiRangeSoTheResultAlwaysIsToo()
    {
        foreach (Scale scale in new[] { Gong, Slendro, Rast, Tritone })
        {
            NearestPitchMapper mapper = MapperTo(scale);

            for (int midi = 0; midi <= Pitch.MaxMidiNote; midi++)
            {
                mapper.Map(Pitch.FromMidi(midi)).Pitch.MidiNote.Should()
                    .BeInRange(Pitch.MinMidiNote, Pitch.MaxMidiNote, "{0} on MIDI {1}", scale.Id, midi);
            }
        }
    }

    /// <summary>
    /// The candidate array is binary-searched, so it has to be ascending. It is built ascending
    /// rather than sorted, which is only correct because degrees stay inside [0, 1200) and the
    /// octave step is exactly 1200.
    /// </summary>
    [Fact]
    public void CandidatesAreBuiltAscendingAndCoverTheWholeRange()
    {
        NearestPitchMapper mapper = MapperTo(Gong);

        // Gong on middle C reaches down to 0 cents (MIDI 0, five octaves below) and up to 12700
        // (MIDI 127, the degree at 700 cents five octaves above). 12900 would be MIDI 129.
        Snap(mapper, -5000).Should().BeApproximately(0, 1e-9);
        Snap(mapper, 40000).Should().BeApproximately(12700, 1e-9);

        double previous = double.NegativeInfinity;
        for (int midi = 0; midi <= Pitch.MaxMidiNote; midi++)
        {
            double snapped = Snap(mapper, midi * 100.0);
            snapped.Should().BeGreaterThanOrEqualTo(previous);
            previous = snapped;
        }

        mapper.CandidateCount.Should().BePositive();
    }

    [Fact]
    public void ATwoDegreeTargetScaleBehaves()
    {
        NearestPitchMapper mapper = MapperTo(Tritone);

        // Candidates are every 600 cents from the tonic: ... 5400, 6000, 6600, 7200 ...
        Snap(mapper, 6000).Should().BeApproximately(6000, 1e-9);
        Snap(mapper, 6200).Should().BeApproximately(6000, 1e-9);
        Snap(mapper, 6300).Should().BeApproximately(6600, 1e-9); // tie, resolved upward
        Snap(mapper, 6400).Should().BeApproximately(6600, 1e-9);
        Snap(mapper, 5600).Should().BeApproximately(5400, 1e-9);
        Snap(mapper, 5700).Should().BeApproximately(6000, 1e-9); // tie again, resolved upward
    }

    [Fact]
    public void AMicrotonalTargetKeepsItsCentsRatherThanQuantising()
    {
        NearestPitchMapper mapper = MapperTo(Rast);

        // Rast on C offers 6350 and 7050, neither of which is a 12-TET pitch.
        Snap(mapper, 6340).Should().BeApproximately(6350, 1e-9);
        Snap(mapper, 7060).Should().BeApproximately(7050, 1e-9);

        // And they round to note + bend away from zero, both giving -50.
        mapper.Map(new Pitch(6340)).Pitch.BendCents.Should().BeApproximately(-50, 1e-9);
        mapper.Map(new Pitch(7060)).Pitch.BendCents.Should().BeApproximately(-50, 1e-9);
    }

    [Fact]
    public void ATonicOtherThanCShiftsEveryCandidate()
    {
        NearestPitchMapper mapper = MapperTo(Gong, tonic: 62); // D

        Snap(mapper, 6200).Should().BeApproximately(6200, 1e-9); // the tonic itself
        Snap(mapper, 6000).Should().BeApproximately(5900, 1e-9); // D + 900, an octave down
        Snap(mapper, 6600).Should().BeApproximately(6600, 1e-9); // D + 400
    }

    // ---------------------------------------------------------------------------------------
    // Wiring - what this mapper is structurally unable to see.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TheMapperDeclaresThatItDoesNotUseTheSourceScale()
    {
        NearestPitchMapper mapper = MapperTo(Gong);

        mapper.Strategy.Should().Be(MappingStrategy.NearestPitch);
        mapper.UsesSourceScale.Should().BeFalse();
        MappingOptions.Default.UsesSourceScale.Should().BeTrue(); // the default strategy does
    }

    /// <summary>
    /// Handing the context a source scale and a source tonic changes nothing, because the
    /// NearestPitch arm of <see cref="MappingContext.CreateMapper"/> never passes them on. This is
    /// the behavioural half of the structural claim.
    /// </summary>
    [Fact]
    public void TheSourceScaleAndSourceTonicCannotInfluenceTheResult()
    {
        var options = MappingOptions.Default with { Strategy = MappingStrategy.NearestPitch };

        IPitchMapper withoutSource =
            new MappingContext(Gong, Pitch.FromMidi(MiddleC), options: options).CreateMapper();

        IPitchMapper withSource = new MappingContext(
            Gong, Pitch.FromMidi(MiddleC), Slendro, Pitch.FromMidi(42), options).CreateMapper();

        withoutSource.Should().BeOfType<NearestPitchMapper>();
        withSource.Should().BeOfType<NearestPitchMapper>();

        for (int midi = 0; midi <= Pitch.MaxMidiNote; midi++)
        {
            var note = Pitch.FromMidi(midi);

            withSource.Map(note).Pitch.Cents.Should()
                .BeApproximately(withoutSource.Map(note).Pitch.Cents, 1e-9, "MIDI {0}", midi);
        }
    }

    /// <summary>
    /// <see cref="RangePolicy"/> cannot bind here: the candidates are filtered to the MIDI range at
    /// construction, so a snap cannot leave the range it snapped within.
    /// </summary>
    [Fact]
    public void NoRangePolicyChangesAnything()
    {
        foreach (RangePolicy policy in Enum.GetValues<RangePolicy>())
        {
            NearestPitchMapper mapper =
                new(Gong, Pitch.FromMidi(MiddleC), MappingOptions.Default with { Range = policy });

            for (int midi = 0; midi <= Pitch.MaxMidiNote; midi++)
            {
                MappingResult result = mapper.Map(Pitch.FromMidi(midi));

                result.IsMapped.Should().BeTrue("{0} on MIDI {1}", policy, midi);
                result.Drop.Should().Be(DropCause.None);
            }
        }
    }

    [Fact]
    public void NoNonScaleNotePolicyChangesAnythingEither()
    {
        foreach (NonScaleNotePolicy policy in Enum.GetValues<NonScaleNotePolicy>())
        {
            NearestPitchMapper mapper = new(
                Gong, Pitch.FromMidi(MiddleC), MappingOptions.Default with { NonScaleNotes = policy });

            // MIDI 61 is not in any plausible source scale, and it is still snapped, not dropped.
            mapper.Map(Pitch.FromMidi(61)).Pitch.MidiNote.Should().Be(62);
        }
    }

    [Fact]
    public void ANullTargetScaleIsRejected()
    {
        Action construct = () => _ = new NearestPitchMapper(null!, Pitch.FromMidi(MiddleC));

        construct.Should().Throw<ArgumentNullException>();
    }
}
