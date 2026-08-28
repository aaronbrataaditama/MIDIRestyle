using MidiRestyle.Core.Output;
using MidiRestyle.Playback;
using DomainChannelEvent = MidiRestyle.Core.Output.ChannelEvent;

namespace MidiRestyle.Playback.Tests;

/// <summary>
/// The A/B switch semantics, asserted on the exact call order and the exact MIDI it emits.
/// </summary>
/// <remarks>
/// Nothing here needs a MIDI device, which is the point: the rules that keep notes from hanging and
/// the next sequence from playing detuned are decisions about sequencing, and decisions are testable.
/// </remarks>
public class AbSwitcherTests
{
    private static readonly int[] Channels = [2, 3, 5];

    /// <summary>
    /// The stop sequence for one channel: CC123, then pitch bend back to centre. 8192 goes on the
    /// wire LSB-first, so 8192 is data1 = 0, data2 = 64.
    /// </summary>
    private static IEnumerable<DomainChannelEvent> Expected(int channel) =>
    [
        new DomainChannelEvent(ChannelEventKind.ControlChange, channel, PitchBendEncoder.CcAllNotesOff, 0),
        new DomainChannelEvent(ChannelEventKind.PitchBend, channel, 0, 64),
    ];

    private static IEnumerable<DomainChannelEvent> ExpectedForAll(params int[] channels) =>
        channels.SelectMany(Expected);

    private sealed record Harness(
        AbSwitcher Switcher,
        FakeSequencePlayer Original,
        FakeSequencePlayer Restyled,
        RecordingMidiSink Sink,
        List<string> Log);

    private static Harness Build(TimeSpan? duration = null, params int[] channels)
    {
        List<string> log = [];
        TimeSpan length = duration ?? TimeSpan.FromSeconds(10);

        FakeSequencePlayer original = new("original", length, log);
        FakeSequencePlayer restyled = new("restyled", length, log);
        RecordingMidiSink sink = new(log);

        AbSwitcher switcher = new(
            original,
            restyled,
            sink,
            channels.Length == 0 ? Channels : channels);

        return new Harness(switcher, original, restyled, sink, log);
    }

    // ---- the stop sequence --------------------------------------------------------------------

    [Fact]
    public void StopSendsCc123AndABendResetTo8192ToEveryAllocatedChannel()
    {
        Harness h = Build();

        h.Switcher.Stop();

        h.Sink.Events.Should().Equal(ExpectedForAll(2, 3, 5));

        // Stated the other way round, because these are the two failures the pair prevents:
        // without CC123 notes hang, and without the bend reset the next sequence plays detuned.
        h.Sink.Events.Where(e => e.Kind == ChannelEventKind.ControlChange)
            .Select(e => e.Channel).Should().Equal(2, 3, 5);
        h.Sink.Events.Where(e => e.Kind == ChannelEventKind.PitchBend)
            .Select(e => (e.Data2 * 128) + e.Data1)
            .Should().AllSatisfy(v => v.Should().Be(PitchBendEncoder.CenterBendValue));
    }

