namespace MidiRestyle.Core.Notation;

/// <summary>
/// A written note value - the shape of the notehead and flags, before dots or tuplets. The names
/// are the MusicXML <c>&lt;type&gt;</c> vocabulary, which is why "16th" and not "Sixteenth" comes
/// back from <see cref="NoteValueExtensions.MusicXmlType"/>.
/// </summary>
/// <remarks>
/// Ordered longest to shortest so that <c>&lt;</c> and <c>&gt;</c> mean what they read as, and so a
/// greedy longest-first decomposition can simply walk the enum.
/// </remarks>
public enum NoteValue
{
    /// <summary>Double whole note (breve). Rare, but a 4/2 bar of it is legal.</summary>
    Breve = 0,
    Whole = 1,
    Half = 2,
    Quarter = 3,
    Eighth = 4,
    Sixteenth = 5,
    ThirtySecond = 6,
    SixtyFourth = 7,
}

/// <summary>Tick arithmetic and MusicXML naming for <see cref="NoteValue"/>.</summary>
public static class NoteValueExtensions
{
    /// <summary>
    /// Undotted length in ticks, given the file's ticks-per-quarter-note. A quarter is exactly
    /// <paramref name="ppqn"/>; every other value doubles or halves from there.
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="double"/> rather than a long on purpose: at a low PPQN a 64th note is a
    /// fraction of a tick, and rounding here - before dots and tuplet ratios have been applied -
    /// would compound the error into something visible. Callers round once, at the end.
    /// </remarks>
    public static double UndottedTicks(this NoteValue value, int ppqn) =>
        value switch
        {
            NoteValue.Breve => ppqn * 8.0,
            NoteValue.Whole => ppqn * 4.0,
            NoteValue.Half => ppqn * 2.0,
            NoteValue.Quarter => ppqn,
            NoteValue.Eighth => ppqn / 2.0,
            NoteValue.Sixteenth => ppqn / 4.0,
            NoteValue.ThirtySecond => ppqn / 8.0,
            NoteValue.SixtyFourth => ppqn / 16.0,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown note value."),
        };

    /// <summary>The MusicXML <c>&lt;type&gt;</c> text. Not the enum name for the short values.</summary>
    public static string MusicXmlType(this NoteValue value) =>
        value switch
        {
            NoteValue.Breve => "breve",
            NoteValue.Whole => "whole",
            NoteValue.Half => "half",
            NoteValue.Quarter => "quarter",
            NoteValue.Eighth => "eighth",
            NoteValue.Sixteenth => "16th",
            NoteValue.ThirtySecond => "32nd",
            NoteValue.SixtyFourth => "64th",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown note value."),
        };

    /// <summary>
    /// How many flags or beams the value carries. Quarter and longer have none; this drives both
    /// the staff renderer's flag drawing and its beam grouping.
    /// </summary>
    public static int FlagCount(this NoteValue value) =>
        value <= NoteValue.Quarter ? 0 : (int)value - (int)NoteValue.Quarter;

    /// <summary>True when the notehead is drawn hollow (breve, whole, half).</summary>
    public static bool IsHollow(this NoteValue value) => value <= NoteValue.Half;

    /// <summary>Longest value first - the order a greedy decomposition wants.</summary>
    public static IReadOnlyList<NoteValue> LongestFirst { get; } =
        Enum.GetValues<NoteValue>().OrderBy(v => (int)v).ToArray();
}
