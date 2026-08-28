using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Model;

/// <summary>
/// Everything the user chose. The transform is a pure function of a project and one of these.
/// </summary>
/// <remarks>
/// Because it is a pure function, changing a setting simply re-runs it - which is why there is no
/// undo stack and none is wanted. Both the original and restyled models live in memory at once, so
/// the piano-roll overlay and A/B playback are nearly free.
/// </remarks>
public sealed record RestyleSettings
{
    /// <summary>The tuning to map into.</summary>
    public required Scale TargetScale { get; init; }

    /// <summary>Where the target scale's degree 0 sits. Always a 12-TET pitch.</summary>
    public required Pitch TargetTonic { get; init; }

    /// <summary>
    /// The target tonic's letter and alteration.
    /// </summary>
    /// <remarks>
    /// Needed because a MIDI note number does not determine a letter: 61 may be C-sharp or D-flat,
    /// and every letter downstream follows from which. Only notation consumes it, but it is recorded
    /// here because only the user can decide it.
    /// </remarks>
    public TonicSpelling TonicSpelling { get; init; } = TonicSpelling.C;

    /// <summary>
    /// The tuning to map out of. A setting, not an assumption.
    /// </summary>
    /// <remarks>
    /// Krumhansl-Schmuckler only ever reports major or minor, but a file may already be pentatonic
    /// or in a maqam, and a degree index computed against the wrong source scale is simply wrong.
    /// Without this the app could restyle <em>into</em> the whole library but only <em>out of</em>
    /// two scales.
    /// </remarks>
    public Scale? SourceScale { get; init; }

    /// <summary>Where the source scale's degree 0 sits.</summary>
    public Pitch SourceTonic { get; init; }

    public MappingOptions Mapping { get; init; } = MappingOptions.Default;

    public OutputMode OutputMode { get; init; } = OutputMode.Auto;

    /// <summary>
    /// How far apart two cent-offsets may be and still share a pitch-bend channel.
    /// </summary>
    /// <remarks>
    /// The user's preference, and a starting point rather than a guarantee: when the channel budget
    /// does not fit, the allocator raises this for the <em>whole project</em>. Never per track -
    /// mixing tunings within one piece produces bitonality, not degradation.
    /// </remarks>
    public double ToleranceCents { get; init; } = OffsetClusterer.DefaultToleranceCents;

    /// <summary>
    /// Track-channels the user has opted out of restyling. Drums are excluded regardless.
    /// </summary>
    public IReadOnlySet<(int Track, int Channel)> Excluded { get; init; } =
        new HashSet<(int, int)>();

    /// <summary>Whether this track-channel should be transformed.</summary>
    public bool ShouldRestyle(TrackInfo track) =>
        track.IsRestylable && !Excluded.Contains((track.TrackIndex, track.Channel));
}
