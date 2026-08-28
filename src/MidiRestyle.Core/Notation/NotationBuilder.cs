using MidiRestyle.Core.Model;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Notation;

/// <summary>
/// Turns a restyled project into a <see cref="NotationScore"/>: quantises, splits at barlines,
/// packs voices, infers rests and spells every pitch.
/// </summary>
/// <remarks>
/// This is the machinery the plan says MusicXML export and the staff view share, and the reason
/// both waited for the same milestone. Measure splitting, rest inference and voice assignment are
/// each required by the file format and by the renderer alike, and maintaining two of them would
/// guarantee that the exported file and the screen eventually disagreed.
/// </remarks>
public static class NotationBuilder
{
    /// <summary>
    /// The readability threshold. MusicXML permits more, and so now does the builder, but four
    /// voices on one staff is as much as anyone can read and a fifth simultaneous line usually
    /// means the quantiser has mis-split something. Crossing it raises a diagnostic; it no longer
    /// changes what is written.
    /// </summary>
    /// <remarks>
    /// This used to be a hard cap, and the cap lost notes. Past the fourth voice a chord was
    /// appended to the last voice - "kept rather than dropped", said the comment - but that voice
    /// was already occupied for the span, so <see cref="BuildVoice"/> clamped the start to its
    /// cursor, computed a length of zero or less and discarded the chord outright. The diagnostic
    /// stayed, and it said the rhythm was approximate when the note was in fact absent.
    /// </remarks>
    public const int MaxVoicesPerStaff = 4;

    /// <summary>
    /// The hard ceiling on voices per staff. Nothing is discarded below it.
    /// </summary>
    /// <remarks>
    /// A ceiling has to exist - a pathological file can hold hundreds of simultaneous notes, and
    /// one voice each would be neither writable nor drawable - but it is a bound on absurdity, not
    /// a layout decision, so it sits four times past the point at which a human stops being able to
    /// read the staff. Anything discarded here is counted and reported; see
    /// <see cref="BuildLog.NotesBeyondVoiceCeiling"/>.
    /// </remarks>
    public const int VoiceCeilingPerStaff = 16;

    /// <summary>Builds a score from a restyle result.</summary>
    /// <param name="project">The source file, for its metre, division and track names.</param>
    /// <param name="tracks">The restyled tracks. Drums are skipped.</param>
    /// <param name="settings">Supplies the target scale and tonic that pitches are spelled against.</param>
    /// <param name="options">Quantisation behaviour; defaults to a sixteenth grid with tuplets on.</param>
    public static NotationScore Build(
        MidiProject project,
        IReadOnlyList<RestyledTrack> tracks,
        RestyleSettings settings,
        QuantiseOptions? options = null)
    {
        options ??= QuantiseOptions.Default;
        settings = WithDerivedSpelling(settings);

        int ppqn = project.Division is TicksPerQuarterNote t && t.Ticks > 0 ? t.Ticks : 480;
        BuildLog log = new();

        // SMPTE files have no quarter-note pulse at all, so there is nothing to notate against.
        // Better to say so than to invent a pulse and print a plausible-looking wrong score.
        if (project.Division is SmpteDivision)
        {
            log.Add(
                "This file uses SMPTE timecode division, which has no musical pulse. Notation "
                + "assumes a 480-tick quarter note, so the rhythm shown is approximate.");
        }

        long totalTicks = tracks
            .Where(t => t.Notes.Count > 0)
            .Select(t => t.Notes.Max(n => n.EndTicks))
            .DefaultIfEmpty(0)
            .Max();

        var measures = MeasureGrid.Build(project.TimeSignatures, totalTicks, ppqn);
        List<NotationPart> parts = [];
        int partNumber = 1;

        foreach (var track in tracks)
        {
            if (track.IsDrums || track.Notes.Count == 0)
            {
                continue;
            }

            var info = project.Tracks.FirstOrDefault(t =>
                t.TrackIndex == track.TrackIndex && t.Channel == track.Channel);

            parts.Add(BuildPart(
                $"P{partNumber}", info, track, measures, ppqn, settings, options, log));

            partNumber++;
        }

        return new NotationScore
        {
            Divisions = ppqn,
            Title = project.Title ?? (project.FilePath is null
                ? null
                : Path.GetFileNameWithoutExtension(project.FilePath)),
            ScaleName = settings.TargetScale.Name,
            Parts = parts,
            Diagnostics = log.Finish(),
        };
    }

