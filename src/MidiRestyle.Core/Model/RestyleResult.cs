namespace MidiRestyle.Core.Model;

/// <summary>One track-channel after restyling.</summary>
/// <param name="TrackIndex">Matches the source track, so the two models line up for the overlay.</param>
/// <param name="Channel">The source channel.</param>
/// <param name="Notes">The restyled notes, or the originals when this track was not restyled.</param>
/// <param name="WasRestyled">
/// False for drums and for track-channels the user opted out of. Those still appear here, carrying
/// their original notes - the result is a complete picture of what will be exported, not only the
/// parts that changed.
/// </param>
public sealed record RestyledTrack(
    int TrackIndex,
    int Channel,
    IReadOnlyList<Note> Notes,
    bool WasRestyled)
{
    public bool IsDrums => Channel == TrackInfo.DrumChannel;
}

/// <summary>What the transform did, beyond producing notes.</summary>
/// <remarks>
/// Every field here is something the engine decided on the user's behalf. Each is defensible; none
/// is acceptable to do silently, which is why they are counted and surfaced in the status bar rather
/// than logged and forgotten.
/// </remarks>
/// <param name="DroppedOutOfRange">
/// Notes whose mapped pitch left MIDI 0..127 and which the range policy could not rescue. Expected
/// on wide-range material: degree mapping scales a piece's range by
/// <c>targetDegreeCount / sourceDegreeCount</c>, so 7 degrees into 5 stretches it by 1.4x.
/// </param>
/// <param name="DroppedNotInScale">Notes discarded by <c>NonScaleNotePolicy.Drop</c>.</param>
/// <param name="Merged">Colliding notes discarded by the collision policy.</param>
/// <param name="Displaced">Colliding notes moved an octave to preserve both voices.</param>
public readonly record struct RestyleTally(
    int DroppedOutOfRange,
    int DroppedNotInScale,
    int Merged,
    int Displaced)
{
    public int TotalDropped => DroppedOutOfRange + DroppedNotInScale;

    public bool IsClean => TotalDropped == 0 && Merged == 0 && Displaced == 0;

    /// <summary>A one-line summary for the status bar, or null when nothing needs saying.</summary>
    public string? Describe()
    {
        if (IsClean)
        {
            return null;
        }

        List<string> parts = [];
        if (DroppedOutOfRange > 0)
        {
            parts.Add($"{DroppedOutOfRange} dropped (out of MIDI range)");
        }

        if (DroppedNotInScale > 0)
        {
            parts.Add($"{DroppedNotInScale} dropped (not in source scale)");
        }

        if (Merged > 0)
        {
            parts.Add($"{Merged} merged (collided on the same pitch)");
        }

        if (Displaced > 0)
        {
            parts.Add($"{Displaced} moved an octave (collided)");
        }

        return string.Join(", ", parts);
    }

    public static RestyleTally operator +(RestyleTally a, RestyleTally b) => new(
        a.DroppedOutOfRange + b.DroppedOutOfRange,
        a.DroppedNotInScale + b.DroppedNotInScale,
        a.Merged + b.Merged,
        a.Displaced + b.Displaced);
}

/// <summary>
/// The restyled model. Immutable, and held alongside the source rather than replacing it.
/// </summary>
public sealed record RestyleResult
{
    public required MidiProject Source { get; init; }

    public required RestyleSettings Settings { get; init; }

    public required IReadOnlyList<RestyledTrack> Tracks { get; init; }

    public RestyleTally Tally { get; init; }

    /// <summary>Only the track-channels that were actually transformed.</summary>
    public IEnumerable<RestyledTrack> RestyledTracks => Tracks.Where(t => t.WasRestyled);

    public int TotalNoteCount => Tracks.Sum(t => t.Notes.Count);

    /// <summary>
    /// Whether this result needs pitch bend to sound correctly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asks about <em>this result</em>, not about the target scale. The distinction matters: a result
    /// with every track excluded - which is how the A/B comparison builds its "original" side, and
    /// what a user gets after unticking everything - contains only untouched source pitches and needs
    /// no bend at all, however microtonal the selected target happens to be. An earlier version
    /// tested only the scale and so refused to export that case as "microtonal", which was wrong.
    /// </para>
    /// <para>
    /// Stated against the tolerance rather than as an exact comparison with zero: a scale within a
    /// few cents of the semitone grid should not buy a second channel, and float equality would be
    /// fragile besides. Still a conservative scale-level answer for the restyled case - cheap, and
    /// callers that need the exact truth walk the notes.
    /// </para>
    /// </remarks>
    public bool NeedsPitchBend =>
        Tracks.Any(t => t.WasRestyled)
        && Settings.TargetScale.MaxOffsetCents > Settings.ToleranceCents;
}
