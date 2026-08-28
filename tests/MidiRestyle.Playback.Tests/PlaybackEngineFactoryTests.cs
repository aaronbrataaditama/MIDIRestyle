using MidiRestyle.Playback;

namespace MidiRestyle.Playback.Tests;

/// <summary>
/// Engine selection, tested against a fake probe rather than against whatever MIDI hardware the
/// machine happens to have. A build agent has none; this developer machine has the Microsoft GS
/// Wavetable Synth. Both must produce the same test result.
/// </summary>
public class PlaybackEngineFactoryTests
{
    [Fact]
    public void PicksTheNullEngineWhenNoDeviceIsAvailableAndSaysWhy()
    {
        FakeDeviceProbe probe = new(MidiDeviceProbeResult.None(
            "No MIDI output device is installed, so playback is disabled."));

        using IPlaybackEngine engine = PlaybackEngineFactory.Create(probe);

        engine.Should().BeOfType<NullPlaybackEngine>();
        engine.IsAvailable.Should().BeFalse();
        engine.DeviceName.Should().BeNull();
        engine.Reason.Should().Be("No MIDI output device is installed, so playback is disabled.");
        probe.ProbeCount.Should().Be(1);
    }

    [Fact]
    public void PicksTheRealEngineWhenADeviceIsAvailable()
    {
        using RecordingOutputDevice device = new();
        FakeDeviceProbe probe = new(MidiDeviceProbeResult.Found(device, "Test Synth"));

        using IPlaybackEngine engine = PlaybackEngineFactory.Create(probe, MidiFixtures.ManagedClock());

        engine.Should().BeOfType<DryWetMidiPlaybackEngine>();
        engine.IsAvailable.Should().BeTrue();
        engine.DeviceName.Should().Be("Test Synth");
        engine.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EveryEngineItCanReturnGivesANonEmptyReason()
    {
        using RecordingOutputDevice device = new();

        using IPlaybackEngine real = PlaybackEngineFactory.Create(
            new FakeDeviceProbe(MidiDeviceProbeResult.Found(device, "Test Synth")),
            MidiFixtures.ManagedClock());
        using IPlaybackEngine none = PlaybackEngineFactory.Create(
            new FakeDeviceProbe(MidiDeviceProbeResult.None("Nothing here.")));

        real.Reason.Should().NotBeNullOrWhiteSpace();
        none.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ANoneResultRefusesToBeCreatedWithoutAReason()
    {
        Action blank = () => MidiDeviceProbeResult.None("  ");

        blank.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The real probe, run against whatever this machine is. The assertion cannot be "a device was
    /// found" - that depends on the machine - but it can be the invariant that actually matters:
    /// probing never throws, and whatever it decides, it says why.
    /// </summary>
    [Fact]
    public void TheRealProbeNeverThrowsAndAlwaysStatesItsReason()
    {
        MidiDeviceProbeResult result = null!;
        Action probe = () => result = new OutputDeviceProbe().Probe();

        probe.Should().NotThrow();

        try
        {
            result.Reason.Should().NotBeNullOrWhiteSpace();
            result.HasDevice.Should().Be(result.Device is not null);

            if (result.HasDevice)
            {
                result.DeviceName.Should().NotBeNullOrWhiteSpace();
            }
            else
            {
                result.DeviceName.Should().BeNull();
            }
        }
        finally
        {
            // The probe opens a handle on the device it chose. Leaking it would hold the port
            // against every other application on the machine for the life of the test run.
            (result.Device as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// And the factory, on the real machine. Same reasoning: the engine it picks depends on the
    /// hardware, but "you always get an engine, and it tells you what it is" does not.
    /// </summary>
    [Fact]
    public void TheDefaultFactoryAlwaysReturnsAUsableEngine()
    {
        IPlaybackEngine engine = null!;
        Action create = () => engine = PlaybackEngineFactory.Create();

        create.Should().NotThrow();

        try
        {
            engine.Should().NotBeNull();
            engine.Reason.Should().NotBeNullOrWhiteSpace();
            engine.IsPlaying.Should().BeFalse();
            engine.IsLoaded.Should().BeFalse();
            engine.ActiveSide.Should().Be(PlaybackSide.Original);
        }
        finally
        {
            engine?.Dispose();
        }
    }
}
