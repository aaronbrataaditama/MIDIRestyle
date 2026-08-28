using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Mapping;

/// <summary>
/// The result of running <see cref="CollisionResolver.Resolve"/>: the resolved notes plus a count of
/// what changed, so callers (the status bar, ultimately) can report it rather than silently losing
/// notes.
/// </summary>
/// <param name="Notes">
/// The resolved notes, in a canonical order (by start tick, then pitch) so the result is identical
/// regardless of the order notes were supplied in. Never the same list instance as the input.
/// </param>
/// <param name="MergedCount">
/// How many source notes were discarded because they collided and <see cref="CollisionPolicy.Merge"/>
/// kept a different note instead - including notes that collided under
/// <see cref="CollisionPolicy.DisplaceOctave"/> but could not be displaced into range without a new
/// collision, and so fell back to being merged away.
/// </param>
/// <param name="DisplacedCount">How many notes were moved an octave to resolve a collision.</param>
public sealed record CollisionResolution(IReadOnlyList<Note> Notes, int MergedCount, int DisplacedCount)
{
    /// <summary>Total notes affected - the number the status bar should report.</summary>
    public int TotalResolvedCount => MergedCount + DisplacedCount;

    /// <summary>Whether any collision was found and resolved.</summary>
    public bool HadCollisions => TotalResolvedCount > 0;
}

/// <summary>
/// Resolves colliding notes: two notes overlapping in time, on the same pitch, within the same
/// (track, channel). See the "Mapped notes can collide" invariant in <c>CLAUDE.md</c> - this is what
/// makes degree mapping's routine 7-into-5 compression produce correct MIDI instead of ambiguous
/// Note On/Off pairs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> <see cref="Resolve"/> takes one flat list of notes and treats all of it as one
/// (track, channel) scope - it never looks at a channel number, because <c>Note</c> deliberately has
/// none (see the remarks on <see cref="Note"/>: channel belongs to <c>TrackInfo</c>, and
/// <c>(track, channel)</c> is a stable key everywhere downstream). Callers resolve collisions once
/// per <c>TrackInfo</c>, passing only that track-channel's notes. Two notes at the same pitch and
/// time on <em>different</em> channels are never a collision, because they are never in the same
/// call - pitch bend and Note On/Off pairing are channel-wide, so cross-channel "collisions" are not
/// ambiguous MIDI at all.
/// </para>
/// <para>
/// <b>Pitch comparison.</b> Notes are grouped by exact <see cref="Pitch.Cents"/>, never by
/// <see cref="Pitch.MidiNote"/>. Two microtonal notes 50 cents apart share a MIDI note number but sit
/// on different pitch-bend channels after allocation and are not in conflict - collapsing them by
/// note number would silently delete a perfectly fine note.
/// </para>
/// <para>
/// <b>Zero-length notes.</b> Zero-length notes are legal MIDI and preserved by the loader
/// (<see cref="Note.LengthTicks"/>). This resolver uses <see cref="Note.OverlapsInTime"/> exactly as
/// given, which already encodes the right answer: two zero-length notes at the very same tick never
/// overlap each other (both of <c>OverlapsInTime</c>'s strict inequalities require positive width to
/// satisfy), because there is no window in which their Note On/Off pairing could be misread - but a
/// zero-length note <em>does</em> collide with a longer note that is already sounding strictly across
/// that tick, because the zero-length note's instantaneous On/Off pair still lands inside the other
/// note's sounding window and the same ambiguity applies. This resolver makes no special case for
/// zero length: it is consistent by construction, simply by trusting <c>OverlapsInTime</c>.
/// </para>
/// <para>
/// <b>Determinism.</b> Within a group of colliding notes, the note kept (or kept in place under
/// displacement) is chosen by longest <see cref="Note.LengthTicks"/>, then earliest
/// <see cref="Note.StartTicks"/>, then highest <see cref="Note.Velocity"/> - fields of the notes
/// themselves, never input order or hashing, so the result is identical no matter how the caller's
/// list was ordered or shuffled.
/// </para>
/// <para>
/// <b>Performance.</b> Runs inside <c>RestyleEngine</c>'s 16 ms budget for a 20,000-note file: one
/// sort by pitch group, one linear sweep per group (not an all-pairs comparison), and one final sort
/// for canonical output order. O(n log n) overall.
/// </para>
/// </remarks>
public static class CollisionResolver
{
    /// <summary>
    /// Resolves every colliding pair or cluster in <paramref name="notes"/> per <paramref name="policy"/>.
    /// Does not mutate <paramref name="notes"/>; always returns a new list.
    /// </summary>
    /// <param name="notes">
    /// Notes from exactly one (track, channel) scope - see the remarks on this type.
    /// </param>
    /// <param name="policy">
    /// <see cref="CollisionPolicy.Merge"/> keeps the longest note in each colliding group and
    /// discards the rest. <see cref="CollisionPolicy.DisplaceOctave"/> keeps the same note in place
    /// and tries to move each remaining colliding note up an octave, then down, then falls back to
    /// merging it away if neither octave both fits the MIDI range and avoids creating a new collision.
    /// </param>
    public static CollisionResolution Resolve(IReadOnlyList<Note> notes, CollisionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var resolved = new List<Note>(notes.Count);
        int mergedCount = 0;
        int displacedCount = 0;

        foreach (IGrouping<double, Note> pitchGroup in notes.GroupBy(n => n.Pitch.Cents))
        {
            List<Note> group = [.. pitchGroup];
            if (group.Count == 1)
            {
                resolved.Add(group[0]);
                continue;
            }

            // Sort by (start, end) ascending. End is a required secondary key, not cosmetic: for two
            // notes that share a start tick, Note.OverlapsInTime says they overlap only if *both*
            // have positive width past that shared point. Processing the zero/shorter-ending one
            // first keeps the running-end sweep below from wrongly inheriting a longer sibling's
            // window before that check is made.
            group.Sort(static (a, b) =>
            {
                int byStart = a.StartTicks.CompareTo(b.StartTicks);
                return byStart != 0 ? byStart : a.EndTicks.CompareTo(b.EndTicks);
            });

            int i = 0;
            while (i < group.Count)
            {
                long runningEnd = group[i].EndTicks;
                int j = i + 1;
                while (j < group.Count && group[j].StartTicks < runningEnd)
                {
                    if (group[j].EndTicks > runningEnd)
                    {
                        runningEnd = group[j].EndTicks;
                    }

                    j++;
                }

                int componentSize = j - i;
                if (componentSize == 1)
                {
                    resolved.Add(group[i]);
                }
                else
                {
                    ResolveComponent(group.GetRange(i, componentSize), policy, resolved, ref mergedCount, ref displacedCount);
                }

                i = j;
            }
        }

        resolved.Sort(static (a, b) =>
        {
            int byStart = a.StartTicks.CompareTo(b.StartTicks);
            if (byStart != 0)
            {
                return byStart;
            }

            int byPitch = a.Pitch.Cents.CompareTo(b.Pitch.Cents);
            if (byPitch != 0)
            {
                return byPitch;
            }

            int byLength = b.LengthTicks.CompareTo(a.LengthTicks);
            return byLength != 0 ? byLength : b.Velocity.CompareTo(a.Velocity);
        });

        return new CollisionResolution(resolved, mergedCount, displacedCount);
    }

