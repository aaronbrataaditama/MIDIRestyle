namespace MidiRestyle.Core.Tuning;

/// <summary>
/// An absolute pitch, measured in cents above MIDI note 0 (C-1).
/// </summary>
/// <remarks>
/// <para>
/// Cents, not MIDI note numbers, is what lets the app express genuinely microtonal scales - Arabic
/// maqam, gamelan, Thai 7-equal, Persian dastgah - rather than 12-tone-equal-tempered caricatures of
/// them. Every mapping algorithm works in cents and never sees a note number;
/// <see cref="MidiNote"/> and <see cref="BendCents"/> are consumed only at the output stage.
/// </para>
/// <para>
/// Deliberately unbounded. Degree mapping changes the range of a piece by
/// <c>targetDegreeCount / sourceDegreeCount</c> - exactly 1.4x for a 7-note source into a 5-note
/// target - so out-of-range results are routine, not exceptional: a full-piano-range file mapped
/// into Slendro lands at 4.80..127.20. Clamping is a policy decision that belongs to
/// <c>MappingOptions.RangePolicy</c> in <c>RestyleEngine</c>, not to this value type. Use
/// <see cref="IsInMidiRange"/> to test.
/// </para>
/// </remarks>
/// <param name="Cents">Absolute cents above MIDI note 0.</param>
public readonly record struct Pitch(double Cents) : IComparable<Pitch>
{
    /// <summary>Lowest representable MIDI note number.</summary>
    public const int MinMidiNote = 0;

    /// <summary>Highest representable MIDI note number.</summary>
    public const int MaxMidiNote = 127;

    /// <summary>
    /// The nearest MIDI note number, ties away from zero. May fall outside 0..127 - see the remarks
    /// on <see cref="Pitch"/>.
    /// </summary>
    public int MidiNote => MidiRounding.ToNearestSemitone(Cents);

    /// <summary>
    /// The pitch-bend needed on top of <see cref="MidiNote"/> to sound this pitch, in cents.
    /// Always within [-50, +50).
    /// </summary>
    public double BendCents => Cents - MidiNote * MidiRounding.CentsPerSemitone;

    /// <summary>Whether <see cref="MidiNote"/> is representable in a MIDI message.</summary>
    public bool IsInMidiRange => MidiNote is >= MinMidiNote and <= MaxMidiNote;

    /// <summary>Whether this pitch sits exactly on the 12-TET grid.</summary>
    public bool IsTwelveTet => BendCents == 0.0;

    /// <summary>The pitch class 0..11 of <see cref="MidiNote"/>, where 0 is C.</summary>
    public int PitchClass
    {
        get
        {
            // Positive modulo: MidiNote can be negative for out-of-range results, and C#'s %
            // keeps the sign of the dividend.
            int pc = MidiNote % MidiRounding.SemitonesPerOctave;
            return pc < 0 ? pc + MidiRounding.SemitonesPerOctave : pc;
        }
    }

    /// <summary>Creates a pitch exactly on a 12-TET MIDI note.</summary>
    public static Pitch FromMidi(int note) => new(note * MidiRounding.CentsPerSemitone);

    /// <summary>Creates a pitch from a MIDI note plus a cents offset.</summary>
    public static Pitch FromMidi(int note, double bendCents) =>
        new(note * MidiRounding.CentsPerSemitone + bendCents);

    /// <summary>This pitch shifted by whole octaves.</summary>
    public Pitch ShiftOctaves(int octaves) => new(Cents + octaves * MidiRounding.CentsPerOctave);

    /// <summary>This pitch shifted by a cents interval.</summary>
    public Pitch ShiftCents(double cents) => new(Cents + cents);

    public int CompareTo(Pitch other) => Cents.CompareTo(other.Cents);

    public static bool operator <(Pitch a, Pitch b) => a.Cents < b.Cents;
    public static bool operator >(Pitch a, Pitch b) => a.Cents > b.Cents;
    public static bool operator <=(Pitch a, Pitch b) => a.Cents <= b.Cents;
    public static bool operator >=(Pitch a, Pitch b) => a.Cents >= b.Cents;

    public override string ToString() =>
        BendCents == 0.0
            ? $"{MidiNote}"
            : $"{MidiNote}{BendCents:+0.##;-0.##}c";
}
