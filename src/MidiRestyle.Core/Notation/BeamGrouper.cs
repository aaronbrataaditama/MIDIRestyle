namespace MidiRestyle.Core.Notation;

/// <summary>
/// Decides which notes of a measure are joined by beams, and what each one does at each beam level.
/// </summary>
/// <remarks>
/// <para>
/// Kept out of <see cref="NotationBuilder"/> for the same reason <see cref="MeasureGrid"/> is: the
/// question it answers is small, entirely determined by its inputs, and the only way to be sure the
/// staff renderer and the MusicXML exporter draw the same beams is for both to read one answer.
/// Beaming is also the part of the layout a reader notices immediately when it is wrong - a 6/8 bar
/// beamed in twos is 3/4 to the eye, whatever the time signature says.
/// </para>
/// <para>
/// <b>The group is not the printed beat.</b> In simple time it is, but in compound time the beat
/// printed in the signature is the eighth while the beat a player feels is the dotted quarter, and
/// beaming follows the felt one. That single distinction is the whole visual difference between
/// 6/8 and 3/4, which are otherwise the same six eighths in the same bar.
/// </para>
/// </remarks>
public static class BeamGrouper
{
    /// <summary>Shared answer for every entry that carries no beam, so no array is allocated for it.</summary>
    private static readonly IReadOnlyList<BeamState> NotBeamed = [];

    /// <summary>
    /// The length of one beaming group, in ticks, for a measure of the given signature.
    /// </summary>
    /// <remarks>
    /// Simple time groups by the printed beat. Compound time - a denominator of 8 with a numerator
    /// divisible by 3 - groups by the dotted quarter, three printed beats at a time, which is what
    /// makes 6/8 look like 6/8. 3/8 satisfies the same rule and is correctly beamed as one group of
    /// three by it.
    /// </remarks>
    public static long GroupTicksFor(int beatsPerMeasure, int beatUnit, long measureLengthTicks)
    {
        if (measureLengthTicks <= 0)
        {
            return 0;
        }

        // Deliberately the same arithmetic as MeasureSpan.BeatTicks: the two must not drift, or a
        // beam group would be measured against a beat the rest of the layout does not use.
        long beat = beatsPerMeasure > 0 ? measureLengthTicks / beatsPerMeasure : measureLengthTicks;

        if (beat <= 0)
        {
            beat = measureLengthTicks;
        }

        bool compound = beatUnit == 8 && beatsPerMeasure > 0 && beatsPerMeasure % 3 == 0;

        return compound ? beat * 3 : beat;
    }

    /// <summary>Assigns beams to the entries of one measure, described by its span.</summary>
    public static IReadOnlyList<IReadOnlyList<BeamState>> Assign(
        IReadOnlyList<NotationEntry> entries, MeasureSpan measure) =>
        Assign(
            entries,
            measure.StartTicks,
            GroupTicksFor(measure.Beats, measure.BeatUnit, measure.LengthTicks));

    /// <summary>Assigns beams to the entries of one measure.</summary>
    /// <param name="entries">
    /// One measure's entries, in the builder's own order: grouped by staff and then voice, and in
    /// time order within each of those groups. Entries of different staves or voices never beam
    /// together, so the order between groups does not matter.
    /// </param>
    /// <param name="measureStartTicks">Absolute tick of the barline, which group boundaries are measured from.</param>
    /// <param name="groupTicks">One beaming group, from <see cref="GroupTicksFor"/>.</param>
    /// <returns>
    /// One list per entry, positionally matching <paramref name="entries"/>: level 1 first, and
    /// empty for every entry that carries no beam.
    /// </returns>
    public static IReadOnlyList<IReadOnlyList<BeamState>> Assign(
        IReadOnlyList<NotationEntry> entries, long measureStartTicks, long groupTicks)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var result = new IReadOnlyList<BeamState>[entries.Count];
        Array.Fill(result, NotBeamed);

        if (entries.Count < 2 || groupTicks <= 0)
        {
            return result;
        }

        // A beam joins notes of one voice on one staff. Two voices sharing a beat are two lines
        // sharing a beat, and beaming across them would say they were one.
        Dictionary<(int Staff, int Voice), List<int>> lines = [];

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            (int, int) key = (entry.Staff, entry.Voice);

            if (!lines.TryGetValue(key, out var line))
            {
                line = [];
                lines[key] = line;
            }

