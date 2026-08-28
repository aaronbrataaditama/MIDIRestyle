using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="CollisionResolver"/> resolves same-pitch, same-(track,channel), time-overlapping notes
/// - the routine consequence of degree mapping compressing a 7-note scale into 5. See the "Mapped
/// notes can collide" invariant in <c>CLAUDE.md</c>.
/// </summary>
public class CollisionResolverTests
{
    private static Note N(int midiNote, long start, long length, byte velocity = 90) =>
        new(Pitch.FromMidi(midiNote), start, length, velocity);

    [Fact]
    public void MergeKeepsLongerNoteAndReportsOneMerged()
    {
        Note shorter = N(60, start: 0, length: 50, velocity: 100);
        Note longer = N(60, start: 0, length: 200, velocity: 40);

        CollisionResolution result = CollisionResolver.Resolve([shorter, longer], CollisionPolicy.Merge);

        result.Notes.Should().Equal([longer]);
        result.MergedCount.Should().Be(1);
        result.DisplacedCount.Should().Be(0);
    }

    [Fact]
    public void SameNotesOnDifferentChannelsAreLeftAloneWhenResolvedSeparately()
    {
        // Note carries no channel - (track, channel) scoping is the caller passing one channel's
        // notes per call. Resolving each channel's identical-looking pair on its own must not see,
        // let alone merge against, the other channel's notes.
        Note channelANoteA = N(60, start: 0, length: 100);
        Note channelANoteB = N(60, start: 0, length: 100);
        Note channelBNoteA = N(60, start: 0, length: 100);
        Note channelBNoteB = N(60, start: 0, length: 100);

        CollisionResolution channelAResult =
            CollisionResolver.Resolve([channelANoteA, channelANoteB], CollisionPolicy.Merge);
        CollisionResolution channelBResult =
            CollisionResolver.Resolve([channelBNoteA, channelBNoteB], CollisionPolicy.Merge);

        // Each channel resolves its own pair (they do collide within their own scope) -
        // independently of the other channel, which is the point being demonstrated.
        channelAResult.Notes.Should().HaveCount(1);
        channelAResult.MergedCount.Should().Be(1);
        channelBResult.Notes.Should().HaveCount(1);
        channelBResult.MergedCount.Should().Be(1);
    }

    [Fact]
    public void NonOverlappingSamePitchNotesAreLeftAlone()
    {
        Note first = N(60, start: 0, length: 100);
        Note second = N(60, start: 100, length: 100); // starts exactly when the first ends - touching, not overlapping

        CollisionResolution result = CollisionResolver.Resolve([first, second], CollisionPolicy.Merge);

        result.Notes.Should().BeEquivalentTo([first, second]);
        result.MergedCount.Should().Be(0);
        result.DisplacedCount.Should().Be(0);
    }

    [Fact]
    public void DisplaceOctavePreservesBothNotesMovingOneAnOctave()
    {
        Note primary = N(60, start: 0, length: 200, velocity: 100); // longest -> stays in place
        Note collider = N(60, start: 0, length: 50, velocity: 100);

        CollisionResolution result = CollisionResolver.Resolve([primary, collider], CollisionPolicy.DisplaceOctave);

        result.Notes.Should().HaveCount(2);
        result.MergedCount.Should().Be(0);
        result.DisplacedCount.Should().Be(1);

        result.Notes.Should().ContainSingle(n => n.Pitch.Cents == primary.Pitch.Cents);
        Note moved = result.Notes.Single(n => n.Pitch.Cents != primary.Pitch.Cents);
        Math.Abs(moved.Pitch.Cents - primary.Pitch.Cents).Should().BeApproximately(MidiRounding.CentsPerOctave, 1e-9);
        // Both original voices are still present, just at different pitches.
        moved.StartTicks.Should().Be(collider.StartTicks);
        moved.LengthTicks.Should().Be(collider.LengthTicks);
    }

    [Fact]
    public void DisplaceOctaveNearTopOfRangeDisplacesDownward()
    {
        Note primary = N(120, start: 0, length: 200, velocity: 100); // longest -> stays; +1 octave (132) is out of range
        Note collider = N(120, start: 0, length: 50, velocity: 100);

        CollisionResolution result = CollisionResolver.Resolve([primary, collider], CollisionPolicy.DisplaceOctave);

        result.DisplacedCount.Should().Be(1);
        result.MergedCount.Should().Be(0);
        Note moved = result.Notes.Single(n => n.Pitch.Cents != primary.Pitch.Cents);
        moved.Pitch.MidiNote.Should().Be(108); // 120 - 12, since 120 + 12 = 132 overflows
        moved.Pitch.IsInMidiRange.Should().BeTrue();
    }

    [Fact]
    public void DisplaceOctaveFallsBackToMergeWhenNeitherOctaveFits()
    {
        // Four fully-overlapping same-pitch notes at MIDI 60 (mid-range, so range itself is never
        // the obstacle). Priority order by length: primary(400) stays; second(300) takes +1 octave;
        // third(200) then finds +1 octave taken and takes -1 octave; fourth(100) finds both the
        // +1 and -1 octave slots already occupied by overlapping notes and must fall back to merge.
        Note primary = N(60, start: 0, length: 400, velocity: 100);
        Note second = N(60, start: 0, length: 300, velocity: 100);
        Note third = N(60, start: 0, length: 200, velocity: 100);
        Note fourth = N(60, start: 0, length: 100, velocity: 100);

        CollisionResolution result = CollisionResolver.Resolve(
            [fourth, third, second, primary], CollisionPolicy.DisplaceOctave);

        result.DisplacedCount.Should().Be(2);
        result.MergedCount.Should().Be(1);
        result.Notes.Should().HaveCount(3);
        result.Notes.Select(n => n.Pitch.MidiNote).Should().BeEquivalentTo([60, 72, 48]);
    }

