using MidiRestyle.Core.Notation;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="BeamGrouper"/> decides what a reader sees before they have read a single pitch.
/// Beaming is how the eye finds the beat, so the cases pinned here are the ones where a wrong
/// grouping does not merely look untidy but says the wrong metre: four eighths beamed as one group
/// in 4/4 reads as 2/2, and six eighths beamed in twos in 6/8 reads as 3/4.
/// </summary>
/// <remarks>
/// Every fixture is hand-built rather than driven through <see cref="NotationBuilder"/>, so a
/// failure here points at the grouping rule and not at the quantiser upstream of it.
/// <see cref="NotationBuilderTests"/> covers the join between the two.
/// </remarks>
public class BeamGrouperTests
{
    private const int Ppqn = 480;
    private const long Eighth = Ppqn / 2;
    private const long Sixteenth = Ppqn / 4;
    private const long DottedEighth = Eighth + Sixteenth;

    private static readonly SpelledNote MiddleC = new(0, 4, 0, ResidualCents: 0);

    private static NotationEntry Note(
        long start,
        long ticks,
        NoteValue value,
        int dots = 0,
        Tuplet tuplet = default,
        int staff = 1,
        int voice = 1,
        bool chord = false) => new()
        {
            Note = MiddleC,
            Duration = new NotatedDuration(value, dots, tuplet),
            StartTicks = start,
            DurationTicks = ticks,
            Staff = staff,
            Voice = voice,
            IsChordMember = chord,
        };

    private static NotationEntry Rest(long start, long ticks, NoteValue value) => new()
    {
        Note = null,
        Duration = new NotatedDuration(value),
        StartTicks = start,
        DurationTicks = ticks,
    };

    /// <summary>A 4/4 bar at 480 PPQN: four quarter beats, so beams group two eighths at a time.</summary>
    private static MeasureSpan CommonTime => new(1, 0, Ppqn * 4, 4, 4, SignatureChanged: true);

    /// <summary>A 6/8 bar at 480 PPQN. The printed beat is the eighth; the beamed group is not.</summary>
    private static MeasureSpan SixEight => new(1, 0, Eighth * 6, 6, 8, SignatureChanged: true);

    private static IReadOnlyList<IReadOnlyList<BeamState>> Assign(
        MeasureSpan measure, params NotationEntry[] entries) =>
        BeamGrouper.Assign(entries, measure);

    /// <summary>The beam states of one entry, as a plain array the assertions can compare against.</summary>
    private static BeamState[] Levels(IReadOnlyList<IReadOnlyList<BeamState>> beams, int index) =>
        [.. beams[index]];

    // --- group boundaries ----------------------------------------------------------------

    /// <summary>
    /// The difference between 4/4 and 2/2 on the page. A beam may not cross a beat in simple time,
    /// so four eighths are two groups of two - one per quarter - and never one group of four.
    /// </summary>
    [Fact]
    public void FourEighthsInCommonTimeBeamAsTwoGroupsOfTwo()
    {
        var beams = Assign(
            CommonTime,
            Note(0, Eighth, NoteValue.Eighth),
            Note(Eighth, Eighth, NoteValue.Eighth),
            Note(Eighth * 2, Eighth, NoteValue.Eighth),
            Note(Eighth * 3, Eighth, NoteValue.Eighth));

        Levels(beams, 0).Should().Equal([BeamState.Begin], "beat 1 opens the first group");
        Levels(beams, 1).Should().Equal([BeamState.End], "the first group closes at the barline of beat 2");
        Levels(beams, 2).Should().Equal([BeamState.Begin], "beat 2 opens a group of its own");
        Levels(beams, 3).Should().Equal([BeamState.End]);
    }

    /// <summary>
    /// Compound time beams by the dotted quarter, not by the printed eighth beat. This is the whole
    /// visual difference between 6/8 and 3/4 - the same six eighths in the same bar, grouped in
    /// threes rather than twos - so grouping 6/8 on the printed beat would make every compound bar
    /// read as simple time.
    /// </summary>
    [Fact]
    public void SixEighthsInCompoundTimeBeamAsTwoGroupsOfThree()
    {
        var beams = Assign(
            SixEight,
            Note(0, Eighth, NoteValue.Eighth),
            Note(Eighth, Eighth, NoteValue.Eighth),
            Note(Eighth * 2, Eighth, NoteValue.Eighth),
            Note(Eighth * 3, Eighth, NoteValue.Eighth),
            Note(Eighth * 4, Eighth, NoteValue.Eighth),
            Note(Eighth * 5, Eighth, NoteValue.Eighth));

        Levels(beams, 0).Should().Equal([BeamState.Begin]);
        Levels(beams, 1).Should().Equal([BeamState.Continue]);
        Levels(beams, 2).Should().Equal([BeamState.End], "the first dotted quarter ends here");
        Levels(beams, 3).Should().Equal([BeamState.Begin], "and the second one starts here");
        Levels(beams, 4).Should().Equal([BeamState.Continue]);
        Levels(beams, 5).Should().Equal([BeamState.End]);
    }

