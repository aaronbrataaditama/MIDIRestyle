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
    [InlineData("B#3", 60, "B#3")]
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

        // Both enharmonic wraps, not just the descending one: Cb crosses down into B's octave and
        // B# crosses up into C's, so the letter/alter must come from what was typed either way.
        TonicParser.TryParse("Cb4", out var flatWrap, out _);
        TonicParser.TryParse("B#3", out var sharpWrap, out _);

        flatWrap!.Midi.Should().Be(59);
        sharpWrap!.Midi.Should().Be(60);
        flatWrap.Spelling.Letter.Should().Be(0);   // C
        flatWrap.Spelling.Alter.Should().Be(-1);
        sharpWrap.Spelling.Letter.Should().Be(6);  // B
        sharpWrap.Spelling.Alter.Should().Be(1);
    }

    [Fact]
    public void FromDetectedUsesTheDefaultOctaveAndPitchClassSpelling()
    {
        var tonic = TonicParser.FromDetected(pitchClass: 9, octave: 4);
        tonic.Midi.Should().Be(69);
        tonic.Name.Should().Be("A4");
    }
}
