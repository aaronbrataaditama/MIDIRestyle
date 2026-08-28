using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Output;

/// <summary>The three MIDI channel-voice message families <see cref="PitchBendEncoder"/> emits.</summary>
public enum ChannelEventKind
{
    /// <summary>A Control Change (status 0xB_). <c>Data1</c> is the controller number, <c>Data2</c> the value.</summary>
    ControlChange,

    /// <summary>A Program Change (status 0xC_). <c>Data1</c> is the program number, <c>Data2</c> is unused (0).</summary>
    ProgramChange,

    /// <summary>
    /// A Pitch Bend (status 0xE_). MIDI pitch bend is a 14-bit value carried as two 7-bit data
    /// bytes, LSB first: <c>Data1</c> is the low 7 bits, <c>Data2</c> the high 7 bits. Combine as
    /// <c>Data2 * 128 + Data1</c> to recover the 14-bit value. This is the raw wire layout on
    /// purpose, so the exporter and the playback engine can each hand the bytes straight to their
    /// own MIDI library without re-deriving anything.
    /// </summary>
    PitchBend,

    /// <summary>
    /// A Channel Pressure / monophonic aftertouch (status 0xD_). <c>Data1</c> is the pressure value,
    /// <c>Data2</c> is unused (0). Channel-wide, like the controllers it is duplicated alongside.
    /// </summary>
    ChannelPressure,
}

/// <summary>
/// One MIDI channel-voice event, at the domain level: no dependency on any concrete MIDI library.
/// The exporter and the playback engine each translate this into their own event types - that
/// translation is the only thing either of them does with pitch bend and channel setup, which is
/// what keeps preview and export from drifting apart.
/// </summary>
public readonly record struct ChannelEvent(ChannelEventKind Kind, int Channel, int Data1, int Data2);

/// <summary>
/// A snapshot of the source channel's state at the point a derived channel's setup sequence is
/// built. Bank/program are copied onto the derived channel via Bank Select + Program Change;
/// controller values and channel pressure are duplicated so the derived channel does not silently
/// lose volume, pan, sustain, modulation, and everything else that lives outside the classic four.
/// </summary>
/// <param name="Program">Source Program Change number, copied verbatim.</param>
/// <param name="BankMsb">Source Bank Select MSB (CC0), copied verbatim.</param>
/// <param name="BankLsb">Source Bank Select LSB (CC32), copied verbatim.</param>
/// <param name="ControllerValues">
/// Every channel-wide controller currently set on the source channel, keyed by controller number.
/// Not a whitelist: whatever is here is duplicated, in ascending controller-number order for
/// determinism. CC0/CC32 (bank select) are handled separately via <see cref="BankMsb"/>/
/// <see cref="BankLsb"/> and need not appear here; CC121 and CC123 are excluded even if present,
/// because both are handled by the caller rather than replayed as ordinary state.
/// </param>
/// <param name="ChannelPressure">Source channel pressure, if any is currently in effect.</param>
public sealed record SourceChannelState(
    int Program,
    int BankMsb,
    int BankLsb,
    IReadOnlyDictionary<int, int> ControllerValues,
    int? ChannelPressure = null);

/// <summary>The kind of source-channel event that can force a derived channel's setup to be rebuilt.</summary>
public enum SourceEventKind
{
    /// <summary>A source Program Change. The derived channel's copied program is now stale.</summary>
    ProgramChange,

    /// <summary>A source Control Change. Only CC121 (Reset All Controllers) is a reset trigger.</summary>
    ControlChange,

    /// <summary>A source System Exclusive message. Only a GM/GM2/GS/XG reset is a reset trigger.</summary>
    SystemExclusive,
}

