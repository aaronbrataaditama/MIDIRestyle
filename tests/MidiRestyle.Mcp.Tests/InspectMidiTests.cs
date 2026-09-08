using System.Text.Json;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MidiRestyle.Core.Restyle;
using ModelContextProtocol.Protocol;

namespace MidiRestyle.Mcp.Tests;

public sealed class InspectMidiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "midirestyle-inspect-" + Guid.NewGuid().ToString("N"));

    public InspectMidiTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task ReportsTracksKeyAndTheSourceScaleThatImplies()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = MidiFixtures.Write(Path.Combine(_dir, "major.mid"), MidiFixtures.CMajorNotes);

        JsonElement r = await host.CallJsonAsync("inspect_midi", new() { ["path"] = input });

        r.GetProperty("path").GetString().Should().Be(input);
        r.GetProperty("format").GetString().Should().Be("multiTrack");
        r.GetProperty("division").GetString().Should().Contain("480");
        r.GetProperty("initialTempoBpm").GetDouble().Should().Be(120);
        r.GetProperty("durationTicks").GetInt64().Should().Be(13 * 480);
        r.GetProperty("durationSeconds").GetDouble().Should().Be(6.5);
        r.GetProperty("totalNotes").GetInt32().Should().Be(13);

        JsonElement track = r.GetProperty("tracks").EnumerateArray().Single();
        track.GetProperty("track").GetInt32().Should().Be(1, "the tempo-only chunk is chunk 0 and carries no channel");
        track.GetProperty("channel").GetInt32().Should().Be(0);
        track.GetProperty("noteCount").GetInt32().Should().Be(13);
        track.GetProperty("isDrums").GetBoolean().Should().BeFalse();
        track.GetProperty("restylable").GetBoolean().Should().BeTrue();
        track.GetProperty("hasPitchBend").GetBoolean().Should().BeFalse();
        track.GetProperty("lowestMidi").GetInt32().Should().Be(60);
        track.GetProperty("highestMidi").GetInt32().Should().Be(72);

        JsonElement key = r.GetProperty("key");
        key.GetProperty("outcome").GetString().Should().Be("detected");
        key.GetProperty("isAmbiguous").GetBoolean().Should().BeFalse();
        key.GetProperty("candidates")[0].GetProperty("tonic").GetString().Should().Be("C");
        key.GetProperty("candidates")[0].GetProperty("mode").GetString().Should().Be("major");
        key.GetProperty("suggestedSourceScaleId").GetString().Should().Be(RestyleDefaults.MajorSourceScaleId);
    }

    /// <summary>A minor file must suggest the minor source scale, or the branch is untested.</summary>
    [Fact]
    public async Task AMinorFileSuggestsTheMinorSourceScale()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = MidiFixtures.Write(Path.Combine(_dir, "minor.mid"), MidiFixtures.AMinorNotes);

        JsonElement key = (await host.CallJsonAsync("inspect_midi", new() { ["path"] = input })).GetProperty("key");

        key.GetProperty("candidates")[0].GetProperty("tonic").GetString().Should().Be("A");
        key.GetProperty("candidates")[0].GetProperty("mode").GetString().Should().Be("minor");
        key.GetProperty("suggestedSourceScaleId").GetString().Should().Be(RestyleDefaults.MinorSourceScaleId);
        RestyleDefaults.MinorSourceScaleId.Should().NotBe(RestyleDefaults.MajorSourceScaleId,
            "otherwise this test and its major sibling could not tell the two branches apart");
    }

    /// <summary>
    /// A bare ascending scale in equal durations is the case key detection is honest about: C major and
    /// its relative minor share all seven pitch classes, so the margin falls under the threshold and the
    /// shortlist is offered without a winner. <c>isAmbiguous</c> must carry that through - it is the
    /// difference between "here is the key" and "here are the candidates".
    /// </summary>
    [Fact]
    public async Task AnUndecidedKeyIsReportedAsAmbiguousWithItsShortlist()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = MidiFixtures.Write(
            Path.Combine(_dir, "bare-scale.mid"), [(60, 0), (62, 0), (64, 0), (65, 0), (67, 0), (69, 0), (71, 0)]);

        JsonElement key = (await host.CallJsonAsync("inspect_midi", new() { ["path"] = input })).GetProperty("key");

        key.GetProperty("outcome").GetString().Should().Be("ambiguous");
        key.GetProperty("isAmbiguous").GetBoolean().Should().BeTrue();
        key.GetProperty("candidates").GetArrayLength().Should().Be(3, "the shortlist is offered whatever the confidence");

        JsonElement leader = key.GetProperty("candidates")[0];
        leader.GetProperty("r").GetDouble().Should().BeGreaterThan(0.5, "the fit itself is good - it is the gap that is not");
        leader.GetProperty("margin").GetDouble().Should().BePositive().And.BeLessThan(0.05,
            "below the ambiguity threshold is exactly why this reads as ambiguous");
        key.GetProperty("candidates")[1].GetProperty("margin").GetDouble().Should()
            .BeNegative("every candidate below the leader reports how far behind it sits");
    }

    [Fact]
    public async Task Format0FilesShowOnePseudoTrackPerChannelIncludingDrums()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = MidiFixtures.Write(Path.Combine(_dir, "f0.mid"), [(60, 0), (67, 1), (36, 9)], format0: true);

        JsonElement r = await host.CallJsonAsync("inspect_midi", new() { ["path"] = input });

        r.GetProperty("format").GetString().Should().Be("singleTrack");
        var tracks = r.GetProperty("tracks").EnumerateArray().ToList();
        tracks.Should().HaveCount(3);
        tracks.Should().OnlyContain(t => t.GetProperty("track").GetInt32() == 0);

        JsonElement drums = tracks.Single(t => t.GetProperty("channel").GetInt32() == 9);
        drums.GetProperty("isDrums").GetBoolean().Should().BeTrue();
        drums.GetProperty("restylable").GetBoolean().Should()
            .BeFalse("channel 10 is percussion: a note number picks the drum, so restyling never touches it");

        tracks.Where(t => t.GetProperty("channel").GetInt32() != 9)
            .Should().OnlyContain(t => t.GetProperty("isDrums").GetBoolean() == false
                                    && t.GetProperty("restylable").GetBoolean());

        r.GetProperty("totalNotes").GetInt32().Should().Be(3, "totalNotes counts every note in the file, drums included");
        r.GetProperty("restylableNoteCount").GetInt32().Should().Be(2, "the drum note is not one restyle_midi will map");
    }

    [Fact]
    public async Task DrumsOnlyReportsNoKey()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = MidiFixtures.Write(Path.Combine(_dir, "drums.mid"), MidiFixtures.DrumsOnlyNotes);

        JsonElement r = await host.CallJsonAsync("inspect_midi", new() { ["path"] = input });

        r.GetProperty("key").GetProperty("outcome").GetString().Should().Be("noKeyDetected");
        r.GetProperty("key").GetProperty("candidates").GetArrayLength().Should()
            .Be(0, "drums are excluded from the pitch-class profile, so there is nothing to rank");
        r.GetProperty("key").GetProperty("isAmbiguous").GetBoolean().Should()
            .BeFalse("nothing was ranked, so nothing is close - ambiguity is a property of a ranking");
        r.GetProperty("key").TryGetProperty("suggestedSourceScaleId", out _).Should().BeFalse("nulls are omitted");
        r.GetProperty("tracks").EnumerateArray().Single().GetProperty("restylable").GetBoolean().Should().BeFalse();
        r.GetProperty("restylableNoteCount").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// The path is validated by <see cref="OutputPathPolicy.ValidateInputPath"/> before the file is
    /// touched, and this pins that call rather than merely pinning "a bad path is an error". Both legs
    /// compare against the policy's own text, so deleting the call fails the test even though the
    /// loader would also refuse these paths: the loader's wording is its own
    /// (<c>Could not open ...</c> / <c>Could not find file ...</c>) and does not mention "absolute" at all.
    /// </summary>
    [Fact]
    public async Task TheInputPathIsValidatedByThePolicyBeforeTheFileIsTouched()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        CallToolResult relative = await host.CallAsync("inspect_midi", new() { ["path"] = "tune.mid" });
        relative.IsError.Should().Be(true);
        ToolResults.TextOf(relative).Should().Be(OutputPathPolicy.ValidateInputPath("tune.mid"));
        ToolResults.TextOf(relative).Should().Contain("absolute");

        string missing = Path.Combine(_dir, "nothing-here.mid");
        CallToolResult absent = await host.CallAsync("inspect_midi", new() { ["path"] = missing });
        absent.IsError.Should().Be(true);
        ToolResults.TextOf(absent).Should().Be(OutputPathPolicy.ValidateInputPath(missing));
        ToolResults.TextOf(absent).Should().Contain("not found");
    }

    /// <summary>
    /// Nothing an agent can put in <c>path</c> may escape as an exception, and the server must still be
    /// answering afterwards - the last leg is what proves the failures were handled rather than merely
    /// survived by luck of the transport.
    /// </summary>
    [Fact]
    public async Task NoAgentSuppliedPathCrashesTheServer()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        string text = Path.Combine(_dir, "text.mid");
        File.WriteAllText(text, "nope");
        CallToolResult notMidi = await host.CallAsync("inspect_midi", new() { ["path"] = text });
        notMidi.IsError.Should().Be(true);
        ToolResults.TextOf(notMidi).Should().Contain("Could not load");

        string truncated = Path.Combine(_dir, "truncated.mid");
        File.WriteAllBytes(truncated, [0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00]);
        (await host.CallAsync("inspect_midi", new() { ["path"] = truncated })).IsError.Should().Be(true);

        foreach (string? hostile in new[] { "", "   ", _dir, Path.Combine(_dir, "no", "such", "dir", "x.mid"), "C:relative.mid", "\\rooted.mid" })
        {
            CallToolResult result = await host.CallAsync("inspect_midi", new() { ["path"] = hostile });
            result.IsError.Should().Be(true, $"'{hostile}' must come back as a tool error");
            ToolResults.TextOf(result).Should().NotBeNullOrWhiteSpace();
        }

        string good = MidiFixtures.Write(Path.Combine(_dir, "after.mid"), MidiFixtures.CMajorNotes);
        (await host.CallJsonAsync("inspect_midi", new() { ["path"] = good }))
            .GetProperty("totalNotes").GetInt32().Should().Be(13, "the server is still serving after every refusal");
    }

    /// <summary>
    /// <c>initialTempoBpm</c> is the tempo in force at the start, not whichever chunk happened to be
    /// read first: this file's opening tempo sits in its second chunk and its later change in the
    /// first. The time signature the same chunk carries is echoed alongside it.
    /// </summary>
    [Fact]
    public async Task TheInitialTempoIsTheEarliestByTickNotByChunkOrder()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Path.Combine(_dir, "tempi.mid");
        var late = new TrackChunk();
        using (TimedObjectsManager<TimedEvent> manager = late.ManageTimedEvents())
        {
            manager.Objects.Add(new TimedEvent(new SetTempoEvent(250_000), 960));
        }

        var early = new TrackChunk();
        using (TimedObjectsManager<TimedEvent> manager = early.ManageTimedEvents())
        {
            manager.Objects.Add(new TimedEvent(new SetTempoEvent(1_000_000), 0));
            manager.Objects.Add(new TimedEvent(new TimeSignatureEvent(6, 8), 0));
        }

        var file = new MidiFile(late, early) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        file.Write(input, overwriteFile: true, format: MidiFileFormat.MultiTrack);

        JsonElement r = await host.CallJsonAsync("inspect_midi", new() { ["path"] = input });

        r.GetProperty("initialTempoBpm").GetDouble().Should().Be(60, "1,000,000 microseconds per quarter is 60 BPM");
        JsonElement signature = r.GetProperty("timeSignatures").EnumerateArray().Single();
        signature.GetProperty("numerator").GetInt32().Should().Be(6);
        signature.GetProperty("denominator").GetInt32().Should().Be(8);
        signature.GetProperty("ticks").GetInt64().Should().Be(0);
    }

    /// <summary>
    /// The three string fields nothing else pins, each against a value only it can hold: the file's
    /// <c>title</c> is the first sequence name found, a row's <c>name</c> is its own chunk's, and
    /// <c>instrument</c> is the GM name of the first program change on that channel. All three are
    /// nullable strings sitting close together in the record, so a positional mix-up would compile,
    /// serialise and say nothing.
    /// </summary>
    [Fact]
    public async Task TitleTrackNameAndInstrumentComeFromTheFile()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = Path.Combine(_dir, "named.mid");

        var first = new TrackChunk(new SequenceTrackNameEvent("Sonata in Slendro"), new SetTempoEvent(500_000));
        var second = new TrackChunk(
            new SequenceTrackNameEvent("Right Hand"),
            new ProgramChangeEvent((SevenBitNumber)40) { Channel = (FourBitNumber)0 });
        using (TimedObjectsManager<Note> manager = second.ManageNotes())
        {
            manager.Objects.Add(new Note((SevenBitNumber)60, 480, 0));
        }

        new MidiFile(first, second) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) }
            .Write(input, overwriteFile: true, format: MidiFileFormat.MultiTrack);

        JsonElement r = await host.CallJsonAsync("inspect_midi", new() { ["path"] = input });

        r.GetProperty("title").GetString().Should().Be("Sonata in Slendro", "the first chunk's name titles the file");
        JsonElement track = r.GetProperty("tracks").EnumerateArray().Single();
        track.GetProperty("name").GetString().Should().Be("Right Hand", "a row is named by its own chunk");
        track.GetProperty("instrument").GetString().Should().Be("Violin", "GM program 40");
    }

    /// <summary>A file that names nothing omits the fields rather than inventing them.</summary>
    [Fact]
    public async Task UnnamedFilesOmitTitleNameAndInstrument()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        string input = MidiFixtures.Write(Path.Combine(_dir, "anon.mid"), MidiFixtures.CMajorNotes);

        JsonElement r = await host.CallJsonAsync("inspect_midi", new() { ["path"] = input });

        r.TryGetProperty("title", out _).Should().BeFalse();
        JsonElement track = r.GetProperty("tracks").EnumerateArray().Single();
        track.TryGetProperty("name", out _).Should().BeFalse();
        track.TryGetProperty("instrument", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TheToolIsAdvertisedAsReadOnly()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        var tool = (await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Single(t => t.Name == "inspect_midi");

        tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
        tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse();
        tool.Description.Should().Contain("restyle_midi", "the description points the agent at the next call");
        tool.JsonSchema.GetProperty("properties").GetProperty("path").GetProperty("description")
            .GetString().Should().Contain("Absolute");
    }
}
