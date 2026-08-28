using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Model;

/// <summary>
/// One track-channel pair: the unit of scope, allocation and degradation throughout the app.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="TrackInfo"/> holds notes for exactly one MIDI channel. Format 1 tracks that use
/// several channels are split, and Format 0's single track is split too - so
/// <c>(TrackIndex, Channel)</c> is a stable key that every later stage can rely on.
/// </para>
/// <para>
/// This matters for correctness, not tidiness: two Format 1 tracks may legally share one channel
/// with different programs, so keying anything on channel alone would silently merge them, and
/// keying on track alone cannot express the drum exclusion.
/// </para>
/// </remarks>
public sealed record TrackInfo
{
    /// <summary>The 0-based MIDI channel reserved for percussion under General MIDI.</summary>
    public const int DrumChannel = 9;

    public required int TrackIndex { get; init; }
    public required int Channel { get; init; }

    /// <summary>Track name from an <c>FF 03</c> meta event, if present.</summary>
    public string? Name { get; init; }

    /// <summary>Program number from the first Program Change on this channel, if any.</summary>
    public int? ProgramNumber { get; init; }

    /// <summary>General MIDI instrument name for <see cref="ProgramNumber"/>, if resolvable.</summary>
    public string? InstrumentName { get; init; }

    /// <summary>
    /// Whether this track-channel already contains pitch-bend events. Microtonal output would
    /// conflict with them, so the UI warns and offers 12-TET.
    /// </summary>
    public bool HasExistingPitchBend { get; init; }

    /// <summary>
    /// Channel-wide controller values, keyed by controller number: the <em>last</em> value seen for
    /// each on this channel before its first note (or, if the channel has no notes at all, the last
    /// value seen anywhere on it). This is the state a derived pitch-bend channel must start in - see
    /// <see cref="MidiRestyle.Core.Output.PitchBendEncoder.SetupSequence"/> - so volume, pan,
    /// expression, sustain and anything else the source set are duplicated rather than silently lost
    /// on channels 2+ of a microtonal cluster. Not a whitelist: whatever the source sets is here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only the initial state is captured, never mid-piece automation.</b> A file that sweeps a
    /// controller - a volume fade, an expression swell - after its first note will not have that
    /// sweep mirrored onto derived microtonal channels: this dictionary is populated once, at load,
    /// and <see cref="PitchBendEncoder.SetupSequence"/> is only ever run once per channel too.
    /// Making mid-piece automation follow onto derived channels would need the exporter and playback
    /// engine to watch the source event stream for ordinary CCs, the way
    /// <see cref="PitchBendEncoder.RequiresSetupReemission"/> already watches for reset triggers -
    /// that is future work, not something this dictionary's presence implies.
    /// </para>
    /// <para>
    /// Bank Select (CC0/CC32) and the two command controllers, Reset All Controllers (CC121) and
    /// All Notes Off (CC123), are deliberately excluded. Bank select is conceptually carried
    /// alongside <see cref="ProgramNumber"/> rather than through this general dictionary - matching
    /// <see cref="MidiRestyle.Core.Output.SourceChannelState"/>'s own contract, which already
    /// documents CC0/CC32 as handled separately. CC121 and CC123 are commands, not state: replaying
    /// them as if they were an ordinary controller value would be wrong, and
    /// <see cref="PitchBendEncoder"/> both excludes and handles them itself.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<int, int> ControllerValues { get; init; } = new Dictionary<int, int>();

    /// <summary>
    /// Channel pressure (monophonic aftertouch), if any is in effect before this channel's first
    /// note. Captured under the same before-first-note rule and the same mid-piece limitation as
    /// <see cref="ControllerValues"/>.
    /// </summary>
    public int? ChannelPressure { get; init; }

    public required IReadOnlyList<Note> Notes { get; init; }

    /// <summary>
    /// Percussion. Never remapped - a note number selects <em>which drum is struck</em>, so
    /// transposing it changes the instrument rather than the pitch. Excluded from restyling, from
    /// key detection's pitch-class profile, and from channel allocation.
    /// </summary>
    public bool IsDrums => Channel == DrumChannel;

    /// <summary>
    /// Whether this track-channel may be restyled at all. Drums never may; the user opts others out
    /// per track in the UI.
    /// </summary>
    public bool IsRestylable => !IsDrums && Notes.Count > 0;

    public int NoteCount => Notes.Count;

    public Pitch? LowestPitch => Notes.Count == 0 ? null : Notes.Min(n => n.Pitch);

    public Pitch? HighestPitch => Notes.Count == 0 ? null : Notes.Max(n => n.Pitch);

    /// <summary>Last tick at which this track-channel is still sounding.</summary>
    public long EndTicks => Notes.Count == 0 ? 0 : Notes.Max(n => n.EndTicks);

    /// <summary>A display label, falling back through name, instrument, then channel.</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name!
        : !string.IsNullOrWhiteSpace(InstrumentName) ? InstrumentName!
        : IsDrums ? "Drums"
        : $"Channel {Channel + 1}";
}
