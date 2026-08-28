using MidiRestyle.Core.Model;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;
using DomainNote = MidiRestyle.Core.Model.Note;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="NotationBuilder"/> is the machinery MusicXML export and the staff view share. The
/// properties pinned here are the ones whose failure is invisible on screen but corrupts the
/// exported file: that every voice fills its measure exactly, that a note crossing a barline
/// becomes a tie rather than two separate notes, and that drums never reach the score at all.
/// </summary>
public class NotationBuilderTests
{
    private const int Ppqn = 480;

    private static readonly Scale CMajor = new(
        "t.major", "Major", "Western", "Europe",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    private static readonly Scale Slendro = new(
        "t.slendro", "Slendro", "Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

    private static TrackInfo Track(
        int trackIndex, int channel, IEnumerable<DomainNote> notes, int? program = 40) => new()
        {
            TrackIndex = trackIndex,
            Channel = channel,
            Name = $"Part {trackIndex + 1}",
            ProgramNumber = program,
            Notes = [.. notes],
        };

    private static DomainNote N(int midi, long start, long length) =>
        new(Pitch.FromMidi(midi), start, length, 90);

    private static readonly int[] MajorSteps = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>
    /// The <paramref name="index"/>th degree of C major from C3 upward.
    /// </summary>
    /// <remarks>
    /// Overlap fixtures have to use scale tones. Chromatic pitches restyled from a heptatonic
    /// source collapse onto shared degrees, <c>CollisionResolver</c> merges the overlapping
    /// same-pitch notes that result, and the count going in stops matching the count arriving -
    /// which looks exactly like the note loss these tests are about and is not.
    /// </remarks>
    private static int DiatonicMidi(int index) =>
        48 + (index / MajorSteps.Length * 12) + MajorSteps[index % MajorSteps.Length];

    private static NotationScore Build(
        IEnumerable<TrackInfo> tracks,
        Scale? target = null,
        IReadOnlyList<TimeSignatureChange>? signatures = null,
        int ppqn = Ppqn)
    {
        MidiProject project = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote((short)ppqn),
            Tracks = [.. tracks],
            TempoMap = [new TempoChange(0, 500_000)],
            TimeSignatures = signatures ?? [new TimeSignatureChange(0, 4, 4)],
        };

        RestyleSettings settings = new()
        {
            TargetScale = target ?? CMajor,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        };

        RestyleResult result = RestyleEngine.Restyle(project, settings);
        return NotationBuilder.Build(project, result.Tracks, settings);
    }

    /// <summary>
    /// Builds a score and reports how many notes actually reached the builder.
    /// </summary>
    /// <remarks>
    /// Not the same as the number that went in, and the difference is not the notation layer's
    /// doing: <c>RestyleEngine</c> may legitimately merge two overlapping notes that map to one
    /// pitch. Counting against the restyled track rather than the source is what makes "no note
    /// vanishes" a statement about notation instead of about the whole pipeline.
    /// </remarks>
    private static (NotationScore Score, int NotesIn) BuildCounting(
        IEnumerable<TrackInfo> tracks,
        Scale? target = null,
        IReadOnlyList<TimeSignatureChange>? signatures = null,
        int ppqn = Ppqn)
    {
        MidiProject project = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote((short)ppqn),
            Tracks = [.. tracks],
            TempoMap = [new TempoChange(0, 500_000)],
            TimeSignatures = signatures ?? [new TimeSignatureChange(0, 4, 4)],
        };

        RestyleSettings settings = new()
        {
            TargetScale = target ?? CMajor,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        };

        RestyleResult result = RestyleEngine.Restyle(project, settings);

        return (
            NotationBuilder.Build(project, result.Tracks, settings),
            result.Tracks.Where(t => !t.IsDrums).Sum(t => t.Notes.Count));
    }

    /// <summary>Every note that survives to a notehead, counted once however many ties it needs.</summary>
    private static int AttackCount(NotationScore score) => score.Parts
        .SelectMany(p => p.Measures)
        .SelectMany(m => m.Entries)
        .Count(e => !e.IsRest && e.Tie is TieState.None or TieState.Start);

