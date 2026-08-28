namespace MidiRestyle.Core.Output;

/// <summary>How restyled pitches are rendered to MIDI.</summary>
public enum OutputMode
{
    /// <summary>
    /// Use 12-TET when the target scale is close enough to the semitone grid to make pitch-bend
    /// channels pointless, and microtonal otherwise. The default.
    /// </summary>
    /// <remarks>
    /// The test is <c>max|offset| &lt;= tolerance</c>, not "every offset is exactly zero". An exact
    /// float comparison with zero would be both fragile and blind to the clustering tolerance the
    /// allocator actually uses - a scale within 3 cents of the grid should not buy a second channel.
    /// </remarks>
    Auto,

    /// <summary>
    /// Force the semitone grid. Costs no channels and plays anywhere, at the price of misrepresenting
    /// roughly a third of the library. This is the mode the fidelity badge escalates to a warning in.
    /// </summary>
    TwelveTet,

    /// <summary>
    /// Force per-offset pitch-bend channel allocation, delivering the scale as tuned.
    /// </summary>
    Microtonal,
}
