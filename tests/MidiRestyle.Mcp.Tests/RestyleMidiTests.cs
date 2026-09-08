using System.Text.Json;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;
using ModelContextProtocol.Protocol;
using Note = Melanchall.DryWetMidi.Interaction.Note;

namespace MidiRestyle.Mcp.Tests;

public sealed class RestyleMidiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "midirestyle-restyle-" + Guid.NewGuid().ToString("N"));

    public RestyleMidiTests() => Directory.CreateDirectory(_dir);

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

    /// <summary>A file whose notes sit where the test puts them, rather than one per beat.</summary>
    private string WriteTimed(string name, params (int Note, int Channel, long Ticks)[] notes)
    {
        string path = Path.Combine(_dir, name);
        var file = new MidiFile(new TrackChunk(new SetTempoEvent(500_000)))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

        foreach (IGrouping<int, (int Note, int Channel, long Ticks)> group in notes.GroupBy(n => n.Channel).OrderBy(g => g.Key))
        {
            var chunk = new TrackChunk();
            using (TimedObjectsManager<Note> manager = chunk.ManageNotes())
            {
                foreach ((int note, int channel, long ticks) in group)
                {
                    manager.Objects.Add(new Note((SevenBitNumber)note, 480, ticks)
                    {
                        Channel = (FourBitNumber)channel,
                        Velocity = (SevenBitNumber)90,
                    });
                }
            }

            file.Chunks.Add(chunk);
        }

        file.Write(path, overwriteFile: true, format: MidiFileFormat.MultiTrack);
        return path;
    }

    /// <summary>One chunk per entry, so track index - not channel - is what separates the demands.</summary>
    private string WriteTracks(string name, params (int Channel, int NoteCount)[] tracks)
    {
        string path = Path.Combine(_dir, name);
        var file = new MidiFile(new TrackChunk(new SetTempoEvent(500_000)))
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

        foreach ((int channel, int count) in tracks)
        {
            var chunk = new TrackChunk();
            using (TimedObjectsManager<Note> manager = chunk.ManageNotes())
            {
                for (int i = 0; i < count; i++)
                {
                    manager.Objects.Add(new Note((SevenBitNumber)(60 + (i % 5)), 480, i * 480L)
                    {
                        Channel = (FourBitNumber)channel,
                        Velocity = (SevenBitNumber)90,
                    });
                }
            }

            file.Chunks.Add(chunk);
        }

        file.Write(path, overwriteFile: true, format: MidiFileFormat.MultiTrack);
        return path;
    }

    [Fact]
    public async Task OutputIsByteIdenticalToTheCorePipelineBuiltFromLiterals()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();
        string output = Path.Combine(_dir, "out.mid");

        JsonElement report = await host.CallJsonAsync("restyle_midi",
            Args(input, ("targetTonic", "D4"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4"), ("outputPath", output)));

        // Reference: Core called directly, settings from literals - never through the resolver.
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
        using var expected = new MemoryStream();
        MidiFileExporter.Export(result, expected, ChannelAllocator.Allocate(result)).Success.Should().BeTrue();

        File.ReadAllBytes(output).Should().Equal(expected.ToArray());
        report.GetProperty("outputPath").GetString().Should().Be(output);
        report.GetProperty("channels").GetProperty("used").GetInt32().Should().Be(2, "Rast is two bend clusters");
        report.GetProperty("resolved").GetProperty("keyDetectionUsed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task DefaultOutputSitsBesideTheInputAndReportsTheDefaultsItChose()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        JsonElement report = await host.CallJsonAsync("restyle_midi", Args(input));

        string expectedPath = Path.Combine(_dir, "major.middleeast.arabic.maqam-rast.mid");
        report.GetProperty("outputPath").GetString().Should().Be(expectedPath);
        File.Exists(expectedPath).Should().BeTrue();
        MidiFileLoader.TryLoad(expectedPath, out _, out string? err).Should().BeTrue(err);

        JsonElement resolved = report.GetProperty("resolved");
        resolved.GetProperty("keyDetectionUsed").GetBoolean().Should().BeTrue();
        resolved.GetProperty("targetTonic").GetProperty("name").GetString().Should().Be("C4");
        resolved.GetProperty("sourceScaleId").GetString().Should().Be(RestyleDefaults.MajorSourceScaleId);
        report.GetProperty("tally").GetProperty("merged").GetInt32().Should().Be(0);

        // Every field of the fidelity block, each against a value only it can hold.
        JsonElement fidelity = report.GetProperty("fidelity");
        fidelity.GetProperty("badge").GetString().Should().Be("approximate", "Rast's neutral degrees sit 50c off 12-TET");
        fidelity.GetProperty("maxDeviationCents").GetDouble().Should().Be(50);
        fidelity.GetProperty("worstDegreeIndex").GetInt32().Should().Be(2, "degree 2 is the first neutral one, at 350c");

        report.GetProperty("notesRestyled").GetInt32().Should().Be(13);
    }

    [Fact]
    public async Task ExistingOutputNeedsOverwrite()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();
        string output = Path.Combine(_dir, "twice.mid");

        await host.CallJsonAsync("restyle_midi", Args(input, ("outputPath", output)));
        CallToolResult second = await host.CallAsync("restyle_midi", Args(input, ("outputPath", output)));
        second.IsError.Should().Be(true);
        ToolResults.TextOf(second).Should().Contain("overwrite");

        CallToolResult third = await host.CallAsync("restyle_midi", Args(input, ("outputPath", output), ("overwrite", true)));
        third.IsError.Should().NotBe(true);
    }

    [Fact]
    public async Task InPlaceOverwriteOfTheInputWorksBecauseTheInputIsReadFirst()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        CallToolResult refused = await host.CallAsync("restyle_midi", Args(input, ("outputPath", input)));
        refused.IsError.Should().Be(true);

        JsonElement report = await host.CallJsonAsync("restyle_midi", Args(input, ("outputPath", input), ("overwrite", true)));
        report.GetProperty("outputPath").GetString().Should().Be(input);
        MidiFileLoader.TryLoad(input, out MidiProject? reloaded, out _).Should().BeTrue();
        reloaded!.TotalNoteCount.Should().Be(13);
    }

    /// <summary>
    /// The output path is checked by <see cref="OutputPathPolicy.ValidateOutputPath"/> BEFORE
    /// <see cref="OutputPathPolicy.WriteAtomically"/> runs, and each leg turns on a discriminator that
    /// separates the two orders rather than merely showing that a bad path is an error.
    /// <list type="bullet">
    /// <item>The protected leg names a folder that exists and is writable, so a write reaching it would
    /// have succeeded - the file's absence is the proof the guard ran first.</item>
    /// <item>The in-place leg is refused by both, in different words: the validator says "is the input
    /// file", the writer says "already exists". Only the validator's wording may come back.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task TheOutputPathIsValidatedBeforeAnythingIsWritten()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();
        byte[] before = File.ReadAllBytes(input);

        string scalesFolder = Path.Combine(TestLibrary.Probe.ResolveWritableRoot().Root, ScaleLibraryLoader.ScalesFolderName);
        Directory.Exists(scalesFolder).Should().BeTrue("the leg only proves ordering if a write there would have succeeded");
        string inScales = Path.Combine(scalesFolder, "x.mid");

        CallToolResult refused = await host.CallAsync("restyle_midi", Args(input, ("outputPath", inScales)));
        refused.IsError.Should().Be(true);
        ToolResults.TextOf(refused).Should().Contain("refused");
        File.Exists(inScales).Should().BeFalse("the write must never have been attempted");
        Directory.GetFiles(scalesFolder, "*.tmp-*").Should().BeEmpty("not even the atomic write's temp file");

        CallToolResult inPlace = await host.CallAsync("restyle_midi", Args(input, ("outputPath", input)));
        inPlace.IsError.Should().Be(true);
        ToolResults.TextOf(inPlace).Should().Contain("is the input file");
        ToolResults.TextOf(inPlace).Should().NotContain("already exists", "that is the writer's refusal, which must not be reached");
        File.ReadAllBytes(input).Should().Equal(before, "the input is untouched by a refused call");
    }

    [Fact]
    public async Task RefusedExtensionsAndMissingDirectoriesAreErrors()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        string wavPath = Path.Combine(_dir, "x.wav");
        CallToolResult wav = await host.CallAsync("restyle_midi", Args(input, ("outputPath", wavPath)));
        wav.IsError.Should().Be(true);
        ToolResults.TextOf(wav).Should().Contain(".mid");
        File.Exists(wavPath).Should().BeFalse();

        string missingDir = Path.Combine(_dir, "missing", "x.mid");
        CallToolResult noDir = await host.CallAsync("restyle_midi", Args(input, ("outputPath", missingDir)));
        noDir.IsError.Should().Be(true);
        ToolResults.TextOf(noDir).Should().Contain("does not exist");
    }

    [Fact]
    public async Task DrumsOnlySucceedsWithAWarningNotSilently()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = MidiFixtures.Write(Path.Combine(_dir, "drums.mid"), MidiFixtures.DrumsOnlyNotes);

        JsonElement report = await host.CallJsonAsync("restyle_midi", Args(input, ("targetTonic", "C4"), ("strategy", "nearest_pitch")));

        report.GetProperty("notesRestyled").GetInt32().Should().Be(0);
        report.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().Contain(w => w!.Contains("No notes will be restyled", StringComparison.Ordinal));
        report.GetProperty("channels").GetProperty("used").GetInt32().Should().Be(0, "nothing pitched is being restyled");
    }

    /// <summary>
    /// Channel 10 is percussion whatever the request says: the drum notes come back with their note
    /// numbers and their channel intact, while the pitched part beside them is remapped.
    /// </summary>
    [Fact]
    public async Task DrumsAreNeverRemappedHoweverTheRequestIsWritten()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WriteTimed("mixed.mid",
            (60, 0, 0), (64, 0, 480), (67, 0, 960),
            (36, 9, 0), (38, 9, 480), (42, 9, 960));
        string output = Path.Combine(_dir, "mixed.out.mid");

        JsonElement report = await host.CallJsonAsync("restyle_midi",
            Args(input, ("targetTonic", "C4"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4"), ("outputPath", output)));

        report.GetProperty("notesRestyled").GetInt32().Should().Be(3, "only the pitched row is restyled");

        MidiProject written = MidiFileLoader.Load(output);
        TrackInfo drums = written.Tracks.Single(t => t.Channel == 9);
        drums.Notes.Select(n => n.Pitch.MidiNote).Should().Equal([36, 38, 42], "a note number picks which drum is struck");
        written.Tracks.Where(t => t.Channel != 9).Sum(t => t.NoteCount).Should().Be(3);
    }

    /// <summary>
    /// Each tally field against a count only it has, so a transposition of the four adjacent ints
    /// cannot pass. Drops are counted here; the two collision counters are pinned by the sibling test,
    /// because a run uses one collision policy or the other and never both.
    /// </summary>
    [Fact]
    public async Task TheTallyCountsEachKindOfLossUnderItsOwnName()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WriteTimed("lossy.mid",
            (60, 0, 0), (62, 0, 480),
            (61, 0, 960), (63, 0, 1440), (66, 0, 1920),
            (84, 0, 2400), (96, 0, 2880));

        JsonElement report = await host.CallJsonAsync("restyle_midi", Args(input,
            ("targetTonic", "C8"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4"),
            ("nonScaleNotes", "drop"), ("range", "drop")));

        JsonElement tally = report.GetProperty("tally");
        tally.GetProperty("droppedOutOfRange").GetInt32().Should().Be(2, "C6 and C7 map above MIDI 127 from a C8 tonic");
        tally.GetProperty("droppedNotInScale").GetInt32().Should().Be(3, "C#4, D#4 and F#4 are not in C ionian");
        tally.GetProperty("merged").GetInt32().Should().Be(0);
        tally.GetProperty("displaced").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// The two collision counters, each nonzero in exactly one of the two runs. Same file, same
    /// mapping - only the policy differs - so neither field can be a constant and neither can stand in
    /// for the other.
    /// </summary>
    [Fact]
    public async Task MergedAndDisplacedAreCountedUnderTheirOwnPolicies()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WriteTimed("collide.mid", (60, 0, 0), (61, 0, 0));

        JsonElement merge = await host.CallJsonAsync("restyle_midi", Args(input,
            ("targetScaleId", "seasia.gamelan.slendro-kanyut-mesem"), ("targetTonic", "C4"),
            ("strategy", "nearestPitch"), ("collisions", "merge"), ("outputPath", Path.Combine(_dir, "merge.mid"))));

        merge.GetProperty("tally").GetProperty("merged").GetInt32().Should().Be(1, "C#4 snaps onto C4, which is already sounding");
        merge.GetProperty("tally").GetProperty("displaced").GetInt32().Should().Be(0);

        JsonElement displace = await host.CallJsonAsync("restyle_midi", Args(input,
            ("targetScaleId", "seasia.gamelan.slendro-kanyut-mesem"), ("targetTonic", "C4"),
            ("strategy", "nearestPitch"), ("collisions", "displaceOctave"), ("outputPath", Path.Combine(_dir, "displace.mid"))));

        displace.GetProperty("tally").GetProperty("displaced").GetInt32().Should().Be(1);
        displace.GetProperty("tally").GetProperty("merged").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// CLAUDE.md: when the budget does not fit, the tolerance is raised for the WHOLE project, never
    /// per track, and the effective tolerance and worst-case error are reported. Four Slendro parts
    /// want 4 x 5 = 20 channels out of 15, so the ladder climbs to 15c where Slendro is 3 clusters.
    /// </summary>
    [Fact]
    public async Task TheToleranceIsRaisedForTheWholeProjectWhenTheBudgetDoesNotFit()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = WriteTracks("quartet.mid", (0, 5), (1, 5), (2, 5), (3, 5));

        JsonElement report = await host.CallJsonAsync("restyle_midi", Args(input,
            ("targetScaleId", "seasia.gamelan.slendro-kanyut-mesem"), ("targetTonic", "C4"),
            ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4")));

        JsonElement channels = report.GetProperty("channels");
        channels.GetProperty("used").GetInt32().Should().Be(12, "four parts at three clusters each");
        channels.GetProperty("effectiveToleranceCents").GetDouble().Should().Be(15, "the ladder's third rung is the first that fits");
        channels.GetProperty("toleranceWasRaised").GetBoolean().Should().BeTrue();
        channels.GetProperty("worstErrorCents").GetDouble().Should().Be(7.05);
        channels.GetProperty("muted").GetArrayLength().Should().Be(0, "raising the tolerance was enough; nothing had to be muted");

        report.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().Contain(w => w!.Contains("tuning accuracy reduced", StringComparison.Ordinal));
    }

    /// <summary>
    /// Past the end of the ladder the excess is muted, not retuned - and what the report names as muted
    /// is what the written file actually leaves out. The muted row's three ints are deliberately all
    /// different, so a transposition of track/channel/noteCount cannot pass.
    /// </summary>
    [Fact]
    public async Task ExcessTrackChannelsAreMutedRatherThanGivenTheirOwnTuning()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        (int Channel, int NoteCount)[] tracks = [.. Enumerable.Repeat((0, 5), 15), (3, 1)];
        string input = WriteTracks("crowd.mid", tracks);
        string output = Path.Combine(_dir, "crowd.out.mid");

        JsonElement report = await host.CallJsonAsync("restyle_midi", Args(input,
            ("targetTonic", "C4"), ("sourceScaleId", "europe.churchmodes.ionian"), ("sourceTonic", "C4"),
            ("outputPath", output)));

        JsonElement channels = report.GetProperty("channels");
        channels.GetProperty("used").GetInt32().Should().Be(15, "one port carries 15 pitched channels");
        channels.GetProperty("effectiveToleranceCents").GetDouble().Should().Be(50);
        channels.GetProperty("worstErrorCents").GetDouble().Should().Be(25);
        channels.GetProperty("toleranceWasRaised").GetBoolean().Should().BeTrue();

        JsonElement muted = channels.GetProperty("muted").EnumerateArray().Single();
        muted.GetProperty("track").GetInt32().Should().Be(16);
        muted.GetProperty("channel").GetInt32().Should().Be(3);
        muted.GetProperty("noteCount").GetInt32().Should().Be(1, "the thinnest part is the one a listener misses least");

        MidiProject written = MidiFileLoader.Load(output);
        written.TotalNoteCount.Should().Be(75, "the muted row's note is absent from the file, exactly as from preview");

        report.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().Contain(w => w!.Contains("muted", StringComparison.Ordinal));
    }

    /// <summary>
    /// The resolver reports every field problem together; this layer must pass that through whole.
    /// Three bad fields, three lines - truncating to the first would cost the agent three round trips.
    /// </summary>
    [Fact]
    public async Task FieldErrorsFromTheResolverComeBackAsOneErrorResult()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        CallToolResult result = await host.CallAsync("restyle_midi", Args("relative.mid", ("targetTonic", "H4"), ("toleranceCents", 99.0)));

        result.IsError.Should().Be(true);
        string text = ToolResults.TextOf(result);
        text.Should().ContainAll("absolute", "H4", "50");
        text.Should().Contain("3 problems", "all three are reported together, not one per round trip");
        text.Should().Contain("targetTonic");
        text.Should().Contain("toleranceCents");
    }

    /// <summary>
    /// Nothing an agent can send may escape as an exception, including the two shapes the resolver was
    /// hardened against in <c>9f2a984</c> - a null <c>exclude</c> entry and a non-finite tolerance.
    /// </summary>
    /// <remarks>
    /// Each case is pinned to wording that only OUR refusal produces, never to "it was an error". The
    /// SDK turns an unhandled throw into an error result too, so <c>IsError</c> alone cannot tell a
    /// handled refusal from a <c>NullReferenceException</c> that took the call down - the message can.
    /// The last call proves the session is still alive after all of them.
    /// </remarks>
    [Fact]
    public async Task NothingAnAgentCanSendThrowsOutOfTheTool()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Major();

        (Dictionary<string, object?> Args, string Expected)[] hostile =
        [
            (Args(input, ("exclude", new object?[] { null })), "exclude entries need track"),
            (Args(input, ("exclude", new object?[] { new { track = -1, channel = 99 } })), "exclude entries need track"),
            (Args(input, ("exclude", new object?[] { new { track = 7, channel = 2 } })), "which the file does not have"),
            // JSON has no NaN literal, so a bare double.NaN cannot even be serialised onto the wire.
            // The string form is the reachable route: McpJson.Options is Web-flavoured, so
            // AllowReadingFromString binds "NaN" to a double? of NaN.
            (Args(input, ("toleranceCents", "NaN")), "toleranceCents"),
            (Args(input, ("strategy", "sideways")), "strategy 'sideways' is not valid"),
            (Args(input, ("outputPath", "")), "must be an absolute path"),
            (Args(input, ("outputPath", "..\\escape.mid")), "must be an absolute path"),
            (Args(input, ("targetScaleId", "")), "targetScaleId is required"),
            (Args(input, ("targetScaleId", "no.such.scale")), "is not a known scale id"),
            (Args(input, ("targetTonic", "")), "targetTonic"),
        ];

        foreach ((Dictionary<string, object?> args, string expected) in hostile)
        {
            CallToolResult result = await host.CallAsync("restyle_midi", args);
            string text = ToolResults.TextOf(result);
            result.IsError.Should().Be(true, $"'{JsonSerializer.Serialize(args)}' must come back as a tool error");
            text.Should().Contain(expected, $"'{JsonSerializer.Serialize(args)}' must be refused in our own words");
            text.Should().NotContain("Exception", "an exception message is a crash that survived, not a refusal");
        }

        JsonElement after = await host.CallJsonAsync("restyle_midi", Args(input, ("outputPath", Path.Combine(_dir, "after.mid"))));
        after.GetProperty("notesRestyled").GetInt32().Should().Be(13, "the server is still serving after every refusal");
    }

    [Fact]
    public async Task TheToolIsAdvertisedAsDestructiveAndNotReadOnly()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        var tool = (await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Single(t => t.Name == "restyle_midi");

        tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
        tool.ProtocolTool.Annotations.DestructiveHint.Should().BeTrue();
        JsonElement properties = tool.JsonSchema.GetProperty("properties");
        properties.GetProperty("inputPath").GetProperty("description").GetString().Should().Contain("Absolute");
        properties.TryGetProperty("outputPath", out _).Should().BeTrue();
        properties.TryGetProperty("overwrite", out _).Should().BeTrue();
    }
}
