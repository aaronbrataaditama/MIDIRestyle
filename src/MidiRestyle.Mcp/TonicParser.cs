using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Mcp;

/// <summary>A tonic an agent asked for, with the spelling implied by how it was written.</summary>
public sealed record ParsedTonic(Pitch Pitch, TonicSpelling Spelling)
{
    public int Midi => Pitch.MidiNote;

    /// <summary>ASCII note name with octave, e.g. <c>Eb4</c>; the form the parser accepts back.</summary>
    public string Name
    {
        get
        {
            string letter = DegreeSpelling.LetterNames[Spelling.Letter].ToString();
            string accidental = Spelling.Alter switch { 1 => "#", -1 => "b", _ => "" };

            // Octave is a property of the letter, not of the sounding pitch: Cb4 sounds a semitone
            // below C4 (crossing into what C-major would call B3) but is still written with the "4"
            // that belongs to its own letter. Un-apply the accidental before dividing by 12, rather
            // than deriving the octave from Midi directly.
            int octave = (Midi - TonicSpelling.NaturalSemitones[Spelling.Letter] - (int)Spelling.Alter) / 12 - 1;
            return string.Create(CultureInfo.InvariantCulture, $"{letter}{accidental}{octave}");
        }
    }
}

/// <summary>
/// Parses <c>"D"</c>, <c>"D4"</c>, <c>"Eb4"</c>, <c>"F#3"</c> (Unicode sharp/flat accepted) or a MIDI
/// number <c>"0"</c>..<c>"127"</c>. The letter typed decides <see cref="TonicSpelling"/> - MIDI 61 is
/// C# or Db depending on which was written, and every letter downstream depends on that.
/// </summary>
public static partial class TonicParser
{
    [GeneratedRegex(@"^\s*([A-Ga-g])([#♯]|[b♭])?(-?\d)?\s*$")]
    private static partial Regex NoteName();

    public static bool TryParse(string text, [NotNullWhen(true)] out ParsedTonic? tonic, [NotNullWhen(false)] out string? error)
    {
        tonic = null;
        error = null;

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int midi))
        {
            if (midi is < Pitch.MinMidiNote or > Pitch.MaxMidiNote)
            {
                error = $"MIDI note '{text}' is outside 0..127.";
                return false;
            }

            tonic = new ParsedTonic(Pitch.FromMidi(midi), TonicSpelling.FromPitchClass(midi % 12));
            return true;
        }

        Match m = NoteName().Match(text);
        if (!m.Success)
        {
            error = $"'{text}' is not a tonic. Use a note name with optional accidental and octave (D, Eb4, F#3) or a MIDI number 0..127.";
            return false;
        }

        int letter = "CDEFGAB".IndexOf(char.ToUpperInvariant(m.Groups[1].Value[0]));
        int alter = m.Groups[2].Value switch { "#" or "♯" => 1, "b" or "♭" => -1, _ => 0 };
        int octave = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : RestyleDefaults.TonicOctave;

        int note = (octave + 1) * 12 + TonicSpelling.NaturalSemitones[letter] + alter;
        if (note is < Pitch.MinMidiNote or > Pitch.MaxMidiNote)
        {
            error = $"'{text}' is MIDI {note}, outside 0..127.";
            return false;
        }

        tonic = new ParsedTonic(Pitch.FromMidi(note), new TonicSpelling(letter, alter));
        return true;
    }

    public static ParsedTonic FromDetected(int pitchClass, int octave) =>
        new(Pitch.FromMidi((octave + 1) * 12 + pitchClass), TonicSpelling.FromPitchClass(pitchClass));
}
