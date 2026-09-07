using MidiRestyle.Mcp;

namespace MidiRestyle.Mcp.Tests;

public class TonicParserTests
{
    [Theory]
    [InlineData("C4", 60, "C4")]
    [InlineData("c4", 60, "C4")]
    [InlineData("D", 62, "D4")]
    [InlineData("Eb4", 63, "Eb4")]
    [InlineData("E♭4", 63, "Eb4")]
    [InlineData("F#3", 54, "F#3")]
    [InlineData("F♯3", 54, "F#3")]
    [InlineData("B3", 59, "B3")]
    [InlineData("Cb4", 59, "Cb4")]
    [InlineData("62", 62, "D4")]
    [InlineData("0", 0, "C-1")]
    [InlineData("127", 127, "G9")]
    public void ParsesNoteNamesAndMidiNumbers(string text, int midi, string name)
    {
        TonicParser.TryParse(text, out var tonic, out var error).Should().BeTrue(error);
        tonic!.Midi.Should().Be(midi);
        tonic.Name.Should().Be(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("H4")]
    [InlineData("128")]
    [InlineData("-1")]
    [InlineData("C10")]
    [InlineData("Dbb4")]
    public void RejectsWhatItCannotName(string text)
    {
        TonicParser.TryParse(text, out var tonic, out var error).Should().BeFalse();
        tonic.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SpellingFollowsTheLetterTyped()
    {
        TonicParser.TryParse("Db4", out var flat, out _);
        TonicParser.TryParse("C#4", out var sharp, out _);

        flat!.Midi.Should().Be(61);
        sharp!.Midi.Should().Be(61);
        flat.Spelling.Letter.Should().Be(1);   // D
        flat.Spelling.Alter.Should().Be(-1);
        sharp.Spelling.Letter.Should().Be(0);  // C
        sharp.Spelling.Alter.Should().Be(1);
    }

    [Fact]
    public void FromDetectedUsesTheDefaultOctaveAndPitchClassSpelling()
    {
        var tonic = TonicParser.FromDetected(pitchClass: 9, octave: 4);
        tonic.Midi.Should().Be(69);
        tonic.Name.Should().Be("A4");
    }
}
