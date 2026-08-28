using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Model;

/// <summary>
/// A single sounding note, positioned in ticks and pitched in cents.
/// </summary>
/// <remarks>
/// One note type serves both the loaded source and the restyled result. Source notes carry pitches
/// that happen to sit on the 12-TET grid (<see cref="Pitch.FromMidi(int)"/>); restyled notes carry
/// true microtonal cents. That uniformity is what lets the piano roll draw ghost originals and solid
/// restyled notes through one code path, reading cents directly and never caring about channels.
/// <para>
/// Channel is deliberately absent: it belongs to <see cref="TrackInfo"/>. Format 0 files are split
/// into per-channel pseudo-tracks at load, so <c>(track, channel)</c> is the scope key everywhere
/// downstream and a note's channel is always its track's.
/// </para>
/// </remarks>
/// <param name="Pitch">Absolute pitch in cents.</param>
/// <param name="StartTicks">Onset, in the file's tick units.</param>
/// <param name="LengthTicks">Duration in ticks. Zero-length notes are legal in MIDI and preserved.</param>
/// <param name="Velocity">MIDI velocity, 1..127.</param>
public readonly record struct Note(Pitch Pitch, long StartTicks, long LengthTicks, byte Velocity)
{
    /// <summary>One tick past the end of this note.</summary>
    public long EndTicks => StartTicks + LengthTicks;

    /// <summary>Whether this note overlaps <paramref name="other"/> in time.</summary>
    public bool OverlapsInTime(Note other) =>
        StartTicks < other.EndTicks && other.StartTicks < EndTicks;

    /// <summary>This note at a different pitch, timing and velocity untouched.</summary>
    public Note WithPitch(Pitch pitch) => this with { Pitch = pitch };
}
