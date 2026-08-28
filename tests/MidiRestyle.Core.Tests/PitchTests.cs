using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="Pitch"/> is the cents-only value type everything else in the domain builds on. These
/// tests guard the conversions at the boundary - <see cref="Pitch.MidiNote"/>,
/// <see cref="Pitch.BendCents"/>, <see cref="Pitch.PitchClass"/> - and the fact that the type is
/// deliberately unbounded, since <c>MappingOptions.RangePolicy</c> (not this type) owns clamping.
/// </summary>
public class PitchTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 1)]
    [InlineData(6000, 60)]
    [InlineData(12700, 127)]
    public void MidiNoteIsExactForWholeSemitones(double cents, int expectedNote) =>
        new Pitch(cents).MidiNote.Should().Be(expectedNote);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(100, 0.0)]
    [InlineData(6000, 0.0)]
    public void BendCentsIsZeroForWholeSemitones(double cents, double expectedBend) =>
        new Pitch(cents).BendCents.Should().BeApproximately(expectedBend, 1e-9);

    /// <summary>
    /// The away-from-zero regression, at the <see cref="Pitch"/> level rather than
    /// <see cref="MidiRounding"/>'s. 350c and 1050c are both exact quarter-tone ties (3.5 and 10.5
    /// semitones). Under banker's rounding 3.5 rounds up to the even 4 while 10.5 rounds down to the
    /// even 10 - two different directions from one kind of inflection. Away-from-zero rounds both up
    /// (4 and 11), so both land -50c below their note. If this regresses, Rast's two neutral degrees
    /// stop sharing a pitch-bend channel.
    /// </summary>
    [Theory]
    [InlineData(350, 4, -50.0)]
    [InlineData(1050, 11, -50.0)]
    public void QuarterToneTiesBothRoundUpGivingMatchingNegativeBend(
        double cents, int expectedNote, double expectedBend)
    {
        var pitch = new Pitch(cents);
        pitch.MidiNote.Should().Be(expectedNote);
        pitch.BendCents.Should().BeApproximately(expectedBend, 1e-9);
    }

    [Theory]
    [InlineData(203.91, 2, 3.91)]
    [InlineData(-100, -1, 0.0)]
    public void BendCentsIsSignedAndWithinHalfSemitone(double cents, int expectedNote, double expectedBend)
    {
        var pitch = new Pitch(cents);
        pitch.MidiNote.Should().Be(expectedNote);
        pitch.BendCents.Should().BeApproximately(expectedBend, 1e-9);
    }

    /// <summary>
    /// Positive modulo: MidiNote can be negative for out-of-range pitches, and C#'s <c>%</c> keeps
    /// the sign of the dividend, so a naive <c>MidiNote % 12</c> would return -1 here instead of 11.
    /// </summary>
    [Fact]
    public void PitchClassOfANegativeMidiNoteIsPositive() =>
        Pitch.FromMidi(-1).PitchClass.Should().Be(11);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(13, 1)]
    [InlineData(-13, 11)]
    [InlineData(-1, 11)]
    [InlineData(-12, 0)]
    public void PitchClassIsAlwaysInZeroToEleven(int midiNote, int expectedPitchClass) =>
        Pitch.FromMidi(midiNote).PitchClass.Should().Be(expectedPitchClass);

    [Theory]
    [InlineData(0, true)]
    [InlineData(127, true)]
    [InlineData(-1, false)]
    [InlineData(128, false)]
    public void IsInMidiRangeIsTrueOnlyWithinZeroTo127(int midiNote, bool expected) =>
        Pitch.FromMidi(midiNote).IsInMidiRange.Should().Be(expected);

    [Fact]
    public void IsTwelveTetIsTrueOnlyWhenBendIsExactlyZero()
    {
        Pitch.FromMidi(60).IsTwelveTet.Should().BeTrue();
        Pitch.FromMidi(60, 0.5).IsTwelveTet.Should().BeFalse();
        Pitch.FromMidi(60, -50).IsTwelveTet.Should().BeFalse();
    }

    [Fact]
    public void FromMidiNoteAloneIsExactlyOnTheSemitone()
    {
        Pitch pitch = Pitch.FromMidi(60);
        pitch.Cents.Should().Be(6000.0);
        pitch.MidiNote.Should().Be(60);
        pitch.BendCents.Should().Be(0.0);
    }

    [Theory]
    [InlineData(60, 37.5)]
    [InlineData(60, -50.0)]
    [InlineData(0, 0.0)]
    public void FromMidiWithBendRoundTripsToTheSameNoteAndBend(int note, double bendCents)
    {
        Pitch pitch = Pitch.FromMidi(note, bendCents);
        pitch.MidiNote.Should().Be(note);
        pitch.BendCents.Should().BeApproximately(bendCents, 1e-9);
    }

    [Fact]
    public void ShiftOctavesMovesByExactly1200CentsPerOctave()
    {
        Pitch pitch = Pitch.FromMidi(60);
        pitch.ShiftOctaves(1).Cents.Should().Be(pitch.Cents + 1200.0);
        pitch.ShiftOctaves(-2).Cents.Should().Be(pitch.Cents - 2400.0);
        pitch.ShiftOctaves(0).Should().Be(pitch);
    }

    [Fact]
    public void ShiftCentsAddsRawCents()
    {
        Pitch pitch = Pitch.FromMidi(60);
        pitch.ShiftCents(50).Cents.Should().Be(pitch.Cents + 50.0);
        pitch.ShiftCents(-1200).Should().Be(pitch.ShiftOctaves(-1));
    }

    [Fact]
    public void OrderingOperatorsAndCompareToAgreeWithCents()
    {
        var low = new Pitch(100);
        var high = new Pitch(200);
        var sameAsLow = new Pitch(100);

        (low < high).Should().BeTrue();
        (high > low).Should().BeTrue();
        (low <= sameAsLow).Should().BeTrue();
        (low >= sameAsLow).Should().BeTrue();
        (high < low).Should().BeFalse();

        low.CompareTo(high).Should().BeLessThan(0);
        high.CompareTo(low).Should().BeGreaterThan(0);
        low.CompareTo(sameAsLow).Should().Be(0);
    }

    /// <summary>
    /// Deliberately unbounded: degree mapping changes a piece's range by
    /// <c>targetDegreeCount / sourceDegreeCount</c>, so a full-range file into a 5-note scale
    /// routinely produces pitches outside 0..127. Clamping is <c>RestyleEngine</c>'s job via
    /// <c>MappingOptions.RangePolicy</c>, never this type's - constructing or deriving an
    /// out-of-range <see cref="Pitch"/> must never throw.
    /// </summary>
    [Fact]
    public void OutOfRangeConstructionAndDerivationNeverThrow()
    {
        var wayHigh = new Pitch(20000);
        var wayLow = new Pitch(-5000);

        wayHigh.IsInMidiRange.Should().BeFalse();
        wayLow.IsInMidiRange.Should().BeFalse();
        wayHigh.MidiNote.Should().Be(200);
        wayLow.MidiNote.Should().Be(-50);

        Pitch.FromMidi(200).IsInMidiRange.Should().BeFalse();
        Pitch.FromMidi(60).ShiftOctaves(10).IsInMidiRange.Should().BeFalse();
    }
}
