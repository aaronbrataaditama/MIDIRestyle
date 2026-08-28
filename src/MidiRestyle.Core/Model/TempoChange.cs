namespace MidiRestyle.Core.Model;

/// <summary>A tempo change at a tick. Restyling never touches these - pitch only.</summary>
/// <param name="Ticks">Position.</param>
/// <param name="MicrosecondsPerQuarterNote">As stored in the <c>FF 51</c> meta event.</param>
public readonly record struct TempoChange(long Ticks, int MicrosecondsPerQuarterNote)
{
    /// <summary>Beats per minute, for display.</summary>
    public double BeatsPerMinute => 60_000_000.0 / MicrosecondsPerQuarterNote;
}

/// <summary>A time-signature change at a tick.</summary>
public readonly record struct TimeSignatureChange(long Ticks, int Numerator, int Denominator)
{
    public override string ToString() => $"{Numerator}/{Denominator}";
}

/// <summary>A marker or cue point.</summary>
public readonly record struct MarkerInfo(long Ticks, string Text);