    /// <summary>
    /// Fills in a target scale's staff spelling when it carries none.
    /// </summary>
    /// <remarks>
    /// Notatability is authored data and spelling is derived - a hand-set flag saying whether a
    /// staff rendering would be honest, and a computation producing one when it would. A scale
    /// reaching here without a spelling is simply one whose definition did not bother to write out
    /// what the speller can work out, so deriving it is exactly right. A scale flagged
    /// <c>Notatable = false</c> short-circuits inside <see cref="DiatonicSpeller"/> and still comes
    /// back with none, which is what sends the UI to the degree view.
    /// </remarks>
    private static RestyleSettings WithDerivedSpelling(RestyleSettings settings)
    {
        var scale = settings.TargetScale;

        if (scale.Spelling is not null || !scale.Notatable)
        {
            return settings;
        }

        var derived = DiatonicSpeller.Derive(scale);

        if (!derived.Succeeded)
        {
            return settings;
        }

        return settings with
        {
            TargetScale = new Scale(
                scale.Id, scale.Name, scale.Tradition, scale.Region, scale.DegreeCents,
                scale.Source, scale.Notatable, derived.Spelling, scale.Description),
        };
    }

    private static NotationPart BuildPart(
        string id,
        TrackInfo? info,
        RestyledTrack track,
        IReadOnlyList<MeasureSpan> measures,
        int ppqn,
        RestyleSettings settings,
        QuantiseOptions options,
        BuildLog log)
    {
        var notes = track.Notes;

        // The beat is a property of each measure, not of the file. Handing the quantiser measure 1's
        // beat and the decomposer each measure's own is how a piece that changes from 4/4 to 6/8
        // ends up grouped on the wrong boundaries from the change onward.
        BeatRuler ruler = new(measures, ppqn);
        var quantised = RhythmQuantiser.QuantiseTrack(notes, ppqn, ruler, options);

        var layout = info is not null
            ? StaffLayout.For(info, notes)
            : new StaffLayout.Layout(1, [StaffLayout.ClefFor(notes)]);

        // Cut every note at the barlines it crosses, tagging the pieces so the tie chain survives.
        List<Segment> segments = [];

        foreach (var note in quantised.Notes)
        {
            int staff = layout.IsGrandStaff ? StaffLayout.StaffFor(note.Source.Pitch) : 1;
            SplitAcrossMeasures(note, staff, measures, ruler, segments);
        }

        var byMeasure = segments.ToLookup(s => s.MeasureNumber);
        List<NotationMeasure> built = [];

        // One buffer for the whole part rather than one per measure: the runs are consumed inside
        // the measure that produced them, and a dense file has thousands of measures.
        List<TupletRun> runs = [];
        SpellBuffers buffers = new();

        foreach (var measure in measures)
        {
            FillTupletRuns(measure, ruler, quantised.BeatTuplets, runs);

            built.Add(BuildMeasure(
                measure,
                [.. byMeasure[measure.Number]],
                runs,
                buffers,
                layout,
                ppqn,
                settings,
                log));
        }

        string name = info?.DisplayName ?? $"Track {track.TrackIndex + 1}";

        return new NotationPart
        {
            Id = id,
            Name = name,
            TrackIndex = track.TrackIndex,
            Channel = track.Channel,
            StaffCount = layout.StaffCount,
            Clefs = layout.Clefs,
            Measures = built,
            ProgramNumber = info?.ProgramNumber,
        };
    }

