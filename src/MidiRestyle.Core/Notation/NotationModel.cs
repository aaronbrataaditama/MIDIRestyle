namespace MidiRestyle.Core.Notation;

/// <summary>Which clef a staff is read in.</summary>
public enum Clef
{
    Treble,
    Bass,
}

/// <summary>
/// Where an entry sits in a chain of tied notes. A span too long or too awkward for one notehead
/// becomes several entries carrying <see cref="Start"/>, then <see cref="Continue"/>, then
/// <see cref="Stop"/>.
/// </summary>
public enum TieState
{
    None,
    Start,
    Continue,
    Stop,
}

/// <summary>
/// What a note does to one beam level: the vocabulary MusicXML's <c>&lt;beam&gt;</c> element takes.
/// </summary>
/// <remarks>
/// A hook is the stub beam on a note whose neighbour at that level is absent - the sixteenth in a
/// dotted-eighth-plus-sixteenth pair carries a <see cref="BackwardHook"/> at level 2 while both
/// notes carry a full beam at level 1. Without hooks that pair cannot be written correctly at all.
/// </remarks>
public enum BeamState
{
    /// <summary>Not beamed at this level.</summary>
    None,
    Begin,
    Continue,
    End,

    /// <summary>A stub pointing right, toward a neighbour that does not share this level.</summary>
    ForwardHook,

    /// <summary>A stub pointing left.</summary>
    BackwardHook,
}

/// <summary>
/// One written event: a note, a chord member, or a rest. Rests are entries with no
/// <see cref="Note"/> rather than a separate type, because every consumer - the exporter, the
/// renderer, the voice packer - has to walk notes and rests in the same timeline anyway.
/// </summary>
public sealed record NotationEntry
{
    /// <summary>The spelled pitch, or <c>null</c> for a rest.</summary>
    public SpelledNote? Note { get; init; }

    /// <summary>The sounding pitch this was spelled from, kept for the renderer's microtonal offset.</summary>
    public Tuning.Pitch? SoundingPitch { get; init; }

    public required NotatedDuration Duration { get; init; }

    /// <summary>Absolute position from the start of the file, in ticks.</summary>
    public required long StartTicks { get; init; }

    public required long DurationTicks { get; init; }

    /// <summary>1-based. 2 only ever appears on a grand staff.</summary>
    public int Staff { get; init; } = 1;

    /// <summary>1-based, and unique within a staff.</summary>
    public int Voice { get; init; } = 1;

    /// <summary>
    /// True for every note of a chord except the first. This is MusicXML's <c>&lt;chord/&gt;</c>
    /// flag: it means "sounds with the previous note" and, critically, means the entry consumes no
    /// time - which is why the exporter must not advance its cursor past one.
    /// </summary>
    public bool IsChordMember { get; init; }

    public TieState Tie { get; init; } = TieState.None;

    /// <summary>
    /// This note's role in each beam level it participates in, level 1 (the eighth-note beam)
    /// first. Empty when the note is not beamed - which is the case for every rest, every value of
    /// a quarter or longer, and any flagged note standing alone.
    /// </summary>
    /// <remarks>
    /// Computed once by <see cref="NotationBuilder"/> rather than by each consumer, for the same
    /// reason measures and ties are: the staff renderer and the MusicXML exporter must not disagree
    /// about where a beam starts. The count is never more than
    /// <see cref="NoteValueExtensions.FlagCount"/> for the entry's own value.
    /// </remarks>
    public IReadOnlyList<BeamState> Beams { get; init; } = [];

    /// <summary>True when this note carries at least one beam, so the renderer draws no flag.</summary>
    public bool IsBeamed => Beams.Count > 0;

    public bool IsRest => Note is null;

    public long EndTicks => StartTicks + DurationTicks;
}

/// <summary>
/// One measure of one part.
/// </summary>
/// <remarks>
/// Entries are grouped by staff, then by voice, and are in time order <i>within</i> each of those
/// groups - they are <b>not</b> globally time-ordered, and start ticks do regress at each group
/// boundary. That layout is what the MusicXML exporter needs, since it writes one voice's timeline
/// and then rewinds with a <c>&lt;backup&gt;</c>. A renderer walking this list must therefore never
/// stop early on the first entry past its right edge: an earlier claim that the entries were
/// interleaved in time order cost the degree view every staff below the first.
/// </remarks>
public sealed record NotationMeasure
{
    /// <summary>1-based, as printed.</summary>
    public required int Number { get; init; }

    public required long StartTicks { get; init; }

    public required long LengthTicks { get; init; }

    public required int BeatsPerMeasure { get; init; }

    /// <summary>The lower number of the time signature: 4 for a quarter, 8 for an eighth.</summary>
    public required int BeatUnit { get; init; }

    /// <summary>
    /// True only where the signature actually changes, since MusicXML prints a
    /// <c>&lt;time&gt;</c> wherever it finds one and repeating it every measure would litter the score.
    /// </summary>
    public bool TimeSignatureChanged { get; init; }

    public required IReadOnlyList<NotationEntry> Entries { get; init; }

    public bool IsEmpty => Entries.All(e => e.IsRest);
}

/// <summary>One instrument's part: one staff, or two when it is a keyboard.</summary>
public sealed record NotationPart
{
    /// <summary>MusicXML part id, "P1" upward.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required int TrackIndex { get; init; }

    public required int Channel { get; init; }

    /// <summary>1, or 2 for a grand staff.</summary>
    public required int StaffCount { get; init; }

    /// <summary>One clef per staff, in staff order.</summary>
    public required IReadOnlyList<Clef> Clefs { get; init; }

    public required IReadOnlyList<NotationMeasure> Measures { get; init; }

    public int? ProgramNumber { get; init; }

    public bool IsGrandStaff => StaffCount > 1;
}

/// <summary>
/// A whole restyled file, ready to be drawn or exported. Immutable and derived purely from a
/// <c>RestyleResult</c> plus settings, so it re-derives rather than mutating when a setting changes -
/// the same rule the rest of the pipeline follows.
/// </summary>
public sealed record NotationScore
{
    /// <summary>
    /// MusicXML divisions per quarter note. Set to the file's own PPQN so that every duration is a
    /// whole number of divisions and nothing has to be re-scaled on the way out.
    /// </summary>
    public required int Divisions { get; init; }

    public string? Title { get; init; }

    public string? ScaleName { get; init; }

    public required IReadOnlyList<NotationPart> Parts { get; init; }

    /// <summary>
    /// Anything the builder had to decide quietly - a track that would not fit its voices, a scale
    /// that could not be spelled. Surfaced rather than swallowed, per the project's rule that a
    /// view which cannot render must explain itself.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    public bool IsEmpty => Parts.Count == 0 || Parts.All(p => p.Measures.Count == 0);

    public int MeasureCount => Parts.Count == 0 ? 0 : Parts.Max(p => p.Measures.Count);
}
