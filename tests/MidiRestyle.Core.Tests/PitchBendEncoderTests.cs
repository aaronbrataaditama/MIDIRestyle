using MidiRestyle.Core.Output;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="PitchBendEncoder"/> turns a required cent-offset into the exact MIDI channel-event
/// sequence a synth needs to sound it. Every assertion here is drawn from the plan's "Source
/// channel state" section and from CLAUDE.md's pitch-bend/RPN/bank-select invariants.
/// </summary>
public class PitchBendEncoderTests
{
    private static SourceChannelState SourceWith(
        int program = 0,
        int bankMsb = 0,
        int bankLsb = 0,
        IReadOnlyDictionary<int, int>? controllers = null,
        int? channelPressure = null) =>
        new(program, bankMsb, bankLsb, controllers ?? new Dictionary<int, int>(), channelPressure);

    // ---- Bend value encoding ------------------------------------------------------------------

    [Fact]
    public void MinusFiftyCentsAtRangeTwoEncodesTo6144() =>
        PitchBendEncoder.EncodeBend(-50, rangeSemitones: 2).Should().Be(6144);

    [Fact]
    public void ZeroCentsEncodesToCentre8192() =>
        PitchBendEncoder.EncodeBend(0, rangeSemitones: 2).Should().Be(8192);

    [Fact]
    public void PlusFiftyCentsAtRangeTwoEncodesTo10240() =>
        PitchBendEncoder.EncodeBend(50, rangeSemitones: 2).Should().Be(10240);

    [Fact]
    public void ResolutionIsApproximatelyPoint0244CentsPerUnit()
    {
        // One raw unit of bend corresponds to (rangeSemitones * 100 * 2) / 16384 cents at range 2:
        // 200 cents / 8192 units ~= 0.0244140625 cents/unit. Confirm via two adjacent bend values.
        int low = PitchBendEncoder.EncodeBend(0, rangeSemitones: 2);
        int high = PitchBendEncoder.EncodeBend(0.0244140625, rangeSemitones: 2);

        (high - low).Should().Be(1);
    }

    // ---- Clamping -------------------------------------------------------------------------------

    [Fact]
    public void BendValueNeverExceedsFourteenBitRangeEvenAtAnImplausibleRange()
    {
        // Offsets are bounded to [-50, +50) by construction, so no realistic range setting alone
        // can overflow a 14-bit value from an in-band offset. But the range is a configurable
        // setting a caller could still shrink well past what construction guarantees elsewhere -
        // pair a narrow range with an offset outside the normal bound and the raw arithmetic
        // overflows a 14-bit value. The clamp must catch that rather than silently wrapping.
        PitchBendEncoder.EncodeBend(500, rangeSemitones: 1).Should().Be(PitchBendEncoder.MaxBendValue);
        PitchBendEncoder.EncodeBend(-500, rangeSemitones: 1).Should().Be(0);
    }

    [Fact]
    public void BendValueClampsForAnOutOfBandOffsetAtTheDefaultRange()
    {
        PitchBendEncoder.EncodeBend(100_000, rangeSemitones: 2).Should().Be(PitchBendEncoder.MaxBendValue);
        PitchBendEncoder.EncodeBend(-100_000, rangeSemitones: 2).Should().Be(0);
    }