    /// <summary>A piece of one note lying within one measure, before it is spelled.</summary>
    /// <remarks>
    /// Deliberately carries no tuplet. The grid a span is spelled on is a property of the beat the
    /// span <i>occupies</i>, not of the note it came from - which is what lets a rest be spelled on
    /// it too, and what keeps a note whose onset was snapped forward onto the next beat from
    /// dragging its old beat's ratio along with it.
    /// </remarks>
    private readonly record struct Segment(
        Pitch Pitch,
        long StartTicks,
        long LengthTicks,
        int Staff,
        int MeasureNumber,
        bool TiedFromPrevious,
        bool TiesToNext);

    /// <summary>
    /// Cuts a note at every barline it crosses. A note may legally last longer than a measure, but
    /// it cannot be <i>written</i> that way - it becomes one notehead per measure, joined by ties.
    /// </summary>
    private static void SplitAcrossMeasures(
        QuantisedNote note,
        int staff,
        IReadOnlyList<MeasureSpan> measures,
        BeatRuler ruler,
        List<Segment> into)
    {
        long start = note.StartTicks;
        long end = note.EndTicks;

        if (end <= start || measures.Count == 0)
        {
            return;
        }

        // Binary search rather than a scan from measure zero. Scanning is what made this
        // O(notes x measures): every note re-walked every bar before it, so a 400-bar file cost
        // five times a 50-bar one for the same note count.
        for (int i = ruler.MeasureIndexFor(start); i < measures.Count; i++)
        {
            var measure = measures[i];

            if (measure.EndTicks <= start)
            {
                continue;
            }

            if (measure.StartTicks >= end)
            {
                break;
            }

            long from = Math.Max(start, measure.StartTicks);
            long to = Math.Min(end, measure.EndTicks);

            if (to <= from)
            {
                continue;
            }

            into.Add(new Segment(
                note.Source.Pitch,
                from,
                to - from,
                staff,
                measure.Number,
                TiedFromPrevious: from > start,
                TiesToNext: to < end));
        }
    }

    /// <summary>
    /// A maximal run of consecutive beats that share one tuplet grid, and therefore one set of
    /// writable durations.
    /// </summary>
    /// <remarks>
    /// This is the unit of exactness. Positions inside a run are always whole multiples of that
    /// run's shortest writable value, so any span whose two ends lie in the same run can be spelled
    /// with nothing left over. Across a run boundary that stops being true - a sextuplet position
    /// (80 ticks at 480 PPQN) is not a whole number of straight sixty-fourths (30) - which is why
    /// every span is cut at run boundaries before it is spelled.
    /// </remarks>
    private readonly record struct TupletRun(long StartTicks, long EndTicks, Tuplet Tuplet);

    /// <summary>Collapses a measure's beats into maximal runs sharing one grid.</summary>
    /// <remarks>
    /// Straight beats are merged so that a whole note stays a whole note rather than becoming four
    /// tied quarters; only a change of grid forces a cut, and a cut there is correct notation
    /// anyway, since a tuplet group cannot extend past the beat it divides.
    /// </remarks>
    private static void FillTupletRuns(
        MeasureSpan measure,
        BeatRuler ruler,
        IReadOnlyDictionary<long, Tuplet> beatTuplets,
        List<TupletRun> runs)
    {
        runs.Clear();

        // Nothing in the whole part was read as a tuplet, so no measure of it can need cutting.
        // This is the ordinary case for ordinary music and skips the beat walk entirely.
        if (beatTuplets.Count == 0)
        {
            runs.Add(new TupletRun(measure.StartTicks, measure.EndTicks, Tuplet.None));
            return;
        }

        foreach ((long start, long end) in ruler.BeatsIn(measure))
        {
            // A beat with no onset at all is absent from the map and is straight by definition.
            var tuplet = beatTuplets.TryGetValue(start, out var found) && !found.IsNone
                ? found
                : Tuplet.None;

            if (runs.Count > 0 && runs[^1].Tuplet == tuplet)
            {
                runs[^1] = runs[^1] with { EndTicks = end };
            }
            else
            {
                runs.Add(new TupletRun(start, end, tuplet));
            }
        }

        if (runs.Count == 0)
        {
            runs.Add(new TupletRun(measure.StartTicks, measure.EndTicks, Tuplet.None));
        }
    }

