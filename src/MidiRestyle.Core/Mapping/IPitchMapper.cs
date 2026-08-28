using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Mapping;

/// <summary>Why a source note produced no output pitch.</summary>
public enum DropCause
{
    /// <summary>Not dropped. The result carries a real pitch.</summary>
    None,

    /// <summary>
    /// The note was not a degree of the source scale and
    /// <see cref="NonScaleNotePolicy.Drop"/> is in force.
    /// </summary>
    NotInSourceScale,

    /// <summary>
    /// The mapped pitch fell outside MIDI 0..127 and the <see cref="RangePolicy"/> in force did not
    /// rescue it. Routine, not exceptional: degree mapping scales a piece's range by
    /// <c>targetDegreeCount / sourceDegreeCount</c>.
    /// </summary>
    OutOfRange,
}

/// <summary>
/// The outcome of mapping one note: either a pitch, or a reason there is none.
/// </summary>
/// <remarks>
/// <para>
/// A value type with no reference fields, because <see cref="IPitchMapper.Map"/> runs once per note
/// - tens of thousands of times per keystroke while the user arrow-keys the scale list - and the
/// engine has a 16 ms budget. Drops are returned, never thrown: they are ordinary output that the
/// status bar counts and the piano roll draws as dashed outlines.
/// </para>
/// <para>
/// Construct through <see cref="Mapped"/> and <see cref="Dropped"/>; the default value is not
/// meaningful.
/// </para>
/// </remarks>
/// <param name="Pitch">The mapped pitch. Meaningless unless <see cref="IsMapped"/>.</param>
/// <param name="Drop">Why there is no pitch, or <see cref="DropCause.None"/>.</param>
public readonly record struct MappingResult(Pitch Pitch, DropCause Drop)
{
    /// <summary>Whether this result carries a usable pitch.</summary>
    public bool IsMapped => Drop == DropCause.None;

    /// <summary>A successful mapping.</summary>
    public static MappingResult Mapped(Pitch pitch) => new(pitch, DropCause.None);

    /// <summary>A dropped note, with the cause the status bar reports.</summary>
    public static MappingResult Dropped(DropCause cause) => new(default, cause);
}

/// <summary>
/// Maps one source pitch onto the target scale.
/// </summary>
/// <remarks>
/// <para>
/// The context is bound at construction, not passed per call. That is deliberate on two counts.
/// First, <see cref="NearestPitchMapper"/> must precompute its candidate pitch set once per run;
/// building it per note would be O(notes x candidates) on the hot path. Second, it lets each
/// implementation take <em>only</em> the inputs it actually uses - <see cref="NearestPitchMapper"/>
/// is never handed a source scale or a detected key, so it cannot silently ignore them, and the
/// UI's claim that it does not consult them is structural rather than a comment.
/// </para>
/// <para>Implementations must be allocation-free in <see cref="Map"/> and safe to call repeatedly.</para>
/// </remarks>
public interface IPitchMapper
{
    /// <summary>Which strategy this mapper implements.</summary>
    MappingStrategy Strategy { get; }

    /// <summary>
    /// Whether the source scale and detected key influence the result. False for
    /// <see cref="NearestPitchMapper"/>, which is what the UI binds its control dimming to.
    /// </summary>
    bool UsesSourceScale { get; }

    /// <summary>Maps one pitch, applying the non-scale-note and range policies in force.</summary>
    MappingResult Map(Pitch source);
}

/// <summary>
/// Everything a mapping run needs: the target tuning, the source tuning when there is one, and the
/// policies. Built once per restyle, never per note.
/// </summary>
/// <remarks>
/// The target is required and the source is optional, which is the honest shape:
/// <see cref="MappingStrategy.NearestPitch"/> has no source scale to speak of, while
/// <see cref="MappingStrategy.ScaleDegree"/> cannot work without one - a note's degree index is only
/// defined relative to a source scale.
/// </remarks>
public sealed class MappingContext
{
    /// <param name="targetScale">The tuning to map into.</param>
    /// <param name="targetTonic">Where the target scale's degree 0 sits. A 12-TET pitch.</param>
    /// <param name="sourceScale">
    /// The tuning to map out of. Required by <see cref="ScaleDegreeMapper"/>; unused - and so
    /// normally left null - by <see cref="NearestPitchMapper"/>. Selectable rather than assumed
    /// diatonic: key detection only reports major or minor, but a file may already be pentatonic or
    /// in a maqam, and degree indices computed against the wrong source scale are wrong.
    /// </param>
    /// <param name="sourceTonic">Where the source scale's degree 0 sits.</param>
    /// <param name="options">The policies. Defaults to <see cref="MappingOptions.Default"/>.</param>
    public MappingContext(
        Scale targetScale,
        Pitch targetTonic,
        Scale? sourceScale = null,
        Pitch sourceTonic = default,
        MappingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(targetScale);

        TargetScale = targetScale;
        TargetTonic = targetTonic;
        SourceScale = sourceScale;
        SourceTonic = sourceTonic;
        Options = options ?? MappingOptions.Default;
    }