            line.Add(i);
        }

        List<int> group = [];

        foreach (var line in lines.Values)
        {
            AssignLine(entries, line, measureStartTicks, groupTicks, group, result);
        }

        return result;
    }

    private static void AssignLine(
        IReadOnlyList<NotationEntry> entries,
        List<int> line,
        long measureStartTicks,
        long groupTicks,
        List<int> group,
        IReadOnlyList<BeamState>[] result)
    {
        group.Clear();

        long groupIndex = 0;
        Tuplet groupTuplet = Tuplet.None;

        foreach (int index in line)
        {
            var entry = entries[index];

            // A chord member sounds with the note before it and consumes no time. The chord's timed
            // head carries the beam and the members hang off the same stem, so a member neither
            // joins the group nor interrupts it.
            if (entry.IsChordMember)
            {
                continue;
            }

            if (!CanBeam(entry))
            {
                // A rest, or a quarter or longer. Either one ends the group: a beam may not span a
                // silence, and a note with no flags has nothing to beam with.
                Flush(entries, group, result);
                continue;
            }

            long index0 = GroupIndexOf(entry.StartTicks - measureStartTicks, groupTicks);
            var tuplet = entry.Duration.EffectiveTuplet;

            // The tuplet is part of the group's identity, not merely of the note's. A triplet run
            // sits inside one beat alongside straight material and has to beam as its own bracket;
            // joining the two would put one beam over two different divisions of the beat.
            if (group.Count > 0 && (index0 != groupIndex || tuplet != groupTuplet))
            {
                Flush(entries, group, result);
            }

            if (group.Count == 0)
            {
                groupIndex = index0;
                groupTuplet = tuplet;
            }

            group.Add(index);
        }

        Flush(entries, group, result);
    }

    /// <summary>Eighth and shorter, and not a rest. Quarter and longer have no flags to join.</summary>
    private static bool CanBeam(NotationEntry entry) =>
        !entry.IsRest && entry.Duration.Value.FlagCount() >= 1;

    /// <summary>Which beaming group a position within the measure falls in.</summary>
    /// <remarks>
    /// Floored rather than truncated. A negative offset should not arise - the builder never places
    /// an entry before its own barline - but truncation would fold the beat before the barline into
    /// group 0 and silently beam across it, which is the kind of wrong that looks like a rendering
    /// glitch rather than a bug.
    /// </remarks>
    private static long GroupIndexOf(long offsetInMeasure, long groupTicks)
    {
        long index = offsetInMeasure / groupTicks;

        return offsetInMeasure < 0 && offsetInMeasure % groupTicks != 0 ? index - 1 : index;
    }

    /// <summary>
    /// Turns a completed run of beamable notes into beam states, then empties it.
    /// </summary>
    /// <remarks>
    /// A run of one is not a group. A lone eighth keeps its flag, and emitting a
    /// <see cref="BeamState.Begin"/> with nothing to end it would leave the renderer drawing a beam
    /// into empty space and MusicXML readers rejecting the note.
    /// </remarks>
    private static void Flush(
        IReadOnlyList<NotationEntry> entries, List<int> group, IReadOnlyList<BeamState>[] result)
    {
        if (group.Count < 2)
        {
            group.Clear();
            return;
        }

        for (int i = 0; i < group.Count; i++)
        {
            int flags = FlagsAt(entries, group, i);

            // Exactly as many levels as this note has flags, and never more: a beam level a note
            // does not own is a beam the renderer cannot draw and a reader will reject.
            var states = new BeamState[flags];

            for (int level = 1; level <= flags; level++)
            {
                // Level 1 always finds both neighbours, since every member of a group has at least
                // one flag - so the hook case below is reachable only from level 2 upward.
                bool left = i > 0 && FlagsAt(entries, group, i - 1) >= level;
                bool right = i < group.Count - 1 && FlagsAt(entries, group, i + 1) >= level;

                states[level - 1] = (left, right) switch
                {
                    (true, true) => BeamState.Continue,
                    (false, true) => BeamState.Begin,
                    (true, false) => BeamState.End,

                    // Neither neighbour reaches this level, so there is nothing to beam to and the
                    // note takes a stub. It points back toward the group unless it is the group's
                    // first note, which has nothing behind it - the dotted-eighth-plus-sixteenth
                    // pair and its mirror image.
                    _ => i > 0 ? BeamState.BackwardHook : BeamState.ForwardHook,
                };
            }

            result[group[i]] = states;
        }

        group.Clear();
    }

    private static int FlagsAt(IReadOnlyList<NotationEntry> entries, List<int> group, int i) =>
        entries[group[i]].Duration.Value.FlagCount();
}
