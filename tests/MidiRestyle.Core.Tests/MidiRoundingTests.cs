using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// Guards the rounding invariant that everything else in the domain depends on.
/// </summary>
/// <remarks>
/// These are not style tests. <c>Math.Round(double)</c> defaults to banker's rounding, and
/// quarter-tone scales land exactly on the +/-50 cent tie on every note - so the tie rule decides
/// how many pitch-bend channels a scale needs, and whether that count is even stable across keys.
/// </remarks>
public class MidiRoundingTests
{
    [Fact]
    public void ModeIsAwayFromZero() =>
        MidiRounding.Mode.Should().Be(MidpointRounding.AwayFromZero);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 1)]
    [InlineData(1200, 12)]
    [InlineData(6000, 60)]
    public void ExactSemitonesRoundToThemselves(double cents, int expected) =>
        MidiRounding.ToNearestSemitone(cents).Should().Be(expected);

    /// <summary>
    /// The regression that matters. Under banker's rounding 3.5 rounds up to the even 4 while 10.5
    /// rounds DOWN to the even 10 - so one half-flat degree reports a -50 cent offset and the other
    /// +50, from a single musical inflection. Away-from-zero makes both -50.
    /// </summary>
    [Theory]
    [InlineData(350, 4)]    // banker's also gives 4 here, by luck of the even neighbour
    [InlineData(1050, 11)]  // banker's gives 10 - this is where the two modes diverge
    [InlineData(450, 5)]    // banker's gives 4
    [InlineData(250, 3)]    // banker's gives 2
    public void QuarterToneTiesRoundAwayFromZero(double cents, int expected) =>
        MidiRounding.ToNearestSemitone(cents).Should().Be(expected);

    [Fact]
    public void BankersRoundingWouldSplitOneInflectionIntoTwoOffsets()
    {
        double[] rast = [0, 200, 350, 500, 700, 900, 1050];

        // What the default would give - reproduced here so the bug can never quietly return.
        double[] bankers = [.. rast
            .Select(d => d - (int)Math.Round(d / 100.0) * 100.0)
            .Distinct()
            .Order()];

        double[] correct = [.. rast
            .Select(MidiRounding.OffsetFromNearestSemitone)
            .Distinct()
            .Order()];

        bankers.Should().BeEquivalentTo([-50.0, 0.0, 50.0],
            "banker's rounding splits Rast's two neutral degrees onto opposite sides");
        correct.Should().BeEquivalentTo([-50.0, 0.0],
            "away-from-zero gives Rast one offset for both neutral degrees, so it needs 2 channels");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(350, -50)]
    [InlineData(1050, -50)]
    [InlineData(240, 40)]
    [InlineData(480, -20)]
    [InlineData(203.91, 3.91)]
    public void OffsetFromNearestSemitoneIsSigned(double cents, double expected) =>
        MidiRounding.OffsetFromNearestSemitone(cents).Should().BeApproximately(expected, 1e-9);

    /// <summary>Offsets are bounded to a half-semitone window by construction, so bend never overflows.</summary>
    [Fact]
    public void OffsetsNeverLeaveTheHalfSemitoneWindow()
    {
        for (double cents = -2400; cents <= 2400; cents += 0.37)
        {
            double offset = MidiRounding.OffsetFromNearestSemitone(cents);
            offset.Should().BeInRange(-50.0, 50.0);
        }
    }

    [Fact]
    public void ConstantsMatchEqualTemperament()
    {
        MidiRounding.CentsPerSemitone.Should().Be(100.0);
        MidiRounding.CentsPerOctave.Should().Be(1200.0);
        MidiRounding.SemitonesPerOctave.Should().Be(12);
        (MidiRounding.CentsPerSemitone * MidiRounding.SemitonesPerOctave)
            .Should().Be(MidiRounding.CentsPerOctave);
    }
}
