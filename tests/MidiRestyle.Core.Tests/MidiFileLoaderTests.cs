using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using DwmTimeDivision = Melanchall.DryWetMidi.Core.TimeDivision;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// Covers <see cref="MidiFileLoader"/> against fixtures built in code rather than committed as
/// binaries - a fixture whose bytes are written by the same library that reads them is auditable in
/// the test, and there is nothing to keep in sync with the csproj.
/// </summary>
public sealed class MidiFileLoaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
    }

    // ---- 1. Format 1, one track using several channels ---------------------------------------

    [Fact]
    public void Format1TrackUsingSeveralChannelsSplitsIntoOneTrackInfoPerChannel()
    {
        // Hand-built bytes, deliberately. DryWetMIDI's writer redistributes a multi-channel chunk
        // into one chunk per channel when asked for Format 1, so it cannot express the file this
        // test is about - verified: writing a single 3-channel chunk as MultiTrack reads back as
        // three chunks. Real Format 1 files from DAWs routinely do put several channels in one
        // track, so the scenario is worth testing; it just has to be constructed literally.
        string path = WriteRawFormat1WithOneMultiChannelTrack();
        MidiProject project = MidiFileLoader.Load(path);

        // Guard the premise: if this ever reads as 3 chunks the fixture has stopped doing its job
        // and the TrackIndex assertions below would pass for the wrong reason.
        MidiFile.Read(path).Chunks.OfType<TrackChunk>().Should().HaveCount(1);

        project.Format.Should().Be(MidiFileFormatKind.MultiTrack);
        project.Tracks.Should().HaveCount(3);

        // One source track, so every split shares its index - that is what makes (track, channel)
        // a usable scope key rather than a renumbering.
        project.Tracks.Select(t => t.TrackIndex).Should().AllBeEquivalentTo(0);
        project.Tracks.Select(t => t.Channel).Should().Equal(0, 1, 9);

        TrackInfo drums = project.Tracks.Single(t => t.Channel == 9);
        drums.IsDrums.Should().BeTrue();
        drums.IsRestylable.Should().BeFalse();

        project.Tracks.Where(t => t.Channel != 9).Should().AllSatisfy(t =>
        {
            t.IsDrums.Should().BeFalse();
            t.IsRestylable.Should().BeTrue();
        });
    }

    // ---- 2. Format 0 ------------------------------------------------------------------------

    [Fact]
    public void Format0SingleTrackSplitsPerChannelSoDrumsCanBeExcluded()
    {
        List<(long, MidiEvent)> events = [];
        AddNote(events, channel: 0, noteNumber: 72, velocity: 100, start: 0, length: 240);
        AddNote(events, channel: 3, noteNumber: 55, velocity: 80, start: 240, length: 240);
        AddNote(events, channel: 9, noteNumber: 38, velocity: 120, start: 0, length: 60);
        AddNote(events, channel: 15, noteNumber: 40, velocity: 70, start: 480, length: 240);

        string path = Write(MidiFileFormat.SingleTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        project.Format.Should().Be(MidiFileFormatKind.SingleTrack);
        project.Tracks.Select(t => t.Channel).Should().Equal(0, 3, 9, 15);
        project.Tracks.Select(t => t.TrackIndex).Should().AllBeEquivalentTo(0);

        // The whole reason splitting exists: a single per-track checkbox could not have excluded
        // channel 10 from a Format 0 file.
        project.Tracks.Where(t => t.IsDrums).Should().ContainSingle()
            .Which.Channel.Should().Be(9);
        project.RestylableTracks.Select(t => t.Channel).Should().Equal(0, 3, 15);
        project.HasDrums.Should().BeTrue();
        project.PitchedTrackChannelCount.Should().Be(3);
    }

    // ---- 3. Two tracks sharing a channel ----------------------------------------------------

    [Fact]
    public void TwoFormat1TracksSharingChannelZeroStayDistinct()
    {
        List<(long, MidiEvent)> first = [];
        first.Add((0, new ProgramChangeEvent((SevenBitNumber)40) { Channel = (FourBitNumber)0 }));
        AddNote(first, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);

        List<(long, MidiEvent)> second = [];
        second.Add((0, new ProgramChangeEvent((SevenBitNumber)73) { Channel = (FourBitNumber)0 }));
        AddNote(second, channel: 0, noteNumber: 67, velocity: 90, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiTrack, Track(first), Track(second));
        MidiProject project = MidiFileLoader.Load(path);

        // Legal MIDI, and merging them would silently combine two instruments into one.
        project.Tracks.Should().HaveCount(2);
        project.Tracks.Select(t => t.TrackIndex).Should().Equal(0, 1);
        project.Tracks.Select(t => t.Channel).Should().Equal(0, 0);
        project.Tracks.Select(t => t.ProgramNumber).Should().Equal(40, 73);
        project.Tracks.Select(t => t.InstrumentName).Should().Equal("Violin", "Flute");
    }

    // ---- 4. PPQN ----------------------------------------------------------------------------

    [Fact]
    public void TicksPerQuarterNoteRoundTrips()
    {
        List<(long, MidiEvent)> events = [];
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiTrack, [Track(events)], new TicksPerQuarterNoteTimeDivision(480));
        MidiProject project = MidiFileLoader.Load(path);

        project.Division.Should().Be(new TicksPerQuarterNote(480));
        project.Division.Describe().Should().Be("480 PPQN");
    }

    // ---- 5. SMPTE ---------------------------------------------------------------------------

    [Fact]
    public void SmpteFileYieldsSmpteDivisionAndNoDurationInSeconds()
    {
        List<(long, MidiEvent)> events = [];
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 240);

        string path = Write(
            MidiFileFormat.MultiTrack,
            [Track(events)],
            new SmpteTimeDivision(SmpteFormat.Thirty, 80));
        MidiProject project = MidiFileLoader.Load(path);

        project.Division.Should().Be(new SmpteDivision(30, 80));

        // No PPQN means no musical timebase to integrate a tempo map against - so seconds are
        // genuinely unknown rather than zero.
        project.DurationSeconds.Should().BeNull();
        project.DurationTicks.Should().Be(240);
    }

    [Theory]
    [InlineData((byte)SmpteFormat.TwentyFour, 24)]
    [InlineData((byte)SmpteFormat.TwentyFive, 25)]
    [InlineData((byte)SmpteFormat.ThirtyDrop, 29)]
    [InlineData((byte)SmpteFormat.Thirty, 30)]
    public void SmpteFrameRatesMapToTheirStoredIntegers(byte format, int expectedFramesPerSecond)
    {
        List<(long, MidiEvent)> events = [];
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 240);

        string path = Write(
            MidiFileFormat.MultiTrack,
            [Track(events)],
            new SmpteTimeDivision((SmpteFormat)format, 4));
        MidiProject project = MidiFileLoader.Load(path);

        project.Division.Should().BeOfType<SmpteDivision>()
            .Which.FramesPerSecond.Should().Be(expectedFramesPerSecond);
    }

    // ---- 6. Format 2 ------------------------------------------------------------------------

    [Fact]
    public void Format2LoadsAsMultiSequenceAndReportsItsSequenceCount()
    {
        List<(long, MidiEvent)> first = [];
        AddNote(first, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);
        List<(long, MidiEvent)> second = [];
        AddNote(second, channel: 1, noteNumber: 62, velocity: 100, start: 0, length: 480);
        List<(long, MidiEvent)> third = [];
        AddNote(third, channel: 2, noteNumber: 64, velocity: 100, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiSequence, Track(first), Track(second), Track(third));

        // Not an error path: DryWetMIDI reads Format 2 with default settings, so this is purely a
        // presentation decision.
        MidiProject project = MidiFileLoader.Load(path);

        project.Format.Should().Be(MidiFileFormatKind.MultiSequence);
        project.SequenceCount.Should().BeGreaterThan(1);
        project.SequenceCount.Should().Be(3);
        project.Tracks.Should().HaveCount(3);
    }

    [Fact]
    public void SequenceCountIsOneForOrdinaryFiles()
    {
        List<(long, MidiEvent)> events = [];
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiTrack, Track(events));

        MidiFileLoader.Load(path).SequenceCount.Should().Be(1);
    }

    // ---- 7. Notes ---------------------------------------------------------------------------

    [Fact]
    public void NotesAreLoadedWithTimingVelocityAndPitchIntact()
    {
        List<(long, MidiEvent)> events = [];
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);
        AddNote(events, channel: 0, noteNumber: 64, velocity: 1, start: 480, length: 0);
        AddNote(events, channel: 0, noteNumber: 67, velocity: 127, start: 960, length: 240);

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        TrackInfo track = project.Tracks.Should().ContainSingle().Subject;
        track.NoteCount.Should().Be(3);
        project.TotalNoteCount.Should().Be(3);

        track.Notes[0].Pitch.MidiNote.Should().Be(60);
        track.Notes[0].StartTicks.Should().Be(0);
        track.Notes[0].LengthTicks.Should().Be(480);
        track.Notes[0].Velocity.Should().Be(100);

        // Zero-length notes are legal MIDI. Filtering them would drop grace notes and any
        // percussive part written as instantaneous triggers.
        track.Notes[1].Pitch.MidiNote.Should().Be(64);
        track.Notes[1].StartTicks.Should().Be(480);
        track.Notes[1].LengthTicks.Should().Be(0);
        track.Notes[1].Velocity.Should().Be(1);

        track.Notes[2].Pitch.MidiNote.Should().Be(67);
        track.Notes[2].StartTicks.Should().Be(960);
        track.Notes[2].LengthTicks.Should().Be(240);
        track.Notes[2].Velocity.Should().Be(127);

        // Pitch is cents throughout: a loaded note sits exactly on the 12-TET grid.
        track.Notes.Should().AllSatisfy(n => n.Pitch.IsTwelveTet.Should().BeTrue());
        track.Notes[0].Pitch.Cents.Should().Be(6000);

        track.EndTicks.Should().Be(1200);
        project.LowestPitch!.Value.MidiNote.Should().Be(60);
        project.HighestPitch!.Value.MidiNote.Should().Be(67);
    }

    [Fact]
    public void ZeroVelocityNoteOnIsTreatedAsANoteOff()
    {
        // The spec's running-status shorthand: 0x9n with velocity 0 ends the note.
        TrackChunk chunk = Track(
        [
            (0, new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0 }),
            (480, new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0 }),
        ]);

        string path = Write(MidiFileFormat.MultiTrack, chunk);
        MidiProject project = MidiFileLoader.Load(path);

        TrackInfo track = project.Tracks.Should().ContainSingle().Subject;
        track.Notes.Should().ContainSingle();
        track.Notes[0].LengthTicks.Should().Be(480);
    }

    // ---- 8. Tempo map, time signature and duration ------------------------------------------

    [Fact]
    public void TempoMapAndTimeSignatureAreCapturedAndDrivePlayingTime()
    {
        // A conductor track carrying only meta events, then one playing track - the ordinary
        // Format 1 shape.
        TrackChunk conductor = Track(
        [
            (0, new SequenceTrackNameEvent("Fixture Song")),
            (0, new TimeSignatureEvent(6, 8)),
            (0, new SetTempoEvent(500_000)),
            (480, new SetTempoEvent(250_000)),
            (960, new MarkerEvent("Chorus")),
        ]);

        List<(long, MidiEvent)> events = [];
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 960);

        string path = Write(
            MidiFileFormat.MultiTrack,
            [conductor, Track(events)],
            new TicksPerQuarterNoteTimeDivision(480));
        MidiProject project = MidiFileLoader.Load(path);

        project.Title.Should().Be("Fixture Song");
        project.TempoMap.Should().Equal(
            new TempoChange(0, 500_000),
            new TempoChange(480, 250_000));
        project.TempoMap[0].BeatsPerMinute.Should().Be(120);
        project.TempoMap[1].BeatsPerMinute.Should().Be(240);
        project.TimeSignatures.Should().Equal(new TimeSignatureChange(0, 6, 8));
        project.Markers.Should().Equal(new MarkerInfo(960, "Chorus"));

        // A conductor track holds no channel events, so it contributes no track-channel.
        project.Tracks.Should().ContainSingle().Which.TrackIndex.Should().Be(1);

        // Hand-computed: ticks 0..480 at 120 bpm is 0.5 s, ticks 480..960 at 240 bpm is 0.25 s.
        project.DurationTicks.Should().Be(960);
        project.DurationSeconds.Should().BeApproximately(0.75, 1e-9);
    }

    // ---- 9. Existing pitch bend -------------------------------------------------------------

    [Fact]
    public void ExistingPitchBendIsFlaggedOnlyOnTheTrackChannelThatCarriesIt()
    {
        List<(long, MidiEvent)> events = [];
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);
        AddNote(events, channel: 1, noteNumber: 62, velocity: 100, start: 0, length: 480);
        events.Add((240, new PitchBendEvent(10_000) { Channel = (FourBitNumber)1 }));

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        // Channel-wide state, so the flag is per track-channel: microtonal output on channel 1
        // would fight the bend already written there.
        project.Tracks.Single(t => t.Channel == 0).HasExistingPitchBend.Should().BeFalse();
        project.Tracks.Single(t => t.Channel == 1).HasExistingPitchBend.Should().BeTrue();
        project.TracksWithExistingPitchBend.Select(t => t.Channel).Should().Equal(1);
    }

    // ---- 10. Program change -----------------------------------------------------------------

    [Fact]
    public void ProgramChangePopulatesProgramNumberAndInstrumentName()
    {
        List<(long, MidiEvent)> events = [];
        events.Add((0, new ProgramChangeEvent((SevenBitNumber)56) { Channel = (FourBitNumber)0 }));
        events.Add((0, new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)9 }));
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);
        AddNote(events, channel: 9, noteNumber: 36, velocity: 100, start: 0, length: 60);

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        TrackInfo melodic = project.Tracks.Single(t => t.Channel == 0);
        melodic.ProgramNumber.Should().Be(56);
        melodic.InstrumentName.Should().NotBeNull().And.Be("Trumpet");
        melodic.DisplayName.Should().Be("Trumpet");

        // On channel 10 a program change picks a drum kit, not a melodic voice - reporting
        // "Acoustic Grand Piano" for program 0 there would be actively misleading.
        TrackInfo drums = project.Tracks.Single(t => t.Channel == 9);
        drums.ProgramNumber.Should().Be(0);
        drums.InstrumentName.Should().NotBeNull().And.Be("Standard Kit");
    }

    [Fact]
    public void ChannelsWithoutAProgramChangeReportNoInstrument()
    {
        List<(long, MidiEvent)> events = [];
        AddNote(events, channel: 4, noteNumber: 60, velocity: 100, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        TrackInfo track = project.Tracks.Should().ContainSingle().Subject;
        track.ProgramNumber.Should().BeNull();
        track.InstrumentName.Should().BeNull();
        track.DisplayName.Should().Be("Channel 5");
    }

    // ---- 10b. Channel-wide controller capture ------------------------------------------------

    [Fact]
    public void CommonControllersSetBeforeTheFirstNoteAreCaptured()
    {
        List<(long, MidiEvent)> events = [];
        events.Add((0, new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100) { Channel = (FourBitNumber)0 }));   // volume
        events.Add((0, new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)64) { Channel = (FourBitNumber)0 }));  // pan
        events.Add((0, new ControlChangeEvent((SevenBitNumber)11, (SevenBitNumber)110) { Channel = (FourBitNumber)0 })); // expression
        events.Add((0, new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)127) { Channel = (FourBitNumber)0 })); // sustain
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        TrackInfo track = project.Tracks.Should().ContainSingle().Subject;
        track.ControllerValues.Should().BeEquivalentTo(new Dictionary<int, int>
        {
            [7] = 100,
            [10] = 64,
            [11] = 110,
            [64] = 127,
        });
    }

    [Fact]
    public void AnUncommonControllerIsCapturedTooBecauseThisIsNotAWhitelist()
    {
        List<(long, MidiEvent)> events = [];
        events.Add((0, new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)45) { Channel = (FourBitNumber)0 }));  // modulation
        events.Add((0, new ControlChangeEvent((SevenBitNumber)91, (SevenBitNumber)80) { Channel = (FourBitNumber)0 })); // reverb send
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        TrackInfo track = project.Tracks.Should().ContainSingle().Subject;
        track.ControllerValues.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 45, [91] = 80 });
    }

    [Fact]
    public void ChannelPressureIsCaptured()
    {
        List<(long, MidiEvent)> events = [];
        events.Add((0, new ChannelAftertouchEvent((SevenBitNumber)90) { Channel = (FourBitNumber)0 }));
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        project.Tracks.Should().ContainSingle().Which.ChannelPressure.Should().Be(90);
    }

    [Fact]
    public void ResetAllControllersAndAllNotesOffAreNotCaptured()
    {
        List<(long, MidiEvent)> events = [];
        events.Add((0, new ControlChangeEvent((SevenBitNumber)121, (SevenBitNumber)0) { Channel = (FourBitNumber)0 }));
        events.Add((0, new ControlChangeEvent((SevenBitNumber)123, (SevenBitNumber)0) { Channel = (FourBitNumber)0 }));
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        project.Tracks.Should().ContainSingle().Which.ControllerValues.Should().BeEmpty();
    }

    [Fact]
    public void ControllersOnOneChannelDoNotLeakIntoAnotherChannelsTrackInfo()
    {
        List<(long, MidiEvent)> events = [];
        events.Add((0, new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100) { Channel = (FourBitNumber)0 }));
        events.Add((0, new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)55) { Channel = (FourBitNumber)1 }));
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);
        AddNote(events, channel: 1, noteNumber: 62, velocity: 100, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        project.Tracks.Single(t => t.Channel == 0).ControllerValues.Should().Equal(
            new Dictionary<int, int> { [7] = 100 });
        project.Tracks.Single(t => t.Channel == 1).ControllerValues.Should().Equal(
            new Dictionary<int, int> { [7] = 55 });
    }

    [Fact]
    public void TheLastValueBeforeTheFirstNoteWinsWhenAControllerIsSetTwice()
    {
        List<(long, MidiEvent)> events = [];
        events.Add((0, new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)40) { Channel = (FourBitNumber)0 }));
        events.Add((100, new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)0 }));
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 200, length: 480);

        // A change after the first note must NOT win - v1 does not mirror mid-piece automation.
        events.Add((300, new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)1) { Channel = (FourBitNumber)0 }));

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        project.Tracks.Should().ContainSingle().Which.ControllerValues[7].Should().Be(90);
    }

    [Fact]
    public void ATrackWithNoControllersYieldsAnEmptyCollectionNotNull()
    {
        List<(long, MidiEvent)> events = [];
        AddNote(events, channel: 0, noteNumber: 60, velocity: 100, start: 0, length: 480);

        string path = Write(MidiFileFormat.MultiTrack, Track(events));
        MidiProject project = MidiFileLoader.Load(path);

        TrackInfo track = project.Tracks.Should().ContainSingle().Subject;
        track.ControllerValues.Should().NotBeNull();
        track.ControllerValues.Should().BeEmpty();
        track.ChannelPressure.Should().BeNull();
    }

    // ---- 11. Malformed input ----------------------------------------------------------------

    [Fact]
    public void TruncatedFileFailsWithAnActionableDomainErrorAndNoLibraryException()
    {
        List<(long, MidiEvent)> events = [];
        for (int i = 0; i < 64; i++)
        {
            AddNote(events, channel: 0, noteNumber: 60 + (i % 12), velocity: 100, start: i * 120, length: 100);
        }

        string good = Write(MidiFileFormat.MultiTrack, Track(events));
        byte[] bytes = File.ReadAllBytes(good);

        // Keep the header chunk and the start of the track chunk's data: the MTrk length field now
        // promises far more than the file holds.
        string truncated = NewTempPath("truncated");
        File.WriteAllBytes(truncated, bytes[..26]);

        Action load = () => MidiFileLoader.Load(truncated);

        MidiFileLoadException failure = load.Should().Throw<MidiFileLoadException>().Which;

        // The domain boundary holds: nothing above the loader has to know DryWetMIDI exists to
        // handle a bad file.
        failure.Should().NotBeAssignableTo<MidiException>();
        failure.InnerException.Should().BeAssignableTo<MidiException>();

        // Actionable: names the file and the failure, and does not invent a byte offset.
        failure.FilePath.Should().Be(truncated);
        // Which DryWetMIDI exception surfaces depends on exactly where the cut lands: slicing
        // inside a chunk body gives InvalidChunkSizeException, slicing mid-value gives
        // NotEnoughBytesException. Both are acceptable and the first is richer (it carries ChunkId,
        // ExpectedSize and ActualSize), so pin the contract - a named, reportable cause - not the
        // library's choice of type.
        failure.CauseTypeName.Should().BeOneOf(
            nameof(NotEnoughBytesException),
            nameof(InvalidChunkSizeException));
        failure.Message.Should().Contain(Path.GetFileName(truncated));
        failure.Message.Should().Contain("truncated");
        failure.Message.Should().NotContainEquivalentOf("offset");
    }

    [Fact]
    public void TryLoadReportsTheMessageInsteadOfThrowing()
    {
        string truncated = NewTempPath("try-truncated");
        File.WriteAllBytes(truncated, [.. "MThd"u8.ToArray(), 0, 0, 0, 6, 0, 1, 0, 1, 1, 224, 0x4D]);

        bool loaded = MidiFileLoader.TryLoad(truncated, out MidiProject? project, out string? error);

        loaded.Should().BeFalse();
        project.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
        error.Should().Contain(Path.GetFileName(truncated));
    }

    [Fact]
    public void AFileThatIsNotMidiAtAllFailsWithADomainError()
    {
        string notMidi = NewTempPath("not-midi");
        File.WriteAllText(notMidi, "this is plainly not a MIDI file, but it is long enough to read");

        Action load = () => MidiFileLoader.Load(notMidi);

        MidiFileLoadException failure = load.Should().Throw<MidiFileLoadException>().Which;
        failure.CauseTypeName.Should().NotBeNullOrWhiteSpace();
        failure.Message.Should().Contain(Path.GetFileName(notMidi));
    }

    [Fact]
    public void AMissingFileFailsWithADomainError()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"midirestyle-missing-{Guid.NewGuid():N}.mid");

        Action load = () => MidiFileLoader.Load(missing);

        load.Should().Throw<MidiFileLoadException>()
            .Which.Message.Should().Contain(Path.GetFileName(missing));
    }

    // ---- fixture plumbing -------------------------------------------------------------------

    /// <summary>
    /// Builds a track chunk from absolute-time events, converting to the delta times the format
    /// actually stores. Stable ordering keeps same-tick events in the order they were added, which is
    /// what makes a zero-length note expressible.
    /// </summary>
    /// <summary>
    /// Writes a Format 1 file containing exactly ONE track chunk that carries channels 0, 1 and 9.
    /// </summary>
    /// <remarks>
    /// Written as raw bytes because DryWetMIDI's <c>MidiFile.Write</c> splits a multi-channel chunk
    /// into one chunk per channel under <see cref="MidiFileFormat.MultiTrack"/>, which is exactly
    /// the shape this fixture must avoid.
    /// </remarks>
    private string WriteRawFormat1WithOneMultiChannelTrack()
    {
        // Variable-length quantity, as the SMF spec encodes delta times.
        static IEnumerable<byte> Vlq(uint value)
        {
            Stack<byte> stack = new();
            stack.Push((byte)(value & 0x7F));
            value >>= 7;
            while (value > 0)
            {
                stack.Push((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }

            return stack;
        }

        List<byte> track = [];
        void Event(uint delta, byte status, byte data1, byte data2)
        {
            track.AddRange(Vlq(delta));
            track.AddRange([status, data1, data2]);
        }

        Event(0, 0x90, 60, 100);   // note on,  channel 0
        Event(0, 0x91, 48, 90);    // note on,  channel 1
        Event(0, 0x99, 36, 110);   // note on,  channel 9 (drums)
        Event(120, 0x89, 36, 0);   // note off, channel 9  - shortest, so it ends first
        Event(360, 0x80, 60, 0);   // note off, channel 0  - 120 + 360 = 480
        Event(0, 0x81, 48, 0);     // note off, channel 1
        track.AddRange([0x00, 0xFF, 0x2F, 0x00]);   // end of track

        static byte[] BigEndian32(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
        static byte[] BigEndian16(int v) => [(byte)(v >> 8), (byte)v];

        List<byte> file =
        [
            .. "MThd"u8.ToArray(),
            .. BigEndian32(6),
            .. BigEndian16(1),      // format 1
            .. BigEndian16(1),      // one track chunk - the whole point
            .. BigEndian16(480),    // 480 PPQN
            .. "MTrk"u8.ToArray(),
            .. BigEndian32(track.Count),
            .. track,
        ];

        string path = NewTempPath("raw-format1-multichannel");
        File.WriteAllBytes(path, [.. file]);
        return path;
    }

    private static TrackChunk Track(IEnumerable<(long Time, MidiEvent Event)> events)
    {
        TrackChunk chunk = new();
        long previous = 0;

        foreach ((long time, MidiEvent midiEvent) in events.OrderBy(e => e.Time))
        {
            midiEvent.DeltaTime = time - previous;
            previous = time;
            chunk.Events.Add(midiEvent);
        }

        return chunk;
    }

    private static void AddNote(
        List<(long Time, MidiEvent Event)> events,
        int channel,
        int noteNumber,
        int velocity,
        long start,
        long length)
    {
        FourBitNumber ch = (FourBitNumber)channel;
        events.Add((start, new NoteOnEvent((SevenBitNumber)noteNumber, (SevenBitNumber)velocity) { Channel = ch }));
        events.Add((start + length, new NoteOffEvent((SevenBitNumber)noteNumber, SevenBitNumber.MinValue) { Channel = ch }));
    }

    private string Write(MidiFileFormat format, params TrackChunk[] chunks) =>
        Write(format, chunks, new TicksPerQuarterNoteTimeDivision(480));

    private string Write(
        MidiFileFormat format,
        IEnumerable<TrackChunk> chunks,
        DwmTimeDivision division)
    {
        MidiFile file = new(chunks) { TimeDivision = division };
        string path = NewTempPath(format.ToString());
        file.Write(path, overwriteFile: true, format);
        return path;
    }

    private string NewTempPath(string hint)
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"midirestyle-{hint}-{Guid.NewGuid():N}.mid");
        _tempFiles.Add(path);
        return path;
    }
}