/// <summary>
/// One source-channel event, described just precisely enough for
/// <see cref="PitchBendEncoder.RequiresSetupReemission"/> to classify it. This is not a general
/// MIDI event model - it exists only to keep that classification a pure, testable function instead
/// of logic duplicated inside the exporter and the playback engine.
/// </summary>
/// <param name="Kind">Which message family this is.</param>
/// <param name="Controller">For <see cref="SourceEventKind.ControlChange"/>, the controller number.</param>
/// <param name="IsGmReset">
/// For <see cref="SourceEventKind.SystemExclusive"/>, whether this message is a GM/GM2/GS/XG reset.
/// Ignored for other kinds.
/// </param>
public readonly record struct SourceEvent(SourceEventKind Kind, int Controller = 0, bool IsGmReset = false)
{
    /// <summary>A source Program Change.</summary>
    public static SourceEvent ProgramChange() => new(SourceEventKind.ProgramChange);

    /// <summary>A source Control Change for the given controller number.</summary>
    public static SourceEvent ControlChange(int controller) => new(SourceEventKind.ControlChange, controller);

    /// <summary>A source System Exclusive message, flagged as a GM/GM2/GS/XG reset or not.</summary>
    public static SourceEvent SystemExclusive(bool isGmReset) =>
        new(SourceEventKind.SystemExclusive, IsGmReset: isGmReset);
}

/// <summary>
/// Turns a required cent-offset into the exact MIDI channel-event sequence a synth needs to sound
/// it, per the plan's "Source channel state" section.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately free of any concrete MIDI library type. It emits a small domain-level
/// event (<see cref="ChannelEvent"/>) that both <c>MidiFileExporter</c> and the playback engine
/// translate into their own event types. Both need the identical sequence, byte for byte - that is
/// the entire reason this is a separate component rather than logic inlined into each.
/// </para>
/// <para>
/// <b>Setup vs. stop.</b> <see cref="SetupSequence"/> is emitted at the start, at every source
/// Program Change, and after any source CC121 (Reset All Controllers) or GM-reset SysEx - all of
/// which reset the pitch wheel and may reset the RPN bend range.
/// <see cref="RequiresSetupReemission"/> is the pure classifier a caller walking the source event
/// stream uses to decide when. <c>CC123</c> (All Notes Off) is deliberately <em>not</em> one of
/// those triggers: it silences sounding notes and nothing else. <see cref="StopSequence"/> is the
/// separate sequence for stop / the A-B switch, which sends CC123 <em>and</em> an explicit bend
/// reset - conflating the two would either leave notes hanging or leave the next sequence detuned.
/// </para>
/// </remarks>
public static class PitchBendEncoder
{
    /// <summary>Default RPN pitch-bend sensitivity, in semitones each way. Matches the plan.</summary>
    public const int DefaultRangeSemitones = 2;

    /// <summary>The centre (zero-offset) 14-bit pitch bend value.</summary>
    public const int CenterBendValue = 8192;

    /// <summary>The largest representable 14-bit pitch bend value.</summary>
    public const int MaxBendValue = 16383;

    /// <summary>Controller number for Bank Select MSB.</summary>
    private const int CcBankSelectMsb = 0;

    private const int CcRpnLsb = 100;
    private const int CcRpnMsb = 101;
    private const int CcDataEntryMsb = 6;
    private const int CcDataEntryLsb = 38;
    private const int CcBankSelectLsb = 32;

    /// <summary>Controller number for Reset All Controllers - a setup re-emission trigger, never duplicated.</summary>
    public const int CcResetAllControllers = 121;

    /// <summary>Controller number for All Notes Off - the stop-sequence controller, never duplicated, never a re-emission trigger.</summary>
    public const int CcAllNotesOff = 123;

    private const int RpnNullValue = 127;

    /// <summary>
    /// Encodes a required cent-offset as a 14-bit MIDI pitch bend value.
    /// </summary>
    /// <remarks>
    /// <c>bend = 8192 + round(offsetCents / (rangeSemitones * 100) * 8192)</c>, rounded through
    /// <see cref="MidiRounding"/> - the one rounding gate the whole domain shares, ties away from
    /// zero. At the default range of +/-2 semitones, -50c maps to 6144 and the resolution is
    /// 200/8192, about 0.0244c per unit - far beyond audible.
    /// </remarks>
    /// <param name="offsetCents">The cent-offset to encode. Bounded to [-50, +50) by construction upstream.</param>
    /// <param name="rangeSemitones">The RPN pitch-bend sensitivity currently in effect, in semitones each way.</param>
    /// <returns>
    /// A value clamped to 0..16383. At the default range, offsets bounded to [-50, +50) can never
    /// reach either edge, but the range is a configurable setting - shrink it enough (or feed an
    /// out-of-band offset) and the raw arithmetic overflows a 14-bit value, so the clamp is kept as
    /// a real guard rather than dead code.
    /// </returns>
    public static int EncodeBend(double offsetCents, int rangeSemitones = DefaultRangeSemitones)
    {
        double rangeCents = rangeSemitones * MidiRounding.CentsPerSemitone;
        double raw = CenterBendValue + (offsetCents / rangeCents * CenterBendValue);
        int rounded = MidiRounding.ToNearestInt(raw);
        return Math.Clamp(rounded, 0, MaxBendValue);
    }