    [Fact]
    public void PauseSendsTheSameStopSequenceAndKeepsThePosition()
    {
        Harness h = Build();
        h.Switcher.Start();
        h.Original.Advance(TimeSpan.FromSeconds(2));

        h.Switcher.Pause();

        h.Sink.Events.Should().Equal(ExpectedForAll(2, 3, 5));
        h.Switcher.IsPlaying.Should().BeFalse();
        h.Switcher.Position.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void StopRewindsBothSidesSoEitherCanStartFromTheTop()
    {
        Harness h = Build();
        h.Switcher.Start();
        h.Original.Advance(TimeSpan.FromSeconds(4));

        h.Switcher.Stop();

        h.Original.CurrentTime.Should().Be(TimeSpan.Zero);
        h.Restyled.CurrentTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TheStopSequenceReachesEveryChannelEvenWhenThereIsOnlyOne()
    {
        Harness h = Build(null, 7);

        h.Switcher.Stop();

        h.Sink.Events.Should().Equal(ExpectedForAll(7));
    }

    [Fact]
    public void DuplicateChannelsAreCollapsedSoNoChannelIsSilencedTwice()
    {
        Harness h = Build(null, 4, 4, 1, 1);

        h.Switcher.StopChannels.Should().Equal(1, 4);

        h.Switcher.Stop();

        h.Sink.Events.Should().Equal(ExpectedForAll(1, 4));
    }

    // ---- the switch --------------------------------------------------------------------------

    [Fact]
    public void ASwitchSendsTheStopSequenceBeforeStartingTheOtherSide()
    {
        Harness h = Build();
        h.Switcher.Start();
        h.Original.Advance(TimeSpan.FromMilliseconds(1234));
        h.Log.Clear();

        h.Switcher.SwitchTo(PlaybackSide.Restyled);

        h.Log.Should().Equal(
            "original.Stop",
            "sink.ControlChange(ch2,123,0)",
            "sink.PitchBend(ch2,0,64)",
            "sink.ControlChange(ch3,123,0)",
            "sink.PitchBend(ch3,0,64)",
            "sink.ControlChange(ch5,123,0)",
            "sink.PitchBend(ch5,0,64)",
            "restyled.MoveToTime(1234)",
            "restyled.Start");
    }

    [Fact]
    public void ASwitchSendsTheStopSequenceToEveryAllocatedChannel()
    {
        Harness h = Build(null, 0, 1, 2, 3, 4, 6, 7);
        h.Switcher.Start();
        h.Sink.Clear();

        h.Switcher.Toggle();

        h.Sink.Events.Should().Equal(ExpectedForAll(0, 1, 2, 3, 4, 6, 7));
    }

    [Fact]
    public void ASwitchPreservesThePlayhead()
    {
        Harness h = Build();
        h.Switcher.Start();
        h.Original.Advance(TimeSpan.FromMilliseconds(7321));

        h.Switcher.Toggle();

        h.Switcher.ActiveSide.Should().Be(PlaybackSide.Restyled);
        h.Switcher.Position.Should().Be(TimeSpan.FromMilliseconds(7321));

        h.Restyled.Advance(TimeSpan.FromMilliseconds(500));
        h.Switcher.Toggle();

        h.Switcher.ActiveSide.Should().Be(PlaybackSide.Original);
        h.Switcher.Position.Should().Be(TimeSpan.FromMilliseconds(7821));
    }

    [Fact]
    public void ASwitchWhilePlayingLeavesTheNewSidePlaying()
    {
        Harness h = Build();
        h.Switcher.Start();

        h.Switcher.Toggle();

        h.Restyled.IsRunning.Should().BeTrue();
        h.Original.IsRunning.Should().BeFalse();
        h.Switcher.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void ASwitchWhilePausedDoesNotStartPlaying()
    {
        Harness h = Build();

        h.Switcher.Toggle();

        h.Restyled.IsRunning.Should().BeFalse();
        h.Restyled.StartCount.Should().Be(0);
        h.Switcher.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public void SwitchingToTheAlreadyActiveSideDoesNothing()
    {
        Harness h = Build();
        h.Switcher.Start();
        h.Log.Clear();

        h.Switcher.SwitchTo(PlaybackSide.Original);

        h.Log.Should().BeEmpty();
        h.Original.IsRunning.Should().BeTrue();
        h.Original.StopCount.Should().Be(0);
        h.Sink.Events.Should().BeEmpty();
    }

    // ---- repeated switching ------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(50)]
    public void ExactlyOneSideIsRunningAfterAnyNumberOfToggles(int toggles)
    {
        Harness h = Build();
        h.Switcher.Start();

        for (int i = 0; i < toggles; i++)
        {
            h.Switcher.Toggle();
        }

        int running = new[] { h.Original, h.Restyled }.Count(p => p.IsRunning);
        running.Should().Be(1);
        h.Switcher.Active.IsRunning.Should().BeTrue();
        h.Switcher.Inactive.IsRunning.Should().BeFalse();
        h.Switcher.ActiveSide.Should().Be(
            toggles % 2 == 0 ? PlaybackSide.Original : PlaybackSide.Restyled);
    }

    [Fact]
    public void RepeatedTogglingAccumulatesNoStateBeyondTheOneStopSequencePerSwitch()
    {
        Harness h = Build();
        h.Switcher.Start();
        h.Original.Advance(TimeSpan.FromSeconds(3));
        h.Sink.Clear();

        const int Toggles = 40;
        for (int i = 0; i < Toggles; i++)
        {
            h.Switcher.Toggle();
        }

        // Two events per channel per switch, and not one more: no doubled sends, no leftovers.
        h.Sink.Events.Should().HaveCount(Toggles * Channels.Length * 2);

        // The playhead is exactly where it started; nothing drifted or reset.
        h.Switcher.Position.Should().Be(TimeSpan.FromSeconds(3));

        // Each side was started exactly as often as it became active, and stopped as often as it
        // stopped being active. Anything else means a stale player was left running.
        h.Original.StartCount.Should().Be(1 + (Toggles / 2));
        h.Restyled.StartCount.Should().Be(Toggles / 2);
        (h.Original.StopCount + h.Restyled.StopCount).Should().Be(Toggles);
    }

    [Fact]
    public void RepeatedTogglingWhilePausedNeverStartsAnything()
    {
        Harness h = Build();

        for (int i = 0; i < 25; i++)
        {
            h.Switcher.Toggle();
        }

        h.Original.StartCount.Should().Be(0);
        h.Restyled.StartCount.Should().Be(0);
        h.Switcher.IsPlaying.Should().BeFalse();
    }

    // ---- seeking ------------------------------------------------------------------------------

    [Fact]
    public void SeekMovesBothSidesSoALaterSwitchLandsInTheSamePlace()
    {
        Harness h = Build(TimeSpan.FromSeconds(10));

        h.Switcher.Seek(TimeSpan.FromSeconds(6));

        h.Original.CurrentTime.Should().Be(TimeSpan.FromSeconds(6));
        h.Restyled.CurrentTime.Should().Be(TimeSpan.FromSeconds(6));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    [InlineData(99, 10)]
    public void SeekClampsToTheSequence(int requestedSeconds, int expectedSeconds)
    {
        Harness h = Build(TimeSpan.FromSeconds(10));

        h.Switcher.Seek(TimeSpan.FromSeconds(requestedSeconds));

        h.Switcher.Position.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    // ---- lifetime -----------------------------------------------------------------------------

    [Fact]
    public void DisposeSilencesEveryChannelThenDisposesBothSides()
    {
        Harness h = Build(null, 2);
        h.Switcher.Start();

        h.Switcher.Dispose();

        h.Log.Should().Equal(
            "original.Start",
            "original.Stop",
            "restyled.Stop",
            "sink.ControlChange(ch2,123,0)",
            "sink.PitchBend(ch2,0,64)",
            "original.Dispose",
            "restyled.Dispose");
    }

    [Fact]
    public void DoubleDisposeIsSafeAndDoesNotResendAnything()
    {
        Harness h = Build();
        h.Switcher.Dispose();
        int after = h.Sink.Events.Count;

        Action again = h.Switcher.Dispose;

        again.Should().NotThrow();
        h.Sink.Events.Should().HaveCount(after);
        h.Original.DisposeCount.Should().Be(1);
        h.Restyled.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void TransportCommandsAfterDisposeThrowRatherThanSilentlyDoingNothing()
    {
        Harness h = Build();
        h.Switcher.Dispose();

        Action start = () => h.Switcher.Start();
        Action pause = () => h.Switcher.Pause();
        Action toggle = () => h.Switcher.Toggle();

        start.Should().Throw<ObjectDisposedException>();
        pause.Should().Throw<ObjectDisposedException>();
        toggle.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void RejectsNullCollaborators()
    {
        FakeSequencePlayer player = new("p", TimeSpan.FromSeconds(1));
        RecordingMidiSink sink = new();

        Action noOriginal = () => _ = new AbSwitcher(null!, player, sink, Channels);
        Action noRestyled = () => _ = new AbSwitcher(player, null!, sink, Channels);
        Action noSink = () => _ = new AbSwitcher(player, player, null!, Channels);
        Action noChannels = () => _ = new AbSwitcher(player, player, sink, null!);

        noOriginal.Should().Throw<ArgumentNullException>();
        noRestyled.Should().Throw<ArgumentNullException>();
        noSink.Should().Throw<ArgumentNullException>();
        noChannels.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// The switch gap with the device stubbed out. This measures the sequencing overhead alone -
    /// what the real device adds is measured in <see cref="DryWetMidiPlaybackEngineTests"/> - and it
    /// should be microseconds. The ceiling is deliberately absurd relative to the target so it only
    /// ever fires on a catastrophic regression, never on a slow build agent.
    /// </summary>
    [Fact]
    public void TheSwitchGapIsMeasured()
    {
        Harness h = Build();
        h.Switcher.Start();

        h.Switcher.Toggle();

        h.Switcher.LastSwitchGap.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        h.Switcher.LastSwitchGap.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }
}
