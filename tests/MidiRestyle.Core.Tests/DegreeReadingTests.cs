using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="DegreeReader"/> is the Core-side model behind the cipher/degree view shown instead of
/// a staff whenever <see cref="Scale.Notatable"/> is false. Fixtures are Slendro (five equal-ish
/// steps, up to 40c from 12-TET) and Gong pentatonic (a notatable scale, used here purely as a second
/// degree count and spacing to guard against Slendro-specific coincidences).
/// </summary>
public class DegreeReadingTests
{
    private static readonly Scale Slendro = new(
        id: "test.slendro",
        name: "Slendro (test fixture)",
        tradition: "Test",
        region: "Test",
        degreeCents: [0, 240, 480, 720, 960],
        source: "Test fixture, not a real citation",
        notatable: false);

    private static readonly Scale GongPentatonic = new(
        id: "test.gong",
        name: "Gong pentatonic (test fixture)",
        tradition: "Test",
        region: "Test",
        degreeCents: [0, 200, 400, 700, 900],
        source: "Test fixture, not a real citation");

    private static readonly Pitch TonicC4 = Pitch.FromMidi(60);

    [Fact]
    public void TonicReadsAsDegreeOneAtOctaveZero()
    {
        DegreeReading reading = DegreeReader.Read(TonicC4, Slendro, TonicC4);

        reading.Degree.Should().Be(1);
        reading.OctaveOffset.Should().Be(0);
        reading.CentsDeviation.Should().Be(0.0);
        reading.IsInScale.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(240, 2)]
    [InlineData(480, 3)]
    [InlineData(720, 4)]
    [InlineData(960, 5)]
    public void EachSlendroDegreeReadsBackAsItselfWithZeroDeviation(double degreeCents, int expectedDegree)
    {
        Pitch pitch = TonicC4.ShiftCents(degreeCents);

        DegreeReading reading = DegreeReader.Read(pitch, Slendro, TonicC4);

        reading.Degree.Should().Be(expectedDegree);
        reading.OctaveOffset.Should().Be(0);
        reading.CentsDeviation.Should().Be(0.0);
        reading.IsInScale.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(200, 2)]
    [InlineData(400, 3)]
    [InlineData(700, 4)]
    [InlineData(900, 5)]
    public void EachGongPentatonicDegreeReadsBackAsItselfWithZeroDeviation(double degreeCents, int expectedDegree)
    {
        Pitch pitch = TonicC4.ShiftCents(degreeCents);

        DegreeReading reading = DegreeReader.Read(pitch, GongPentatonic, TonicC4);

        reading.Degree.Should().Be(expectedDegree);
        reading.OctaveOffset.Should().Be(0);
        reading.CentsDeviation.Should().Be(0.0);
        reading.IsInScale.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-3)]
    public void OctaveShiftsCarryTheOffsetAndKeepTheDegree(int octaves)
    {
        Pitch pitch = TonicC4.ShiftCents(240).ShiftOctaves(octaves);

        DegreeReading reading = DegreeReader.Read(pitch, Slendro, TonicC4);

        reading.Degree.Should().Be(2);
        reading.OctaveOffset.Should().Be(octaves);
        reading.CentsDeviation.Should().Be(0.0);
        reading.IsInScale.Should().BeTrue();
    }

    /// <summary>
    /// The floor-division invariant, exercised directly rather than via a clean whole-octave shift.
    /// A naive <c>rel / 1200</c> / <c>rel % 1200</c> truncates -960/1200 to octave 0 and remainder
    /// -960, which would index <c>DegreeCents[-1]</c> and throw. Floor division gives octave -1 and
    /// remainder 240 - the tonic's octave below, at its own second degree.
    /// </summary>
    [Fact]
    public void ANoteBelowTheTonicUsesFloorDivisionNotTruncation()
    {
        Pitch pitch = TonicC4.ShiftCents(-960);

        DegreeReading reading = DegreeReader.Read(pitch, Slendro, TonicC4);

        reading.Degree.Should().Be(2);
        reading.OctaveOffset.Should().Be(-1);
        reading.CentsDeviation.Should().Be(0.0);
    }

    /// <summary>
    /// A note 20c below the tonic one octave up is nearer to that octave's tonic (degree 1, octave
    /// carried forward) than to Slendro's own top degree (960c, 220c away). Attaching it to the top
    /// degree instead would be the wrap-handling bug this guards against.
    /// </summary>
    [Fact]
    public void ANoteJustBelowTheNextOctaveWrapsToDegreeOneWithTheOctaveCarried()
    {
        Pitch pitch = TonicC4.ShiftCents(1180);

        DegreeReading reading = DegreeReader.Read(pitch, Slendro, TonicC4);

        reading.Degree.Should().Be(1);
        reading.OctaveOffset.Should().Be(1);
        reading.CentsDeviation.Should().BeApproximately(-20.0, 1e-9);
    }

    /// <summary>
    /// Slendro's 240c second degree, after 12-TET output mode has rounded it to MIDI note tonic+2
    /// (200c, 40c away - the documented worst case for this scale). It must still read as degree 2,
    /// not as an unscaled note, because the degree survives 12-TET rounding even though the exact
    /// cents do not.
    /// </summary>
    [Fact]
    public void TwelveTetQuantisedSlendroDegreeStillReadsAsInScaleWithNonZeroDeviation()
    {
        Pitch pitch = Pitch.FromMidi(62);

        DegreeReading reading = DegreeReader.Read(pitch, Slendro, TonicC4);

        reading.Degree.Should().Be(2);
        reading.IsInScale.Should().BeTrue();
        reading.CentsDeviation.Should().BeApproximately(-40.0, 1e-9);
    }

    [Fact]
    public void AGenuinelyOffScalePitchIsNotInScaleAndReadsAsQuestionMark()
    {
        Pitch pitch = Pitch.FromMidi(61);

        DegreeReading reading = DegreeReader.Read(pitch, Slendro, TonicC4);

        reading.IsInScale.Should().BeFalse();
        reading.Numeral.Should().Be("?");
    }

    [Fact]
    public void NumeralIsQuestionMarkWhenNotInScaleEvenWithASpecificDegree()
    {
        var reading = new DegreeReading(Degree: 4, OctaveOffset: 0, CentsDeviation: 37, IsInScale: false);

        reading.Numeral.Should().Be("?");
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "̇")]
    [InlineData(2, "̇̇")]
    [InlineData(-1, "̣")]
    [InlineData(-2, "̣̣")]
    public void OctaveMarksRepeatTheCombiningDotOncePerOctave(int octaveOffset, string expectedMarks)
    {
        var reading = new DegreeReading(Degree: 5, OctaveOffset: octaveOffset, CentsDeviation: 0, IsInScale: true);

        reading.OctaveMarks.Should().Be(expectedMarks);
    }

    [Fact]
    public void DisplayAndToStringCombineNumeralAndOctaveMarks()
    {
        var reading = new DegreeReading(Degree: 3, OctaveOffset: 1, CentsDeviation: 0, IsInScale: true);

        reading.Display.Should().Be("3̇");
        reading.ToString().Should().Be("3̇");
    }
}