    /// <summary>Builds the Pitch Bend <see cref="ChannelEvent"/> for a given channel and cent-offset.</summary>
    public static ChannelEvent BendEvent(int channel, double offsetCents, int rangeSemitones = DefaultRangeSemitones) =>
        BendEventForValue(channel, EncodeBend(offsetCents, rangeSemitones));

    private static ChannelEvent BendEventForValue(int channel, int bendValue) =>
        new(ChannelEventKind.PitchBend, channel, bendValue & 0x7F, (bendValue >> 7) & 0x7F);

    /// <summary>
    /// Builds the full per-channel setup sequence: RPN bend-range, RPN-null, Bank Select + Program
    /// Change, Pitch Bend for <paramref name="offsetCents"/>, then every duplicated channel-wide
    /// controller and channel pressure from <paramref name="source"/>. Order matches the plan
    /// exactly and is load-bearing - see the assertions on ordering rather than presence.
    /// </summary>
    /// <param name="channel">The derived (allocated) channel this sequence targets.</param>
    /// <param name="offsetCents">This channel's cluster offset, in cents.</param>
    /// <param name="source">A snapshot of the source channel's state to copy/duplicate.</param>
    /// <param name="rangeSemitones">The RPN pitch-bend sensitivity to set, in semitones each way.</param>
    public static IReadOnlyList<ChannelEvent> SetupSequence(
        int channel,
        double offsetCents,
        SourceChannelState source,
        int rangeSemitones = DefaultRangeSemitones)
    {
        ArgumentNullException.ThrowIfNull(source);

        var events = new List<ChannelEvent>(9 + source.ControllerValues.Count)
        {
            // 1. RPN pitch-bend sensitivity, then RPN-null so a later stray CC6 is not misread as
            //    another bend-range change.
            Cc(channel, CcRpnMsb, 0),
            Cc(channel, CcRpnLsb, 0),
            Cc(channel, CcDataEntryMsb, rangeSemitones),
            Cc(channel, CcDataEntryLsb, 0),
            Cc(channel, CcRpnMsb, RpnNullValue),
            Cc(channel, CcRpnLsb, RpnNullValue),

            // 2. Bank Select, immediately before Program Change.
            Cc(channel, CcBankSelectMsb, source.BankMsb),
            Cc(channel, CcBankSelectLsb, source.BankLsb),

            // 3. Program Change, copied from the source channel.
            new ChannelEvent(ChannelEventKind.ProgramChange, channel, source.Program, 0),

            // 4. Pitch Bend for this channel's cluster offset.
            BendEvent(channel, offsetCents, rangeSemitones),
        };

        // 5. Duplicate every channel-wide controller present on the source channel - not a
        //    whitelist - except CC121/CC123, which are handled by the caller, never replayed here.
        //    Sorted for determinism: the source dictionary carries no ordering guarantee of its own.
        foreach (int controller in source.ControllerValues.Keys.OrderBy(c => c))
        {
            if (controller is CcResetAllControllers or CcAllNotesOff)
            {
                continue;
            }

            events.Add(Cc(channel, controller, source.ControllerValues[controller]));
        }

        if (source.ChannelPressure is int pressure)
        {
            events.Add(new ChannelEvent(ChannelEventKind.ChannelPressure, channel, pressure, 0));
        }

        return events;
    }