    private static NotationMeasure BuildMeasure(
        MeasureSpan measure,
        List<Segment> segments,
        List<TupletRun> runs,
        SpellBuffers buffers,
        StaffLayout.Layout layout,
        int ppqn,
        RestyleSettings settings,
        BuildLog log)
    {
        List<NotationEntry> entries = [];

        for (int staff = 1; staff <= layout.StaffCount; staff++)
        {
            var onStaff = segments.Where(s => s.Staff == staff).ToList();
            var voices = PackVoices(onStaff, log);

            for (int voice = 0; voice < voices.Count; voice++)
            {
                entries.AddRange(BuildVoice(
                    voices[voice], measure, runs, staff, voice + 1, ppqn, settings, buffers, log));
            }

            // A staff with nothing in it this measure still needs a full-measure rest, or the
            // measure is short and every downstream reader - MusicXML validators included - objects.
            if (voices.Count == 0)
            {
                AppendRests(
                    entries, measure.StartTicks, measure.LengthTicks, measure, runs, staff, 1, ppqn,
                    buffers);
            }
        }

        ApplyBeams(entries, measure);

        return new NotationMeasure
        {
            Number = measure.Number,
            StartTicks = measure.StartTicks,
            LengthTicks = measure.LengthTicks,
            BeatsPerMeasure = measure.Beats,
            BeatUnit = measure.BeatUnit,
            TimeSignatureChanged = measure.SignatureChanged,
            Entries = entries,
        };
    }

    /// <summary>
    /// Replaces every entry that carries a beam with a copy holding its beam states.
    /// </summary>
    /// <remarks>
    /// Done here, once, rather than in each consumer: the staff renderer and the MusicXML exporter
    /// must not disagree about where a beam starts, for the same reason they must not disagree
    /// about where a barline does. Only beamed entries are rewritten, so an ordinary measure of
    /// quarter notes allocates nothing.
    /// </remarks>
    private static void ApplyBeams(List<NotationEntry> entries, MeasureSpan measure)
    {
        var beams = BeamGrouper.Assign(entries, measure);

        for (int i = 0; i < entries.Count; i++)
        {
            if (beams[i].Count > 0)
            {
                entries[i] = entries[i] with { Beams = beams[i] };
            }
        }
    }

    /// <summary>
    /// Groups simultaneous notes into chords, then packs the chords into as few voices as their
    /// overlaps allow.
    /// </summary>
    /// <remarks>
    /// Two notes belong to the same chord only if they start together <i>and</i> last the same time.
    /// Notes that merely overlap are independent lines and must go to different voices, because a
    /// voice is a strictly sequential timeline and neither MusicXML nor a stem can express two
    /// different durations at once within one.
    /// </remarks>
    private static List<List<List<Segment>>> PackVoices(
        List<Segment> segments, BuildLog log)
    {
        if (segments.Count == 0)
        {
            return [];
        }

        var chords = segments
            .GroupBy(s => (s.StartTicks, s.LengthTicks))
            .OrderBy(g => g.Key.StartTicks)
            .ThenByDescending(g => g.Key.LengthTicks)
            .Select(g => g.OrderByDescending(s => s.Pitch.Cents).ToList())
            .ToList();

        List<List<List<Segment>>> voices = [];
        List<long> voiceEnds = [];

        foreach (var chord in chords)
        {
            long start = chord[0].StartTicks;
            long end = start + chord[0].LengthTicks;
            int target = -1;

            for (int v = 0; v < voices.Count; v++)
            {
                if (voiceEnds[v] <= start)
                {
                    target = v;
                    break;
                }
            }

            if (target < 0)
            {
                if (voices.Count >= VoiceCeilingPerStaff)
                {
                    // The only path on which a note is discarded, and it is counted rather than
                    // quietly skipped. Folding it into an occupied voice instead - which is what
                    // this did - does not keep it: the voice's cursor is already past the note's
                    // whole span, so it is written as nothing at all.
                    log.NotesBeyondVoiceCeiling += chord.Count;
                    continue;
                }

                if (voices.Count >= MaxVoicesPerStaff)
                {
                    // Past four simultaneous lines nobody can read the staff, but the note is real
                    // and gets its own voice regardless. Readability is a warning, not a reason to
                    // lose music.
                    log.Add(
                        "Some measures need more than four simultaneous voices on one staff. Every "
                        + "note is written, but the staff will be hard to read.");
                }

                voices.Add([]);
                voiceEnds.Add(0);
                target = voices.Count - 1;
            }

            voices[target].Add(chord);
            voiceEnds[target] = Math.Max(voiceEnds[target], end);
        }

        return voices;
    }

