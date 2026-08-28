namespace MidiRestyle.Core.Mapping;

/// <summary>Which algorithm maps source pitches onto the target scale.</summary>
public enum MappingStrategy
{
    /// <summary>
    /// Re-emit each note at the same <em>degree index</em> in the target scale. Contour survives - a
    /// rising line stays rising even when a 7-note source maps into a 5-note target - at the cost of
    /// changing the absolute register. The default, and the one that makes a restyle sound like the
    /// original melody in a new tuning.
    /// </summary>
    ScaleDegree,

    /// <summary>
    /// Snap each note to the nearest available target pitch. Preserves absolute register, flattens
    /// contour. Uses neither the source scale nor the detected key - so the UI must dim both of those
    /// controls when this is selected, or it implies an influence they do not have.
    /// </summary>
    NearestPitch,
}

/// <summary>What to do with a source note that is not in the source scale.</summary>
public enum NonScaleNotePolicy
{
    /// <summary>Snap it to the nearest source-scale degree first, then map that. The default.</summary>
    SnapToNearestSourceDegree,

    /// <summary>Leave it at its original pitch, unmapped.</summary>
    PassThrough,

    /// <summary>Drop it, and report how many were dropped.</summary>
    Drop,
}

/// <summary>What to do when two notes map onto the same pitch at the same time.</summary>
/// <remarks>
/// Not a nicety. Overlapping Note On/Off pairs on one pitch and channel are ambiguous MIDI: the
/// second Note Off is what most synths act on, so the first note hangs. Compressing a 7-note scale
/// into 5 makes this routine, not rare.
/// </remarks>
public enum CollisionPolicy
{
    /// <summary>Keep the longest note and discard the rest. The default.</summary>
    Merge,

    /// <summary>Move the colliding note an octave, preserving both voices.</summary>
    DisplaceOctave,
}

/// <summary>
/// What to do when a mapped note falls outside the MIDI note range.
/// </summary>
/// <remarks>
/// This is not an edge case. Degree mapping scales the range of a piece by
/// <c>targetDegreeCount / sourceDegreeCount</c> - exactly 1.4x for 7 degrees into 5 - so a
/// full-piano-range file (MIDI 21..108) mapped into Slendro lands at 4.80..127.20 and overflows.
/// Without a policy the exporter throws <see cref="ArgumentOutOfRangeException"/> from inside
/// DryWetMIDI when it builds a seven-bit note number.
/// </remarks>
public enum RangePolicy
{
    /// <summary>
    /// Shift the note by whole octaves until it fits; drop it and count it if no octave does.
    /// Preserves pitch class and scale degree, which is what the ear notices. The default.
    /// </summary>
    ShiftIntoRange,

    /// <summary>
    /// Reflect the note back into range. Keeps every note at the cost of inverting contour at the
    /// extremes.
    /// </summary>
    FoldOctave,

    /// <summary>Drop out-of-range notes and report the count.</summary>
    Drop,
}

/// <summary>The set-once policy choices that shape a mapping.</summary>
public sealed record MappingOptions
{
    public MappingStrategy Strategy { get; init; } = MappingStrategy.ScaleDegree;

    public NonScaleNotePolicy NonScaleNotes { get; init; } = NonScaleNotePolicy.SnapToNearestSourceDegree;

    public CollisionPolicy Collisions { get; init; } = CollisionPolicy.Merge;

    public RangePolicy Range { get; init; } = RangePolicy.ShiftIntoRange;

    /// <summary>
    /// Whether the source scale and detected key influence the result. False under
    /// <see cref="MappingStrategy.NearestPitch"/>, which is what the UI binds its dimming to.
    /// </summary>
    public bool UsesSourceScale => Strategy == MappingStrategy.ScaleDegree;

    public static MappingOptions Default { get; } = new();
}