    /// <summary>Resolves one connected group of mutually time-overlapping, same-pitch notes.</summary>
    private static void ResolveComponent(
        List<Note> component, CollisionPolicy policy, List<Note> resolved, ref int mergedCount, ref int displacedCount)
    {
        // Priority order, independent of sweep/input order: longest survives, ties go to the
        // earliest start, further ties to the highest velocity. See the determinism remarks above.
        component.Sort(static (a, b) =>
        {
            int byLength = b.LengthTicks.CompareTo(a.LengthTicks);
            if (byLength != 0)
            {
                return byLength;
            }

            int byStart = a.StartTicks.CompareTo(b.StartTicks);
            return byStart != 0 ? byStart : b.Velocity.CompareTo(a.Velocity);
        });

        switch (policy)
        {
            case CollisionPolicy.Merge:
                resolved.Add(component[0]);
                mergedCount += component.Count - 1;
                break;

            case CollisionPolicy.DisplaceOctave:
            {
                Note primary = component[0];
                var kept = new List<Note>(component.Count) { primary };
                resolved.Add(primary);

                for (int k = 1; k < component.Count; k++)
                {
                    Note? displaced = TryDisplaceOctave(component[k], kept);
                    if (displaced is { } placed)
                    {
                        kept.Add(placed);
                        resolved.Add(placed);
                        displacedCount++;
                    }
                    else
                    {
                        // Neither octave fits without overflowing the MIDI range or creating a new
                        // collision: fall back to merging this one note away, as documented.
                        mergedCount++;
                    }
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown collision policy.");
        }
    }

    /// <summary>
    /// Tries to move <paramref name="candidate"/> an octave up, then an octave down, landing it
    /// somewhere that is both in MIDI range and does not collide with any note already kept for this
    /// component. Returns null if neither octave works.
    /// </summary>
    private static Note? TryDisplaceOctave(Note candidate, IReadOnlyList<Note> kept)
    {
        Pitch up = candidate.Pitch.ShiftOctaves(1);
        if (up.IsInMidiRange)
        {
            Note attempt = candidate.WithPitch(up);
            if (!CollidesWithAny(attempt, kept))
            {
                return attempt;
            }
        }

        Pitch down = candidate.Pitch.ShiftOctaves(-1);
        if (down.IsInMidiRange)
        {
            Note attempt = candidate.WithPitch(down);
            if (!CollidesWithAny(attempt, kept))
            {
                return attempt;
            }
        }

        return null;
    }

    private static bool CollidesWithAny(Note note, IReadOnlyList<Note> others)
    {
        foreach (Note other in others)
        {
            if (other.Pitch.Cents == note.Pitch.Cents && other.OverlapsInTime(note))
            {
                return true;
            }
        }

        return false;
    }
}
