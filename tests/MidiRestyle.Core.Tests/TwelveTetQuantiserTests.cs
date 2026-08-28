using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="TwelveTetQuantiser"/> snaps a scale onto the 12-TET grid while preserving degree
/// count, which matters because the degree mapper indexes by degree - a quantisation that merged two
/// degrees would silently change which target degree a source note maps to.
/// </summary>
public class TwelveTetQuantiserTests
{
    /// <summary>
    /// The cascade, not a single push. Three degrees (0, 30, 60) sit inside one semitone; a single
    /// push of the collided degree up by one semitone would leave two of them still colliding. Taking
    /// the max of "this degree's own rounding" and "the previous quantised degree plus a semitone"
    /// chains automatically, spreading all three across three consecutive semitones.
    /// </summary>
    [Fact]
    public void CascadePushesEachCollidedDegreeToTheNextFreeSemitone()
    {
        double[] input = [0, 30, 60, 500, 700];

        QuantisationResult result = TwelveTetQuantiser.Quantise(input);

        result.Succeeded.Should().BeTrue();
        result.Degrees.Should().Equal([0.0, 100.0, 200.0, 500.0, 700.0]);
    }

    /// <summary>
    /// Without the octave guard, a top degree near 1160c quantises to exactly 1200 - which duplicates
    /// the tonic and emits two identical pitches every octave. The guard must fail instead of
    /// emitting that degree.
    /// </summary>
    [Fact]
    public void TopDegreeNear1160FailsWithExceedsOctaveInsteadOfDuplicatingTheTonic()
    {
        double[] input = [0, 200, 400, 600, 800, 1000, 1160];

        QuantisationResult result = TwelveTetQuantiser.Quantise(input);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(QuantisationFailure.ExceedsOctave);
        result.Reason.Should().NotBeNullOrWhiteSpace();
        result.Degrees.Should().BeEmpty();
    }

    [Fact]
    public void ThaiSevenEqualQuantisesToTheVerifiedGrid()
    {
        double[] thaiSevenEqual = [.. Enumerable.Range(0, 7).Select(i => i * 1200.0 / 7)];

        QuantisationResult result = TwelveTetQuantiser.Quantise(thaiSevenEqual);

        result.Succeeded.Should().BeTrue();
        result.Degrees.Should().Equal([0.0, 200.0, 300.0, 500.0, 700.0, 900.0, 1000.0]);
    }

    [Theory]
    [InlineData(new double[] { 0, 200, 400, 700, 900 })]
    [InlineData(new double[] { 0, 200, 350, 500, 700, 900, 1050 })]
    public void DegreeCountIsAlwaysPreservedOnSuccess(double[] degreeCents)
    {
        QuantisationResult result = TwelveTetQuantiser.Quantise(degreeCents);

        result.Succeeded.Should().BeTrue();
        result.Degrees.Should().HaveCount(degreeCents.Length);
    }

    [Fact]
    public void EmptyInputSucceedsWithNoDegrees()
    {
        QuantisationResult result = TwelveTetQuantiser.Quantise(Array.Empty<double>());

        result.Succeeded.Should().BeTrue();
        result.Degrees.Should().BeEmpty();
    }

    [Fact]
    public void NullInputThrows()
    {
        Action act = () => TwelveTetQuantiser.Quantise((IReadOnlyList<double>)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ScaleOverloadQuantisesTheScalesDegreeCents()
    {
        Scale scale = new(
            id: "test.quantiser",
            name: "Test",
            tradition: "Test",
            region: "Test",
            degreeCents: [0, 200, 350, 500, 700, 900, 1050],
            source: "Unit test fixture, TwelveTetQuantiserTests");

        QuantisationResult result = TwelveTetQuantiser.Quantise(scale);

        result.Succeeded.Should().BeTrue();
        result.Degrees.Should().Equal([0.0, 200.0, 400.0, 500.0, 700.0, 900.0, 1100.0]);
    }
}
