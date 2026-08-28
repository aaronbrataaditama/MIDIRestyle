using MidiRestyle.Playback;

namespace MidiRestyle.Playback.Tests;

/// <summary>
/// The rate limit that stands between "DryWetMIDI raises an event per MIDI message on a background
/// thread" and "the UI thread's dispatcher queue".
/// </summary>
/// <remarks>
/// Asserted against a simulated clock, so the numbers are exact and the test cannot flake under
/// parallel load - which a sleep-based version of this test certainly would.
/// </remarks>
public class PlayheadThrottleTests
{
    /// <summary>A clock a test advances by hand.</summary>
    private sealed class SimulatedClock
    {
        public TimeSpan Now { get; private set; }

        public void Advance(TimeSpan by) => Now += by;

        public Func<TimeSpan> Read => () => Now;
    }

    [Fact]
    public void DrivingTenThousandPositionChangesYieldsAtMostSixtyNotificationsPerSimulatedSecond()
    {
        SimulatedClock clock = new();
        PlayheadThrottle throttle = new(clock: clock.Read);

        const int Changes = 10_000;
        TimeSpan step = TimeSpan.FromSeconds(1.0 / Changes);
        int emitted = 0;

        for (int i = 0; i < Changes; i++)
        {
            clock.Advance(step);

            if (throttle.TryEmit())
            {
                emitted++;
            }
        }

        // One simulated second at the default 60 Hz ceiling. The window is 59..61 rather than
        // exactly 60 because where the boundary lands depends on the step size, not on the rule.
        emitted.Should().BeInRange(59, 61);

        // And the point of the whole thing, stated plainly.
        emitted.Should().BeLessThan(Changes / 100);
    }

    [Fact]
    public void ManySimulatedSecondsStayOnRate()
    {
        SimulatedClock clock = new();
        PlayheadThrottle throttle = new(clock: clock.Read);

        const int Seconds = 10;
        const int PerSecond = 5_000;
        TimeSpan step = TimeSpan.FromSeconds(1.0 / PerSecond);
        int emitted = 0;

        for (int i = 0; i < Seconds * PerSecond; i++)
        {
            clock.Advance(step);

            if (throttle.TryEmit())
            {
                emitted++;
            }
        }

        // Never above the ceiling, and never drifting far below it. The small shortfall is the
        // simulated clock's step granularity, not slippage in the rule.
        emitted.Should().BeInRange(
            (Seconds * PlayheadThrottle.DefaultHertz) - 10,
            Seconds * PlayheadThrottle.DefaultHertz);
    }

    [Fact]
    public void ADifferentRateIsHonoured()
    {
        SimulatedClock clock = new();
        PlayheadThrottle throttle = new(TimeSpan.FromMilliseconds(100), clock.Read);

        int emitted = 0;
        for (int i = 0; i < 1_000; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));

            if (throttle.TryEmit())
            {
                emitted++;
            }
        }

        throttle.MinimumInterval.Should().Be(TimeSpan.FromMilliseconds(100));
        emitted.Should().Be(10);
    }

    [Fact]
    public void NothingIsEmittedWhenNoTimePasses()
    {
        SimulatedClock clock = new();
        PlayheadThrottle throttle = new(clock: clock.Read);

        int emitted = Enumerable.Range(0, 5_000).Count(_ => throttle.TryEmit());

        emitted.Should().Be(0);
    }

    [Fact]
    public void ResetLetsTheNextCallThroughImmediately()
    {
        SimulatedClock clock = new();
        PlayheadThrottle throttle = new(clock: clock.Read);

        throttle.TryEmit().Should().BeFalse();

        throttle.Reset();

        throttle.TryEmit().Should().BeTrue();
        throttle.TryEmit().Should().BeFalse();
    }

    [Fact]
    public void ItIsSafeToAskFromSeveralThreadsAtOnce()
    {
        // The real caller is DryWetMIDI's playback thread and a timer thread, concurrently.
        PlayheadThrottle throttle = new(TimeSpan.FromMilliseconds(50));
        int emitted = 0;

        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 5_000; i++)
            {
                if (throttle.TryEmit())
                {
                    Interlocked.Increment(ref emitted);
                }
            }
        });

        // 40,000 asks; a handful of 50 ms windows can have elapsed at most.
        emitted.Should().BeLessThan(100);
    }

    [Fact]
    public void ANegativeIntervalIsRejected()
    {
        Action negative = () => _ = new PlayheadThrottle(TimeSpan.FromMilliseconds(-1));

        negative.Should().Throw<ArgumentOutOfRangeException>();
    }
}