    /// <summary>
    /// The invariant the whole format rests on: within one measure, each voice of each staff must
    /// account for exactly the measure's length. Short a voice and MusicXML readers reject the file;
    /// long, and every later measure is displaced.
    /// </summary>
    private static void AssertEveryVoiceFillsItsMeasure(NotationScore score)
    {
        foreach (var part in score.Parts)
        {
            foreach (var measure in part.Measures)
            {
                var lines = measure.Entries
                    .Where(e => !e.IsChordMember)
                    .GroupBy(e => (e.Staff, e.Voice));

                foreach (var line in lines)
                {
                    line.Sum(e => e.DurationTicks).Should().Be(
                        measure.LengthTicks,
                        $"staff {line.Key.Staff} voice {line.Key.Voice} of measure "
                        + $"{measure.Number} in {part.Name} must fill the measure exactly");
                }
            }
        }
    }

    [Fact]
    public void ASimpleScaleFillsEveryMeasureExactly()
    {
        var score = Build([Track(0, 0, Enumerable.Range(0, 8).Select(i => N(60 + i, i * 480, 480)))]);

        score.Parts.Should().ContainSingle();
        AssertEveryVoiceFillsItsMeasure(score);
    }

    [Fact]
    public void SilenceBecomesRestsRatherThanAShortMeasure()
    {
        // One note on beat 1 of a 4/4 bar, then nothing. The remaining three beats must come back
        // as written rests, not as an absence.
        var score = Build([Track(0, 0, [N(60, 0, 480)])]);
        var measure = score.Parts[0].Measures[0];

        measure.Entries.Should().Contain(e => e.IsRest);
        AssertEveryVoiceFillsItsMeasure(score);
    }

    [Fact]
    public void ANoteCrossingABarlineBecomesTwoTiedNotes()
    {
        // A note starting on beat 4 and lasting two beats runs into the next measure. It cannot be
        // written as one notehead, so it must become a tie - and the two halves must still add up.
        var score = Build([Track(0, 0, [N(60, 1440, 960)])]);

        var first = score.Parts[0].Measures[0].Entries.Where(e => !e.IsRest).ToList();
        var second = score.Parts[0].Measures[1].Entries.Where(e => !e.IsRest).ToList();

        first.Should().NotBeEmpty();
        second.Should().NotBeEmpty();
        first[^1].Tie.Should().BeOneOf(TieState.Start, TieState.Continue);
        second[0].Tie.Should().BeOneOf(TieState.Stop, TieState.Continue);
        AssertEveryVoiceFillsItsMeasure(score);
    }

    [Fact]
    public void SimultaneousNotesOfEqualLengthBecomeOneChord()
    {
        var score = Build([Track(0, 0, [N(60, 0, 480), N(64, 0, 480), N(67, 0, 480)])]);

        var chord = score.Parts[0].Measures[0].Entries
            .Where(e => !e.IsRest)
            .Take(3)
            .ToList();

        chord.Should().HaveCount(3);
        chord[0].IsChordMember.Should().BeFalse("the first note of a chord carries the duration");
        chord.Skip(1).Should().AllSatisfy(e => e.IsChordMember.Should().BeTrue());
        chord.Should().AllSatisfy(e => e.Voice.Should().Be(1), "a chord is one voice, not three");
    }

    [Fact]
    public void OverlappingNotesOfDifferentLengthsGoToSeparateVoices()
    {
        // A held whole note under a moving quarter line. These cannot share a voice: a voice is a
        // single sequential timeline and cannot hold two durations at once.
        var score = Build([Track(0, 0, [
            N(48, 0, 1920),
            N(72, 0, 480), N(74, 480, 480), N(76, 960, 480), N(77, 1440, 480),
        ])]);

        var voices = score.Parts[0].Measures[0].Entries
            .Where(e => !e.IsRest)
            .Select(e => e.Voice)
            .Distinct();

        voices.Should().HaveCountGreaterThan(1, "two independent lines need two voices");
        AssertEveryVoiceFillsItsMeasure(score);
    }

