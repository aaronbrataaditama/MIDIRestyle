namespace MidiRestyle.Core.Model;

/// <summary>
/// How a MIDI file divides time. Two genuinely different schemes, not one with a variant.
/// </summary>
/// <remarks>
/// Most files are <see cref="TicksPerQuarterNote"/>. SMPTE-timed files exist and have
/// <em>no PPQN at all</em> - and no meaningful tempo map, since their timebase is absolute wall
/// clock rather than musical. Modelling this as "PPQN, possibly zero" would put a wrong number in
/// the metadata header; a closed hierarchy forces the UI to say which it is.
/// </remarks>
public abstract record TimeDivision
{
    /// <summary>A short human-readable description for the metadata header.</summary>
    public abstract string Describe();
}

/// <summary>Musical timebase: <paramref name="Ticks"/> ticks per quarter note.</summary>
public sealed record TicksPerQuarterNote(short Ticks) : TimeDivision
{
    public override string Describe() => $"{Ticks} PPQN";
}

/// <summary>
/// Absolute timebase. <paramref name="FramesPerSecond"/> is 24, 25, 29 (drop-frame) or 30.
/// </summary>
public sealed record SmpteDivision(int FramesPerSecond, int TicksPerFrame) : TimeDivision
{
    public override string Describe() => $"SMPTE {FramesPerSecond} fps, {TicksPerFrame} ticks/frame";
}
