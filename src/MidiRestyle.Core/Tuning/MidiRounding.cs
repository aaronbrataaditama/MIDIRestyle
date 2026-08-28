namespace MidiRestyle.Core.Tuning;

/// <summary>
/// The single rounding gate for every cents-to-semitone conversion in the domain.
/// </summary>
/// <remarks>
/// <para>
/// This type exists to make an invariant structural instead of hoped-for. <c>Math.Round(double)</c>
/// defaults to <see cref="MidpointRounding.ToEven"/> (banker's rounding), and quarter-tone scales
/// land <em>exactly</em> on the +/-50 cent tie on every single note - so the tie-breaking rule is
/// not an edge case here, it is the common case.
/// </para>
/// <para>
/// Under the default, Maqam Rast on C spells its half-flat third as <c>E -50c</c> (6350/100 = 63.5,
/// rounding up to the even 64) but its half-flat seventh as <c>B-flat +50c</c> (7050/100 = 70.5,
/// rounding down to the even 70). That is two distinct offsets from one musical inflection, so the
/// scale allocates three pitch-bend channels instead of two - and which way each degree falls
/// changes with the tonic, so the channel count is not even stable.
/// </para>
/// <para>
/// The quantiser, <c>TuningFidelity</c>, <c>OffsetClusterer</c> and <c>PitchBendEncoder</c> must all
/// round identically or they disagree with each other. Route every such conversion through here.
/// </para>
/// </remarks>
public static class MidiRounding
{
    /// <summary>Cents in one equal-tempered semitone.</summary>
    public const double CentsPerSemitone = 100.0;

    /// <summary>Cents in one octave. Load-bearing: the domain assumes exact 1200-cent periodicity.</summary>
    public const double CentsPerOctave = 1200.0;

    /// <summary>Semitones in one octave.</summary>
    public const int SemitonesPerOctave = 12;

    /// <summary>The one rounding mode the whole domain uses. See the remarks on this type.</summary>
    public const MidpointRounding Mode = MidpointRounding.AwayFromZero;

    /// <summary>Rounds a cents value to the nearest semitone number, ties away from zero.</summary>
    public static int ToNearestSemitone(double cents) =>
        (int)Math.Round(cents / CentsPerSemitone, Mode);

    /// <summary>Rounds a cents value to the nearest whole semitone, expressed in cents.</summary>
    public static double ToNearestSemitoneCents(double cents) =>
        ToNearestSemitone(cents) * CentsPerSemitone;

    /// <summary>
    /// The signed distance from <paramref name="cents"/> to its nearest semitone, in cents.
    /// Always within [-50, +50).
    /// </summary>
    public static double OffsetFromNearestSemitone(double cents) =>
        cents - ToNearestSemitoneCents(cents);

    /// <summary>Rounds to the nearest integer, ties away from zero.</summary>
    public static int ToNearestInt(double value) => (int)Math.Round(value, Mode);
}