    [Fact]
    public void APianoTrackSpanningMiddleCGetsAGrandStaff()
    {
        var score = Build([Track(0, 0, [N(40, 0, 960), N(76, 0, 960)], program: 0)]);
        var part = score.Parts[0];

        part.StaffCount.Should().Be(2);
        part.Clefs.Should().Equal([Clef.Treble, Clef.Bass]);
        part.Measures[0].Entries.Select(e => e.Staff).Distinct().Should().HaveCount(2);
        AssertEveryVoiceFillsItsMeasure(score);
    }

    [Fact]
    public void APianoTrackEntirelyAboveMiddleCStaysOnOneStaff()
    {
        // A right-hand-only part does not need an empty bass staff running the length of the piece.
        var score = Build([Track(0, 0, [N(72, 0, 480), N(76, 480, 480)], program: 0)]);

        score.Parts[0].StaffCount.Should().Be(1);
        score.Parts[0].Clefs.Should().Equal([Clef.Treble]);
    }

    [Fact]
    public void ABassTrackGetsTheBassClef() =>
        Build([Track(0, 0, [N(36, 0, 480), N(40, 480, 480)], program: 33)])
            .Parts[0].Clefs.Should().Equal([Clef.Bass]);

    [Fact]
    public void DrumsNeverReachTheScore()
    {
        // Channel 10 note numbers select a drum, not a pitch, so notating them would be nonsense.
        var score = Build([
            Track(0, 0, [N(60, 0, 480)]),
            Track(1, TrackInfo.DrumChannel, [N(38, 0, 480), N(42, 240, 240)]),
        ]);

        score.Parts.Should().ContainSingle();
        score.Parts[0].Channel.Should().NotBe(TrackInfo.DrumChannel);
    }

    [Fact]
    public void TheTimeSignatureIsAnnouncedOnceAndThenOnlyWhenItChanges()
    {
        var score = Build(
            [Track(0, 0, Enumerable.Range(0, 12).Select(i => N(60, i * 480, 480)))],
            signatures: [new TimeSignatureChange(0, 4, 4), new TimeSignatureChange(1920, 3, 4)]);

        var flagged = score.Parts[0].Measures.Where(m => m.TimeSignatureChanged).ToList();

        flagged.Should().HaveCountGreaterThanOrEqualTo(2);
        flagged[0].Number.Should().Be(1, "the opening signature is always printed");
        flagged[1].BeatsPerMeasure.Should().Be(3);
    }

    [Fact]
    public void ThreeFourMeasuresAreThreeBeatsLong()
    {
        var score = Build(
            [Track(0, 0, Enumerable.Range(0, 6).Select(i => N(60, i * 480, 480)))],
            signatures: [new TimeSignatureChange(0, 3, 4)]);

        score.Parts[0].Measures[0].LengthTicks.Should().Be(1440);
        AssertEveryVoiceFillsItsMeasure(score);
    }

    [Fact]
    public void ANonNotatableScaleStillBuildsAScore()
    {
        // Slendro has no Western spelling at all - Notatable is false and Spelling is null. The
        // staff view will refuse it and offer the degree view, but the builder must not throw:
        // the degree view is built from this very score.
        var score = Build(
            [Track(0, 0, Enumerable.Range(0, 8).Select(i => N(60 + i, i * 480, 480)))],
            target: Slendro);

        score.Parts.Should().ContainSingle();
        score.Parts[0].Measures.Should().NotBeEmpty();
        AssertEveryVoiceFillsItsMeasure(score);
    }

    [Fact]
    public void NotesBelowTheTonicAreNotatedRatherThanThrowing()
    {
        // Any bass line has them, and the degree mapper's floor-division invariant exists precisely
        // because the naive formula indexes out of bounds here.
        var score = Build([Track(0, 0, [N(36, 0, 480), N(40, 480, 480), N(43, 960, 960)])]);

        score.Parts[0].Measures[0].Entries.Where(e => !e.IsRest).Should().NotBeEmpty();
        AssertEveryVoiceFillsItsMeasure(score);
    }