    /// <summary>
    /// Renders one voice as a complete, gapless timeline: rests fill every hole, and every span is
    /// decomposed into tied written durations.
    /// </summary>
    /// <remarks>
    /// The cursor advances by what was <i>written</i>, never by the span that was asked for. Those
    /// two were allowed to differ, and the difference is the whole of the overrun bug: a span that
    /// rounds up by a sixty-fourth adds that sixty-fourth to the measure's written total while the
    /// cursor sails past as though nothing had happened. Written-total is now the only clock, so a
    /// round-up is paid for out of the next rest and the barline still falls where it should.
    /// </remarks>
    private static IEnumerable<NotationEntry> BuildVoice(
        List<List<Segment>> chords,
        MeasureSpan measure,
        List<TupletRun> runs,
        int staff,
        int voice,
        int ppqn,
        RestyleSettings settings,
        SpellBuffers buffers,
        BuildLog log)
    {
        List<NotationEntry> entries = [];
        long cursor = measure.StartTicks;

        foreach (var chord in chords)
        {
            if (chord[0].StartTicks > cursor)
            {
                cursor = AppendRests(
                    entries, cursor, chord[0].StartTicks - cursor, measure, runs, staff, voice, ppqn,
                    buffers);
            }

            // A chord starting before the cursor overlaps something already written in this voice.
            // The packer should have prevented it; clamping is a cheap guard that keeps the
            // measure's durations summing correctly even if it did not.
            long start = cursor;
            long length = Math.Min(chord[0].StartTicks + chord[0].LengthTicks, measure.EndTicks) - start;

            if (length <= 0)
            {
                // Unreachable from the packer, which never puts two overlapping chords in one
                // voice, and unreachable from the decomposer's round-up, which overshoots by less
                // than a sixty-fourth while every quantised span is at least a grid step. Counted
                // rather than skipped all the same: this is the exact shape of the bug the voice
                // ceiling used to cause, and a silent guard is how it stayed hidden.
                log.NotesDisplacedByRounding += chord.Count;
                continue;
            }

            var written = Spell(start, length, measure, runs, ppqn, buffers);
            long partStart = start;

            for (int p = 0; p < written.Count; p++)
            {
                (var duration, long partTicks) = written[p];
                bool firstPart = p == 0;
                bool lastPart = p == written.Count - 1;

                for (int n = 0; n < chord.Count; n++)
                {
                    var segment = chord[n];

                    entries.Add(new NotationEntry
                    {
                        Note = SpellOrNull(segment.Pitch, settings),
                        SoundingPitch = segment.Pitch,
                        Duration = duration,
                        StartTicks = partStart,
                        DurationTicks = partTicks,
                        Staff = staff,
                        Voice = voice,
                        IsChordMember = n > 0,
                        Tie = TieFor(
                            segment.TiedFromPrevious || !firstPart,
                            segment.TiesToNext || !lastPart),
                    });
                }

                partStart += partTicks;
            }

            cursor = partStart;
        }

        if (cursor < measure.EndTicks)
        {
            AppendRests(
                entries, cursor, measure.EndTicks - cursor, measure, runs, staff, voice, ppqn, buffers);
        }

        return entries;
    }

