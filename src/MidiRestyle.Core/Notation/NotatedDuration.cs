namespace MidiRestyle.Core.Notation;

/// <summary>
/// One written duration: a note value, its augmentation dots, and any tuplet ratio in force. This
/// is what a single notehead can express - anything longer or more awkward becomes several of these
/// tied together, which is <see cref="DurationDecomposer"/>'s job.
/// </summary>
public readonly record struct NotatedDuration(NoteValue Value, int Dots = 0, Tuplet Tuplet = default)
{
    /// <summary>Three dots is the practical limit; beyond it nobody can read the rhythm.</summary>
    public const int MaxDots = 3;

    /// <summary>
    /// The tuplet, normalised. A default-constructed <see cref="Tuplet"/> is 0:0, which would make
    /// the arithmetic produce NaN - so an unset ratio reads as <see cref="Tuplet.None"/>.
    /// </summary>
    public Tuplet EffectiveTuplet =>
        Tuplet.ActualNotes == 0 ? Notation.Tuplet.None : Tuplet;

    /// <summary>
    /// What the dots multiply the value by: one dot is 1.5x, two is 1.75x, three is 1.875x. The
    /// series is 2 - 2^-dots, which is where the familiar "half as much again" comes from.
    /// </summary>
    public double DotFactor => 2.0 - Math.Pow(2, -Dots);

    /// <summary>Sounding length in ticks. May be fractional; callers round once, at the end.</summary>
    public double Ticks(int ppqn) =>
        Value.UndottedTicks(ppqn) * DotFactor * EffectiveTuplet.Scale;

    public override string ToString() =>
        $"{Value}{new string('.', Dots)}{(EffectiveTuplet.IsNone ? "" : $" [{EffectiveTuplet}]")}";
}