    [Fact]
    public void AnEmptyProjectProducesAnEmptyScoreRatherThanThrowing()
    {
        var score = Build([Track(0, 0, [])]);

        score.Parts.Should().BeEmpty();
        score.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void TheScoreCarriesItsDivisionsAndScaleName()
    {
        var score = Build([Track(0, 0, [N(60, 0, 480)])]);

        score.Divisions.Should().Be(Ppqn, "durations are written in the file's own ticks");
        score.ScaleName.Should().Be("Major");
    }

    [Fact]
    public void EveryEntryHasAWrittenDurationMatchingItsTicks()
    {
        // The renderer draws Duration and the exporter writes DurationTicks. If the two disagree,
        // the screen and the file disagree - the exact failure the shared builder exists to prevent.
        var score = Build([Track(0, 0, [
            N(60, 0, 720), N(62, 720, 240), N(64, 960, 480), N(65, 1440, 480),
        ])]);

        foreach (var entry in score.Parts.SelectMany(p => p.Measures).SelectMany(m => m.Entries))
        {
            long written = (long)Math.Round(entry.Duration.Ticks(Ppqn));
            written.Should().Be(entry.DurationTicks,
                "the written value and the tick length are two views of one duration");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Jittered input. Everything above places its notes on exact 480-tick boundaries, which is
    // the one input class that cannot fail AssertEveryVoiceFillsItsMeasure: a machine-perfect
    // span is always writable, so the decomposer never rounds up and the overrun never appears.
    // Real playing is not machine-perfect, and the measured failure rate on jittered input was
    // 266 files out of 300.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A line played with human-sized timing error, deterministic for a given seed so that a
    /// failure can be reproduced exactly from the seed the assertion prints.
    /// </summary>
    /// <remarks>
    /// Notes overlap heavily - the gap between entries is one or two sixteenths and a note may run
    /// for sixteen, so five to nine simultaneous voices is routine. They used to be generated clear
    /// of one another for the note-survival assertion, because the voice packer's overflow path
    /// discarded anything past the fourth simultaneous line and overlapping input would have been
    /// asserting that bug rather than finding it. Measured on this generator before the packer was
    /// fixed: 262 of 300 files lost at least one note, 908 notes out of 4,708.
    /// </remarks>
    private static List<DomainNote> JitteredLine(Random random, int ppqn, int noteCount)
    {
        long sixteenth = Math.Max(1, ppqn / 4);
        List<DomainNote> notes = [];
        long tick = 0;

        for (int i = 0; i < noteCount; i++)
        {
            long jitter = random.NextInt64(-(sixteenth / 2), (sixteenth / 2) + 1);
            long gap = sixteenth * (1 + random.Next(2));
            long length = sixteenth * (1 + random.Next(16));

            notes.Add(new DomainNote(
                Pitch.FromMidi(48 + random.Next(36)),
                Math.Max(0, tick + jitter),
                Math.Max(1, length),
                90));

            tick += gap;
        }

        return notes;
    }

    private static NotationScore BuildJittered(
        Random random, int ppqn, int numerator, int denominator, int noteCount,
        Scale? target = null) =>
        Build(
            [Track(0, 0, JitteredLine(random, ppqn, noteCount))],
            target,
            [new TimeSignatureChange(0, numerator, denominator)],
            ppqn);

    [Theory]
    [InlineData(96, 4, 4)]
    [InlineData(192, 3, 4)]
    [InlineData(384, 6, 8)]
    [InlineData(480, 4, 4)]
    [InlineData(480, 5, 4)]
    [InlineData(480, 7, 8)]
    [InlineData(960, 12, 8)]
    public void EveryVoiceStillFillsItsMeasureWhenTheTimingIsHuman(
        int ppqn, int numerator, int denominator)
    {
        // The same helper the exact-boundary tests use, pointed at input that can actually break
        // it: notes displaced by up to half a sixteenth, which is what turns a beat into a tuplet
        // reading and a span into something no note value can spell.
        Random random = new(20260828);

        for (int file = 0; file < 20; file++)
        {
            AssertEveryVoiceFillsItsMeasure(
                BuildJittered(random, ppqn, numerator, denominator, 1 + random.Next(24)));
        }
    }

    [Fact]
    public void EveryVoiceFillsItsMeasureAcrossAFuzzOfGeneratedFiles()
    {
        // Fixed seed: a failure here is reproducible, and the file index narrows it down. This is
        // the guard the suite lacked - the properties are the two that make a score wrong rather
        // than ugly, and neither is visible in a hand-written fixture.
        Random random = new(1_618_033);
        int[] ppqns = [96, 192, 384, 480, 960];
        (int Numerator, int Denominator)[] signatures =
            [(4, 4), (3, 4), (6, 8), (5, 4), (7, 8), (12, 8)];

        for (int file = 0; file < 300; file++)
        {
            int ppqn = ppqns[random.Next(ppqns.Length)];
            (int numerator, int denominator) = signatures[random.Next(signatures.Length)];
            var target = random.Next(2) == 0 ? CMajor : Slendro;

            var score = BuildJittered(
                random, ppqn, numerator, denominator, 1 + random.Next(24), target);

            foreach (var part in score.Parts)
            {
                foreach (var measure in part.Measures)
                {
                    var lines = measure.Entries
                        .Where(e => !e.IsChordMember)
                        .GroupBy(e => (e.Staff, e.Voice));

                    foreach (var line in lines)
                    {
                        line.Sum(e => e.DurationTicks).Should().Be(
                            measure.LengthTicks,
                            $"file {file} ({ppqn} ppqn, {numerator}/{denominator}) staff "
                            + $"{line.Key.Staff} voice {line.Key.Voice} of measure {measure.Number}");
                    }
                }
            }
        }
    }

    [Fact]
    public void NoNoteVanishesAcrossAFuzzOfGeneratedFiles()
    {
        // Overlapping notes, which is what this used to avoid. The separation was not a property of
        // the input worth testing - it was there because the voice packer's overflow path really
        // did discard notes past the fourth simultaneous line, and generating overlaps would have
        // been asserting that bug. It no longer does, so the overlaps are back and the assertion
        // now covers the packer as well as the quantise-split-spell chain. A note may be moved and
        // it may be cut into tied pieces, but exactly one attack has to come out the other end for
        // each one that went in.
        Random random = new(2_718_281);
        int[] ppqns = [96, 192, 384, 480, 960];
        (int Numerator, int Denominator)[] signatures =
            [(4, 4), (3, 4), (6, 8), (5, 4), (7, 8), (12, 8)];

        for (int file = 0; file < 300; file++)
        {
            int ppqn = ppqns[random.Next(ppqns.Length)];
            (int numerator, int denominator) = signatures[random.Next(signatures.Length)];
            int noteCount = 1 + random.Next(24);

            (var score, int notesIn) = BuildCounting(
                [Track(0, 0, JitteredLine(random, ppqn, noteCount))],
                signatures: [new TimeSignatureChange(0, numerator, denominator)],
                ppqn: ppqn);

            AttackCount(score).Should().Be(
                notesIn,
                $"file {file} ({ppqn} ppqn, {numerator}/{denominator}) must keep every note");

            score.Diagnostics.Should().NotContain(
                d => d.Contains("could not be written", StringComparison.Ordinal),
                $"file {file} must not have to discard anything");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Voice overflow. Four voices is where a staff stops being readable, not where the builder
    // stops writing: the old hard cap folded the fifth line into the fourth, whose cursor was
    // already past it, and BuildVoice then discarded it with a length of zero or less. The
    // diagnostic survived and said the rhythm was approximate when the note was simply gone.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Seven independent lines entering one after another on one staff, all sounding to the end of
    /// the bar. Nothing may be lost: this is the case that lost three notes under the old cap.
    /// </summary>
    [Fact]
    public void MoreThanFourOverlappingLinesKeepEveryNote()
    {
        // Each line enters a sixteenth after the last and holds to the barline, so line n cannot
        // share a voice with any of lines 1..n-1 - seven voices are genuinely needed. Every onset
        // and length is an exact multiple of a sixteenth, so nothing here is a quantiser accident.
        const int Lines = 7;

        var notes = Enumerable.Range(0, Lines)
            .Select(i => N(DiatonicMidi(i), i * 120, 1920 - (i * 120)))
            .ToList();

        var score = Build([Track(0, 0, notes)]);
        var measure = score.Parts[0].Measures[0];

        AttackCount(score).Should().Be(Lines, "every line has to appear on the staff");

        measure.Entries.Where(e => !e.IsRest).Select(e => e.Voice).Distinct()
            .Should().HaveCount(Lines, "each line needs a voice of its own to be written at all");

        score.Diagnostics.Should().Contain(
            d => d.Contains("hard to read", StringComparison.Ordinal),
            "crossing four voices is worth saying, even though nothing was lost");

        score.Diagnostics.Should().NotContain(
            d => d.Contains("could not be written", StringComparison.Ordinal),
            "seven is well inside the hard ceiling");

        AssertEveryVoiceFillsItsMeasure(score);
    }

    /// <summary>
    /// The readability threshold is exactly that, and nothing more: four lines raise nothing.
    /// </summary>
    [Fact]
    public void FourOverlappingLinesRaiseNoDiagnostic()
    {
        var notes = Enumerable.Range(0, 4)
            .Select(i => N(DiatonicMidi(i), i * 120, 1920 - (i * 120)))
            .ToList();

        Build([Track(0, 0, notes)]).Diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// Past the hard ceiling something has to give, but it is counted and named rather than
    /// silently skipped - the whole failure of the old path was a diagnostic that described the
    /// wrong outcome.
    /// </summary>
    [Fact]
    public void PastTheHardCeilingTheDiscardedNotesAreCounted()
    {
        int lines = NotationBuilder.VoiceCeilingPerStaff + 3;

        // A 12/8 bar is 2,880 ticks - twenty-four sixteenths - which is the smallest ordinary metre
        // with room for nineteen entries a sixteenth apart inside one measure. Keeping it to one
        // measure is what makes the count assertable: a second bar would drop its own three.
        const long BarTicks = 2880;

        var notes = Enumerable.Range(0, lines)
            .Select(i => N(DiatonicMidi(i), i * 120, BarTicks - (i * 120)))
            .ToList();

        var score = Build(
            [Track(0, 0, notes)], signatures: [new TimeSignatureChange(0, 12, 8)]);

        AttackCount(score).Should().Be(
            NotationBuilder.VoiceCeilingPerStaff,
            "the ceiling is the only place a note may be dropped");

        score.Diagnostics.Should().Contain(
            d => d.Contains("3 note(s) could not be written", StringComparison.Ordinal),
            "the count is the point: a diagnostic that does not say how many is not a report");

        AssertEveryVoiceFillsItsMeasure(score);
    }

    // ---------------------------------------------------------------------------------------
    // Beaming. BeamGrouperTests pins the grouping rule against hand-built entries; these two
    // pin the join - that the builder hands it the right beat and writes the answer back.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void EighthsInCommonTimeComeBackBeamedInPairs()
    {
        var score = Build([Track(0, 0, Enumerable.Range(0, 8).Select(i => N(60 + i, i * 240, 240)))]);

        score.Parts[0].Measures[0].Entries
            .Where(e => !e.IsRest)
            .SelectMany(e => e.Beams)
            .Should().Equal(
                [
                    BeamState.Begin, BeamState.End,
                    BeamState.Begin, BeamState.End,
                    BeamState.Begin, BeamState.End,
                    BeamState.Begin, BeamState.End,
                ],
                "four quarter beats, two eighths each - one beam group per beat");
    }

    [Fact]
    public void EighthsInSixEightComeBackBeamedInThrees()
    {
        // The builder has to hand the grouper the measure's own metre for this to work. Handed the
        // printed eighth beat instead, the six eighths would come back in twos and the bar would
        // read as 3/4.
        var score = Build(
            [Track(0, 0, Enumerable.Range(0, 6).Select(i => N(60 + i, i * 240, 240)))],
            signatures: [new TimeSignatureChange(0, 6, 8)]);

        score.Parts[0].Measures[0].Entries
            .Where(e => !e.IsRest)
            .SelectMany(e => e.Beams)
            .Should().Equal(
                [
                    BeamState.Begin, BeamState.Continue, BeamState.End,
                    BeamState.Begin, BeamState.Continue, BeamState.End,
                ],
                "compound time beams by the dotted quarter, not by the printed beat");
    }

    [Fact]
    public void QuartersAreNeverBeamed()
    {
        var score = Build([Track(0, 0, Enumerable.Range(0, 4).Select(i => N(60 + i, i * 480, 480)))]);

        score.Parts[0].Measures[0].Entries.Should().AllSatisfy(
            e => e.IsBeamed.Should().BeFalse(), "a quarter has no flags to join");
    }

    [Fact]
    public void TheReviewsMinimalOverrunCaseFillsItsMeasureExactly()
    {
        // The reproduction from the notation review: one note played 54 ticks late in an otherwise
        // empty 4/4 bar. It came back as a *sextuplet quarter* and the bar totalled 1950 divisions
        // against 1920. Both halves are pinned - the reading and the arithmetic.
        var score = Build([Track(0, 0, [N(60, 54, 480)])]);
        var measure = score.Parts[0].Measures[0];

        measure.Entries.Sum(e => e.DurationTicks).Should().Be(
            1920, "one late note must not lengthen the bar");

        measure.Entries.Should().AllSatisfy(
            e => e.Duration.EffectiveTuplet.IsNone.Should().BeTrue(),
            "a bar with one onset in it is not divided into six");
    }

    [Fact]
    public void ARestInsideATupletBeatIsWrittenOnTheTupletGrid()
    {
        // Five of the six steps of a sextuplet beat, with the fourth missing. That hole is 80 ticks
        // and no straight value is 80 ticks, so spelling rests straight could not express it at all
        // and it took the round-up path - 90 ticks written for an 80-tick gap, which is the exact
        // line the review's overrun dump shows. It also left the rest with no ratio, which breaks
        // the tuplet bracket that has to run across it.
        var score = Build([Track(0, 0, [
            N(60, 0, 80), N(62, 80, 80), N(64, 240, 80), N(65, 320, 80), N(67, 400, 80),
        ])]);

        var measure = score.Parts[0].Measures[0];

        var insideTheBeat = measure.Entries.FirstOrDefault(
            e => e.IsRest && e.StartTicks == 160);

        insideTheBeat.Should().NotBeNull("the fourth sextuplet step is silent");
        insideTheBeat!.DurationTicks.Should().Be(80);
        insideTheBeat.Duration.EffectiveTuplet.Should().Be(Tuplet.Sextuplet);
        AssertEveryVoiceFillsItsMeasure(score);
    }

    [Fact]
    public void AMetreChangeQuantisesTheSecondHalfAgainstItsOwnBeat()
    {
        // The quantiser used to be handed measure 1's beat for the whole part while the decomposer
        // used each measure's own. In a file that changes from 4/4 (beat 480) to 6/8 (beat 240)
        // that groups the tuplet decision on doubled boundaries for everything after the change.
        // Three even notes filling one 6/8 eighth are a triplet; read on a 480-tick beat they are
        // three notes scattered through half of one.
        var notes = new List<DomainNote>
        {
            N(60, 0, 480), N(62, 480, 480), N(64, 960, 480), N(65, 1440, 480),
        };

        // Measure 2 is 6/8 starting at 1920: a triplet inside its first eighth-note beat.
        notes.AddRange([N(67, 1920, 80), N(69, 2000, 80), N(71, 2080, 80)]);

        var score = Build(
            [Track(0, 0, notes)],
            signatures: [new TimeSignatureChange(0, 4, 4), new TimeSignatureChange(1920, 6, 8)]);

        var second = score.Parts[0].Measures[1];

        second.BeatsPerMeasure.Should().Be(6);
        second.Entries.Where(e => !e.IsRest).Should().Contain(
            e => e.Duration.EffectiveTuplet == Tuplet.Triplet,
            "three even notes in one printed eighth of 6/8 are a triplet on that beat");

        AssertEveryVoiceFillsItsMeasure(score);
    }
}