    /// <summary>
    /// 3/8 satisfies the same rule - denominator 8, numerator divisible by 3 - and is conventionally
    /// beamed as one group of three across the whole bar rather than as three flagged eighths.
    /// </summary>
    [Fact]
    public void ThreeEightIsOneGroupAcrossTheWholeBar()
    {
        MeasureSpan threeEight = new(1, 0, Eighth * 3, 3, 8, SignatureChanged: true);

        var beams = Assign(
            threeEight,
            Note(0, Eighth, NoteValue.Eighth),
            Note(Eighth, Eighth, NoteValue.Eighth),
            Note(Eighth * 2, Eighth, NoteValue.Eighth));

        Levels(beams, 0).Should().Equal([BeamState.Begin]);
        Levels(beams, 1).Should().Equal([BeamState.Continue]);
        Levels(beams, 2).Should().Equal([BeamState.End]);
    }

    // --- what breaks a group -------------------------------------------------------------

    /// <summary>
    /// A group needs two notes. One eighth on its own keeps its flag: a lone <c>begin</c> is a beam
    /// drawn into empty space, and MusicXML readers reject a beam that never ends.
    /// </summary>
    [Fact]
    public void ALoneEighthKeepsItsFlag()
    {
        var beams = Assign(
            CommonTime,
            Note(0, Eighth, NoteValue.Eighth),
            Rest(Eighth, Eighth, NoteValue.Eighth),
            Note(Ppqn, Ppqn, NoteValue.Quarter));

        beams[0].Should().BeEmpty("one eighth alone in its beat is not a group");
    }

    /// <summary>A beam may not span a silence, so a rest ends the group it interrupts.</summary>
    [Fact]
    public void ARestBreaksTheGroup()
    {
        // Four sixteenths' worth of beat 1, with the second one silent. The first sixteenth is left
        // alone by the split and keeps its flag; the last two are a pair.
        var beams = Assign(
            CommonTime,
            Note(0, Sixteenth, NoteValue.Sixteenth),
            Rest(Sixteenth, Sixteenth, NoteValue.Sixteenth),
            Note(Sixteenth * 2, Sixteenth, NoteValue.Sixteenth),
            Note(Sixteenth * 3, Sixteenth, NoteValue.Sixteenth));

        beams[0].Should().BeEmpty("the rest leaves the first sixteenth with nothing to beam to");
        beams[1].Should().BeEmpty("a rest is never beamed");
        Levels(beams, 2).Should().Equal([BeamState.Begin, BeamState.Begin]);
        Levels(beams, 3).Should().Equal([BeamState.End, BeamState.End]);
    }

    /// <summary>A quarter has no flags, so it can neither be beamed nor beamed across.</summary>
    [Fact]
    public void AQuarterBreaksTheGroupAndIsNeverBeamed()
    {
        MeasureSpan wide = new(1, 0, Ppqn * 4, 1, 4, SignatureChanged: true);

        var beams = Assign(
            wide,
            Note(0, Eighth, NoteValue.Eighth),
            Note(Eighth, Ppqn, NoteValue.Quarter),
            Note(Eighth + Ppqn, Eighth, NoteValue.Eighth),
            Note(Eighth * 2 + Ppqn, Eighth, NoteValue.Eighth));

        beams[0].Should().BeEmpty("the quarter cuts it off from the pair that follows");
        beams[1].Should().BeEmpty("a quarter has no flags to join");
        Levels(beams, 2).Should().Equal([BeamState.Begin]);
        Levels(beams, 3).Should().Equal([BeamState.End]);
    }