    /// <summary>
    /// Just the RPN pitch-bend-sensitivity handshake, for re-establishing the bend range alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists because a seek cannot safely re-emit the whole <see cref="SetupSequence"/>. That
    /// sequence carries bank select and a program change, and a player seeking mid-file has no
    /// <see cref="SourceChannelState"/> to supply them - it would send <c>Program 0</c> and reset
    /// every instrument in the piece to Acoustic Grand Piano.
    /// </para>
    /// <para>
    /// <b>Why a seek needs this at all.</b> DryWetMIDI's <c>Playback</c> re-sends tracked controllers
    /// in ascending controller number, so the authored handshake comes back as
    /// <c>CC6, CC38, ... CC100, CC101</c> - the data entry <em>before</em> the RPN-null, and with no
    /// re-selection of RPN 0/0 first. It therefore lands on whichever RPN the synth currently points
    /// at. That happens to be 0/0 after a GM reset, and GM's default sensitivity is +/-2 semitones,
    /// which happens to equal <see cref="DefaultRangeSemitones"/> - so the observable result is
    /// correct on a fresh synth <em>by luck, not by design</em>. A synth left pointing at another
    /// RPN, or a project using a range other than +/-2, would be silently mistuned after seeking
    /// into the middle of a file. Re-emitting this sequence after a seek removes the luck.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ChannelEvent> BendRangeSequence(
        int channel,
        int rangeSemitones = DefaultRangeSemitones) =>
    [
        Cc(channel, CcRpnMsb, 0),
        Cc(channel, CcRpnLsb, 0),
        Cc(channel, CcDataEntryMsb, rangeSemitones),
        Cc(channel, CcDataEntryLsb, 0),
        Cc(channel, CcRpnMsb, RpnNullValue),
        Cc(channel, CcRpnLsb, RpnNullValue),
    ];

    /// <summary>
    /// The bend range plus the channel's own bend, for re-establishing tuning after a seek.
    /// </summary>
    /// <remarks>
    /// The pair a player needs and nothing more: it restores <em>tuning</em> without touching
    /// instrument selection. Order matters - the range must be established before the bend value is
    /// interpreted against it.
    /// </remarks>
    public static IReadOnlyList<ChannelEvent> RetuneSequence(
        int channel,
        double offsetCents,
        int rangeSemitones = DefaultRangeSemitones) =>
    [
        .. BendRangeSequence(channel, rangeSemitones),
        BendEvent(channel, offsetCents, rangeSemitones),
    ];

    /// <summary>
    /// Whether a source-channel event requires every derived channel's setup sequence to be
    /// re-emitted. True for a Program Change, for CC121 (Reset All Controllers), and for a
    /// GM/GM2/GS/XG reset SysEx - all three reset the pitch wheel and may reset the RPN bend range.
    /// False for every other Control Change, including CC123 (All Notes Off): that silences
    /// sounding notes and nothing else, which is exactly why the A/B switch sends CC123 <em>and</em>
    /// a separate bend reset via <see cref="StopSequence"/> rather than relying on this.
    /// </summary>
    public static bool RequiresSetupReemission(SourceEvent sourceEvent) => sourceEvent.Kind switch
    {
        SourceEventKind.ProgramChange => true,
        SourceEventKind.ControlChange => sourceEvent.Controller == CcResetAllControllers,
        SourceEventKind.SystemExclusive => sourceEvent.IsGmReset,
        _ => false,
    };

    /// <summary>
    /// The stop / A-B-switch sequence: CC123 to every allocated channel, plus an explicit bend
    /// reset to centre (8192) on each. Both are required and neither substitutes for the other -
    /// CC123 alone leaves the next sequence detuned by a stale bend, and a bend reset alone leaves
    /// notes hanging.
    /// </summary>
    /// <param name="allocatedChannels">Every channel currently allocated, in the order to emit into.</param>
    public static IReadOnlyList<ChannelEvent> StopSequence(IEnumerable<int> allocatedChannels)
    {
        ArgumentNullException.ThrowIfNull(allocatedChannels);

        var events = new List<ChannelEvent>();
        foreach (int channel in allocatedChannels)
        {
            events.Add(Cc(channel, CcAllNotesOff, 0));
            events.Add(BendEventForValue(channel, CenterBendValue));
        }

        return events;
    }

    private static ChannelEvent Cc(int channel, int controller, int value) =>
        new(ChannelEventKind.ControlChange, channel, controller, value);
}