    [Theory]
    [InlineData(-500)]
    [InlineData(-50)]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(500)]
    public void BendValueIsAlwaysWithinTheFourteenBitRange(double offsetCents)
    {
        int value = PitchBendEncoder.EncodeBend(offsetCents);
        value.Should().BeInRange(0, PitchBendEncoder.MaxBendValue);
    }

    // ---- Rounding goes through MidiRounding, ties away from zero --------------------------------

    [Fact]
    public void RoundingUsesAwayFromZeroNotBankersRounding()
    {
        // Choose an offset/range pair whose raw bend value lands exactly on a .5 tie, so banker's
        // rounding (round-to-even) and away-from-zero visibly disagree.
        // offsetCents / (range*100) * 8192 = 0.5 exactly when offsetCents = range*100/16384.
        // At range = 2: offsetCents = 200/16384 = 0.01220703125 -> raw = 8192.5.
        // Away-from-zero rounds 8192.5 up to 8193; banker's rounding would round to 8192 (even).
        const double offsetCents = 200.0 / 16384.0;

        PitchBendEncoder.EncodeBend(offsetCents, rangeSemitones: 2).Should().Be(8193);
    }

    [Fact]
    public void RoundingUsesAwayFromZeroBelowCentreToo()
    {
        // A tie below centre: raw = 8192 - 1.5 = 8190.5, straddling even 8190 and odd 8191.
        // MidpointRounding.AwayFromZero rounds a positive value's tie up (away from zero), giving
        // 8191; banker's rounding would instead land on the even 8190.
        const double offsetCents = -300.0 / 8192.0;

        PitchBendEncoder.EncodeBend(offsetCents, rangeSemitones: 2).Should().Be(8191);
    }

    // ---- Bend event byte layout ------------------------------------------------------------------

    [Fact]
    public void BendEventSplitsTheFourteenBitValueIntoLsbFirstDataBytes()
    {
        // 6144 = 0b0011_0000_0000_0000 -> low 7 bits = 0, high 7 bits = 48.
        ChannelEvent evt = PitchBendEncoder.BendEvent(channel: 3, offsetCents: -50, rangeSemitones: 2);

        evt.Kind.Should().Be(ChannelEventKind.PitchBend);
        evt.Channel.Should().Be(3);
        evt.Data1.Should().Be(0);
        evt.Data2.Should().Be(48);
        (evt.Data2 * 128 + evt.Data1).Should().Be(6144);
    }

    // ---- RPN sequence ordering --------------------------------------------------------------------

    [Fact]
    public void RpnSequenceIsEmittedInOrderFollowedByTheRpnNullPair()
    {
        SourceChannelState source = SourceWith(program: 12, bankMsb: 0, bankLsb: 0);

        IReadOnlyList<ChannelEvent> events = PitchBendEncoder.SetupSequence(
            channel: 5, offsetCents: 0, source: source, rangeSemitones: 2);

        events.Take(6).Should().Equal(
            new ChannelEvent(ChannelEventKind.ControlChange, 5, 101, 0),
            new ChannelEvent(ChannelEventKind.ControlChange, 5, 100, 0),
            new ChannelEvent(ChannelEventKind.ControlChange, 5, 6, 2),
            new ChannelEvent(ChannelEventKind.ControlChange, 5, 38, 0),
            new ChannelEvent(ChannelEventKind.ControlChange, 5, 101, 127),
            new ChannelEvent(ChannelEventKind.ControlChange, 5, 100, 127));
    }

    [Fact]
    public void RpnRangeValueMatchesTheConfiguredRangeSemitones()
    {
        SourceChannelState source = SourceWith();

        IReadOnlyList<ChannelEvent> events = PitchBendEncoder.SetupSequence(
            channel: 0, offsetCents: 0, source: source, rangeSemitones: 7);

        events[2].Should().Be(new ChannelEvent(ChannelEventKind.ControlChange, 0, 6, 7));
    }

    // ---- Bank select / program change ordering ------------------------------------------------

    [Fact]
    public void BankSelectMsbAndLsbImmediatelyPrecedeTheProgramChange()
    {
        SourceChannelState source = SourceWith(program: 41, bankMsb: 2, bankLsb: 9);

        IReadOnlyList<ChannelEvent> events = PitchBendEncoder.SetupSequence(
            channel: 4, offsetCents: 0, source: source, rangeSemitones: 2);

        int programChangeIndex = events.ToList().FindIndex(e => e.Kind == ChannelEventKind.ProgramChange);

        programChangeIndex.Should().BeGreaterThanOrEqualTo(2);
        events[programChangeIndex - 2].Should().Be(new ChannelEvent(ChannelEventKind.ControlChange, 4, 0, 2));
        events[programChangeIndex - 1].Should().Be(new ChannelEvent(ChannelEventKind.ControlChange, 4, 32, 9));
        events[programChangeIndex].Should().Be(new ChannelEvent(ChannelEventKind.ProgramChange, 4, 41, 0));
    }

    [Fact]
    public void ProgramChangeCopiesTheSourceProgramNumber()
    {
        SourceChannelState source = SourceWith(program: 73);

        IReadOnlyList<ChannelEvent> events = PitchBendEncoder.SetupSequence(
            channel: 1, offsetCents: 0, source: source);

        events.Should().ContainSingle(e => e.Kind == ChannelEventKind.ProgramChange)
            .Which.Data1.Should().Be(73);
    }

    // ---- Pitch bend for the cluster offset appears after Program Change -----------------------

    [Fact]
    public void PitchBendForTheClusterOffsetFollowsTheProgramChange()
    {
        SourceChannelState source = SourceWith(program: 0);

        IReadOnlyList<ChannelEvent> events = PitchBendEncoder.SetupSequence(
            channel: 2, offsetCents: -50, source: source, rangeSemitones: 2);

        int programChangeIndex = events.ToList().FindIndex(e => e.Kind == ChannelEventKind.ProgramChange);
        ChannelEvent bendEvent = events[programChangeIndex + 1];

        bendEvent.Kind.Should().Be(ChannelEventKind.PitchBend);
        (bendEvent.Data2 * 128 + bendEvent.Data1).Should().Be(6144);
    }

    // ---- Controller duplication: all channel-wide CCs, not a whitelist -------------------------

    [Fact]
    public void AllChannelWideControllersOnTheSourceChannelAreDuplicatedIncludingUncommonOnes()
    {
        var controllers = new Dictionary<int, int>
        {
            [7] = 100,  // volume
            [10] = 64,  // pan
            [11] = 127, // expression
            [64] = 127, // sustain
            [1] = 30,   // modulation
            [91] = 40,  // reverb
            [93] = 20,  // chorus
            [5] = 10,   // portamento time
            [65] = 127, // portamento on/off
        };
        SourceChannelState source = SourceWith(controllers: controllers, channelPressure: 55);

        IReadOnlyList<ChannelEvent> events = PitchBendEncoder.SetupSequence(
            channel: 6, offsetCents: 0, source: source);

        foreach ((int controller, int value) in controllers)
        {
            events.Should().ContainSingle(e =>
                e.Kind == ChannelEventKind.ControlChange && e.Channel == 6 &&
                e.Data1 == controller && e.Data2 == value,
                $"controller {controller} must be duplicated verbatim");
        }

        events.Should().ContainSingle(e => e.Kind == ChannelEventKind.ChannelPressure)
            .Which.Data1.Should().Be(55);
    }

    [Fact]
    public void Cc121AndCc123AreNeverDuplicatedEvenIfPresentInSourceState()
    {
        var controllers = new Dictionary<int, int>
        {
            [7] = 100,
            [121] = 0,
            [123] = 0,
        };
        SourceChannelState source = SourceWith(controllers: controllers);

        IReadOnlyList<ChannelEvent> events = PitchBendEncoder.SetupSequence(
            channel: 0, offsetCents: 0, source: source);

        events.Should().NotContain(e =>
            e.Kind == ChannelEventKind.ControlChange && (e.Data1 == 121 || e.Data1 == 123));
    }

    [Fact]
    public void NoChannelPressureEventIsEmittedWhenSourceHasNone()
    {
        SourceChannelState source = SourceWith(channelPressure: null);

        IReadOnlyList<ChannelEvent> events = PitchBendEncoder.SetupSequence(
            channel: 0, offsetCents: 0, source: source);

        events.Should().NotContain(e => e.Kind == ChannelEventKind.ChannelPressure);
    }

    // ---- Setup re-emission triggers --------------------------------------------------------------

    [Fact]
    public void ProgramChangeRequiresSetupReemission() =>
        PitchBendEncoder.RequiresSetupReemission(SourceEvent.ProgramChange()).Should().BeTrue();

    [Fact]
    public void Cc121RequiresSetupReemission() =>
        PitchBendEncoder.RequiresSetupReemission(SourceEvent.ControlChange(121)).Should().BeTrue();

    [Fact]
    public void GmResetSysExRequiresSetupReemission() =>
        PitchBendEncoder.RequiresSetupReemission(SourceEvent.SystemExclusive(isGmReset: true)).Should().BeTrue();

    [Fact]
    public void NonGmResetSysExDoesNotRequireSetupReemission() =>
        PitchBendEncoder.RequiresSetupReemission(SourceEvent.SystemExclusive(isGmReset: false)).Should().BeFalse();

    [Fact]
    public void Cc123DoesNotRequireSetupReemission() =>
        PitchBendEncoder.RequiresSetupReemission(SourceEvent.ControlChange(123)).Should().BeFalse();

    [Theory]
    [InlineData(7)]   // volume
    [InlineData(64)]  // sustain
    [InlineData(1)]   // modulation
    public void OrdinaryControllersDoNotRequireSetupReemission(int controller) =>
        PitchBendEncoder.RequiresSetupReemission(SourceEvent.ControlChange(controller)).Should().BeFalse();

    // ---- Stop / A-B-switch sequence --------------------------------------------------------------

    [Fact]
    public void StopSequenceSendsAllNotesOffAndABendResetToEveryAllocatedChannel()
    {
        IReadOnlyList<ChannelEvent> events = PitchBendEncoder.StopSequence([2, 5, 9]);

        foreach (int channel in new[] { 2, 5, 9 })
        {
            events.Should().ContainSingle(e =>
                e.Kind == ChannelEventKind.ControlChange && e.Channel == channel &&
                e.Data1 == 123 && e.Data2 == 0);

            events.Should().ContainSingle(e =>
                e.Kind == ChannelEventKind.PitchBend && e.Channel == channel &&
                e.Data2 * 128 + e.Data1 == PitchBendEncoder.CenterBendValue);
        }
    }

    [Fact]
    public void StopSequenceIsEmptyForNoAllocatedChannels() =>
        PitchBendEncoder.StopSequence([]).Should().BeEmpty();

    [Fact]
    public void StopSequenceBendResetValueIsExactlyCentre8192()
    {
        IReadOnlyList<ChannelEvent> events = PitchBendEncoder.StopSequence([0]);
        ChannelEvent bend = events.Single(e => e.Kind == ChannelEventKind.PitchBend);

        (bend.Data2 * 128 + bend.Data1).Should().Be(8192);
    }
}