    /// <summary>
    /// Working buffers for spelling, reused across every span in one measure.
    /// </summary>
    /// <remarks>
    /// The builder re-runs on every arrow-key press through the scale list, so a list allocated per
    /// note and per rest is a per-keystroke garbage source proportional to the size of the file.
    /// Only one span is ever being spelled at a time, so one pair of buffers per measure is enough.
    /// </remarks>
    private sealed class SpellBuffers
    {
        public List<(NotatedDuration Duration, long Ticks)> Written { get; } = [];

        public List<(long Start, long Length, Tuplet Tuplet)> Pieces { get; } = [];
    }

    /// <summary>
    /// Cuts a span at the tuplet-run boundaries it crosses, so that every piece can be spelled on a
    /// grid both of its ends actually sit on.
    /// </summary>
    private static void CutIntoRuns(
        long start, long length, List<TupletRun> runs, List<(long Start, long Length, Tuplet Tuplet)> into)
    {
        long end = start + length;
        long covered = start;

        foreach (var run in runs)
        {
            if (run.EndTicks <= start)
            {
                continue;
            }

            if (run.StartTicks >= end)
            {
                break;
            }

            long from = Math.Max(start, run.StartTicks);
            long to = Math.Min(end, run.EndTicks);

            if (to > from)
            {
                into.Add((from, to - from, run.Tuplet));
                covered = to;
            }
        }

        // A span reaching past the last run cannot happen for a measure's own content, but losing
        // time here would be silent and would show up only as a short measure, so it is closed out
        // rather than trusted away.
        if (covered < end)
        {
            var tuplet = runs.Count > 0 ? runs[^1].Tuplet : Tuplet.None;
            into.Add((covered, end - covered, tuplet));
        }
    }

    /// <summary>
    /// Spells a span as written durations, each paired with the exact number of ticks it occupies.
    /// </summary>
    /// <remarks>
    /// The ticks are taken as differences between rounded <i>absolute</i> positions rather than by
    /// rounding each duration on its own. The two agree wherever a written value is a whole number
    /// of ticks, which is every ordinary PPQN; where it is not - a sixty-fourth at 120 PPQN is seven
    /// and a half ticks - differencing still tiles the span exactly, while independent rounding
    /// would leave the measure a tick long or short.
    /// </remarks>
    private static List<(NotatedDuration Duration, long Ticks)> Spell(
        long start, long length, MeasureSpan measure, List<TupletRun> runs, int ppqn,
        SpellBuffers buffers)
    {
        var written = buffers.Written;
        var pieces = buffers.Pieces;
        written.Clear();
        pieces.Clear();

        // An all-straight measure is one run and needs no cutting at all, which is the common case
        // and worth not allocating a piece list for.
        if (runs.Count == 1)
        {
            pieces.Add((start, length, runs[0].Tuplet));
        }
        else
        {
            CutIntoRuns(start, length, runs, pieces);
        }

        long cursor = start;

        foreach ((long pieceStart, long pieceLength, var tuplet) in pieces)
        {
            // A piece the cursor has already passed - only reachable when an earlier piece had to
            // round up - is skipped rather than written backwards.
            long from = Math.Max(cursor, pieceStart);
            long to = pieceStart + pieceLength;

            if (to <= from)
            {
                continue;
            }

            var parts = DurationDecomposer.DecomposeAt(
                from - measure.StartTicks, to - from, ppqn, measure.BeatTicks, tuplet);

            double accumulated = 0;

            foreach (var part in parts)
            {
                accumulated += part.Ticks(ppqn);
                long partEnd = from + (long)Math.Round(accumulated, MidpointRounding.AwayFromZero);

                if (partEnd <= cursor)
                {
                    continue;
                }

                written.Add((part, partEnd - cursor));
                cursor = partEnd;
            }
        }

        if (written.Count == 0 && length > 0)
        {
            // Nothing survived the rounding, which would delete the note outright. One shortest
            // value, standing for the whole span, is wrong by less than a sixty-fourth and visible.
            written.Add((new NotatedDuration(NoteValue.SixtyFourth), length));
        }

        return written;
    }

