using Melanchall.DryWetMidi.Core;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;
using DomainNote = MidiRestyle.Core.Model.Note;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// The phase 8 gate: a microtonal scale exports onto pitch-bent channels with the right bends.
/// </summary>
public class MicrotonalExportTests
{
    private static readonly Scale CMajor = new(
        "t.cmajor", "C major", "Western", "Europe & Balkans",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    private static readonly Scale Rast = new(
        "t.rast", "Maqam Rast", "Arabic maqam", "Middle East",
        [0, 200, 350, 500, 700, 900, 1050], "Test fixture, 2026");

    private static readonly Scale Slendro = new(
        "t.slendro", "Slendro", "Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

    /// <summary>A full diatonic octave, so every degree of the target gets exercised.</summary>
    private static TrackInfo Track(int channel, int trackIndex) => new()
    {
        TrackIndex = trackIndex,
        Channel = channel,
        Name = $"Part {trackIndex + 1}",
        ProgramNumber = 40,
        Notes = [.. new[] { 60, 62, 64, 65, 67, 69, 71, 72 }
            .Select((n, i) => new DomainNote(Pitch.FromMidi(n), i * 480, 480, 90))],
    };

    private static (RestyleResult Result, ChannelAllocation Allocation) Restyle(
        Scale target, int trackCount = 1)
    {
        MidiProject project = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [.. Enumerable.Range(0, trackCount).Select(i => Track(i, i))],
            TempoMap = [new TempoChange(0, 500_000)],
        };

        RestyleResult result = RestyleEngine.Restyle(project, new RestyleSettings
        {
            TargetScale = target,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        });

        return (result, ChannelAllocator.Allocate(result));
    }

    private static MidiFile ExportAndReload(RestyleResult result, ChannelAllocation allocation)
    {
        using MemoryStream stream = new();
        MidiFileExporter.Export(result, stream, allocation).Success.Should().BeTrue();
        stream.Position = 0;
        return MidiFile.Read(stream);
    }

    /// <summary>The gate, stated as the plan states it.</summary>
    [Fact]
    public void RastExportsOnTwoChannelsWithCorrectBends()
    {
        (RestyleResult result, ChannelAllocation allocation) = Restyle(Rast);

        allocation.ChannelCount.Should().Be(2);

        MidiFile file = ExportAndReload(result, allocation);
        var bends = file.GetTrackChunks()
            .SelectMany(c => c.Events.OfType<PitchBendEvent>())
            .ToList();

        bends.Should().HaveCount(2, "one bend per allocated channel, held for its whole life");

        // -50 cents at the default +/-2 semitone range is 6144; 0 cents is centre, 8192.
        bends.Select(b => (int)b.PitchValue).Order().Should().Equal([6144, 8192]);
        bends.Select(b => (int)b.Channel).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryBentChannelCarriesItsRpnBendRangeSetup()
    {
        (RestyleResult result, ChannelAllocation allocation) = Restyle(Rast);
        MidiFile file = ExportAndReload(result, allocation);

        foreach (TrackChunk chunk in file.GetTrackChunks()
            .Where(c => c.Events.OfType<PitchBendEvent>().Any()))
        {
            var ccs = chunk.Events.OfType<ControlChangeEvent>()
                .Select(e => ((int)e.ControlNumber, (int)e.ControlValue))
                .ToList();

            // RPN 0,0 selects pitch-bend sensitivity; CC6 sets it; the 127/127 null closes the RPN
            // so a later stray CC6 is not read as another range change.
            ccs.Should().ContainInOrder((101, 0), (100, 0), (6, 2), (38, 0), (101, 127), (100, 127));
        }
    }

    /// <summary>
    /// Bank Select must precede the Program Change on a derived channel too. Without it the derived
    /// channel plays a different instrument from the source on any GS/XG device.
    /// </summary>
    [Fact]
    public void BentChannelsCarryBankSelectBeforeTheirProgramChange()
    {
        (RestyleResult result, ChannelAllocation allocation) = Restyle(Rast);
        MidiFile file = ExportAndReload(result, allocation);

        foreach (TrackChunk chunk in file.GetTrackChunks()
            .Where(c => c.Events.OfType<PitchBendEvent>().Any()))
        {
            List<MidiEvent> events = [.. chunk.Events];
            int pc = events.FindIndex(e => e is ProgramChangeEvent);
            pc.Should().BeGreaterThan(1, "a program change must have its bank select ahead of it");

            events[pc - 2].Should().BeOfType<ControlChangeEvent>()
                .Which.ControlNumber.Should().Be((Melanchall.DryWetMidi.Common.SevenBitNumber)0);
            events[pc - 1].Should().BeOfType<ControlChangeEvent>()
                .Which.ControlNumber.Should().Be((Melanchall.DryWetMidi.Common.SevenBitNumber)32);
        }
    }

    /// <summary>Every note must be emitted exactly once, on the channel carrying its offset.</summary>
    [Fact]
    public void EveryNoteIsEmittedExactlyOnceAcrossTheBentChannels()
    {
        (RestyleResult result, ChannelAllocation allocation) = Restyle(Slendro);

        MidiFile file = ExportAndReload(result, allocation);
        int exported = file.GetTrackChunks().SelectMany(c => c.Events.OfType<NoteOnEvent>()).Count();

        exported.Should().Be(result.TotalNoteCount,
            "splitting a track across bend channels must neither duplicate nor lose a note");
    }

    [Fact]
    public void SlendroExportsOnFiveChannels()
    {
        (RestyleResult result, ChannelAllocation allocation) = Restyle(Slendro);

        allocation.ChannelCount.Should().Be(5);
        ExportAndReload(result, allocation)
            .GetTrackChunks().SelectMany(c => c.Events.OfType<PitchBendEvent>())
            .Should().HaveCount(5);
    }

    [Fact]
    public void ChannelNineIsNeverWrittenTo()
    {
        (RestyleResult result, ChannelAllocation allocation) = Restyle(Slendro, trackCount: 3);
        MidiFile file = ExportAndReload(result, allocation);

        file.GetTrackChunks()
            .SelectMany(c => c.Events.OfType<Melanchall.DryWetMidi.Core.ChannelEvent>())
            .Should().OnlyContain(e => e.Channel != 9,
                "channel 9 is percussion; a melodic part there would play as drum hits");
    }

    [Fact]
    public void TempoAndDivisionSurviveMicrotonalExport()
    {
        (RestyleResult result, ChannelAllocation allocation) = Restyle(Rast);
        MidiFile file = ExportAndReload(result, allocation);

        file.TimeDivision.Should().BeOfType<TicksPerQuarterNoteTimeDivision>()
            .Which.TicksPerQuarterNote.Should().Be(480);
        file.GetTrackChunks().SelectMany(c => c.Events.OfType<SetTempoEvent>())
            .Should().ContainSingle().Which.MicrosecondsPerQuarterNote.Should().Be(500_000);
    }

    /// <summary>
    /// The guarantee the whole allocator exists for: the same plan drives both, so what you heard is
    /// what you exported.
    /// </summary>
    [Fact]
    public void ExportUsesExactlyTheAllocationPreviewWouldUse()
    {
        (RestyleResult result, ChannelAllocation allocation) = Restyle(Slendro, trackCount: 4);

        // Playback would plan against the same ceiling and get the same answer.
        ChannelAllocation playback = ChannelAllocator.Allocate(result, ChannelBudget.DefaultCeiling);
        playback.Channels.Should().BeEquivalentTo(allocation.Channels);

        MidiFile file = ExportAndReload(result, allocation);
        var exportedBends = file.GetTrackChunks()
            .SelectMany(c => c.Events.OfType<PitchBendEvent>())
            .Select(b => (int)b.PitchValue)
            .Order();

        var plannedBends = allocation.Channels
            .Select(c => PitchBendEncoder.EncodeBend(c.BendCents))
            .Order();

        exportedBends.Should().Equal(plannedBends);
    }

    [Fact]
    public void AMutedTrackChannelIsAbsentFromTheExportedFile()
    {
        (RestyleResult result, ChannelAllocation allocation) = Restyle(Slendro, trackCount: 20);

        allocation.Muted.Should().NotBeEmpty();

        MidiFile file = ExportAndReload(result, allocation);
        int channelsUsed = file.GetTrackChunks()
            .SelectMany(c => c.Events.OfType<Melanchall.DryWetMidi.Core.ChannelEvent>())
            .Select(e => (int)e.Channel)
            .Distinct()
            .Count();

        channelsUsed.Should().BeLessThanOrEqualTo(15);
    }

    /// <summary>
    /// The assertion that actually proves the gap is closed: every channel a source track-channel
    /// is split across must carry the source's captured controller state, not just its program.
    /// </summary>
    [Fact]
    public void DerivedChannelsEachCarryTheSourceChannelsVolumeAndSustain()
    {
        TrackInfo source = new()
        {
            TrackIndex = 0,
            Channel = 0,
            Name = "Part 1",
            ProgramNumber = 40,
            ControllerValues = new Dictionary<int, int> { [7] = 77, [64] = 127 },
            Notes = [.. new[] { 60, 62, 64, 65, 67, 69, 71, 72 }
                .Select((n, i) => new DomainNote(Pitch.FromMidi(n), i * 480, 480, 90))],
        };

        MidiProject project = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [source],
            TempoMap = [new TempoChange(0, 500_000)],
        };

        RestyleResult result = RestyleEngine.Restyle(project, new RestyleSettings
        {
            TargetScale = Rast,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        });

        ChannelAllocation allocation = ChannelAllocator.Allocate(result);
        allocation.ChannelCount.Should().Be(2, "Rast needs two derived channels - both must carry the state");

        MidiFile file = ExportAndReload(result, allocation);

        List<TrackChunk> bentChunks = [.. file.GetTrackChunks()
            .Where(c => c.Events.OfType<PitchBendEvent>().Any())];

        bentChunks.Should().HaveCount(2);
        bentChunks.Should().AllSatisfy(chunk =>
        {
            var ccs = chunk.Events.OfType<ControlChangeEvent>()
                .Select(e => ((int)e.ControlNumber, (int)e.ControlValue))
                .ToList();

            ccs.Should().Contain((7, 77), "volume (CC7) must reach every derived channel, not only the first");
            ccs.Should().Contain((64, 127), "sustain (CC64) must reach every derived channel too");
        });
    }

    [Fact]
    public void DrumsRideAlongUnbentOnChannelTen()
    {
        MidiProject project = new()
        {
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks =
            [
                Track(0, 0),
                new TrackInfo
                {
                    TrackIndex = 1,
                    Channel = TrackInfo.DrumChannel,
                    Notes = [new DomainNote(Pitch.FromMidi(36), 0, 120, 100)],
                },
            ],
        };

        RestyleResult result = RestyleEngine.Restyle(project, new RestyleSettings
        {
            TargetScale = Rast,
            TargetTonic = Pitch.FromMidi(60),
            SourceScale = CMajor,
            SourceTonic = Pitch.FromMidi(60),
        });

        MidiFile file = ExportAndReload(result, ChannelAllocator.Allocate(result));

        var drumNotes = file.GetTrackChunks()
            .SelectMany(c => c.Events.OfType<NoteOnEvent>())
            .Where(n => n.Channel == 9)
            .ToList();

        drumNotes.Should().ContainSingle().Which.NoteNumber.Should().Be(
            (Melanchall.DryWetMidi.Common.SevenBitNumber)36,
            "a percussion note number selects the drum, so it must survive untransposed");
    }
}
