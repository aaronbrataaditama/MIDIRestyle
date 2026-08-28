namespace MidiRestyle.Core.Notation;

/// <summary>
/// Turns a raw tick span into the tied written durations that represent it. This is the smallest
/// piece of the notation machinery and every other piece leans on it: measure splitting produces
/// spans, rest inference produces spans, and both hand them here to be spelled.
/// </summary>
/// <remarks>
/// <b>The contract with the caller has two halves and both are load-bearing.</b> Notation cannot
/// write a span that is not a whole number of sixty-fourths (of whatever tuplet is in force), so
/// for such a span this class rounds <i>up</i> - never down, which would delete the note - by
/// strictly less than one sixty-fourth. The caller's half is that it must advance its own cursor by
/// what was actually written, which <see cref="WrittenTicks"/> reports, and not by the span it
/// asked for. Emitting the rounded value while advancing by the true span is exactly how a voice
/// comes to overrun its own measure.
/// </remarks>
public static class DurationDecomposer
{
    /// <summary>Rounding slack, in ticks. Below this a remainder is considered spent.</summary>
    private const double Epsilon = 1e-6;

    /// <summary>
    /// Every writable duration, longest first. Built once per (ppqn, tuplet) pair because the table
    /// is the same for every note in a file and rebuilding it per note showed up in the profile.
    /// </summary>
    private static readonly Dictionary<(int Ppqn, int Actual, int Normal), NotatedDuration[]> TableCache = [];

    private static readonly Lock CacheLock = new();

    private static NotatedDuration[] CandidatesFor(int ppqn, Tuplet tuplet)
    {
        var key = (ppqn, tuplet.ActualNotes, tuplet.NormalNotes);

        lock (CacheLock)
        {
            if (TableCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var table = NoteValueExtensions.LongestFirst
                .SelectMany(v => Enumerable
                    .Range(0, NotatedDuration.MaxDots + 1)
                    .Select(d => new NotatedDuration(v, d, tuplet)))
                .OrderByDescending(d => d.Ticks(ppqn))
                .ToArray();

            TableCache[key] = table;
            return table;
        }
    }

    /// <summary>
    /// Spells <paramref name="lengthTicks"/> as one or more tied durations, longest first.
    /// </summary>
    /// <remarks>
    /// Greedy longest-first is not merely convenient, it is the conventional spelling: it is what
    /// produces a double-dotted half for seven eighths rather than a half tied to a dotted quarter.
    /// A span with no exact spelling - a triplet handed in without its ratio - comes back as several
    /// parts that sum correctly but read badly, which is the caller's cue to detect the tuplet.
    /// </remarks>
    public static IReadOnlyList<NotatedDuration> Decompose(
        long lengthTicks, int ppqn, Tuplet tuplet = default)
    {
        if (lengthTicks <= 0)
        {
            return [];
        }

        var candidates = CandidatesFor(ppqn, tuplet.ActualNotes == 0 ? Tuplet.None : tuplet);
        List<NotatedDuration> parts = [];
        double remaining = lengthTicks;

        while (remaining > Epsilon)
        {
            NotatedDuration? chosen = null;

            foreach (var candidate in candidates)
            {
                if (candidate.Ticks(ppqn) <= remaining + Epsilon)
                {
                    chosen = candidate;
                    break;
                }
            }

            if (chosen is null)
            {
                // The remainder is shorter than a 64th. Returning empty here would silently delete
                // the note, so the shortest available value is written and the span rounds up.
                parts.Add(candidates[^1] with { Dots = 0 });
                break;
            }

            parts.Add(chosen.Value);
            remaining -= chosen.Value.Ticks(ppqn);
        }

        return parts;
    }

    /// <summary>
    /// The tick total a decomposition actually writes, which is what the caller must advance by.
    /// </summary>
    /// <remarks>
    /// This is never less than the span that produced the parts, and less than one sixty-fourth
    /// more. It is the answer to "how much of the measure did that just consume", and the reason it
    /// is a method here rather than arithmetic at each call site is that there were two call sites
    /// and they both got it wrong in the same way.
    /// </remarks>
    public static long WrittenTicks(IReadOnlyList<NotatedDuration> parts, int ppqn)
    {
        double total = 0;

        for (int i = 0; i < parts.Count; i++)
        {
            total += parts[i].Ticks(ppqn);
        }

        return (long)Math.Round(total, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// True when <paramref name="lengthTicks"/> can be written exactly under
    /// <paramref name="tuplet"/> - that is, when it is a whole number of the shortest value
    /// available, so nothing has to round up.
    /// </summary>
    /// <remarks>
    /// Every writable value is a power-of-two multiple of that shortest value, so any whole number
    /// of them can be spelled exactly by the greedy walk (a dotted value is just two of them, and a
    /// count above the longest value becomes several tied breves). The converse is what makes this
    /// worth asking: anything else is unwritable, and the builder's job is to make sure it never
    /// hands one over.
    /// </remarks>
    public static bool IsExactlyWritable(long lengthTicks, int ppqn, Tuplet tuplet = default)
    {
        if (lengthTicks <= 0)
        {
            return true;
        }

        double atom = new NotatedDuration(NoteValue.SixtyFourth, 0, tuplet).Ticks(ppqn);
        double units = lengthTicks / atom;

        return Math.Abs(units - Math.Round(units)) < 1e-9;
    }

    /// <summary>
    /// Spells a span that sits at a known position within its measure, splitting it at the beat
    /// where leaving it whole would obscure the pulse.
    /// </summary>
    /// <remarks>
    /// The rule is the conventional one: a note that begins <i>off</i> the beat and runs through the
    /// next beat is split there, because a reader locates beat 2 by seeing something start on it.
    /// A note that begins <i>on</i> a beat may span as many beats as a dotted value allows, so a
    /// dotted quarter on beat 1 stays a dotted quarter rather than becoming two tied eighths.
    /// </remarks>
    public static IReadOnlyList<NotatedDuration> DecomposeAt(
        long startInMeasure, long lengthTicks, int ppqn, long beatTicks, Tuplet tuplet = default)
    {
        if (lengthTicks <= 0)
        {
            return [];
        }

        if (beatTicks <= 0 || startInMeasure % beatTicks == 0)
        {
            return Decompose(lengthTicks, ppqn, tuplet);
        }

        long nextBeat = ((startInMeasure / beatTicks) + 1) * beatTicks;
        long toBeat = nextBeat - startInMeasure;

        if (lengthTicks <= toBeat)
        {
            return Decompose(lengthTicks, ppqn, tuplet);
        }

        // Split once at the beat, then let the on-beat remainder spell itself normally.
        List<NotatedDuration> parts = [.. Decompose(toBeat, ppqn, tuplet)];
        parts.AddRange(Decompose(lengthTicks - toBeat, ppqn, tuplet));
        return parts;
    }
}