    /// <summary>The tuning being mapped into.</summary>
    public Scale TargetScale { get; }

    /// <summary>Where the target scale's degree 0 sits.</summary>
    public Pitch TargetTonic { get; }

    /// <summary>The source tuning, or null when the strategy in force does not use one.</summary>
    public Scale? SourceScale { get; }

    /// <summary>Where the source scale's degree 0 sits.</summary>
    public Pitch SourceTonic { get; }

    /// <summary>The policies in force.</summary>
    public MappingOptions Options { get; }

    /// <summary>
    /// Builds the mapper this context's <see cref="MappingOptions.Strategy"/> calls for.
    /// </summary>
    /// <remarks>
    /// Note what the <see cref="MappingStrategy.NearestPitch"/> arm passes: the target scale, the
    /// target tonic and the policies, and nothing else. That is the invariant made visible.
    /// </remarks>
    public IPitchMapper CreateMapper() => Options.Strategy switch
    {
        MappingStrategy.NearestPitch => new NearestPitchMapper(TargetScale, TargetTonic, Options),
        _ => new ScaleDegreeMapper(this),
    };
}

/// <summary>
/// Applies <see cref="RangePolicy"/> to a mapped pitch.
/// </summary>
/// <remarks>
/// Public and shared so the mapper, <c>RestyleEngine</c> and the exporter's re-assertion cannot
/// drift apart. Degree mapping changes the range of a piece by
/// <c>targetDegreeCount / sourceDegreeCount</c> - exactly 1.4x for 7 degrees into 5 - so overflow
/// happens on ordinary input, not on edge cases. A full-piano-range file into Slendro reaches MIDI
/// 154 at the top and -24 at the bottom, and <c>(SevenBitNumber)130</c> throws inside DryWetMIDI at
/// export.
/// </remarks>
public static class RangeEnforcer
{
    /// <summary>The lowest in-range pitch, MIDI 0, in cents.</summary>
    public const double MinCents = Pitch.MinMidiNote * MidiRounding.CentsPerSemitone;

    /// <summary>The highest in-range pitch, MIDI 127, in cents.</summary>
    public const double MaxCents = Pitch.MaxMidiNote * MidiRounding.CentsPerSemitone;

    // 128 semitones is under 11 octaves, so a shift or a fold always settles well inside this. The
    // bound exists so a pathological input cannot spin, not because it is expected to be reached.
    private const int MaxSteps = 32;

    /// <summary>Brings <paramref name="pitch"/> into MIDI range, or reports it as dropped.</summary>
    public static MappingResult Apply(Pitch pitch, RangePolicy policy)
    {
        if (pitch.IsInMidiRange)
        {
            return MappingResult.Mapped(pitch);
        }

        return policy switch
        {
            RangePolicy.Drop => MappingResult.Dropped(DropCause.OutOfRange),
            RangePolicy.FoldOctave => MappingResult.Mapped(Fold(pitch)),
            _ => ShiftIntoRange(pitch),
        };
    }

    /// <summary>
    /// Shifts by whole octaves until the note fits, which preserves pitch class and scale degree -
    /// what the ear notices. Drops it if no octave fits.
    /// </summary>
    public static MappingResult ShiftIntoRange(Pitch pitch)
    {
        if (pitch.IsInMidiRange)
        {
            return MappingResult.Mapped(pitch);
        }

        int direction = pitch.MidiNote < Pitch.MinMidiNote ? 1 : -1;
        Pitch shifted = pitch;

        for (int step = 0; step < MaxSteps; step++)
        {
            shifted = shifted.ShiftOctaves(direction);

            if (shifted.IsInMidiRange)
            {
                return MappingResult.Mapped(shifted);
            }

            // Overshot straight past the range without landing in it. Cannot happen while the range
            // spans more than an octave, but the policy is specified to drop rather than to loop.
            int nextDirection = shifted.MidiNote < Pitch.MinMidiNote ? 1 : -1;
            if (nextDirection != direction)
            {
                break;
            }
        }

        return MappingResult.Dropped(DropCause.OutOfRange);
    }

    /// <summary>
    /// Reflects the note back off the range boundaries. Keeps every note, at the cost of inverting
    /// contour at the extremes.
    /// </summary>
    public static Pitch Fold(Pitch pitch)
    {
        double cents = pitch.Cents;

        for (int step = 0; step < MaxSteps && (cents < MinCents || cents > MaxCents); step++)
        {
            cents = cents < MinCents
                ? MinCents + (MinCents - cents)
                : MaxCents - (cents - MaxCents);
        }

        return new Pitch(Math.Clamp(cents, MinCents, MaxCents));
    }
}
