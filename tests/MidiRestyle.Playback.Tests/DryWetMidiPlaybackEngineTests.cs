using Melanchall.DryWetMidi.Core;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Output;
using MidiRestyle.Playback;

namespace MidiRestyle.Playback.Tests;

/// <summary>
/// The real engine, driving real DryWetMIDI <c>Playback</c> objects into a recording device.
/// </summary>
/// <remarks>
/// <para>
/// No MIDI hardware is involved, and none is needed: <c>Playback</c> takes DryWetMIDI's own
/// <c>IOutputDevice</c> interface, so a recorder can stand in for a synth while the clock, the
/// seeking and the value tracking are all genuine. What these tests cannot cover is the driver
/// itself - opening a port, and whether anything is audible.
/// </para>
/// <para>
/// These are the only wall-clock tests in the suite. Every timing assertion here is a
/// catastrophic-regression guard with a ceiling far above the target, not a benchmark: a 50 ms
/// assertion in this repository has already flaked at 51.7 ms under parallel test load.
/// </para>
/// </remarks>
public class DryWetMidiPlaybackEngineTests(ITestOutputHelper output)
{
    private const int OriginalChannel = 0;
    private const int RestyledChannel = 1;

    /// <summary>Eight quarter notes at the default tempo, so each side lasts four seconds.</summary>
    private static PlaybackSequences FourSecondSequences() =>
        MidiFixtures.Sequences(
            MidiFixtures.Notes(OriginalChannel, quarterNotes: 8),
            MidiFixtures.Notes(RestyledChannel, quarterNotes: 8, firstNoteNumber: 64),
            RestyledChannel);

    private static DryWetMidiPlaybackEngine Engine(
        RecordingOutputDevice device,
        bool ownsDevice = false) =>
        new(device, "Recording Device", reason: null, ownsDevice, MidiFixtures.ManagedClock());

