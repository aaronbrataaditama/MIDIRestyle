namespace MidiRestyle.Core.Model;

/// <summary>The three Standard MIDI File formats.</summary>
public enum MidiFileFormatKind
{
    /// <summary>
    /// Format 0: one track holding every channel. The loader splits these into per-channel
    /// pseudo-tracks, because the drum rule is per-channel while the restyle opt-out is per-track -
    /// a single checkbox otherwise could not exclude channel 10.
    /// </summary>
    SingleTrack = 0,

    /// <summary>Format 1: several tracks, one song. The primary case.</summary>
    MultiTrack = 1,

    /// <summary>
    /// Format 2: independent sequences rather than one song. Readable, not an error - a presentation
    /// decision, so the UI reports the sequence count and opens the first.
    /// </summary>
    MultiSequence = 2,
}
