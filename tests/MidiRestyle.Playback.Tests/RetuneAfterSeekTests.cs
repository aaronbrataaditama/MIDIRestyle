using MidiRestyle.Core.Output;
using MidiRestyle.Playback;

namespace MidiRestyle.Playback.Tests;

/// <summary>
/// Guards the tuning restore that a seek and an A/B switch both need.
/// </summary>
/// <remarks>
/// The failure this prevents was found by reading DryWetMIDI's behaviour rather than by a test
/// failing: <c>Playback</c> replays tracked controllers in <em>ascending controller number</em>, so an
/// authored RPN handshake comes back as <c>CC6, CC38, ... CC100, CC101</c> - the data entry before
/// the RPN-null, with no re-selection of RPN 0/0. It lands on whichever RPN the synth currently
/// points at. On a fresh synth that is 0/0 with a GM default of +/-2 semitones, which happens to
/// equal our default range - so it looked right <b>by luck</b>. Change the range, or seek on a synth
/// pointing elsewhere, and the restyled side plays silently mistuned.
/// </remarks>
public class RetuneAfterSeekTests
{
    private sealed class RecordingSink : IMidiSink
    {
        public List<ChannelEvent> Events { get; } = [];

        public void Send(ChannelEvent channelEvent) => Events.Add(channelEvent);

        public void Clear() => Events.Clear();
    }

    private sealed class FakePlayer : ISequencePlayer
    {
        public bool IsRunning { get; private set; }

        public TimeSpan Duration => TimeSpan.FromSeconds(60);

        public TimeSpan CurrentTime { get; private set; }

        public void MoveToTime(TimeSpan time) => CurrentTime = time;

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;

        public void Dispose() => IsRunning = false;
    }

    /// <summary>Maqam Rast: two channels, one at centre and one 50 cents flat.</summary>
    private static (int Channel, double BendCents)[] RastTuning => [(0, 0.0), (1, -50.0)];

    private static (AbSwitcher Switcher, RecordingSink Sink) Build(
        (int Channel, double BendCents)[]? tuning = null)
    {
        RecordingSink sink = new();
        AbSwitcher switcher = new(
            new FakePlayer(), new FakePlayer(), sink, [0, 1], tuning ?? RastTuning);
        return (switcher, sink);
    }

    private static IEnumerable<(int Controller, int Value)> Ccs(
        IEnumerable<ChannelEvent> events, int channel) =>
        events
            .Where(e => e.Kind == ChannelEventKind.ControlChange && e.Channel == channel)
            .Select(e => (e.Data1, e.Data2));

    [Fact]
    public void SeekingReestablishesTheBendRangeOnEveryAllocatedChannel()
    {
        (AbSwitcher switcher, RecordingSink sink) = Build();
        sink.Clear();

        switcher.Seek(TimeSpan.FromSeconds(10));

        foreach ((int channel, _) in RastTuning)
        {
            // The full handshake, in order, including the RPN-null that closes it. Without the
            // re-selection of RPN 0/0 at the front, the data entry applies to whatever RPN the
            // synth happens to be pointing at.
            Ccs(sink.Events, channel).Should().ContainInOrder(
                (101, 0), (100, 0), (6, 2), (38, 0), (101, 127), (100, 127));
        }
    }

    [Fact]
    public void SeekingRestoresEachChannelsOwnBendNotJustTheRange()
    {
        (AbSwitcher switcher, RecordingSink sink) = Build();
        sink.Clear();

        switcher.Seek(TimeSpan.FromSeconds(10));

        var bends = sink.Events
            .Where(e => e.Kind == ChannelEventKind.PitchBend)
            .ToDictionary(e => e.Channel, e => (e.Data2 * 128) + e.Data1);

        bends[0].Should().Be(8192, "channel 0 carries no offset, so it sits at centre");
        bends[1].Should().Be(6144, "-50 cents at the default range is 6144");
    }

    [Fact]
    public void TheRangeIsSentBeforeTheBendValue()
    {
        (AbSwitcher switcher, RecordingSink sink) = Build();
        sink.Clear();

        switcher.Seek(TimeSpan.FromSeconds(5));

        List<ChannelEvent> channelOne = [.. sink.Events.Where(e => e.Channel == 1)];
        int rangeAt = channelOne.FindIndex(e =>
            e.Kind == ChannelEventKind.ControlChange && e.Data1 == 6);
        int bendAt = channelOne.FindIndex(e => e.Kind == ChannelEventKind.PitchBend);

        rangeAt.Should().BeGreaterThanOrEqualTo(0);
        bendAt.Should().BeGreaterThan(rangeAt,
            "a bend value is meaningless until the range it is measured against is established");
    }

