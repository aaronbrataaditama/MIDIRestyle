using MidiRestyle.Core.Model;

namespace MidiRestyle.Core.Notation;

/// <summary>A note after quantisation, carrying the tuplet grid its beat was read on.</summary>
public readonly record struct QuantisedNote(
    Note Source,
    long StartTicks,
    long LengthTicks,
    Tuplet Tuplet)
{
    public long EndTicks => StartTicks + LengthTicks;
}

/// <summary>
/// A whole track's quantisation: the notes, plus the grid each beat was read on.
/// </summary>
/// <remarks>
/// The beat map is returned rather than left implicit because <i>rests</i> need it too. A rest
/// filling a gap on a triplet beat has to be spelled as a triplet rest, and the rest never belonged
/// to a note that could have carried the ratio - so the builder has to be able to ask "what grid is
/// this beat on" for a tick with no note anywhere near it. A beat absent from the map has no onset
/// at all and is straight by definition.
/// </remarks>
public readonly record struct QuantisedTrack(
    IReadOnlyList<QuantisedNote> Notes,
    IReadOnlyDictionary<long, Tuplet> BeatTuplets);

/// <summary>
/// Where the beats fall across a run of measures, and which measure a tick belongs to.
/// </summary>
/// <remarks>
/// This exists because a beat is a property of the measure it is in, not of the file. A piece that
/// changes from 4/4 to 6/8 changes the notated beat from a quarter to an eighth at the barline, and
/// quantising the second half against the first half's beat groups the tuplet decision on the wrong
/// boundaries for the rest of the piece.
/// <para>
/// The measure lookup is a binary search rather than a scan from zero. The scan is what made
/// notation cost <c>O(notes x measures)</c> - 5,000 notes over 400 bars took 96 ms, all of it in
/// re-walking measures that had already been passed.
/// </para>
/// </remarks>
public sealed class BeatRuler
{
    private readonly IReadOnlyList<MeasureSpan> _measures;
    private readonly long _uniformBeat;

    /// <summary>
    /// A ruler over real measures. <paramref name="uniformBeatTicks"/> is used only when
    /// <paramref name="measures"/> is empty, which is the degenerate "no metre at all" case.
    /// </summary>
    public BeatRuler(IReadOnlyList<MeasureSpan> measures, long uniformBeatTicks)
    {
        _measures = measures;
        _uniformBeat = Math.Max(1, uniformBeatTicks);
    }

    /// <summary>A ruler with one beat length everywhere, starting at tick zero.</summary>
    public BeatRuler(long uniformBeatTicks)
        : this([], uniformBeatTicks)
    {
    }

    public int MeasureCount => _measures.Count;

