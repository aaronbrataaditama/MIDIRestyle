namespace MidiRestyle.Core.Scales;

/// <summary>
/// The letter and accidental the target tonic is written as.
/// </summary>
/// <remarks>
/// <para>
/// Necessary because a MIDI note number does not determine a letter name: MIDI 61 may be C-sharp or
/// D-flat, and <em>every</em> letter downstream follows from which one it is. Without this recorded,
/// notation output has to guess, and it will guess wrong half the time on the black keys.
/// </para>
/// <para>
/// It also completes the frame conversion. <see cref="DegreeSpelling.Alter"/> is stored relative to
/// the major-scale degree, which is tonic-independent and therefore storable once per scale; turning
/// that into a letter plus an absolute alteration - what a staff or MusicXML needs - requires knowing
/// the tonic's own letter and alteration. That is what this type supplies.
/// </para>
/// </remarks>
/// <param name="Letter">0..6 for C D E F G A B.</param>
/// <param name="Alter">Semitones applied to the letter, e.g. -1 for D-flat.</param>
public readonly record struct TonicSpelling(int Letter, double Alter)
{
    /// <summary>Semitones above C of each natural letter.</summary>
    public static readonly int[] NaturalSemitones = [0, 2, 4, 5, 7, 9, 11];

    public static TonicSpelling C => new(0, 0);

    /// <summary>The pitch class 0..11 this spelling denotes.</summary>
    public int PitchClass
    {
        get
        {
            int pc = (int)Math.Round(NaturalSemitones[Letter] + Alter) % 12;
            return pc < 0 ? pc + 12 : pc;
        }
    }

    /// <summary>The letter with its accidental, e.g. <c>Db</c>.</summary>
    public override string ToString() =>
        $"{DegreeSpelling.LetterNames[Letter]}{Alter switch
        {
            0 => "",
            1 => "#",
            -1 => "b",
            2 => "##",
            -2 => "bb",
            _ => Alter.ToString("+0.##;-0.##"),
        }}";

    /// <summary>
    /// The conventional spellings of each pitch class, preferring flats - which matches how this
    /// library spells scales, since the speller resolves ties to the higher diatonic step.
    /// </summary>
    public static TonicSpelling FromPitchClass(int pitchClass)
    {
        int pc = pitchClass % 12;
        if (pc < 0)
        {
            pc += 12;
        }

        return pc switch
        {
            0 => new(0, 0),    // C
            1 => new(1, -1),   // Db
            2 => new(1, 0),    // D
            3 => new(2, -1),   // Eb
            4 => new(2, 0),    // E
            5 => new(3, 0),    // F
            6 => new(4, -1),   // Gb
            7 => new(4, 0),    // G
            8 => new(5, -1),   // Ab
            9 => new(5, 0),    // A
            10 => new(6, -1),  // Bb
            _ => new(6, 0),    // B
        };
    }
}
