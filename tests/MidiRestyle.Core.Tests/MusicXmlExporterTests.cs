using System.Xml.Linq;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="MusicXmlExporter"/> serialises an already-built <see cref="NotationScore"/>. Every
/// fixture here is constructed by hand rather than through <see cref="NotationBuilder"/>, so a
/// failure points at the writer and not at the rhythm machinery upstream of it - except in
/// <see cref="EndToEndThroughTheBuilderProducesAParsableScore"/>, which exists precisely to catch
/// the two drifting apart.
/// </summary>
/// <remarks>
/// The load-bearing case is <see cref="AChordAdvancesTheCursorByOneNoteNotThree"/>. MusicXML has a
/// single moving cursor per part and a <c>&lt;chord/&gt;</c> note consumes no time; getting that
/// wrong produces a file that still parses and still looks plausible, and is wrong everywhere after
/// the first chord.
/// </remarks>
public class MusicXmlExporterTests
{
    private const int C = 0;
    private const int D = 1;
    private const int E = 2;
    private const int F = 3;
    private const int G = 4;
    private const int B = 6;

    private const int Ppqn = 480;
    private const long MeasureTicks = Ppqn * 4;

    private static readonly Scale CMajor = new(
        "test.cmajor", "C major", "Western", "Europe & Balkans",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    // --- fixtures ----------------------------------------------------------------------

    private static NotationEntry NoteEntry(
        int letter,
        int octave,
        long start,
        double alter = 0,
        long ticks = Ppqn,
        NoteValue value = NoteValue.Quarter,
        int dots = 0,
        Tuplet tuplet = default,
        int staff = 1,
        int voice = 1,
        bool chord = false,
        TieState tie = TieState.None) => new()
        {
            Note = new SpelledNote(letter, octave, alter, ResidualCents: 0),
            Duration = new NotatedDuration(value, dots, tuplet),
            StartTicks = start,
            DurationTicks = ticks,
            Staff = staff,
            Voice = voice,
            IsChordMember = chord,
            Tie = tie,
        };

    private static NotationEntry RestEntry(
        long start,
        long ticks = Ppqn,
        NoteValue value = NoteValue.Quarter,
        int staff = 1,
        int voice = 1) => new()
        {
            Note = null,
            Duration = new NotatedDuration(value),
            StartTicks = start,
            DurationTicks = ticks,
            Staff = staff,
            Voice = voice,
        };

    private static NotationMeasure Measure(
        int number,
        long startTicks,
        NotationEntry[] entries,
        int beats = 4,
        int beatUnit = 4,
        bool signatureChanged = false,
        long lengthTicks = MeasureTicks) => new()
        {
            Number = number,
            StartTicks = startTicks,
            LengthTicks = lengthTicks,
            BeatsPerMeasure = beats,
            BeatUnit = beatUnit,
            TimeSignatureChanged = signatureChanged,
            Entries = entries,
        };

    private static NotationPart Part(
        NotationMeasure[] measures,
        int staffCount = 1,
        string name = "Lead",
        IReadOnlyList<Clef>? clefs = null) => new()
        {
            Id = "P1",
            Name = name,
            TrackIndex = 0,
            Channel = 0,
            StaffCount = staffCount,
            Clefs = clefs ?? (staffCount == 2 ? [Clef.Treble, Clef.Bass] : [Clef.Treble]),
            Measures = measures,
        };

    private static NotationScore Score(params NotationPart[] parts) => new()
    {
        Divisions = Ppqn,
        Title = "Test Piece",
        ScaleName = "C major",
        Parts = parts,
    };

    /// <summary>One part, one 4/4 measure, whatever entries the test cares about.</summary>
    private static NotationScore OneMeasure(params NotationEntry[] entries) =>
        Score(Part([Measure(1, 0, entries, signatureChanged: true)]));

    private static XDocument Export(NotationScore score) =>
        XDocument.Parse(MusicXmlExporter.ToXml(score));

    private static List<XElement> Notes(XDocument doc) => [.. doc.Descendants("note")];

    private static string Value(XElement parent, string child) =>
        parent.Element(child)?.Value ?? string.Empty;

    // --- document shape ----------------------------------------------------------------

    [Fact]
    public void TheDocumentParsesAndIsAPartwiseScoreAtVersionFour()
    {
        XDocument doc = Export(OneMeasure(NoteEntry(C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole)));

        doc.Root!.Name.LocalName.Should().Be("score-partwise");
        doc.Root.Attribute("version")!.Value.Should().Be("4.0");
    }

    [Fact]
    public void TheDocumentDeclaresTheMusicXmlFourPartwiseDoctype()
    {
        string xml = MusicXmlExporter.ToXml(
            OneMeasure(NoteEntry(C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole)));

        xml.Should().Contain(
            "<!DOCTYPE score-partwise PUBLIC \"-//Recordare//DTD MusicXML 4.0 Partwise//EN\" "
            + "\"http://www.musicxml.org/dtds/partwise.dtd\">");
        xml.Should().Contain("encoding=\"utf-8\"",
            "the declaration has to agree with how Write actually encodes the bytes");
    }

    [Fact]
    public void TheHeaderCarriesTheTitleThePartNameAndTheEncodingSoftware()
    {
        XDocument doc = Export(OneMeasure(NoteEntry(C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole)));

        doc.Descendants("work-title").Single().Value.Should().Be("Test Piece");
        doc.Descendants("software").Single().Value.Should().Be("MIDIRestyle");
        doc.Descendants("encoding-date").Single().Value.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
        doc.Descendants("score-part").Single().Attribute("id")!.Value.Should().Be("P1");
        doc.Descendants("part-name").Single().Value.Should().Be("Lead");
        doc.Descendants("part").Single().Attribute("id")!.Value.Should().Be("P1");
    }

    [Fact]
    public void DivisionsMatchTheScoreSoNoDurationHasToBeRescaled()
    {
        NotationScore score =
            OneMeasure(NoteEntry(C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole))
                with { Divisions = 960 };

        XDocument doc = Export(score);

        doc.Descendants("divisions").Single().Value.Should().Be("960");
    }

    [Fact]
    public void AnEmptyScoreIsRefusedWithAReadableReasonRatherThanWrittenAsAPartlessFile()
    {
        NotationScore empty = new() { Divisions = Ppqn, Parts = [] };

        Action export = () => MusicXmlExporter.ToXml(empty);

        export.Should().Throw<MusicXmlExportException>().WithMessage("*no parts*");
    }

    // --- the classic bug ---------------------------------------------------------------

    /// <summary>
    /// A C major triad is one <c>&lt;note&gt;</c> without <c>&lt;chord/&gt;</c> and two with it, and
    /// the three of them together advance the cursor by <em>one</em> quarter. An exporter that
    /// advances per notehead writes a measure three times too long and every later voice lands in
    /// the wrong place.
    /// </summary>
    [Fact]
    public void AChordAdvancesTheCursorByOneNoteNotThree()
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(C, 4, 0),
            NoteEntry(E, 4, 0, chord: true),
            NoteEntry(G, 4, 0, chord: true),
            RestEntry(480),
            RestEntry(960),
            RestEntry(1440)));

        List<XElement> notes = Notes(doc);

        notes.Should().HaveCount(6);
        notes[0].Element("chord").Should().BeNull("the lowest-indexed note of a chord leads it");
        notes[1].Element("chord").Should().NotBeNull();
        notes[2].Element("chord").Should().NotBeNull();

        long advanced = notes
            .Where(n => n.Element("chord") is null)
            .Sum(n => (long)n.Element("duration")!);

        advanced.Should().Be(MeasureTicks,
            "a chord member sounds with the note before it and consumes no time");

        long everyNotehead = notes.Sum(n => (long)n.Element("duration")!);

        everyNotehead.Should().Be(MeasureTicks + (2 * Ppqn),
            "the sum over all noteheads is the wrong number to position anything by - which is "
            + "exactly why the cursor must skip chord members");
    }

    /// <summary>
    /// The same bug seen from the other side: the <c>&lt;backup&gt;</c> before the second voice is
    /// computed from the cursor, so if chord members advanced it the rewind would be too far and
    /// voice 2 would start before the barline.
    /// </summary>
    [Fact]
    public void TheBackupBeforeASecondVoiceRewindsExactlyOneMeasureEvenAfterAChord()
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(C, 4, 0),
            NoteEntry(E, 4, 0, chord: true),
            NoteEntry(G, 4, 0, chord: true),
            RestEntry(480),
            RestEntry(960),
            RestEntry(1440),
            RestEntry(0, voice: 2),
            RestEntry(480, voice: 2),
            RestEntry(960, voice: 2),
            RestEntry(1440, voice: 2)));

        List<XElement> backups = [.. doc.Descendants("backup")];

        backups.Should().HaveCount(1, "one rewind, between the two voices");
        ((long)backups[0].Element("duration")!).Should().Be(MeasureTicks);
    }

    // --- pitch and accidentals ---------------------------------------------------------

    [Fact]
    public void AQuarterToneNoteWritesAHalfFlatAlterAndAQuarterFlatAccidental()
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(E, 4, 0, alter: -0.5, ticks: MeasureTicks, value: NoteValue.Whole)));

        XElement note = Notes(doc).Single();
        XElement pitch = note.Element("pitch")!;

        Value(pitch, "step").Should().Be("E");
        Value(pitch, "alter").Should().Be("-0.5",
            "MusicXML's alter is a decimal precisely so a quarter-tone can be written");
        Value(pitch, "octave").Should().Be("4");
        Value(note, "accidental").Should().Be("quarter-flat");
    }

    [Theory]
    [InlineData(2.0, "2", "double-sharp")]
    [InlineData(1.5, "1.5", "three-quarters-sharp")]
    [InlineData(1.0, "1", "sharp")]
    [InlineData(0.5, "0.5", "quarter-sharp")]
    [InlineData(-0.5, "-0.5", "quarter-flat")]
    [InlineData(-1.0, "-1", "flat")]
    [InlineData(-1.5, "-1.5", "three-quarters-flat")]
    [InlineData(-2.0, "-2", "flat-flat")]
    public void EveryNotatableAlterationGetsItsAlterValueAndItsAccidentalName(
        double alter, string expectedAlter, string expectedAccidental)
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(D, 4, 0, alter: alter, ticks: MeasureTicks, value: NoteValue.Whole)));

        XElement note = Notes(doc).Single();

        Value(note.Element("pitch")!, "alter").Should().Be(expectedAlter);
        Value(note, "accidental").Should().Be(expectedAccidental);
    }

    [Fact]
    public void AnUnalteredNoteOmitsAlterAndAccidentalEntirely()
    {
        XDocument doc = Export(OneMeasure(NoteEntry(G, 3, 0, ticks: MeasureTicks, value: NoteValue.Whole)));

        XElement note = Notes(doc).Single();

        note.Element("pitch")!.Element("alter").Should().BeNull("zero is not an alteration");
        note.Element("accidental").Should().BeNull("there is nothing for a natural to cancel");
    }

    [Fact]
    public void ANaturalIsWrittenOnlyWhereItCancelsAnEarlierAccidentalOnTheSameStaffPosition()
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(B, 3, 0, alter: -1),
            NoteEntry(B, 3, 480),
            NoteEntry(C, 4, 960),
            RestEntry(1440)));

        List<XElement> notes = Notes(doc);

        Value(notes[0], "accidental").Should().Be("flat");
        Value(notes[1], "accidental").Should().Be("natural",
            "the B-flat earlier in the measure is still in force at that staff position");
        notes[2].Element("accidental").Should().BeNull(
            "a different staff position was never altered, so nothing needs cancelling");
    }

    /// <summary>
    /// The key signature is always empty. A restyled maqam or pentatonic is not a major or minor
    /// key, so there is no correct signature and every accidental is written out instead.
    /// </summary>
    [Theory]
    [InlineData(C, 0.0)]
    [InlineData(F, -1.0)]
    [InlineData(B, 1.0)]
    public void FifthsIsAlwaysZeroWhateverTheTonic(int tonicLetter, double tonicAlter)
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(tonicLetter, 4, 0, alter: tonicAlter, ticks: MeasureTicks, value: NoteValue.Whole)));

        doc.Descendants("fifths").Should().HaveCount(1);
        doc.Descendants("fifths").Single().Value.Should().Be("0");
    }

    // --- rests, durations, tuplets -----------------------------------------------------

    [Fact]
    public void ARestWritesRestAndNoPitch()
    {
        XDocument doc = Export(OneMeasure(RestEntry(0, MeasureTicks, NoteValue.Whole)));

        XElement note = Notes(doc).Single();

        note.Element("rest").Should().NotBeNull();
        note.Element("pitch").Should().BeNull();
        note.Element("accidental").Should().BeNull();
        note.Element("tie").Should().BeNull("a rest has nothing to tie to");
        Value(note, "duration").Should().Be("1920");
        Value(note, "type").Should().Be("whole");
    }

    [Fact]
    public void ADottedHalfWritesItsTypeAndOneDot()
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(C, 4, 0, ticks: 1440, value: NoteValue.Half, dots: 1),
            RestEntry(1440)));

        XElement note = Notes(doc)[0];

        Value(note, "type").Should().Be("half");
        note.Elements("dot").Should().HaveCount(1);
        Value(note, "duration").Should().Be("1440");
    }

    [Fact]
    public void ATripletWritesTimeModificationOfThreeInTheTimeOfTwo()
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(C, 4, 0, ticks: 160, value: NoteValue.Eighth, tuplet: Tuplet.Triplet),
            NoteEntry(D, 4, 160, ticks: 160, value: NoteValue.Eighth, tuplet: Tuplet.Triplet),
            NoteEntry(E, 4, 320, ticks: 160, value: NoteValue.Eighth, tuplet: Tuplet.Triplet),
            RestEntry(480),
            RestEntry(960),
            RestEntry(1440)));

        List<XElement> triplet = [.. Notes(doc).Take(3)];

        foreach (XElement note in triplet)
        {
            XElement modification = note.Element("time-modification")!;

            Value(modification, "actual-notes").Should().Be("3");
            Value(modification, "normal-notes").Should().Be("2");
            Value(note, "type").Should().Be("eighth");
        }

        Notes(doc)[3].Element("time-modification").Should().BeNull(
            "an ordinary rest is not in the tuplet");
    }

    [Fact]
    public void AnUntupletedNoteWritesNoTimeModification()
    {
        XDocument doc = Export(OneMeasure(NoteEntry(C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole)));

        Notes(doc).Single().Element("time-modification").Should().BeNull();
    }

    // --- ties --------------------------------------------------------------------------

    [Fact]
    public void ANoteTiedAcrossABarlineStartsInOneMeasureAndStopsInTheNext()
    {
        NotationScore score = Score(Part([
            Measure(1, 0, [NoteEntry(C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole, tie: TieState.Start)],
                signatureChanged: true),
            Measure(2, MeasureTicks, [NoteEntry(C, 4, MeasureTicks, ticks: MeasureTicks, value: NoteValue.Whole, tie: TieState.Stop)]),
        ]));

        XDocument doc = Export(score);
        List<XElement> measures = [.. doc.Descendants("measure")];

        measures[0].Descendants("tie").Single().Attribute("type")!.Value.Should().Be("start");
        measures[0].Descendants("tied").Single().Attribute("type")!.Value.Should().Be("start");
        measures[1].Descendants("tie").Single().Attribute("type")!.Value.Should().Be("stop");
        measures[1].Descendants("tied").Single().Attribute("type")!.Value.Should().Be("stop");
    }

    [Fact]
    public void AContinueTieWritesBothAStopAndAStartWithTheStopFirst()
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole, tie: TieState.Continue)));

        XElement note = Notes(doc).Single();

        note.Elements("tie").Select(t => t.Attribute("type")!.Value)
            .Should().Equal(["stop", "start"],
                "MusicXML reads the ties in document order, so the arriving tie has to close "
                + "before the departing one opens");

        note.Element("notations")!.Elements("tied").Select(t => t.Attribute("type")!.Value)
            .Should().Equal(["stop", "start"], "the printed slurs mirror the sounding ties");
    }

    [Fact]
    public void AnUntiedNoteWritesNeitherTieNorNotations()
    {
        XDocument doc = Export(OneMeasure(NoteEntry(C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole)));

        XElement note = Notes(doc).Single();

        note.Elements("tie").Should().BeEmpty();
        note.Element("notations").Should().BeNull();
    }

    // --- staves and voices -------------------------------------------------------------

    [Fact]
    public void AGrandStaffPartDeclaresTwoStavesTagsEveryNoteAndBacksUpBetweenThem()
    {
        NotationScore score = Score(Part(
            [
                Measure(1, 0,
                    [
                        NoteEntry(G, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole, staff: 1),
                        NoteEntry(C, 3, 0, ticks: MeasureTicks, value: NoteValue.Whole, staff: 2),
                    ],
                    signatureChanged: true),
            ],
            staffCount: 2));

        XDocument doc = Export(score);

        doc.Descendants("staves").Single().Value.Should().Be("2");

        List<XElement> clefs = [.. doc.Descendants("clef")];

        clefs.Should().HaveCount(2);
        clefs[0].Attribute("number")!.Value.Should().Be("1");
        Value(clefs[0], "sign").Should().Be("G");
        Value(clefs[0], "line").Should().Be("2");
        clefs[1].Attribute("number")!.Value.Should().Be("2");
        Value(clefs[1], "sign").Should().Be("F");
        Value(clefs[1], "line").Should().Be("4");

        Notes(doc).Select(n => Value(n, "staff")).Should().Equal(["1", "2"]);

        XElement backup = doc.Descendants("backup").Single();

        ((long)backup.Element("duration")!).Should().Be(MeasureTicks,
            "the bass staff restarts at the barline, so the cursor has to be rewound to it");
    }

    [Fact]
    public void ASingleStaffPartTagsNoNoteWithAStaff()
    {
        XDocument doc = Export(OneMeasure(NoteEntry(C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole)));

        Notes(doc).Single().Element("staff").Should().BeNull(
            "one staff needs no disambiguation, and the element only adds noise");
    }

    /// <summary>
    /// A MusicXML voice number is unique across the whole part, but the model's voice is unique only
    /// within a staff. Reusing 1 on both staves of a grand staff leaves a reader unable to say which
    /// staff a voice belongs to, so the second staff is offset.
    /// </summary>
    [Fact]
    public void GrandStaffVoiceNumbersAreMadeUniqueAcrossTheWholePart()
    {
        NotationScore score = Score(Part(
            [
                Measure(1, 0,
                    [
                        NoteEntry(G, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole, staff: 1, voice: 1),
                        NoteEntry(C, 3, 0, ticks: MeasureTicks, value: NoteValue.Whole, staff: 2, voice: 1),
                    ],
                    signatureChanged: true),
            ],
            staffCount: 2));

        Export(score).Descendants("note").Select(n => Value(n, "voice"))
            .Should().Equal(["1", "5"],
                "staff 1 keeps voices 1-4 and staff 2 takes 5-8, the convention Finale and "
                + "MuseScore both write");
    }

    [Fact]
    public void EntriesAreEmittedGroupedByStaffThenVoiceHoweverTheyArrive()
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(C, 4, 0, voice: 1),
            RestEntry(0, voice: 2),
            NoteEntry(D, 4, 480, voice: 1),
            RestEntry(480, voice: 2),
            RestEntry(960, voice: 1),
            RestEntry(960, voice: 2),
            RestEntry(1440, voice: 1),
            RestEntry(1440, voice: 2)));

        Notes(doc).Select(n => Value(n, "voice"))
            .Should().Equal(["1", "1", "1", "1", "2", "2", "2", "2"],
                "MusicXML wants one complete voice at a time, not the model's time-ordered "
                + "interleaving");
    }

    // --- attributes and time signatures ------------------------------------------------

    [Fact]
    public void TimeIsWrittenInTheFirstMeasureAndThenOnlyWhereTheSignatureChanges()
    {
        NotationScore score = Score(Part([
            Measure(1, 0, [RestEntry(0, MeasureTicks, NoteValue.Whole)], signatureChanged: true),
            Measure(2, MeasureTicks, [RestEntry(MeasureTicks, MeasureTicks, NoteValue.Whole)]),
            Measure(3, MeasureTicks * 2, [RestEntry(MeasureTicks * 2, 1440, NoteValue.Half)],
                beats: 3, signatureChanged: true, lengthTicks: 1440),
        ]));

        XDocument doc = Export(score);
        List<XElement> measures = [.. doc.Descendants("measure")];

        measures[0].Descendants("time").Should().HaveCount(1);
        Value(measures[0].Descendants("time").Single(), "beats").Should().Be("4");
        Value(measures[0].Descendants("time").Single(), "beat-type").Should().Be("4");

        measures[1].Descendants("attributes").Should().BeEmpty(
            "repeating an unchanged 4/4 every measure litters the printed score");

        measures[2].Descendants("time").Should().HaveCount(1);
        Value(measures[2].Descendants("time").Single(), "beats").Should().Be("3");

        doc.Descendants("divisions").Should().HaveCount(1,
            "divisions is declared once, in the part's first measure");
    }

    [Fact]
    public void ABassPartGetsAnFClefOnTheFourthLine()
    {
        NotationScore score = Score(Part(
            [Measure(1, 0, [NoteEntry(C, 2, 0, ticks: MeasureTicks, value: NoteValue.Whole)], signatureChanged: true)],
            clefs: [Clef.Bass]));

        XElement clef = Export(score).Descendants("clef").Single();

        Value(clef, "sign").Should().Be("F");
        Value(clef, "line").Should().Be("4");
    }

    // --- escaping and IO ---------------------------------------------------------------

    [Fact]
    public void APartNameWithMarkupCharactersIsEscapedAndTheDocumentStillParses()
    {
        const string Awkward = "Brass & <Strings> \"Section\"";

        NotationScore score = Score(Part(
            [Measure(1, 0, [RestEntry(0, MeasureTicks, NoteValue.Whole)], signatureChanged: true)],
            name: Awkward));

        string xml = MusicXmlExporter.ToXml(score);

        xml.Should().Contain("Brass &amp; &lt;Strings&gt;", "the writer escapes, it does not strip");
        XDocument.Parse(xml).Descendants("part-name").Single().Value.Should().Be(Awkward,
            "escaping has to round-trip, not merely avoid throwing");
    }

    [Fact]
    public void ControlCharactersInATrackNameAreDroppedRatherThanCrashingTheWriter()
    {
        NotationScore score = Score(Part(
            [Measure(1, 0, [RestEntry(0, MeasureTicks, NoteValue.Whole)], signatureChanged: true)],
            name: "Lead\u0001Synth\u000B"));

        Export(score).Descendants("part-name").Single().Value.Should().Be("LeadSynth",
            "XML 1.0 cannot represent a control character in any encoding, and a track name is "
            + "raw bytes from someone else's file");
    }

    [Fact]
    public void WriteProducesAFileThatParses()
    {
        string path = Path.GetTempFileName();

        try
        {
            MusicXmlExporter.Write(
                OneMeasure(NoteEntry(C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole)), path);

            File.Exists(path).Should().BeTrue();

            XDocument doc = XDocument.Parse(File.ReadAllText(path));

            doc.Root!.Name.LocalName.Should().Be("score-partwise");
            doc.Descendants("note").Should().HaveCount(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteWrapsAnIoFailureInAMusicXmlExportExceptionCarryingThePath()
    {
        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        try
        {
            // A path whose parent is an existing *file* cannot be created, which is the tidiest
            // portable way to provoke a genuine IO failure.
            string blocker = Path.Combine(directory, "blocker");
            File.WriteAllText(blocker, "not a folder");

            Action write = () => MusicXmlExporter.Write(
                OneMeasure(RestEntry(0, MeasureTicks, NoteValue.Whole)),
                Path.Combine(blocker, "score.musicxml"));

            write.Should().Throw<MusicXmlExportException>()
                .Which.FilePath.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // --- beams ---------------------------------------------------------------------------

    /// <summary>
    /// One <c>&lt;beam&gt;</c> per level, numbered from 1. The builder decides the levels; all this
    /// writer does is number them in order and name them.
    /// </summary>
    [Fact]
    public void EachBeamLevelIsWrittenAsItsOwnNumberedElement()
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(C, 4, 0, ticks: 240, value: NoteValue.Eighth) with
            {
                Beams = [BeamState.Begin],
            },
            NoteEntry(D, 4, 240, ticks: 240, value: NoteValue.Eighth) with
            {
                Beams = [BeamState.End],
            },
            RestEntry(480, MeasureTicks - 480, NoteValue.Half)));

        List<XElement> beams = [.. doc.Descendants("beam")];

        beams.Select(b => b.Value).Should().Equal(["begin", "end"]);
        beams.Select(b => b.Attribute("number")!.Value).Should().Equal(
            ["1", "1"], "both notes are on the first beam level");
    }

    /// <summary>
    /// A hook's MusicXML text has a space in it - <c>backward hook</c>, not <c>backward-hook</c> -
    /// which is one of the few places the vocabulary does not hyphenate, and therefore one of the
    /// few places a plausible-looking spelling is silently rejected.
    /// </summary>
    [Fact]
    public void AHookIsWrittenWithASpaceRatherThanAHyphen()
    {
        XDocument doc = Export(OneMeasure(
            NoteEntry(C, 4, 0, ticks: 360, value: NoteValue.Eighth, dots: 1) with
            {
                Beams = [BeamState.Begin],
            },
            NoteEntry(D, 4, 360, ticks: 120, value: NoteValue.Sixteenth) with
            {
                Beams = [BeamState.End, BeamState.BackwardHook],
            },
            RestEntry(480, MeasureTicks - 480, NoteValue.Half)));

        Notes(doc)[1].Elements("beam")
            .Select(b => $"{b.Attribute("number")!.Value}:{b.Value}")
            .Should().Equal(
                ["1:end", "2:backward hook"],
                "the sixteenth of a dotted-eighth pair ends the beam and hooks its second level");
    }

    /// <summary>
    /// The DTD's child order, with <c>&lt;beam&gt;</c> in it. Readers are strict about this and a
    /// misplaced element makes the whole note unreadable, so the order is pinned as a whole rather
    /// than as "beam is present somewhere".
    /// </summary>
    [Fact]
    public void BeamsSitAfterTheStaffAndBeforeTheNotations()
    {
        NotationScore score = Score(Part(
            [
                Measure(1, 0,
                    [
                        NoteEntry(
                            C, 4, 0, ticks: 240, value: NoteValue.Eighth, staff: 1,
                            tie: TieState.Start) with
                        {
                            Beams = [BeamState.Begin],
                        },
                        NoteEntry(
                            C, 4, 240, ticks: 240, value: NoteValue.Eighth, staff: 1,
                            tie: TieState.Stop) with
                        {
                            Beams = [BeamState.End],
                        },
                        RestEntry(480, MeasureTicks - 480, NoteValue.Half),
                        RestEntry(0, MeasureTicks, NoteValue.Whole, staff: 2),
                    ],
                    signatureChanged: true),
            ],
            staffCount: 2));

        Notes(Export(score))[0].Elements().Select(e => e.Name.LocalName)
            .Should().Equal(
                ["pitch", "duration", "tie", "voice", "type", "staff", "beam", "notations"],
                "beam comes after staff and before notations in the MusicXML 4.0 DTD");
    }

    /// <summary>
    /// The coupling between the builder's voice ceiling and this writer's staff offset. Four voices
    /// per staff is now only a readability threshold, so a staff may genuinely use six - and a fixed
    /// offset of four would then number staff 1's voice 5 and staff 2's voice 1 identically, merging
    /// the two staves' music in every reader that opens the file.
    /// </summary>
    [Fact]
    public void AStaffUsingMoreThanFourVoicesStillCannotCollideWithTheOtherStaff()
    {
        int wide = NotationBuilder.MaxVoicesPerStaff + 2;

        NotationEntry[] entries =
        [
            .. Enumerable.Range(1, wide).Select(v => NoteEntry(
                C, 4, 0, ticks: MeasureTicks, value: NoteValue.Whole, staff: 1, voice: v)),
            NoteEntry(C, 3, 0, ticks: MeasureTicks, value: NoteValue.Whole, staff: 2, voice: 1),
        ];

        XDocument doc = Export(Score(Part(
            [Measure(1, 0, entries, signatureChanged: true)], staffCount: 2)));

        List<string> staffOne = [.. doc.Descendants("note")
            .Where(n => Value(n, "staff") == "1")
            .Select(n => Value(n, "voice"))];

        List<string> staffTwo = [.. doc.Descendants("note")
            .Where(n => Value(n, "staff") == "2")
            .Select(n => Value(n, "voice"))];

        staffOne.Should().Equal(["1", "2", "3", "4", "5", "6"]);
        staffTwo.Should().Equal(
            ["7"], "the offset has to clear the widest staff, not the conventional four");
        staffOne.Should().NotIntersectWith(staffTwo);
    }

    // --- end to end --------------------------------------------------------------------

    /// <summary>
    /// The one test that goes through <see cref="NotationBuilder"/>. Everything above it exercises
    /// the writer against hand-built fixtures; this exists so that a change to the builder's output
    /// shape - a different voice numbering, an extra rest - cannot pass unnoticed.
    /// </summary>
    [Fact]
    public void EndToEndThroughTheBuilderProducesAParsableScore()
    {
        TrackInfo info = new()
        {
            TrackIndex = 0,
            Channel = 0,
            Name = "Lead",
            Notes =
            [
                new Note(Pitch.FromMidi(60), 0, 480, 90),
                new Note(Pitch.FromMidi(62), 480, 480, 90),
                new Note(Pitch.FromMidi(64), 960, 480, 90),
                new Note(Pitch.FromMidi(65), 1440, 480, 90),
            ],
        };

        MidiProject project = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [info],
            Title = "Round Trip",
        };

        RestyleSettings settings = new()
        {
            TargetScale = CMajor,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        };

        NotationScore score = NotationBuilder.Build(
            project, [new RestyledTrack(0, 0, info.Notes, WasRestyled: true)], settings);

        XDocument doc = Export(score);

        doc.Root!.Name.LocalName.Should().Be("score-partwise");
        doc.Descendants("divisions").Single().Value.Should().Be("480");
        doc.Descendants("work-title").Single().Value.Should().Be("Round Trip");
        doc.Descendants("part-name").Single().Value.Should().Be("Lead");
        doc.Descendants("fifths").Single().Value.Should().Be("0");

        doc.Descendants("note")
            .Where(n => n.Element("rest") is null)
            .Select(n => Value(n.Element("pitch")!, "step"))
            .Should().Equal(["C", "D", "E", "F"],
                "the builder and the writer have to agree on what came out of the pipeline");

        // Every measure has to add up: the durations of the notes that actually advance the cursor,
        // less anything a backup rewound, must equal the measure's length.
        foreach (XElement measure in doc.Descendants("measure"))
        {
            long advanced = measure.Elements("note")
                .Where(n => n.Element("chord") is null)
                .Sum(n => (long)n.Element("duration")!);
            long rewound = measure.Elements("backup").Sum(b => (long)b.Element("duration")!);

            (advanced - rewound).Should().Be(MeasureTicks,
                $"measure {measure.Attribute("number")!.Value} must be exactly one 4/4 bar long");
        }
    }
}
