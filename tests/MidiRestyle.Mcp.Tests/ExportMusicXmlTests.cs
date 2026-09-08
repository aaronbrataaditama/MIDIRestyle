using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;
using ModelContextProtocol.Protocol;
using Note = Melanchall.DryWetMidi.Interaction.Note;

namespace MidiRestyle.Mcp.Tests;

public sealed class ExportMusicXmlTests : IDisposable
{
    private const int Ppqn = 480;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "midirestyle-mxl-" + Guid.NewGuid().ToString("N"));

    public ExportMusicXmlTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Major() => MidiFixtures.Write(Path.Combine(_dir, "major.mid"), MidiFixtures.CMajorNotes);

    private static Dictionary<string, object?> Args(string input, params (string Key, object? Value)[] more)
    {
        var args = new Dictionary<string, object?> { ["inputPath"] = input, ["targetScaleId"] = "middleeast.arabic.maqam-rast" };
        foreach ((string key, object? value) in more) { args[key] = value; }
        return args;
    }

    /// <summary>
    /// One named track on one channel, with every onset placed by the caller. <paramref name="jitter"/>
    /// of zero writes them exactly; anything else nudges each one deterministically, which is the input
    /// class CLAUDE.md warns the measure-length assertion cannot fail without.
    /// </summary>
    private string WriteTrack(
        string file, string trackName, int? program, (long Start, long Length, int Midi)[] notes,
        int jitter = 0, int channel = 0)
    {
        string path = Path.Combine(_dir, file);
        Random? random = jitter == 0 ? null : new Random(jitter);

        var conductor = new TrackChunk(new TimeSignatureEvent(4, 4), new SetTempoEvent(500_000));
        var chunk = new TrackChunk(new SequenceTrackNameEvent(trackName));
        if (program is { } number)
        {
            chunk.Events.Add(new ProgramChangeEvent((SevenBitNumber)number) { Channel = (FourBitNumber)channel });
        }

        using (TimedObjectsManager<Note> manager = chunk.ManageNotes())
        {
            foreach ((long start, long length, int midi) in notes)
            {
                long nudgedStart = random is null ? start : Math.Max(0, start + random.Next(-jitter, jitter + 1));
                long nudgedLength = random is null ? length : Math.Max(30, length + random.Next(-jitter, jitter + 1));
                manager.Objects.Add(new Note((SevenBitNumber)midi, nudgedLength, nudgedStart)
                {
                    Channel = (FourBitNumber)channel,
                    Velocity = (SevenBitNumber)90,
                });
            }
        }

        var midiFile = new MidiFile(conductor, chunk) { TimeDivision = new TicksPerQuarterNoteTimeDivision(Ppqn) };
        midiFile.Write(path, overwriteFile: true, format: MidiFileFormat.MultiTrack);
        return path;
    }

    /// <summary>Both hands of a keyboard part: a chord, a tie over a barline, triplets, a bass line.</summary>
    private string WritePiano(string file, int jitter = 0) => WriteTrack(file, "Piano", program: 0,
        [
            (0, 480, 60), (0, 480, 64), (0, 480, 67),
            (480, 240, 69), (720, 240, 71),
            (960, 160, 72), (1120, 160, 74), (1280, 160, 76),
            (1440, 960, 77),
            (0, 960, 36), (960, 960, 43), (1920, 960, 41), (2880, 960, 40),
            (2400, 480, 72), (2880, 480, 71), (3360, 480, 69),
        ],
        jitter);

    // ---------------------------------------------------------------------------------------------
    // The happy path
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task RastExportsAScoreBesideTheInput()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        JsonElement report = await host.CallJsonAsync("export_musicxml", Args(input, ("detectTuplets", false)));

        string output = Path.Combine(_dir, "major.middleeast.arabic.maqam-rast.musicxml");
        report.GetProperty("outputPath").GetString().Should().Be(output);
        report.GetProperty("measureCount").GetInt32().Should().BeGreaterThan(0);
        report.GetProperty("parts").GetArrayLength().Should().Be(1);

        File.ReadAllText(output).Should().StartWith("<?xml");
        File.ReadAllBytes(output).Take(3).Should().NotEqual([(byte)0xEF, 0xBB, 0xBF], "UTF-8 without a BOM");
        XDocument.Parse(File.ReadAllText(output)).Root!.Name.LocalName.Should().Be("score-partwise");
    }

    /// <summary>
    /// The tool must go through <see cref="NotationBuilder"/> and <see cref="MusicXmlExporter"/> and
    /// nothing else - a second path would eventually disagree with the staff view. The reference is
    /// built from literals rather than through the resolver, so the resolver's defaults are pinned too.
    /// </summary>
    [Fact]
    public async Task OutputIsByteIdenticalToTheCoreNotationPipelineBuiltFromLiterals()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WritePiano("piano.mid");
        string output = Path.Combine(_dir, "piano.musicxml");

        await host.CallJsonAsync("export_musicxml", Args(input,
            ("targetTonic", "D4"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4"),
            ("outputPath", output)));

        ScaleLibrary library = TestLibrary.Load();
        MidiProject project = MidiFileLoader.Load(input);
        var settings = new RestyleSettings
        {
            TargetScale = library.Find("middleeast.arabic.maqam-rast")!,
            TargetTonic = Pitch.FromMidi(62),
            TonicSpelling = new TonicSpelling(1, 0),
            SourceScale = library.Find("europe.churchmodes.ionian")!,
            SourceTonic = Pitch.FromMidi(60),
        };
        RestyleResult result = RestyleEngine.Restyle(project, settings);
        NotationScore score = NotationBuilder.Build(project, result.Tracks, settings, QuantiseOptions.Default);
        byte[] expected = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(MusicXmlExporter.ToXml(score));

        File.ReadAllBytes(output).Should().Equal(expected, "the tool must not build a score of its own");
    }

    // ---------------------------------------------------------------------------------------------
    // The report's fields
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Every field of <see cref="MusicXmlReport"/> against a value only it can hold. Three adjacent
    /// <c>IReadOnlyList&lt;string&gt;</c> fields - parts, diagnostics, warnings - can be transposed
    /// without the compiler noticing, so each is pinned to content the other two cannot contain.
    /// </summary>
    [Fact]
    public async Task ThePartsDiagnosticsAndWarningsListsEachHoldOnlyTheirOwnContent()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        // Five overlapping notes on one staff: past four voices the builder raises its readability
        // diagnostic, and every note is still written. The onsets are staggered deliberately - five
        // notes struck at the same instant for the same length are one chord in one voice, not five
        // voices, so an aligned fixture raises nothing at all.
        string input = WriteTrack("crowded.mid", "PinnedPartName", program: null,
            [(0, 1920, 60), (120, 1800, 62), (240, 1680, 64), (360, 1560, 65), (480, 1440, 67)]);

        JsonElement report = await host.CallJsonAsync("export_musicxml", Args(input,
            ("targetTonic", "C4"), ("strategy", "nearestPitch"), ("sourceScaleId", "europe.churchmodes.ionian")));

        string[] parts = [.. report.GetProperty("parts").EnumerateArray().Select(p => p.GetString()!)];
        string[] diagnostics = [.. report.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetString()!)];
        string[] warnings = [.. report.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!)];

        parts.Should().Equal(["PinnedPartName"], "the part list is the score's part names and nothing else");
        diagnostics.Should().ContainSingle().Which.Should().Contain("four simultaneous voices");
        warnings.Should().ContainSingle().Which.Should().Contain("nearestPitch does not use a source scale");

        // ...and neither of the other two lists may carry it, or a transposition passes.
        parts.Should().NotContain(p =>
            p.Contains("voices", StringComparison.Ordinal) || p.Contains("nearestPitch", StringComparison.Ordinal));
        diagnostics.Should().NotContain(d =>
            d.Contains("PinnedPartName", StringComparison.Ordinal) || d.Contains("nearestPitch", StringComparison.Ordinal));
        warnings.Should().NotContain(w =>
            w.Contains("PinnedPartName", StringComparison.Ordinal) || w.Contains("four simultaneous", StringComparison.Ordinal));

        report.GetProperty("measureCount").GetInt32().Should().Be(1);
        report.GetProperty("outputPath").GetString().Should().EndWith(".musicxml");
        report.GetProperty("resolved").GetProperty("targetScaleId").GetString().Should().Be("middleeast.arabic.maqam-rast");
    }

    [Fact]
    public async Task MeasureCountAndPartsCountWhatTheScoreActuallyHolds()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WriteTrack("threebars.mid", "Lead", program: null,
            [(0, 480, 60), (1920, 480, 62), (3840, 480, 64)]);

        JsonElement report = await host.CallJsonAsync("export_musicxml", Args(input,
            ("targetTonic", "C4"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4")));

        report.GetProperty("measureCount").GetInt32().Should().Be(3, "three 4/4 bars carry the three notes");
        report.GetProperty("parts").EnumerateArray().Select(p => p.GetString()).Should().Equal(["Lead"]);
    }

    /// <summary>
    /// Notes the restyle lost are named in the report rather than left for the agent to notice by
    /// counting noteheads. The score is still written - the warning is how the loss is surfaced.
    /// </summary>
    [Fact]
    public async Task NotesLostInTheRestyleAreReportedAsAWarning()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WriteTrack("lossy.mid", "Lead", program: null,
            [(0, 480, 60), (480, 480, 61), (960, 480, 63), (1440, 480, 66)]);
        string output = Path.Combine(_dir, "lossy.musicxml");

        JsonElement report = await host.CallJsonAsync("export_musicxml", Args(input,
            ("targetTonic", "C4"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4"),
            ("nonScaleNotes", "drop"), ("outputPath", output)));

        report.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().Contain(w => w!.Contains("3 dropped (not in source scale)", StringComparison.Ordinal));
        File.Exists(output).Should().BeTrue("a lossy restyle still produces a score");
    }

    // ---------------------------------------------------------------------------------------------
    // Notatability
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ANonNotatableScaleIsRefusedWithTheReasonAndTheAlternative()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        CallToolResult result = await host.CallAsync("export_musicxml", new()
        {
            ["inputPath"] = input,
            ["targetScaleId"] = "seasia.gamelan.slendro-kanyut-mesem",
        });

        result.IsError.Should().Be(true);
        ToolResults.TextOf(result).Should().ContainAll("staff", "restyle_midi");
        Directory.EnumerateFiles(_dir, "*.musicxml").Should().BeEmpty("a refused scale writes nothing");
        Directory.EnumerateFiles(_dir, "*.tmp-*").Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // The write path
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ExtensionMustBeMusicXmlOrXml()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        CallToolResult refused = await host.CallAsync("export_musicxml",
            Args(input, ("outputPath", Path.Combine(_dir, "score.mid"))));
        refused.IsError.Should().Be(true);
        ToolResults.TextOf(refused).Should().Contain(".musicxml");

        JsonElement plainXml = await host.CallJsonAsync("export_musicxml",
            Args(input, ("outputPath", Path.Combine(_dir, "score.xml"))));
        plainXml.GetProperty("outputPath").GetString().Should().EndWith("score.xml", ".xml is the other accepted extension");
    }

    [Fact]
    public async Task ExistingOutputNeedsOverwrite()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();
        string output = Path.Combine(_dir, "twice.musicxml");

        await host.CallJsonAsync("export_musicxml", Args(input, ("outputPath", output)));
        byte[] first = File.ReadAllBytes(output);

        CallToolResult second = await host.CallAsync("export_musicxml", Args(input, ("outputPath", output)));
        second.IsError.Should().Be(true);
        ToolResults.TextOf(second).Should().Contain("overwrite");
        File.ReadAllBytes(output).Should().Equal(first, "a refused overwrite leaves the good file alone");

        CallToolResult third = await host.CallAsync("export_musicxml", Args(input, ("outputPath", output), ("overwrite", true)));
        third.IsError.Should().NotBe(true);
    }

    /// <summary>
    /// The path is judged by <see cref="OutputPathPolicy.ValidateOutputPath"/> BEFORE
    /// <see cref="OutputPathPolicy.WriteAtomically"/> runs. The discriminator is a folder that exists
    /// and is writable, so a write reaching it would have succeeded: the file's absence - and the
    /// absence of even the atomic write's temp file - is the proof the guard ran first.
    /// </summary>
    [Fact]
    public async Task TheOutputPathIsValidatedBeforeAnythingIsWritten()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        string scalesFolder = Path.Combine(TestLibrary.Probe.ResolveWritableRoot().Root, ScaleLibraryLoader.ScalesFolderName);
        Directory.Exists(scalesFolder).Should().BeTrue("the leg only proves ordering if a write there would have succeeded");
        string inScales = Path.Combine(scalesFolder, "x.musicxml");

        CallToolResult refused = await host.CallAsync("export_musicxml", Args(input, ("outputPath", inScales)));

        refused.IsError.Should().Be(true);
        ToolResults.TextOf(refused).Should().Contain("refused");
        File.Exists(inScales).Should().BeFalse("the write must never have been attempted");
        Directory.GetFiles(scalesFolder, "*.tmp-*").Should().BeEmpty("not even the atomic write's temp file");
    }

    /// <summary>
    /// The path is canonicalised once, and the canonical form is what is written and what is reported -
    /// so an agent can hand the reported path straight back without normalising it itself.
    /// </summary>
    [Fact]
    public async Task TheReportedPathIsTheCanonicalOneTheFileWasWrittenTo()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        string canonical = Path.Combine(_dir, "canon.musicxml");
        string roundabout = Path.Combine(_dir, "sub", "..", "canon.musicxml");

        JsonElement report = await host.CallJsonAsync("export_musicxml", Args(input, ("outputPath", roundabout)));

        report.GetProperty("outputPath").GetString().Should().Be(canonical, "the raw string is normalised once, not echoed back");
        File.Exists(canonical).Should().BeTrue();
        Directory.GetFiles(_dir, "*.tmp-*").Should().BeEmpty();
    }

    [Fact]
    public async Task MissingOutputDirectoriesAreAnError()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        CallToolResult noDir = await host.CallAsync("export_musicxml",
            Args(input, ("outputPath", Path.Combine(_dir, "missing", "x.musicxml"))));

        noDir.IsError.Should().Be(true);
        ToolResults.TextOf(noDir).Should().Contain("does not exist");
    }

    /// <summary>
    /// A drums-only file notates to nothing at all, and MusicXML has no way to say "no parts". The
    /// exporter refuses; that refusal must reach the agent as a message, and must leave no file.
    /// </summary>
    [Fact]
    public async Task AFileWithNothingToNotateIsRefusedRatherThanWrittenEmpty()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = MidiFixtures.Write(Path.Combine(_dir, "drums.mid"), MidiFixtures.DrumsOnlyNotes);
        string output = Path.Combine(_dir, "drums.musicxml");

        CallToolResult result = await host.CallAsync("export_musicxml",
            Args(input, ("targetTonic", "C4"), ("strategy", "nearestPitch"), ("outputPath", output)));

        result.IsError.Should().Be(true);
        ToolResults.TextOf(result).Should().Contain("no parts");
        ToolResults.TextOf(result).Should().NotContain("Exception");
        File.Exists(output).Should().BeFalse();
        Directory.GetFiles(_dir, "*.tmp-*").Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // The notation invariants, read off the file the tool actually wrote
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// CLAUDE.md's load-bearing rule: a voice short by one division makes readers reject the file, and
    /// long, every later measure is displaced. Asserted over JITTERED input, because onsets on exact
    /// tick boundaries are the one input class that cannot fail - a span landing on an exact multiple
    /// of a sixty-fourth is always writable, so the decomposer never rounds up.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public async Task EveryVoiceInTheWrittenFileAccountsForExactlyItsMeasureLength(int jitter)
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WritePiano($"jitter{jitter}.mid", jitter);
        string output = Path.Combine(_dir, $"jitter{jitter}.musicxml");

        await host.CallJsonAsync("export_musicxml", Args(input,
            ("targetTonic", "C4"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4"),
            ("outputPath", output)));

        XDocument document = XDocument.Parse(File.ReadAllText(output));
        document.Descendants("measure").Should().NotBeEmpty();

        foreach (XElement part in document.Descendants("part"))
        {
            foreach (XElement measure in part.Elements("measure"))
            {
                long cursor = 0;
                long high = 0;

                foreach (XElement element in measure.Elements())
                {
                    long value = long.TryParse(element.Element("duration")?.Value ?? element.Value, out long parsed) ? parsed : 0;
                    cursor += element.Name.LocalName switch
                    {
                        "note" when element.Element("chord") is null => value,
                        "backup" => -value,
                        "forward" => value,
                        _ => 0,
                    };
                    high = Math.Max(high, cursor);
                }

                high.Should().Be(4 * Ppqn, $"measure {measure.Attribute("number")?.Value} is 4/4 at {Ppqn} divisions");
            }
        }
    }

    /// <summary>
    /// No key signature is correct for a restyled maqam, so <c>fifths</c> is 0 and every accidental is
    /// written out. <c>alter</c> carries the quantised half-accidental; the leftover comma is dropped,
    /// which is why no value has a fractional part other than a half.
    /// </summary>
    [Fact]
    public async Task TheScoreCarriesNoKeySignatureAndSpellsEveryAccidentalOut()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WritePiano("accidentals.mid");
        string output = Path.Combine(_dir, "accidentals.musicxml");

        await host.CallJsonAsync("export_musicxml", Args(input,
            ("targetTonic", "C4"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4"),
            ("outputPath", output)));

        XDocument document = XDocument.Parse(File.ReadAllText(output));

        document.Descendants("fifths").Select(f => f.Value).Should().AllBe("0", "a maqam is neither major nor minor");
        document.Descendants("accidental").Should().NotBeEmpty("accidentals are explicit, since no key signature carries them");

        double[] alters = [.. document.Descendants("alter").Select(a => double.Parse(a.Value, CultureInfo.InvariantCulture))];
        alters.Should().NotBeEmpty();
        alters.Should().Contain(a => Math.Abs(a % 1) > 0.1, "Rast's neutral degrees need a half-flat");
        alters.Should().OnlyContain(a => Math.Abs((a * 2) - Math.Round(a * 2)) < 1e-9,
            "MusicXML carries the quantised half-accidental only; the residual comma is deliberately dropped");
    }

    /// <summary>
    /// A keyboard part straddling middle C exports as a grand staff, and a MusicXML voice belongs to
    /// exactly one staff for the whole part - so the two staves' voice numbers must not overlap.
    /// </summary>
    [Fact]
    public async Task AGrandStaffPartGivesItsTwoStavesDisjointVoiceNumbers()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WritePiano("grand.mid");
        string output = Path.Combine(_dir, "grand.musicxml");

        await host.CallJsonAsync("export_musicxml", Args(input,
            ("targetTonic", "C4"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4"),
            ("outputPath", output)));

        XDocument document = XDocument.Parse(File.ReadAllText(output));
        document.Descendants("staves").Select(s => s.Value).Should().Equal(["2"], "the left hand runs well below middle C");

        Dictionary<string, HashSet<string>> byStaff = document.Descendants("note")
            .Where(n => n.Element("staff") is not null && n.Element("voice") is not null)
            .GroupBy(n => n.Element("staff")!.Value, n => n.Element("voice")!.Value)
            .ToDictionary(g => g.Key, g => g.Distinct().ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        byStaff.Should().ContainKeys("1", "2");
        byStaff["1"].Overlaps(byStaff["2"]).Should().BeFalse("a voice belongs to exactly one staff for the whole part");
    }

    /// <summary>
    /// <c>detectTuplets</c> must actually reach the quantiser. Triplet material comes back with a
    /// <c>time-modification</c> when it is on, and as straight values when it is off.
    /// </summary>
    [Fact]
    public async Task DetectTupletsDecidesWhetherTripletsAreWrittenAsTuplets()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WriteTrack("triplets.mid", "Flute", program: null,
        [
            (0, 160, 60), (160, 160, 62), (320, 160, 64),
            (480, 160, 65), (640, 160, 67), (800, 160, 69),
            (960, 160, 71), (1120, 160, 72), (1280, 160, 71),
            (1440, 160, 69), (1600, 160, 67), (1760, 160, 65),
        ]);

        string on = Path.Combine(_dir, "tuplets-on.musicxml");
        string off = Path.Combine(_dir, "tuplets-off.musicxml");
        (string Path, bool Detect)[] runs = [(on, true), (off, false)];

        foreach ((string path, bool detect) in runs)
        {
            await host.CallJsonAsync("export_musicxml", Args(input,
                ("targetTonic", "C4"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4"),
                ("outputPath", path), ("detectTuplets", detect)));
        }

        XDocument.Parse(File.ReadAllText(on)).Descendants("time-modification")
            .Should().NotBeEmpty("three even onsets to a beat is a triplet");
        XDocument.Parse(File.ReadAllText(off)).Descendants("time-modification")
            .Should().BeEmpty("with detection off the same beat is spelled straight");
    }

    // ---------------------------------------------------------------------------------------------
    // Crash safety and advertising
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Nothing an agent can send may escape as an exception. Each case is pinned to wording only OUR
    /// refusal produces: the SDK turns an unhandled throw into an error result too, so <c>IsError</c>
    /// alone cannot tell a refusal from a crash - and its generic wrapper text does not contain the
    /// word "Exception" either, so that assertion cannot stand in for a per-case one.
    /// </summary>
    [Fact]
    public async Task NothingAnAgentCanSendThrowsOutOfTheTool()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        (Dictionary<string, object?> Args, string Expected)[] hostile =
        [
            // Path.GetFullPath THROWS on these two rather than returning anything, and validation runs
            // before the tool's try block - so without the shared guard they escape as an SDK-wrapped
            // exception rather than as a refusal.
            (Args(input, ("outputPath", "C:\\music\\x\u0000y.musicxml")), "is not a usable path"),
            (Args(input, ("outputPath", "C:\\music\\" + new string('x', 40_000) + ".musicxml")), "is not a usable path"),
            (Args(input, ("outputPath", "")), "must be an absolute path"),
            (Args(input, ("outputPath", "..\\escape.musicxml")), "must be an absolute path"),
            (Args("C:\\music\\x\u0000y.mid"), "was not found"),
            (Args(input, ("targetScaleId", "no.such.scale")), "is not a known scale id"),
            (Args(input, ("exclude", new object?[] { null })), "exclude entries need track"),
            (Args(input, ("toleranceCents", "NaN")), "toleranceCents"),
            (Args(input, ("strategy", "sideways")), "strategy 'sideways' is not valid"),
        ];

        foreach ((Dictionary<string, object?> args, string expected) in hostile)
        {
            CallToolResult result = await host.CallAsync("export_musicxml", args);
            string text = ToolResults.TextOf(result);
            result.IsError.Should().Be(true, $"'{JsonSerializer.Serialize(args)}' must come back as a tool error");
            text.Should().Contain(expected, $"'{JsonSerializer.Serialize(args)}' must be refused in our own words");
            text.Should().NotContain("Exception", "an exception message is a crash that survived, not a refusal");
        }

        JsonElement after = await host.CallJsonAsync("export_musicxml",
            Args(input, ("outputPath", Path.Combine(_dir, "after.musicxml"))));
        after.GetProperty("measureCount").GetInt32().Should().BeGreaterThan(0, "the server is still serving after every refusal");
    }

    [Fact]
    public async Task TheToolIsAdvertisedAsDestructiveAndNotReadOnly()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        var tool = (await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Single(t => t.Name == "export_musicxml");

        tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
        tool.ProtocolTool.Annotations.DestructiveHint.Should().BeTrue();

        JsonElement properties = tool.JsonSchema.GetProperty("properties");
        properties.GetProperty("inputPath").GetProperty("description").GetString().Should().Contain("Absolute");
        properties.TryGetProperty("outputPath", out _).Should().BeTrue();
        properties.TryGetProperty("overwrite", out _).Should().BeTrue();
        properties.TryGetProperty("detectTuplets", out _).Should().BeTrue();
    }
}