    private static TieState TieFor(bool from, bool to) =>
        (from, to) switch
        {
            (true, true) => TieState.Continue,
            (true, false) => TieState.Stop,
            (false, true) => TieState.Start,
            _ => TieState.None,
        };

    /// <summary>Fills a gap with written rests, and answers with the tick the fill actually reached.</summary>
    /// <remarks>
    /// Rests go through the same run cutting as notes, which is what gives a rest inside a triplet
    /// beat a triplet rest with its own ratio. Spelling every rest straight left the ones sitting on
    /// a tuplet beat inexpressible, so they took the round-up path and quietly lengthened the
    /// measure - and they carried no ratio, so the staff view's tuplet bracket broke across them.
    /// </remarks>
    private static long AppendRests(
        List<NotationEntry> into,
        long start,
        long length,
        MeasureSpan measure,
        List<TupletRun> runs,
        int staff,
        int voice,
        int ppqn,
        SpellBuffers buffers)
    {
        if (length <= 0)
        {
            return start;
        }

        long cursor = start;

        foreach ((var duration, long ticks) in Spell(start, length, measure, runs, ppqn, buffers))
        {
            into.Add(new NotationEntry
            {
                Note = null,
                Duration = duration,
                StartTicks = cursor,
                DurationTicks = ticks,
                Staff = staff,
                Voice = voice,
            });

            cursor += ticks;
        }

        return cursor;
    }

    /// <summary>
    /// Everything the build had to decide quietly, and the counts behind the sentences.
    /// </summary>
    /// <remarks>
    /// A plain list of strings was enough while every diagnostic was a standing fact ("this file is
    /// SMPTE"). It stopped being enough the moment one of them had to say <i>how many</i> notes
    /// were affected, because a count is only known when the whole score has been walked. The
    /// counters accumulate during the build and <see cref="Finish"/> turns them into sentences at
    /// the end.
    /// </remarks>
    private sealed class BuildLog
    {
        private readonly List<string> _messages = [];

        /// <summary>Notes discarded because a staff exceeded <see cref="VoiceCeilingPerStaff"/>.</summary>
        public int NotesBeyondVoiceCeiling { get; set; }

        /// <summary>
        /// Notes a voice's own cursor had already passed by the time they came up. Held at zero by
        /// the packer and the written-ticks cursor; counted because "held at zero" is a claim, and
        /// an uncounted claim is how the voice-ceiling bug survived a review.
        /// </summary>
        public int NotesDisplacedByRounding { get; set; }

        /// <summary>Adds a standing message, once however many times the condition is met.</summary>
        public void Add(string message)
        {
            if (!_messages.Contains(message))
            {
                _messages.Add(message);
            }
        }

        /// <summary>Closes the log out, appending the messages that needed a total to be written.</summary>
        public IReadOnlyList<string> Finish()
        {
            if (NotesBeyondVoiceCeiling > 0)
            {
                _messages.Add(
                    $"{NotesBeyondVoiceCeiling} note(s) could not be written: a staff needed more "
                    + $"than {VoiceCeilingPerStaff} simultaneous voices in one measure.");
            }

            if (NotesDisplacedByRounding > 0)
            {
                _messages.Add(
                    $"{NotesDisplacedByRounding} note(s) could not be written: the voice they "
                    + "belong to had already filled the time they occupy.");
            }

            return _messages;
        }
    }

    /// <summary>
    /// Spells a pitch against the target scale. Returns <c>null</c> only for a pitch outside MIDI
    /// range, which the range policy should already have dealt with.
    /// </summary>
    private static SpelledNote? SpellOrNull(Pitch pitch, RestyleSettings settings)
    {
        if (!pitch.IsInMidiRange)
        {
            return null;
        }

        return NoteSpeller.Spell(
            pitch, settings.TargetScale, settings.TargetTonic, settings.TonicSpelling);
    }
}