    [Fact]
    public void NotesFiftyCentsApartOnSameMidiNoteDoNotCollide()
    {
        // -25c and +25c both round (ties away from zero) to MIDI note 60, but are 50 cents apart -
        // genuinely different pitches, on what would be different pitch-bend channels after
        // allocation, that must never be treated as colliding.
        Note bentDown = new(Pitch.FromMidi(60, bendCents: -25), StartTicks: 0, LengthTicks: 100, Velocity: 90);
        Note bentUp = new(Pitch.FromMidi(60, bendCents: 25), StartTicks: 0, LengthTicks: 100, Velocity: 90);

        bentDown.Pitch.MidiNote.Should().Be(bentUp.Pitch.MidiNote);
        (bentUp.Pitch.Cents - bentDown.Pitch.Cents).Should().BeApproximately(50.0, 1e-9);

        CollisionResolution result = CollisionResolver.Resolve([bentDown, bentUp], CollisionPolicy.Merge);

        result.Notes.Should().BeEquivalentTo([bentDown, bentUp]);
        result.MergedCount.Should().Be(0);
    }

    [Fact]
    public void ThreeOrMoreCollidingNotesMergeToTheLongestOneWithoutDroppingTheWrongOne()
    {
        Note shortest = N(60, start: 0, length: 50, velocity: 100);
        Note longest = N(60, start: 0, length: 300, velocity: 100); // the one that must survive
        Note middle = N(60, start: 0, length: 100, velocity: 100);

        // Deliberately not sorted by length, to make sure the resolver - not input order - decides.
        CollisionResolution result = CollisionResolver.Resolve([shortest, longest, middle], CollisionPolicy.Merge);

        result.Notes.Should().Equal([longest]);
        result.MergedCount.Should().Be(2);
        result.DisplacedCount.Should().Be(0);
    }

    [Fact]
    public void MergeTieBreaksAreDeterministicRegardlessOfInputOrder()
    {
        // Tied on length and start; velocity is the deciding tiebreak, so the higher-velocity note
        // must win regardless of which order the two are supplied in.
        Note lowerVelocity = N(60, start: 10, length: 100, velocity: 40);
        Note higherVelocity = N(60, start: 10, length: 100, velocity: 100);

        CollisionResolution forwardOrder =
            CollisionResolver.Resolve([lowerVelocity, higherVelocity], CollisionPolicy.Merge);
        CollisionResolution reverseOrder =
            CollisionResolver.Resolve([higherVelocity, lowerVelocity], CollisionPolicy.Merge);

        forwardOrder.Notes.Should().Equal([higherVelocity]);
        reverseOrder.Notes.Should().Equal([higherVelocity]);
        forwardOrder.Should().BeEquivalentTo(reverseOrder);
    }

    [Fact]
    public void ShuffledInputProducesTheSameResultAsSortedInputAcrossRepeatedRuns()
    {
        Note a = N(60, start: 0, length: 200, velocity: 90);
        Note b = N(60, start: 0, length: 200, velocity: 90); // full tie with `a` - result is symmetric
        Note c = N(64, start: 5, length: 40, velocity: 90); // distinct pitch, never collides
        Note d = N(60, start: 500, length: 30, velocity: 90); // distinct time, never collides

        Note[] sorted = [a, b, c, d];
        var rng = new Random(2026);

        CollisionResolution baseline = CollisionResolver.Resolve(sorted, CollisionPolicy.Merge);

        for (int i = 0; i < 5; i++)
        {
            Note[] shuffled = [.. sorted.OrderBy(_ => rng.Next())];
            CollisionResolution result = CollisionResolver.Resolve(shuffled, CollisionPolicy.Merge);

            result.Should().BeEquivalentTo(baseline, options => options.WithStrictOrdering());
        }
    }

    [Fact]
    public void EmptyInputIsHandled()
    {
        CollisionResolution result = CollisionResolver.Resolve([], CollisionPolicy.Merge);

        result.Notes.Should().BeEmpty();
        result.MergedCount.Should().Be(0);
        result.DisplacedCount.Should().Be(0);
        result.HadCollisions.Should().BeFalse();
    }

    [Fact]
    public void SingleNoteInputIsHandled()
    {
        Note only = N(60, start: 0, length: 100);

        CollisionResolution result = CollisionResolver.Resolve([only], CollisionPolicy.DisplaceOctave);

        result.Notes.Should().Equal([only]);
        result.MergedCount.Should().Be(0);
        result.DisplacedCount.Should().Be(0);
    }

    [Fact]
    public void InputCollectionIsNotMutated()
    {
        List<Note> input =
        [
            N(60, start: 0, length: 50, velocity: 100),
            N(60, start: 0, length: 200, velocity: 40),
            N(64, start: 0, length: 80, velocity: 90),
        ];
        List<Note> snapshot = [.. input];

        CollisionResolution result = CollisionResolver.Resolve(input, CollisionPolicy.Merge);

        input.Should().Equal(snapshot);
        result.Notes.Should().NotBeSameAs(input);
    }
}