    /// <summary>
    /// The index of the measure containing <paramref name="ticks"/>, clamped to the ends. Ticks past
    /// the final barline answer with the final measure, which is what every caller wants.
    /// </summary>
    public int MeasureIndexFor(long ticks)
    {
        if (_measures.Count == 0)
        {
            return -1;
        }

        // Lower bound on StartTicks: measures are contiguous and ordered, so the last measure that
        // starts at or before the tick is the one containing it.
        int low = 0;
        int high = _measures.Count - 1;

        while (low < high)
        {
            int mid = low + ((high - low + 1) / 2);

            if (_measures[mid].StartTicks <= ticks)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    /// <summary>The first tick of the beat containing <paramref name="ticks"/>.</summary>
    public long BeatStartFor(long ticks)
    {
        if (_measures.Count == 0)
        {
            return FloorTo(ticks, _uniformBeat);
        }

        var measure = _measures[MeasureIndexFor(ticks)];
        long beat = BeatTicksOf(measure);
        long offset = Math.Max(0, ticks - measure.StartTicks);

        // The final beat of a measure absorbs any remainder, so that a signature whose length is not
        // an exact multiple of its beat cannot produce a stray sliver of a beat at the barline.
        long index = Math.Min(offset / beat, Math.Max(0, BeatCountOf(measure) - 1));
        return measure.StartTicks + (index * beat);
    }

    /// <summary>The length of the beat containing <paramref name="ticks"/>.</summary>
    public long BeatLengthFor(long ticks)
    {
        if (_measures.Count == 0)
        {
            return _uniformBeat;
        }

        var measure = _measures[MeasureIndexFor(ticks)];
        long beat = BeatTicksOf(measure);
        long start = BeatStartFor(ticks);

        // Only the last beat of a measure may be long, and only when the signature's length is not
        // an exact multiple of its own beat - the remainder is absorbed there rather than left as a
        // sliver of a beat against the barline.
        return start + beat >= measure.EndTicks ? measure.EndTicks - start : beat;
    }

    /// <summary>Every beat of one measure, in order, as half-open tick ranges.</summary>
    public IEnumerable<(long StartTicks, long EndTicks)> BeatsIn(MeasureSpan measure)
    {
        long beat = BeatTicksOf(measure);
        int count = BeatCountOf(measure);

        for (int i = 0; i < count; i++)
        {
            long start = measure.StartTicks + (i * beat);
            long end = i == count - 1 ? measure.EndTicks : start + beat;

            if (end > start)
            {
                yield return (start, end);
            }
        }
    }

    private static long BeatTicksOf(MeasureSpan measure) => Math.Max(1, measure.BeatTicks);

    private static int BeatCountOf(MeasureSpan measure) =>
        Math.Max(1, (int)(measure.LengthTicks / BeatTicksOf(measure)));

    private static long FloorTo(long ticks, long step)
    {
        long remainder = ticks % step;
        return remainder >= 0 ? ticks - remainder : ticks - remainder - step;
    }
}

/// <summary>
/// Snaps recorded MIDI timing onto a grid a reader can follow, deciding beat by beat whether that
/// beat is straight or a tuplet.
/// </summary>
/// <remarks>
/// The decision is per beat rather than per file because real music mixes the two freely - a
/// straight tune with a triplet turn in bar 9 is the normal case, not an exotic one. It is also not
/// per note: a triplet is a property of how a beat is divided, and notes inside one beat must agree
/// or they cannot be written down together.
/// </remarks>
public static class RhythmQuantiser
{
    /// <summary>
    /// The grids a beat is tested against. Straight first, so that an exact tie goes to the simpler
    /// reading; <see cref="QuantiseOptions.TupletBias"/> then makes a tuplet earn its place.
    /// </summary>
    private static readonly (int Divisions, Tuplet Tuplet)[] BeatGrids =
    [
        (4, Notation.Tuplet.None),        // sixteenths
        (3, Notation.Tuplet.Triplet),     // triplet eighths
        (6, Notation.Tuplet.Sextuplet),   // sextuplet sixteenths
    ];

    /// <summary>
    /// Quantises <paramref name="notes"/> against a single beat length used everywhere.
    /// </summary>
    public static IReadOnlyList<QuantisedNote> Quantise(
        IReadOnlyList<Note> notes, int ppqn, long beatTicks, QuantiseOptions? options = null) =>
        QuantiseTrack(notes, ppqn, new BeatRuler(beatTicks <= 0 ? ppqn : beatTicks), options).Notes;

    /// <summary>
    /// Quantises <paramref name="notes"/> against the beats <paramref name="ruler"/> describes,
    /// returning the grid chosen for each beat alongside the notes.
    /// </summary>
    public static QuantisedTrack QuantiseTrack(
        IReadOnlyList<Note> notes, int ppqn, BeatRuler ruler, QuantiseOptions? options = null)
    {
        options ??= QuantiseOptions.Default;

        if (notes.Count == 0)
        {
            return new QuantisedTrack([], new Dictionary<long, Tuplet>());
        }

        double straightStep = options.Resolution.UndottedTicks(ppqn);
        Dictionary<long, (long Step, Tuplet Tuplet)> gridForBeat = [];

        foreach (var beat in notes.GroupBy(n => ruler.BeatStartFor(n.StartTicks)))
        {
            gridForBeat[beat.Key] = ChooseGrid(
                [.. beat], beat.Key, ruler.BeatLengthFor(beat.Key), straightStep, options);
        }

        List<QuantisedNote> result = new(notes.Count);
        Dictionary<long, Tuplet> beatTuplets = new(gridForBeat.Count);

        foreach ((long beatStart, (long _, Tuplet tuplet)) in gridForBeat)
        {
            beatTuplets[beatStart] = tuplet;
        }

        foreach (var note in notes)
        {
            long beatStart = ruler.BeatStartFor(note.StartTicks);
            (long step, var tuplet) = gridForBeat[beatStart];

            // Snapping is relative to the beat, not to tick zero: once measures may change metre,
            // a beat no longer necessarily starts at a multiple of its own length.
            long start = beatStart + SnapTo(note.StartTicks - beatStart, step);

            // The end is snapped on the grid of the beat it *lands* in, not the one it started in -
            // a note beginning in a triplet beat and ending in a straight one is ordinary.
            long endBeat = ruler.BeatStartFor(Math.Max(note.StartTicks, note.EndTicks - 1));
            long endStep = gridForBeat.TryGetValue(endBeat, out var endGrid)
                ? endGrid.Step
                : Math.Max(1, (long)Math.Round(straightStep, MidpointRounding.AwayFromZero));

            long end = endBeat + SnapTo(note.EndTicks - endBeat, endStep);
            long length = end - start;

            if (length < step * options.MinimumStepFraction)
            {
                length = step;
            }

            result.Add(new QuantisedNote(note, start, length, tuplet));
        }

        return new QuantisedTrack(result, beatTuplets);
    }

    /// <summary>
    /// Picks the grid that explains a beat's onsets with the least error, biased toward straight,
    /// and only once the beat has enough onsets for "how is this divided" to be a real question.
    /// </summary>
    private static (long Step, Tuplet Tuplet) ChooseGrid(
        IReadOnlyList<Note> beatNotes, long beatStart, long beatTicks, double straightStep,
        QuantiseOptions options)
    {
        long defaultStep = Math.Max(1, (long)Math.Round(straightStep, MidpointRounding.AwayFromZero));

        // Chord members share one onset and divide nothing between them, so the count that matters
        // is of distinct attack points.
        int onsets = beatNotes.Select(n => n.StartTicks).Distinct().Count();

        if (!options.DetectTuplets || onsets < options.MinimumTupletOnsets)
        {
            return (defaultStep, Tuplet.None);
        }

        double bestError = double.MaxValue;
        (long Step, Tuplet Tuplet) best = (defaultStep, Tuplet.None);

        foreach ((int divisions, var tuplet) in BeatGrids)
        {
            double step = tuplet.IsNone ? straightStep : (double)beatTicks / divisions;

            if (step < 1)
            {
                continue;
            }

            double error = 0;

            foreach (var note in beatNotes)
            {
                error += GridError(note.StartTicks - beatStart, step);
            }

            error /= beatNotes.Count;

            // A tuplet reading has to be clearly better, not merely luckier, or ordinary human
            // timing on a straight beat starts coming out as triplets.
            double scored = tuplet.IsNone ? error : error * options.TupletBias;

            if (scored < bestError)
            {
                bestError = scored;
                best = (Math.Max(1, (long)Math.Round(step, MidpointRounding.AwayFromZero)), tuplet);
            }
        }

        return best;
    }

    /// <summary>Distance from a position to the nearest line of a grid of the given step.</summary>
    private static double GridError(double offset, double step)
    {
        double mod = Math.Abs(offset) % step;
        return Math.Min(mod, step - mod);
    }

    private static long SnapTo(long ticks, long step) =>
        step <= 1 ? ticks : (long)Math.Round((double)ticks / step, MidpointRounding.AwayFromZero) * step;
}
