namespace MidiRestyle.Core.Notation;

/// <summary>
/// A tuplet ratio - <paramref name="ActualNotes"/> written in the time of
/// <paramref name="NormalNotes"/>. A triplet is 3-in-the-time-of-2.
/// </summary>
/// <remarks>
/// <see cref="None"/> is 1:1 rather than null, so that duration arithmetic never has to branch on
/// "is this a tuplet". The ratio multiplies straight through in every case.
/// </remarks>
public readonly record struct Tuplet(int ActualNotes, int NormalNotes)
{
    /// <summary>No tuplet: one note in the time of one note.</summary>
    public static Tuplet None => new(1, 1);

    /// <summary>Three in the time of two - by far the most common.</summary>
    public static Tuplet Triplet => new(3, 2);

    /// <summary>Six in the time of four.</summary>
    public static Tuplet Sextuplet => new(6, 4);

    /// <summary>Five in the time of four.</summary>
    public static Tuplet Quintuplet => new(5, 4);

    public bool IsNone => ActualNotes == NormalNotes;

    /// <summary>
    /// What a written value is multiplied by to get its sounding length. Three-in-two makes each
    /// note two-thirds of its written value.
    /// </summary>
    public double Scale => (double)NormalNotes / ActualNotes;

    public override string ToString() => IsNone ? "-" : $"{ActualNotes}:{NormalNotes}";
}
