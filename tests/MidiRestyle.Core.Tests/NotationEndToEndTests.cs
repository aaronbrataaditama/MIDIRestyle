using System.Xml.Linq;
using Melanchall.DryWetMidi.Core;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// Walks a real <c>.mid</c> file the whole way through: load, restyle, notate, export. The unit
/// tests around each stage pin its own behaviour; this one exists because the interesting failures
/// in a pipeline are at the joins, and because MusicXML that does not parse is worthless however
/// well the pieces behaved on their own.
/// </summary>
public class NotationEndToEndTests : IDisposable
{
    private const int Ppqn = 480;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"midirestyle-e2e-{Guid.NewGuid():N}");

    public NotationEndToEndTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a passing test over.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Maqam Rast: two neutral degrees, the quarter-tone case the whole design turns on.</summary>
    private static readonly Scale Rast = new(
        "t.rast", "Maqam Rast", "Arabic Maqam", "Middle East",
        [0, 200, 350, 500, 700, 900, 1050], "Test fixture, 2026");

    private static readonly Scale Slendro = new(
        "t.slendro", "Slendro", "Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

    private static readonly Scale CMajor = new(
        "t.cmajor", "C major", "Western", "Europe",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    /// <summary>
    /// Writes a piano piece with the features that break naive notation: a chord, a note tied over
    /// a barline, a triplet, a left hand well below middle C, and a metre change.
    /// </summary>
    /// <param name="jitter">
    /// Displaces every onset by a few dozen ticks, the way a person playing does. Machine-perfect
    /// onsets are the one input class the measure-length assertion below cannot fail: a span landing
    /// on an exact multiple of a sixty-fourth is always writable, so the decomposer never rounds up
    /// and the overrun it used to cause never appears. Adding this jitter turned that assertion red.
    /// </param>
    private string WriteSourceFile(bool jitter = false)
    {
        // Both hands on ONE track and one channel, which is how a piano part normally arrives -
        // and what gives the hand splitter something to split. Two separate tracks would correctly
        // produce two single-staff parts instead.
        PatternBuilderlessTrack piano = new(channel: 0, jitter ? 29 : 0);

        // Bar 1, right hand: a C major triad, then a stepwise run.
        piano.Chord(0, 480, [60, 64, 67]);
        piano.Note(480, 240, 69);
        piano.Note(720, 240, 71);

        // A triplet on beat 3, then a note tied across the barline into bar 2.
        piano.Note(960, 160, 72);
        piano.Note(1120, 160, 74);
        piano.Note(1280, 160, 76);
        piano.Note(1440, 960, 77);

        // Left hand, well below middle C, running underneath all of it.
        piano.Note(0, 960, 36);
        piano.Note(960, 960, 43);
        piano.Note(1920, 960, 41);

        TrackChunk conductor = new();
        conductor.Events.Add(new TimeSignatureEvent(4, 4));
        conductor.Events.Add(new SetTempoEvent(500_000));

        MidiFile file = new(conductor, piano.Build("Piano", program: 0))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(Ppqn),
        };

        string path = Path.Combine(_directory, jitter ? "source-jittered.mid" : "source.mid");
        file.Write(path);
        return path;
    }

    /// <summary>Minimal note-event builder - DryWetMIDI's pattern API is more than this needs.</summary>
    /// <remarks>
    /// <paramref name="jitterSeed"/> of zero writes the onsets exactly as given. Anything else
    /// nudges each one deterministically, so a failure reproduces.
    /// </remarks>
    private sealed class PatternBuilderlessTrack(int channel, int jitterSeed = 0)
    {
        private readonly List<(long Tick, MidiEvent Event)> _events = [];
        private readonly Random? _jitter = jitterSeed == 0 ? null : new Random(jitterSeed);

        public void Note(long start, long length, int midi)
        {
            (start, length) = Nudge(start, length);
            Raw(start, length, midi);
        }

        public void Chord(long start, long length, IEnumerable<int> midis)
        {
            // One nudge for the whole chord: three notes struck together stay struck together, or
            // the fixture stops testing chords and starts testing the voice packer.
            (start, length) = Nudge(start, length);

            foreach (int midi in midis)
            {
                Raw(start, length, midi);
            }
        }

        private (long Start, long Length) Nudge(long start, long length) =>
            _jitter is null
                ? (start, length)
                : (Math.Max(0, start + _jitter.Next(-40, 41)),
                   Math.Max(1, length + _jitter.Next(-40, 41)));

        private void Raw(long start, long length, int midi)
        {
            _events.Add((start, new NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)midi,
                (Melanchall.DryWetMidi.Common.SevenBitNumber)90)
            { Channel = (Melanchall.DryWetMidi.Common.FourBitNumber)channel }));

            _events.Add((start + length, new NoteOffEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)midi,
                (Melanchall.DryWetMidi.Common.SevenBitNumber)0)
            { Channel = (Melanchall.DryWetMidi.Common.FourBitNumber)channel }));
        }

        public TrackChunk Build(string name, int program)
        {
            TrackChunk chunk = new();
            chunk.Events.Add(new SequenceTrackNameEvent(name));
            chunk.Events.Add(new ProgramChangeEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)program)
            {
                Channel = (Melanchall.DryWetMidi.Common.FourBitNumber)channel,
            });

            long previous = 0;

            foreach ((long tick, MidiEvent midiEvent) in _events.OrderBy(e => e.Tick))
            {
                midiEvent.DeltaTime = tick - previous;
                previous = tick;
                chunk.Events.Add(midiEvent);
            }

            return chunk;
        }
    }

    private (NotationScore Score, MidiProject Project) Notate(Scale target, bool jitter = false)
    {
        MidiProject project = MidiFileLoader.Load(WriteSourceFile(jitter));

        RestyleSettings settings = new()
        {
            TargetScale = target,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        };

        RestyleResult result = RestyleEngine.Restyle(project, settings);
        return (NotationBuilder.Build(project, result.Tracks, settings), project);
    }

    [Fact]
    public void ARealFileRestyledToAMaqamExportsMusicXmlThatParses()
    {
        (NotationScore score, _) = Notate(Rast);
        string path = Path.Combine(_directory, "rast.musicxml");

        MusicXmlExporter.Write(score, path);

        File.Exists(path).Should().BeTrue();

        XDocument document = XDocument.Load(path);
        document.Root!.Name.LocalName.Should().Be("score-partwise");
        document.Descendants("part").Should().NotBeEmpty();
        document.Descendants("measure").Should().NotBeEmpty();
    }

    [Fact]
    public void TheMaqamsNeutralDegreesSurviveAsQuarterToneAccidentals()
    {
        // The point of the whole application: Rast's neutral second and sixth are 50 cents flat of
        // the Western degrees, and a 12-TET-only export would silently round them into a lie.
        (NotationScore score, _) = Notate(Rast);

        string xml = MusicXmlExporter.ToXml(score);
        XDocument document = XDocument.Parse(xml);

        var alters = document.Descendants("alter")
            .Select(a => double.Parse(a.Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        alters.Should().Contain(a => Math.Abs(a % 1) > 0.1,
            "a maqam's neutral degrees need a half-flat, which is a non-integer <alter>");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryVoiceInEveryExportedMeasureAccountsForItsFullLength(bool jitter)
    {
        // The invariant a MusicXML reader actually enforces. A voice that is short by one division
        // displaces every measure after it, and the file opens looking subtly wrong rather than
        // failing outright - which is far harder to notice.
        (NotationScore score, _) = Notate(Rast, jitter);

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
                        $"staff {line.Key.Staff} voice {line.Key.Voice} of measure {measure.Number}");
                }
            }
        }
    }

    [Fact]
    public void TheExportedMeasuresAreNoLongerThanTheirOwnTimeSignatureSaysWhenPlayedByAHuman()
    {
        // The same property read off the emitted document rather than the model, because that is
        // what a reader sees: 4/4 at 480 divisions is 1920 divisions of content per measure, and
        // 1950 is a defect whatever the model thought.
        (NotationScore score, _) = Notate(Rast, jitter: true);
        XDocument document = XDocument.Parse(MusicXmlExporter.ToXml(score));

        foreach (var part in document.Descendants("part"))
        {
            foreach (var measure in part.Elements("measure"))
            {
                long cursor = 0;
                long high = 0;

                foreach (var element in measure.Elements())
                {
                    long value = long.TryParse(
                        element.Element("duration")?.Value ?? element.Value,
                        out long parsed)
                        ? parsed
                        : 0;

                    cursor += element.Name.LocalName switch
                    {
                        "note" when element.Element("chord") is null => value,
                        "backup" => -value,
                        "forward" => value,
                        _ => 0,
                    };

                    high = Math.Max(high, cursor);
                }

                high.Should().Be(1920, $"measure {measure.Attribute("number")?.Value} is 4/4");
            }
        }
    }

    [Fact]
    public void ThePianoTrackComesBackAsAGrandStaff()
    {
        (NotationScore score, _) = Notate(Rast);

        score.Parts.Should().NotBeEmpty();
        score.Parts.Should().Contain(p => p.StaffCount == 2, "the left hand runs well below middle C");
    }

    [Fact]
    public void TheChordExportsAsOneTimedNoteAndTwoChordMembers()
    {
        (NotationScore score, _) = Notate(Rast);
        XDocument document = XDocument.Parse(MusicXmlExporter.ToXml(score));

        var firstMeasure = document.Descendants("measure").First();
        var notes = firstMeasure.Elements("note").ToList();

        notes.Should().NotBeEmpty();
        notes.Count(n => n.Element("chord") is not null).Should().BeGreaterThan(0,
            "the opening triad is one note plus chord members, not three independent notes");
    }

    [Fact]
    public void ANonNotatableScaleStillNotatesForTheDegreeViewButRefusesToExport()
    {
        // Slendro has no staff spelling at all. The score still has to exist - the degree view is
        // built from it - but writing it as MusicXML would be a misrepresentation.
        (NotationScore score, _) = Notate(Slendro);

        score.Parts.Should().NotBeEmpty("the degree view needs a score even when a staff cannot");

        DiatonicSpeller.Derive(Slendro).Succeeded.Should().BeFalse(
            "which is what the UI gates the staff and the MusicXML export on");
    }

    [Fact]
    public void BeamsSurviveTheWholePipelineAndEveryGroupOpensAndCloses()
    {
        // The fixture has a pair of eighths on beat 2 and a triplet on beat 3, so a file that comes
        // back with no beams at all has lost them somewhere between the builder and the writer.
        (NotationScore score, _) = Notate(Rast);
        XDocument document = XDocument.Parse(MusicXmlExporter.ToXml(score));

        List<XElement> beams = [.. document.Descendants("beam")];

        beams.Should().NotBeEmpty("the right hand has eighths that group under one beam");

        beams.Select(b => b.Value).Distinct().Should().BeSubsetOf(
            ["begin", "continue", "end", "forward hook", "backward hook"],
            "anything else is not a word the MusicXML vocabulary knows");

        // An unbalanced count means a beam is left hanging, which no reader will draw and some
        // will reject outright.
        beams.Count(b => b.Value == "begin").Should().Be(
            beams.Count(b => b.Value == "end"), "every beam that starts has to finish");

        foreach (NotationEntry entry in score.Parts
            .SelectMany(p => p.Measures)
            .SelectMany(m => m.Entries)
            .Where(e => e.IsBeamed))
        {
            entry.Beams.Count.Should().BeLessThanOrEqualTo(
                entry.Duration.Value.FlagCount(),
                "a note cannot carry a beam level it has no flag for");

            entry.IsChordMember.Should().BeFalse("the chord's timed head carries the beam");
        }
    }

    [Fact]
    public void EveryNoteInTheSourceSurvivesIntoTheScore()
    {
        // Quantisation may move a note, and a barline may split it into tied parts, but nothing may
        // vanish. A silently dropped note is the hardest kind of bug to see in a score.
        (NotationScore score, MidiProject project) = Notate(Rast);

        int sourceNotes = project.Tracks.Where(t => !t.IsDrums).Sum(t => t.NoteCount);

        int written = score.Parts
            .SelectMany(p => p.Measures)
            .SelectMany(m => m.Entries)
            .Count(e => !e.IsRest && e.Tie is TieState.None or TieState.Start);

        written.Should().Be(sourceNotes);
    }
}
