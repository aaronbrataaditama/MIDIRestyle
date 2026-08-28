using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Restyle;

/// <summary>
/// Turns a loaded project plus settings into a restyled one. A pure function.
/// </summary>
/// <remarks>
/// <para>
/// Pure in the strong sense: same inputs, same output, no mutation of the source, no IO, no clock.
/// Three things follow from that, and all three are load-bearing elsewhere. There is no undo stack
/// and none is wanted, because changing a setting just re-runs this. The piano-roll overlay is
/// nearly free, because both models exist at once. And the scale list can be arrow-key browsable,
/// because re-running is cheap.
/// </para>
/// <para>
/// That last point is a real constraint: the list re-runs this on every keystroke, so the target is
/// <b>under 16 ms for a 20,000-note file</b>. It is a per-note pure function so that is achievable,
/// but it rules out re-parsing or re-deriving anything per note - the mapper is built once per run,
/// and the per-note path allocates nothing.
/// </para>
/// </remarks>
public static class RestyleEngine
{
    /// <summary>Restyles a project.</summary>
    public static RestyleResult Restyle(MidiProject project, RestyleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(settings);

        // Built once per run, never per note: NearestPitchMapper precomputes its candidate set, and
        // rebuilding that per note would be O(notes x candidates) on the hot path.
        //
        // Built lazily, though, because a run may map nothing at all - every track excluded, which is
        // how the A/B comparison builds its "original" side and what a user gets after unticking
        // everything. ScaleDegreeMapper throws without a source scale, so constructing one eagerly
        // made a transform that touches no notes fail for want of an input it would never read.
        IPitchMapper? mapper = null;

        IPitchMapper Mapper() => mapper ??= new MappingContext(
            settings.TargetScale,
            settings.TargetTonic,
            settings.SourceScale,
            settings.SourceTonic,
            settings.Mapping).CreateMapper();

        var tracks = new List<RestyledTrack>(project.Tracks.Count);
        RestyleTally tally = default;

        foreach (TrackInfo track in project.Tracks)
        {
            if (!settings.ShouldRestyle(track))
            {
                // Drums and opted-out tracks pass through unchanged. They still appear in the
                // result: it is a complete picture of what will be exported, not a diff.
                tracks.Add(new RestyledTrack(track.TrackIndex, track.Channel, track.Notes, WasRestyled: false));
                continue;
            }

            (IReadOnlyList<Note> notes, RestyleTally trackTally) = RestyleTrack(track, Mapper(), settings);
            tracks.Add(new RestyledTrack(track.TrackIndex, track.Channel, notes, WasRestyled: true));
            tally += trackTally;
        }

        return new RestyleResult
        {
            Source = project,
            Settings = settings,
            Tracks = tracks,
            Tally = tally,
        };
    }

    private static (IReadOnlyList<Note> Notes, RestyleTally Tally) RestyleTrack(
        TrackInfo track,
        IPitchMapper mapper,
        RestyleSettings settings)
    {
        var mapped = new List<Note>(track.Notes.Count);
        int droppedRange = 0;
        int droppedScale = 0;

        // Indexed rather than foreach: TrackInfo.Notes is IReadOnlyList, and the enumerator would
        // be the only allocation in this loop.
        IReadOnlyList<Note> source = track.Notes;
        for (int i = 0; i < source.Count; i++)
        {
            Note note = source[i];
            MappingResult result = mapper.Map(note.Pitch);

            if (result.IsMapped)
            {
                mapped.Add(note.WithPitch(result.Pitch));
                continue;
            }

            switch (result.Drop)
            {
                case DropCause.OutOfRange:
                    droppedRange++;
                    break;
                case DropCause.NotInSourceScale:
                    droppedScale++;
                    break;
            }
        }

        // Collisions are resolved per track-channel, which is the scope where overlapping Note
        // On/Off pairs on one pitch are actually ambiguous. Compressing a scale makes this routine:
        // two source degrees can land on one target degree.
        CollisionResolution resolved = CollisionResolver.Resolve(mapped, settings.Mapping.Collisions);

        return (
            resolved.Notes,
            new RestyleTally(droppedRange, droppedScale, resolved.MergedCount, resolved.DisplacedCount));
    }

    /// <summary>
    /// The restyled notes of every track, flattened and sorted by start tick.
    /// </summary>
    /// <remarks>
    /// Convenience for the piano roll, whose culling requires start order. Kept out of
    /// <see cref="RestyleResult"/> so the engine's hot path never pays for a sort the caller may not
    /// want - export, for instance, needs the notes grouped by track, not merged.
    /// </remarks>
    public static Note[] FlattenSortedByStart(RestyleResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        int total = result.TotalNoteCount;
        var all = new Note[total];
        int n = 0;

        foreach (RestyledTrack track in result.Tracks)
        {
            for (int i = 0; i < track.Notes.Count; i++)
            {
                all[n++] = track.Notes[i];
            }
        }

        Array.Sort(all, static (a, b) =>
            a.StartTicks != b.StartTicks
                ? a.StartTicks.CompareTo(b.StartTicks)
                : a.Pitch.Cents.CompareTo(b.Pitch.Cents));

        return all;
    }
}