    /// <summary>
    /// A beam says "these notes are one gesture in one line". Two voices sharing a beat are two
    /// lines, and one staff of a grand staff is not the other.
    /// </summary>
    [Fact]
    public void NotesInDifferentVoicesOrStavesNeverBeamTogether()
    {
        var beams = Assign(
            CommonTime,
            Note(0, Eighth, NoteValue.Eighth, voice: 1),
            Note(0, Eighth, NoteValue.Eighth, voice: 2),
            Note(0, Eighth, NoteValue.Eighth, staff: 2));

        beams.Should().AllSatisfy(
            b => b.Should().BeEmpty("each of the three is alone in its own line"));
    }

    /// <summary>
    /// A tuplet run is already cut to a beat by the builder and beams as its own bracket. Joining
    /// it to the straight material beside it would draw one beam over two different divisions of
    /// the pulse.
    /// </summary>
    [Fact]
    public void ATripletRunBeamsAsItsOwnGroup()
    {
        long tripletEighth = Ppqn / 3;

        var beams = Assign(
            CommonTime,
            Note(0, tripletEighth, NoteValue.Eighth, tuplet: Tuplet.Triplet),
            Note(tripletEighth, tripletEighth, NoteValue.Eighth, tuplet: Tuplet.Triplet),
            Note(tripletEighth * 2, tripletEighth, NoteValue.Eighth, tuplet: Tuplet.Triplet),
            Note(Ppqn, Eighth, NoteValue.Eighth),
            Note(Ppqn + Eighth, Eighth, NoteValue.Eighth));

        Levels(beams, 0).Should().Equal([BeamState.Begin]);
        Levels(beams, 1).Should().Equal([BeamState.Continue]);
        Levels(beams, 2).Should().Equal([BeamState.End], "the triplet closes with its own beat");
        Levels(beams, 3).Should().Equal([BeamState.Begin], "beat 2's straight pair is a new group");
        Levels(beams, 4).Should().Equal([BeamState.End]);
    }

    /// <summary>
    /// A chord member consumes no time and hangs off the timed head's stem, so the head carries the
    /// beam for all of them. It must also not interrupt the group, which is what would happen if it
    /// were treated as an ordinary entry that cannot beam.
    /// </summary>
    [Fact]
    public void ChordMembersCarryNoBeamsAndDoNotBreakTheGroup()
    {
        var beams = Assign(
            CommonTime,
            Note(0, Eighth, NoteValue.Eighth),
            Note(0, Eighth, NoteValue.Eighth, chord: true),
            Note(0, Eighth, NoteValue.Eighth, chord: true),
            Note(Eighth, Eighth, NoteValue.Eighth));

        Levels(beams, 0).Should().Equal([BeamState.Begin], "the timed head carries the beam");
        beams[1].Should().BeEmpty();
        beams[2].Should().BeEmpty();
        Levels(beams, 3).Should().Equal(
            [BeamState.End], "the members must not have split the pair apart");
    }

    // --- levels and hooks ------------------------------------------------------------------

    /// <summary>
    /// The canonical hook. Both notes share the eighth-note beam; only the sixteenth owns a second
    /// level, and its neighbour does not reach it, so the second beam becomes a stub pointing back
    /// toward the dotted eighth. Without hooks this pair - the commonest dotted figure in Western
    /// music - simply cannot be written.
    /// </summary>
    [Fact]
    public void ADottedEighthAndASixteenthProduceABackwardHook()
    {
        var beams = Assign(
            CommonTime,
            Note(0, DottedEighth, NoteValue.Eighth, dots: 1),
            Note(DottedEighth, Sixteenth, NoteValue.Sixteenth));

        Levels(beams, 0).Should().Equal([BeamState.Begin], "the dotted eighth has one flag, so one level");
        Levels(beams, 1).Should().Equal(
            [BeamState.End, BeamState.BackwardHook],
            "the sixteenth's second beam has nothing to its right and nothing at that level to its "
            + "left, so it is a stub pointing back");
    }

    /// <summary>
    /// The mirror image. A hook on the first note of a group has nothing behind it to point at, so
    /// it points forward instead.
    /// </summary>
    [Fact]
    public void ASixteenthFollowedByADottedEighthProducesAForwardHook()
    {
        var beams = Assign(
            CommonTime,
            Note(0, Sixteenth, NoteValue.Sixteenth),
            Note(Sixteenth, DottedEighth, NoteValue.Eighth, dots: 1));

        Levels(beams, 0).Should().Equal([BeamState.Begin, BeamState.ForwardHook]);
        Levels(beams, 1).Should().Equal([BeamState.End]);
    }

