using MidiRestyle.Core.Model;

namespace MidiRestyle.Core.Notation;

/// <summary>One measure's position and metre, before any notes are placed in it.</summary>
public readonly record struct MeasureSpan(
    int Number, long StartTicks, long LengthTicks, int Beats, int BeatUnit, bool SignatureChanged)
{
    public long EndTicks => StartTicks + LengthTicks;

    /// <summary>
    /// Ticks per notated beat. In 6/8 the printed beat is the eighth, not the dotted quarter -
    /// the decomposer splits at this value, and splitting compound time at the dotted beat would
    /// hide the internal eighths rather than reveal them.
    /// </summary>
    public long BeatTicks => Beats == 0 ? LengthTicks : LengthTicks / Beats;
}

/// <summary>
/// Turns a file's time-signature map into the run of measures a score is laid out on.
/// </summary>
/// <remarks>
/// Kept separate from the builder because both the exporter and the staff renderer need to agree on
/// exactly where the barlines fall, and a second implementation would eventually disagree.
/// </remarks>
public static class MeasureGrid
{
    /// <summary>What a file with no time signature is assumed to be in. The MIDI default.</summary>
    public const int DefaultNumerator = 4;

    /// <summary>Ditto.</summary>
    public const int DefaultDenominator = 4;

    /// <summary>
    /// Builds measures covering <paramref name="totalTicks"/>, honouring every signature change.
    /// </summary>
    public static IReadOnlyList<MeasureSpan> Build(
        IReadOnlyList<TimeSignatureChange> signatures, long totalTicks, int ppqn)
    {
        if (ppqn <= 0)
        {
            ppqn = 480;
        }

        // A file may carry no signature at all, or start its first one late; either way the opening
        // measures still have to be drawn, so an implicit 4/4 covers from tick zero.
        List<TimeSignatureChange> map = [.. signatures.OrderBy(s => s.Ticks)];

        if (map.Count == 0 || map[0].Ticks > 0)
        {
            map.Insert(0, new TimeSignatureChange(0, DefaultNumerator, DefaultDenominator));
        }

        List<MeasureSpan> measures = [];
        long tick = 0;
        int number = 1;
        int index = 0;
        int lastNumerator = 0;
        int lastDenominator = 0;

        // An empty file still gets one measure: a score with no barline at all reads as broken
        // rather than as empty.
        long target = Math.Max(totalTicks, 1);

        while (tick < target && measures.Count < MaxMeasures)
        {
            while (index + 1 < map.Count && map[index + 1].Ticks <= tick)
            {
                index++;
            }

            var signature = map[index];
            long length = MeasureTicks(signature.Numerator, signature.Denominator, ppqn);

            if (length <= 0)
            {
                length = ppqn * 4L;
            }

            bool changed = signature.Numerator != lastNumerator || signature.Denominator != lastDenominator;
            lastNumerator = signature.Numerator;
            lastDenominator = signature.Denominator;

            measures.Add(new MeasureSpan(
                number, tick, length, signature.Numerator, signature.Denominator, changed));

            tick += length;
            number++;
        }

        return measures;
    }

    /// <summary>
    /// A guard against a corrupt division or a pathological signature generating millions of
    /// measures and hanging the UI thread. 10,000 bars is far past any real piece.
    /// </summary>
    private const int MaxMeasures = 10_000;

    /// <summary>Length of one measure of the given signature, in ticks.</summary>
    public static long MeasureTicks(int numerator, int denominator, int ppqn)
    {
        if (numerator <= 0 || denominator <= 0)
        {
            return ppqn * 4L;
        }

        // A quarter note is one PPQN, so a unit of 1/denominator is 4/denominator quarters.
        return (long)Math.Round(numerator * (4.0 / denominator) * ppqn);
    }

    /// <summary>Finds the measure containing a tick, or the last one if the tick runs past the end.</summary>
    public static MeasureSpan MeasureAt(IReadOnlyList<MeasureSpan> measures, long ticks)
    {
        for (int i = 0; i < measures.Count; i++)
        {
            if (ticks < measures[i].EndTicks)
            {
                return measures[i];
            }
        }

        return measures[^1];
    }
}
