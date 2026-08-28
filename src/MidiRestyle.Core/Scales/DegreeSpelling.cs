namespace MidiRestyle.Core.Scales;

/// <summary>
/// How one scale degree is written on a Western staff.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Alter"/> is NOT the MusicXML <c>&lt;alter&gt;</c> value.</b> It is the alteration
/// relative to the <em>major-scale degree at the same index</em>, which makes it tonic-independent
/// and storable once per scale. MusicXML's <c>&lt;alter&gt;</c> is an absolute alteration of the
/// natural letter. On a D tonic, Hijaz's step 2 has <c>Alter = 0</c> here yet notates as F-sharp,
/// needing <c>&lt;alter&gt;1&lt;/alter&gt;</c>. Conflating the two frames is the trap; conversion is
/// explicit and lives with the notation exporter.
/// </para>
/// <para>
/// A <see cref="double"/> so quarter-tones are +/-0.5 - the same type MusicXML's element takes.
/// </para>
/// </remarks>
/// <param name="DiatonicStep">0..6, a letter offset from the tonic's letter (0 = the tonic's own letter).</param>
/// <param name="Alter">Semitones relative to the major-scale degree at this index.</param>
/// <param name="ResidualCents">
/// Cents left over after snapping <paramref name="Alter"/> to a real accidental. Comma-based scales
/// need this: AEU Rast derives alterations like -0.151 semitones, which no renderer can draw, so the
/// alteration is snapped to the nearest half-semitone and the remainder kept here for a future staff
/// view to show as comma marks.
/// </param>
public readonly record struct DegreeSpelling(int DiatonicStep, double Alter, double ResidualCents = 0.0)
{
    /// <summary>Cents of each major-scale degree, the frame <see cref="Alter"/> is measured against.</summary>
    public static readonly double[] MajorScaleCents = [0, 200, 400, 500, 700, 900, 1100];

    /// <summary>Letter names by diatonic step, for a C tonic.</summary>
    public static readonly char[] LetterNames = ['C', 'D', 'E', 'F', 'G', 'A', 'B'];

    /// <summary>The largest alteration that is still notatable. Double accidentals are legitimate.</summary>
    public const double MaxAlter = 2.0;

    /// <summary>Accidental granularity: naturals, quarter-tones, semitones, sesqui-tones.</summary>
    public const double AlterQuantum = 0.5;

    /// <summary>
    /// The largest residual that may remain after quantising. Beyond this the scale is not
    /// meaningfully notatable, since the written pitch would mislead by more than a quarter-tone's
    /// half.
    /// </summary>
    public const double MaxResidualCents = 25.0;

    /// <summary>Whether this alteration is representable as a real accidental.</summary>
    public bool IsNotatable =>
        Math.Abs(Alter) <= MaxAlter
        && Math.Abs(Alter / AlterQuantum - Math.Round(Alter / AlterQuantum)) < 1e-9
        && Math.Abs(ResidualCents) <= MaxResidualCents;

    /// <summary>The accidental glyph, for a C tonic. Uses Unicode musical accidentals.</summary>
    public string AccidentalSymbol => Alter switch
    {
        0 => "",
        0.5 => "½♯",   // half-sharp
        -0.5 => "½♭",  // half-flat
        1 => "♯",           // sharp
        -1 => "♭",          // flat
        1.5 => "¾♯",   // sesqui-sharp
        -1.5 => "¾♭",  // sesqui-flat
        2 => "𝄪",     // double sharp
        -2 => "𝄫",    // double flat
        _ => $"{Alter:+0.##;-0.##}",
    };

    /// <summary>This degree written against a C tonic, e.g. <c>E-half-flat</c>.</summary>
    public string ToStringOnC() => $"{LetterNames[DiatonicStep]}{AccidentalSymbol}";

    public override string ToString() => ToStringOnC();
}
