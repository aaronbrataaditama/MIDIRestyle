using MidiRestyle.Core.Io;
using MidiRestyle.Playback;

namespace MidiRestyle.Playback.Tests;

/// <summary>
/// The null engine's whole job is that nothing above it has to know it is null. So every member has
/// to be callable, and it has to say why it cannot play.
/// </summary>
public class NullPlaybackEngineTests
{
    private static PlaybackSequences Sequences() =>
        MidiFixtures.Sequences(
            MidiFixtures.Notes(channel: 0, quarterNotes: 2),
            MidiFixtures.Notes(channel: 1, quarterNotes: 2),
            2,
            3);

    [Fact]
    public void EveryMemberIsSafeToCall()
    {
        NullPlaybackEngine engine = new();

        Action everything = () =>
        {
            engine.Load(Sequences());
            engine.Play();
            engine.Pause();
            engine.Seek(TimeSpan.FromSeconds(3));
            engine.SwitchTo(PlaybackSide.Restyled);
            engine.Toggle();
            engine.Stop();
            engine.Dispose();
        };

        everything.Should().NotThrow();
    }

    [Fact]
    public void ReportsNotPlaying()
    {
        NullPlaybackEngine engine = new();
        engine.Load(Sequences());
        engine.Play();

        engine.IsPlaying.Should().BeFalse();
        engine.IsAvailable.Should().BeFalse();
        engine.DeviceName.Should().BeNull();
        engine.Duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GivesANonEmptyReason()
    {
        new NullPlaybackEngine().Reason.Should().NotBeNullOrWhiteSpace();
        new NullPlaybackEngine("There is no synth on this box.").Reason
            .Should().Be("There is no synth on this box.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FallsBackToADefaultReasonRatherThanASilentBlank(string? reason) =>
        new NullPlaybackEngine(reason).Reason.Should().Be(NullPlaybackEngine.DefaultReason);

    [Fact]
    public void ReportsTheChannelsAStopSequenceWouldReach()
    {
        NullPlaybackEngine engine = new();
        engine.StopChannels.Should().BeEmpty();

        engine.Load(Sequences());

        engine.StopChannels.Should().Equal(2, 3);
    }

    [Fact]
    public void TracksTheSelectedSideSoExactlyOneIsEverActive()
    {
        NullPlaybackEngine engine = new();

        engine.ActiveSide.Should().Be(PlaybackSide.Original);
        engine.Toggle().Should().Be(PlaybackSide.Restyled);
        engine.ActiveSide.Should().Be(PlaybackSide.Restyled);
        engine.Toggle().Should().Be(PlaybackSide.Original);

        engine.SwitchTo(PlaybackSide.Restyled);
        engine.ActiveSide.Should().Be(PlaybackSide.Restyled);
    }

    [Fact]
    public void SubscribingToPlayheadMovedIsSafeEvenThoughItNeverFires()
    {
        NullPlaybackEngine engine = new();
        int calls = 0;
        void Handler(object? sender, PlayheadEventArgs e) => calls++;

        Action subscribe = () =>
        {
            engine.PlayheadMoved += Handler;
            engine.Load(Sequences());
            engine.Play();
            engine.PlayheadMoved -= Handler;
        };

        subscribe.Should().NotThrow();
        calls.Should().Be(0);
    }

    [Fact]
    public void DoubleDisposeIsSafe()
    {
        NullPlaybackEngine engine = new();
        engine.Load(Sequences());

        Action twice = () =>
        {
            engine.Dispose();
            engine.Dispose();
        };

        twice.Should().NotThrow();
    }

    [Fact]
    public void RejectsANullSequenceRatherThanPretendingItLoaded()
    {
        NullPlaybackEngine engine = new();

        Action load = () => engine.Load(null!);

        load.Should().Throw<ArgumentNullException>();
        engine.IsLoaded.Should().BeFalse();
    }
}
