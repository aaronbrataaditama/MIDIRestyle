using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Notation;

/// <summary>
/// Decides how many staves a track gets, in which clefs, and - for a grand staff - which hand each
/// note belongs to.
/// </summary>
public static class StaffLayout
{
    /// <summary>
    /// GM program numbers that get a grand staff, 0-based. The plan names GM 1-8 (the pianos) and
    /// 17-24 (the organs) in the 1-based numbering the GM tables print, which is 0-7 and 16-23 here.
    /// </summary>
    /// <remarks>
    /// Restricted to keyboards on purpose. A guitar or a string pad written across two staves is
    /// harder to read, not easier, and nothing else in GM is idiomatically notated that way.
    /// </remarks>
    public static bool IsKeyboard(int? programNumber) =>
        programNumber is >= 0 and <= 7 or >= 16 and <= 23;

    /// <summary>
    /// Where the hands divide on a grand staff. Middle C is the conventional split and, more to the
    /// point, the one a reader expects; anything cleverer produces a different answer per file.
    /// </summary>
    public const int DefaultSplitMidiNote = 60;

    /// <summary>Treble sits above the split, bass below.</summary>
    public static int StaffFor(Pitch pitch, int splitMidiNote = DefaultSplitMidiNote) =>
        pitch.MidiNote >= splitMidiNote ? 1 : 2;

    /// <summary>
    /// Picks a clef for a single-staff part from where its notes actually sit.
    /// </summary>
    /// <remarks>
    /// The median rather than the mean, because one stray low pedal note in an otherwise high part
    /// would drag a mean below the threshold and put the whole part in the wrong clef.
    /// </remarks>
    public static Clef ClefFor(IReadOnlyList<Note> notes)
    {
        if (notes.Count == 0)
        {
            return Clef.Treble;
        }

        int[] pitches = [.. notes.Select(n => n.Pitch.MidiNote).Order()];
        int median = pitches[pitches.Length / 2];

        return median >= DefaultSplitMidiNote ? Clef.Treble : Clef.Bass;
    }

    /// <summary>How a track will be laid out, decided once before any measure is built.</summary>
    public readonly record struct Layout(int StaffCount, IReadOnlyList<Clef> Clefs)
    {
        public bool IsGrandStaff => StaffCount > 1;
    }

    /// <summary>
    /// Chooses the layout for a track: a grand staff for keyboards that actually span the split,
    /// a single staff in the clef that fits otherwise.
    /// </summary>
    /// <remarks>
    /// The span check matters. A piano track that never goes below middle C is a right-hand part,
    /// and giving it an empty bass staff for the length of the piece wastes half the page.
    /// </remarks>
    public static Layout For(TrackInfo track, IReadOnlyList<Note> notes)
    {
        if (notes.Count == 0)
        {
            return new Layout(1, [Clef.Treble]);
        }

        if (IsKeyboard(track.ProgramNumber))
        {
            bool hasTreble = notes.Any(n => n.Pitch.MidiNote >= DefaultSplitMidiNote);
            bool hasBass = notes.Any(n => n.Pitch.MidiNote < DefaultSplitMidiNote);

            if (hasTreble && hasBass)
            {
                return new Layout(2, [Clef.Treble, Clef.Bass]);
            }
        }

        return new Layout(1, [ClefFor(notes)]);
    }
}