    /// <summary>
    /// Level 2 exists only where two adjacent notes both own it. Eighth-sixteenth-sixteenth-eighth
    /// has a full second beam across the middle pair and nothing at that level on either end.
    /// </summary>
    [Fact]
    public void ASecondLevelSpansOnlyTheNotesThatBothOwnIt()
    {
        // One beat covering the whole bar, so that the four notes are one group and the level
        // arithmetic is the only thing under test.
        MeasureSpan wide = new(1, 0, Ppqn * 4, 1, 4, SignatureChanged: true);

        var beams = Assign(
            wide,
            Note(0, Sixteenth * 2, NoteValue.Eighth),
            Note(Sixteenth * 2, Sixteenth, NoteValue.Sixteenth),
            Note(Sixteenth * 3, Sixteenth, NoteValue.Sixteenth),
            Note(Sixteenth * 4, Sixteenth * 2, NoteValue.Eighth));

        Levels(beams, 0).Should().Equal([BeamState.Begin]);
        Levels(beams, 1).Should().Equal([BeamState.Continue, BeamState.Begin]);
        Levels(beams, 2).Should().Equal([BeamState.Continue, BeamState.End]);
        Levels(beams, 3).Should().Equal([BeamState.End]);
    }

    /// <summary>
    /// The hard cap on the whole scheme. A beam level a note does not own is a beam the renderer
    /// cannot draw and a reader will reject, so the count of states never exceeds the note's own
    /// flag count - however many levels its neighbours carry.
    /// </summary>
    [Fact]
    public void NoNoteEverCarriesMoreLevelsThanItHasFlags()
    {
        var entries = new[]
        {
            Note(0, Sixteenth * 2, NoteValue.Eighth),
            Note(Sixteenth * 2, Ppqn / 8, NoteValue.ThirtySecond),
            Note(Sixteenth * 2 + (Ppqn / 8), Ppqn / 16, NoteValue.SixtyFourth),
            Note(Sixteenth * 2 + (Ppqn / 8) + (Ppqn / 16), Ppqn / 16, NoteValue.SixtyFourth),
        };

        var beams = BeamGrouper.Assign(entries, CommonTime);

        for (int i = 0; i < entries.Length; i++)
        {
            beams[i].Count.Should().BeLessThanOrEqualTo(
                entries[i].Duration.Value.FlagCount(),
                $"entry {i} is a {entries[i].Duration.Value} and owns only that many beams");
        }

        Levels(beams, 0).Should().Equal([BeamState.Begin], "an eighth has exactly one flag");
        beams[2].Should().HaveCount(4, "a sixty-fourth has four");
    }

    /// <summary>
    /// Level 1 is unconditional across the group: whatever the values, the eighth-note beam runs
    /// from the first note to the last with no hooks and no gaps.
    /// </summary>
    [Fact]
    public void LevelOneAlwaysSpansTheWholeGroup()
    {
        var beams = Assign(
            CommonTime,
            Note(0, Sixteenth, NoteValue.Sixteenth),
            Note(Sixteenth, Ppqn / 8, NoteValue.ThirtySecond),
            Note(Sixteenth + (Ppqn / 8), Ppqn / 8, NoteValue.ThirtySecond),
            Note(Sixteenth * 2, Sixteenth * 2, NoteValue.Eighth));

        beams.Select(b => b[0]).Should().Equal(
            [BeamState.Begin, BeamState.Continue, BeamState.Continue, BeamState.End],
            "the first beam is what makes the group a group");
    }

    // --- degenerate input --------------------------------------------------------------------

    [Fact]
    public void AnEmptyMeasureIsAnEmptyAnswer()
    {
        BeamGrouper.Assign([], CommonTime).Should().BeEmpty();
    }

    /// <summary>
    /// A measure with no usable metre must not divide by zero. It falls back to one group covering
    /// the whole bar, which is the only honest answer when nothing says where the beats are.
    /// </summary>
    [Fact]
    public void AMeasureWithNoBeatsGroupsAcrossTheWholeBar()
    {
        MeasureSpan degenerate = new(1, 0, Ppqn * 4, 0, 0, SignatureChanged: false);

        BeamGrouper.GroupTicksFor(0, 0, Ppqn * 4).Should().Be(Ppqn * 4);

        var beams = Assign(
            degenerate,
            Note(0, Eighth, NoteValue.Eighth),
            Note(Ppqn * 3, Eighth, NoteValue.Eighth));

        Levels(beams, 0).Should().Equal([BeamState.Begin]);
        Levels(beams, 1).Should().Equal([BeamState.End]);
    }
}