    /// <summary>
    /// The stop sequence resets every channel to centre, so a switch onto the restyled side must put
    /// the tuning back before anything sounds.
    /// </summary>
    [Fact]
    public void SwitchingToTheRestyledSideRetunesAfterTheStopSequence()
    {
        (AbSwitcher switcher, RecordingSink sink) = Build();
        switcher.SwitchTo(PlaybackSide.Original);
        sink.Clear();

        switcher.SwitchTo(PlaybackSide.Restyled);

        List<ChannelEvent> events = [.. sink.Events];

        int resetAt = events.FindIndex(e =>
            e.Kind == ChannelEventKind.PitchBend && ((e.Data2 * 128) + e.Data1) == 8192
            && e.Channel == 1);
        int retuneAt = events.FindIndex(e =>
            e.Kind == ChannelEventKind.PitchBend && ((e.Data2 * 128) + e.Data1) == 6144
            && e.Channel == 1);

        resetAt.Should().BeGreaterThanOrEqualTo(0, "the stop sequence resets bend to centre");
        retuneAt.Should().BeGreaterThan(resetAt,
            "the retune must come after the reset, or the reset wins and the side plays 12-TET");
    }

    [Fact]
    public void SwitchingToTheOriginalSideDoesNotRetune()
    {
        (AbSwitcher switcher, RecordingSink sink) = Build();
        switcher.SwitchTo(PlaybackSide.Restyled);
        sink.Clear();

        switcher.SwitchTo(PlaybackSide.Original);

        sink.Events.Where(e => e.Kind == ChannelEventKind.PitchBend)
            .Should().OnlyContain(e => ((e.Data2 * 128) + e.Data1) == 8192,
                "the untouched side wants centre, which the stop sequence already set");
    }

    [Fact]
    public void ATwelveTetTargetNeedsNoRetuneAndSendsNone()
    {
        (AbSwitcher switcher, RecordingSink sink) = Build(tuning: []);
        sink.Clear();

        switcher.Seek(TimeSpan.FromSeconds(10));

        sink.Events.Should().BeEmpty(
            "a target on the semitone grid holds no bend, so there is nothing to restore");
    }

    [Fact]
    public void TheTuningIsExposedForInspection() =>
        Build().Switcher.RestyledTuning.Should().BeEquivalentTo(RastTuning);

    /// <summary>
    /// The Core-side sequence must carry the RPN-null. Without it a later stray CC6 anywhere in the
    /// file would be read as another bend-range change.
    /// </summary>
    [Fact]
    public void TheBendRangeSequenceClosesItsRpn()
    {
        IReadOnlyList<ChannelEvent> sequence = PitchBendEncoder.BendRangeSequence(3);

        sequence.Select(e => (e.Data1, e.Data2)).Should().Equal(
            [(101, 0), (100, 0), (6, 2), (38, 0), (101, 127), (100, 127)]);
        sequence.Should().OnlyContain(e => e.Channel == 3);
    }

    [Theory]
    [InlineData(2, 6144)]
    [InlineData(12, 8192 - (int)(50.0 / 1200.0 * 8192))]
    public void ANonDefaultRangeChangesBothTheRangeAndTheEncodedBend(int range, int expectedBend)
    {
        IReadOnlyList<ChannelEvent> sequence = PitchBendEncoder.RetuneSequence(0, -50, range);

        sequence.Should().Contain(e =>
            e.Kind == ChannelEventKind.ControlChange && e.Data1 == 6 && e.Data2 == range);
        sequence.Last(e => e.Kind == ChannelEventKind.PitchBend)
            .Let(e => (e.Data2 * 128) + e.Data1)
            .Should().BeCloseTo(expectedBend, 1,
                "a wider range means the same offset needs a smaller bend value - which is exactly "
                + "why the range must be re-established rather than assumed");
    }
}

internal static class LetExtensions
{
    /// <summary>Projects a value inline, so an assertion can read left to right.</summary>
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> project) => project(value);
}