    /// <summary>Waits for a condition, so no test sleeps longer than it has to.</summary>
    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && !condition())
        {
            Thread.Sleep(5);
        }
    }

    // ---- loading ------------------------------------------------------------------------------

    [Fact]
    public void LoadPreparesTheDeviceAndReportsWhatItLoaded()
    {
        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);

        engine.IsLoaded.Should().BeFalse();

        engine.Load(FourSecondSequences());

        engine.IsAvailable.Should().BeTrue();
        engine.IsLoaded.Should().BeTrue();
        engine.IsPlaying.Should().BeFalse();
        engine.DeviceName.Should().Be("Recording Device");
        engine.Reason.Should().NotBeNullOrWhiteSpace();
        engine.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(50));
        device.PrepareCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The stop sequence must reach the allocated pitch-bend channels - and, because a note hanging
    /// on an untouched track is just as stuck, every other channel either side sounds on.
    /// </summary>
    [Fact]
    public void StopChannelsCoverBothSidesNotJustTheAllocatedOnes()
    {
        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);

        engine.Load(FourSecondSequences());

        engine.StopChannels.Should().Equal(OriginalChannel, RestyledChannel);
    }

    [Fact]
    public void ReloadingKeepsThePlayheadWhereItWas()
    {
        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);

        engine.Load(FourSecondSequences());
        engine.Seek(TimeSpan.FromMilliseconds(1500));

        engine.Load(FourSecondSequences());

        engine.Position.Should().BeCloseTo(TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(50));
    }

    // ---- the stop sequence, on a real device -------------------------------------------------

    [Fact]
    public void StopSendsCc123AndABendResetTo8192ToEveryAllocatedChannel()
    {
        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);
        engine.Load(FourSecondSequences());
        device.Clear();

        engine.Stop();

        device.ChannelsSentAllNotesOff().Should().Equal(engine.StopChannels);
        device.ChannelsSentBendReset().Should().Equal(engine.StopChannels);

        foreach (ControlChangeEvent cc in device.Events.OfType<ControlChangeEvent>()
            .Where(e => e.ControlNumber == PitchBendEncoder.CcAllNotesOff))
        {
            cc.ControlValue.Should().Be((Melanchall.DryWetMidi.Common.SevenBitNumber)0);
        }

        foreach (PitchBendEvent bend in device.Events.OfType<PitchBendEvent>())
        {
            bend.PitchValue.Should().Be(PitchBendEncoder.CenterBendValue);
        }
    }

    [Fact]
    public void PauseAlsoSendsTheStopSequence()
    {
        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);
        engine.Load(FourSecondSequences());
        device.Clear();

        engine.Pause();

        device.ChannelsSentAllNotesOff().Should().Equal(engine.StopChannels);
        device.ChannelsSentBendReset().Should().Equal(engine.StopChannels);
    }

    [Fact]
    public void ASwitchSendsTheStopSequenceToEveryAllocatedChannelBeforeStartingTheOtherSide()
    {
        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);
        engine.Load(FourSecondSequences());
        engine.Play();
        WaitUntil(() => engine.Position > TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
        device.Clear();

        engine.Toggle();

        device.ChannelsSentAllNotesOff().Should().Equal(engine.StopChannels);
        device.ChannelsSentBendReset().Should().Contain(engine.StopChannels);

        // The stop sequence comes before anything the arriving side plays.
        IReadOnlyList<MidiEvent> events = device.Events;
        int lastAllNotesOff = events
            .Select((e, i) => (Event: e, Index: i))
            .Where(x => x.Event is ControlChangeEvent cc
                && cc.ControlNumber == PitchBendEncoder.CcAllNotesOff)
            .Max(x => x.Index);
        int firstNoteOn = events
            .Select((e, i) => (Event: e, Index: i))
            .Where(x => x.Event is NoteOnEvent)
            .Select(x => (int?)x.Index)
            .FirstOrDefault() ?? int.MaxValue;

        firstNoteOn.Should().BeGreaterThan(lastAllNotesOff);

        engine.Pause();
    }

    // ---- the switch, with a real clock -------------------------------------------------------

    [Fact]
    public void ASwitchPreservesThePlayhead()
    {
        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);
        engine.Load(FourSecondSequences());
        engine.Play();
        WaitUntil(() => engine.Position > TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(3));

        TimeSpan before = engine.Position;
        engine.Toggle();
        TimeSpan after = engine.Position;

        engine.ActiveSide.Should().Be(PlaybackSide.Restyled);
        before.Should().BeGreaterThan(TimeSpan.FromMilliseconds(400));

        // The tolerance is the switch itself plus one clock tick of the managed timer, not a claim
        // about how accurate the seek is.
        after.Should().BeCloseTo(before, TimeSpan.FromMilliseconds(100));

        engine.Pause();
    }

    [Fact]
    public void ExactlyOneSideSoundsAfterRepeatedToggling()
    {
        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);
        engine.Load(FourSecondSequences());
        engine.Play();

        for (int i = 0; i < 7; i++)
        {
            engine.Toggle();
        }

        engine.ActiveSide.Should().Be(PlaybackSide.Restyled);
        engine.IsPlaying.Should().BeTrue();

        // Clear, then let it run: only the restyled side's channel may sound. If both playbacks were
        // still running - the failure this guards - the original's channel would keep emitting too.
        device.Clear();
        WaitUntil(
            () => device.Events.OfType<NoteOnEvent>().Any(),
            TimeSpan.FromSeconds(2));
        Thread.Sleep(300);

        IReadOnlyList<int> sounding =
        [
            .. device.Events.OfType<NoteOnEvent>()
                .Select(e => (int)e.Channel)
                .Distinct()
                .Order(),
        ];

        sounding.Should().Equal(RestyledChannel);

        engine.Pause();
    }

    /// <summary>
    /// The measured switch gap. The target is <b>under 30 ms</b>; the assertion is against a far
    /// looser ceiling on purpose. A hard wall-clock assertion inside <c>dotnet test</c> is a
    /// catastrophic-regression guard, not a benchmark - the test assemblies run in parallel, and a
    /// 50 ms assertion in this repository has already flaked at 51.7 ms.
    /// </summary>
    [Fact]
    public void TheSwitchGapIsMeasuredAndWellUnderTheRegressionCeiling()
    {
        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);
        engine.Load(FourSecondSequences());
        engine.Play();
        WaitUntil(() => engine.Position > TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));

        List<double> gaps = [];

        for (int i = 0; i < 10; i++)
        {
            engine.Toggle();
            gaps.Add(engine.LastSwitchGap.TotalMilliseconds);
        }

        engine.Pause();

        output.WriteLine(
            $"A/B switch gap over {gaps.Count} switches: "
            + $"min {gaps.Min():0.###} ms, median {Median(gaps):0.###} ms, max {gaps.Max():0.###} ms "
            + "(target: under 30 ms).");

        gaps.Max().Should().BeLessThan(300);
    }

    private static double Median(List<double> values)
    {
        List<double> sorted = [.. values.Order()];
        int middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    // ---- playhead notifications ---------------------------------------------------------------

    /// <summary>
    /// A dense sequence - thousands of events in half a second - must not produce thousands of
    /// playhead notifications. This is the concrete form of "never raise playhead updates per MIDI
    /// event": the exact rate is pinned deterministically in
    /// <see cref="PlayheadThrottleTests"/>, and what is asserted here is that the engine actually
    /// routes its notifications through that throttle.
    /// </summary>
    [Fact]
    public void PlayheadNotificationsAreRateLimitedNotRaisedPerMidiEvent()
    {
        const int DenseEvents = 4_000;

        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);

        int notifications = 0;
        engine.PlayheadMoved += (_, _) => Interlocked.Increment(ref notifications);

        PlaybackSequences sequences = new(
            MidiFixtures.ToBytes(MidiFixtures.Dense(OriginalChannel, DenseEvents, totalTicks: 480)),
            MidiFixtures.ToBytes(MidiFixtures.Notes(RestyledChannel, quarterNotes: 1)),
            MidiFixtures.Allocation(RestyledChannel));

        engine.Load(sequences);
        engine.Play();

        WaitUntil(
            () => device.Events.OfType<ControlChangeEvent>().Count(e => e.ControlNumber == 7)
                >= DenseEvents,
            TimeSpan.FromSeconds(5));

        engine.Pause();

        int delivered = device.Events.OfType<ControlChangeEvent>().Count(e => e.ControlNumber == 7);
        int seen = Volatile.Read(ref notifications);

        output.WriteLine($"{delivered} MIDI events played, {seen} playhead notifications raised.");

        delivered.Should().BeGreaterThan(DenseEvents / 2);
        seen.Should().BeLessThan(delivered / 5);
        seen.Should().BeLessThan(400);
    }

    [Fact]
    public void PlayheadNotificationsCarryThePositionAndTheActiveSide()
    {
        using RecordingOutputDevice device = new();
        using DryWetMidiPlaybackEngine engine = Engine(device);

        List<PlayheadEventArgs> seen = [];
        engine.PlayheadMoved += (_, e) =>
        {
            lock (seen)
            {
                seen.Add(e);
            }
        };

        engine.Load(FourSecondSequences());
        engine.Seek(TimeSpan.FromSeconds(2));
        engine.SwitchTo(PlaybackSide.Restyled);

        PlayheadEventArgs last;
        lock (seen)
        {
            seen.Should().NotBeEmpty();
            last = seen[^1];
        }

        last.Side.Should().Be(PlaybackSide.Restyled);
        last.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(50));
        last.Position.Should().BeCloseTo(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100));
    }

    // ---- lifetime -----------------------------------------------------------------------------

    [Fact]
    public void DisposeSilencesTheSynthAndReleasesTheDeviceItOwns()
    {
        RecordingOutputDevice device = new();
        DryWetMidiPlaybackEngine engine = Engine(device, ownsDevice: true);
        engine.Load(FourSecondSequences());
        engine.Play();
        device.Clear();

        engine.Dispose();

        device.ChannelsSentAllNotesOff().Should().Equal(OriginalChannel, RestyledChannel);
        device.ChannelsSentBendReset().Should().Contain([OriginalChannel, RestyledChannel]);
        device.IsDisposed.Should().BeTrue();
        device.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void DoubleDisposeIsSafeAndReleasesTheDeviceOnlyOnce()
    {
        RecordingOutputDevice device = new();
        DryWetMidiPlaybackEngine engine = Engine(device, ownsDevice: true);
        engine.Load(FourSecondSequences());

        Action twice = () =>
        {
            engine.Dispose();
            engine.Dispose();
        };

        twice.Should().NotThrow();
        device.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void ADeviceItDoesNotOwnIsLeftAlone()
    {
        using RecordingOutputDevice device = new();
        DryWetMidiPlaybackEngine engine = Engine(device, ownsDevice: false);
        engine.Load(FourSecondSequences());

        engine.Dispose();

        device.DisposeCount.Should().Be(0);
    }

    [Fact]
    public void TransportCommandsAfterDisposeAreIgnoredRatherThanThrowing()
    {
        RecordingOutputDevice device = new();
        DryWetMidiPlaybackEngine engine = Engine(device, ownsDevice: true);
        engine.Load(FourSecondSequences());
        engine.Dispose();

        Action transport = () =>
        {
            engine.Play();
            engine.Pause();
            engine.Stop();
            engine.Seek(TimeSpan.FromSeconds(1));
            engine.SwitchTo(PlaybackSide.Restyled);
            engine.Toggle();
        };

        transport.Should().NotThrow();
        engine.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void LoadingAfterDisposeThrowsRatherThanSilentlyDoingNothing()
    {
        RecordingOutputDevice device = new();
        DryWetMidiPlaybackEngine engine = Engine(device, ownsDevice: true);
        engine.Dispose();

        Action load = () => engine.Load(FourSecondSequences());

        load.Should().Throw<ObjectDisposedException>();
    }
}
