using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MidiRestyle.Core.Tuning;

[assembly: InternalsVisibleTo("MidiRestyle.Core.Tests")]

namespace MidiRestyle.Core.Scales;

/// <summary>
/// Why a Scala <c>.scl</c> import was refused. Malformed <c>.scl</c> content is user input, not
/// programmer error, so the UI needs to say why - not just report a caught exception type.
/// </summary>
public enum ScalaImportFailureReason
{
    /// <summary>The file is not shaped like a .scl file at all (missing lines, unparsable note count).</summary>
    Malformed,

    /// <summary>A pitch line's value token could not be parsed as either cents or a ratio.</summary>
    InvalidPitchValue,

    /// <summary>A ratio pitch line had a non-positive numerator or denominator. Meaningless per spec.</summary>
    NegativeRatio,

    /// <summary>The note-count line did not match the number of pitch lines actually present.</summary>
    DeclaredCountMismatch,

    /// <summary>
    /// The file's last pitch line (its period) is not ~1200 cents. MIDIRestyle's per-scale offset
    /// model assumes exact octave periodicity, so stretched-octave and non-octave tunings (e.g.
    /// Bohlen-Pierce's 3/1 period) are out of scope and refused rather than silently misread.
    /// </summary>
    NonOctavePeriod,

    /// <summary>The scale has more degrees than <see cref="Scale.MaxDegrees"/> supports.</summary>
    TooManyDegrees,

    /// <summary>
    /// Some other <see cref="Scale"/> invariant was violated - e.g. degrees that do not strictly
    /// ascend, or too few degrees once the implicit tonic and stripped period are accounted for.
    /// </summary>
    Validation,
}

/// <summary>A stated reason a Scala import failed, suitable for showing directly to a user.</summary>
public sealed record ScalaImportError(ScalaImportFailureReason Reason, string Message);

/// <summary>
/// The outcome of importing a Scala <c>.scl</c> file: either the parsed <see cref="Scale"/> or a
/// <see cref="ScalaImportError"/> explaining why the import was refused.
/// </summary>
public sealed record ScalaImportResult
{
    private ScalaImportResult(Scale? scale, ScalaImportError? error)
    {
        Scale = scale;
        Error = error;
    }

    /// <summary>True when <see cref="Scale"/> is populated.</summary>
    public bool Success => Scale is not null;

    /// <summary>The imported scale, or null on failure.</summary>
    public Scale? Scale { get; }

    /// <summary>Why the import failed, or null on success.</summary>
    public ScalaImportError? Error { get; }

    internal static ScalaImportResult Ok(Scale scale) => new(scale, null);

    internal static ScalaImportResult Fail(ScalaImportFailureReason reason, string message) =>
        new(null, new ScalaImportError(reason, message));
}

/// <summary>
/// Parses Scala <c>.scl</c> tuning files (format: https://huygens-fokker.org/scala/scl_format.html)
/// into <see cref="Scale"/>. See the remarks for the format quirks a naive line-by-line parser gets
/// wrong - each one has previously produced a plausible-looking but silently wrong tuning.
/// </summary>
/// <remarks>
/// <para>
/// <b>The implicit 1/1 and the explicit 2/1.</b> The first note, <c>1/1</c> or <c>0.0</c> cents, is
/// implicit and never appears in the file. The declared note count on line 2 excludes that implicit
/// tonic but <em>includes</em> the final period entry - a file describing a 12-tone scale declares
/// 12 and its twelfth pitch line is the period. <see cref="Scale.DegreeCents"/> starts at 0 and
/// excludes the octave, so this reader prepends 0 and strips the trailing period entry. Omitting the
/// prepend loses every imported scale's tonic; omitting the strip duplicates the octave in
/// <see cref="Scale.DegreeCents"/>.
/// </para>
/// <para>
/// <b>Cents vs. ratio.</b> A pitch value containing a decimal point is cents (<c>408.</c> is legal
/// and means 408.0 cents; negative cents such as <c>-5.0</c> are legal syntax). Anything else is a
/// ratio, including a bare integer - <c>700</c> means the ratio 700/1 (about 11,344 cents), not 700
/// cents. Ratios may be sub-unity (<c>10/20</c>, about -1200 cents). Negative or zero ratios are a
/// read error per spec. Anything after a valid pitch value on the same line is ignored, and <c>!</c>
/// comment lines may appear between pitch lines, not only in the header.
/// </para>
/// <para>
/// <b>The period need not be 2/1.</b> <c>bohlen-p.scl</c> ends on <c>3/1</c>. MIDIRestyle assumes
/// every scale is octave-periodic at exactly 1200 cents - the per-scale offset model depends on it -
/// so a period more than about a cent from 1200 is rejected with a stated reason rather than
/// silently reinterpreted as an octave.
/// </para>
/// <para>
/// <b>Cardinality is unbounded in the format</b> but capped at <see cref="Scale.MaxDegrees"/> (12)
/// here: a 31-EDO or 22-shruti file needs more pitch-bend channels than the 15-channel budget allows
/// and produces non-monotonic 12-TET quantiser output, so it is refused with an explanatory message
/// naming the actual count rather than left to fail later, deeper in the pipeline.
/// </para>
/// </remarks>
public static class ScalaFileReader
{
    /// <summary>
    /// How far a file's final pitch line may sit from 1200 cents and still be treated as the octave.
    /// Ratio-to-cents rounding for values like <c>2/1</c> is exact, but this leaves headroom for
    /// files that spell the period in cents with limited precision.
    /// </summary>
    private const double PeriodToleranceCents = 1.0;

    /// <summary>
    /// Parses <c>.scl</c> content already in memory. The primary entry point - needs no filesystem,
    /// so tests can exercise every rule without fixture files.
    /// </summary>
    /// <param name="content">The full text of a .scl file.</param>
    /// <param name="sourceLabel">
    /// A label for provenance and the generated <see cref="Scale.Id"/>, typically a filename.
    /// Optional; falls back to the file's own description line when omitted.
    /// </param>
    public static ScalaImportResult ReadFromString(string content, string? sourceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = SplitIntoContentLines(content);
        if (lines.Count < 2)
        {
            return ScalaImportResult.Fail(ScalaImportFailureReason.Malformed,
                "the file has no description line and note-count line to read.");
        }

        string description = lines[0].Trim();
        string countLine = lines[1].Trim();

        if (!int.TryParse(countLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out int declaredCount)
            || declaredCount < 0)
        {
            return ScalaImportResult.Fail(ScalaImportFailureReason.Malformed,
                $"the note-count line ('{countLine}') is not a non-negative integer.");
        }

        var pitchLines = lines.Skip(2).ToList();

        if (pitchLines.Count != declaredCount)
        {
            return ScalaImportResult.Fail(ScalaImportFailureReason.DeclaredCountMismatch,
                $"declares {declaredCount} pitch value(s) but the file contains {pitchLines.Count}. " +
                "The declared count excludes the implicit 1/1 tonic but includes the final period entry.");
        }

        if (declaredCount == 0)
        {
            return ScalaImportResult.Fail(ScalaImportFailureReason.Malformed,
                "declares 0 pitch values - a scale needs at least a period plus one further degree.");
        }

        var rawCents = new double[declaredCount];
        for (int i = 0; i < declaredCount; i++)
        {
            var outcome = ParsePitchToken(pitchLines[i]);
            if (!outcome.Success)
            {
                return ScalaImportResult.Fail(outcome.Reason,
                    $"pitch line {i + 1} ('{pitchLines[i].Trim()}'): {outcome.Message}");
            }

            rawCents[i] = outcome.Cents;
        }

        double period = rawCents[^1];
        if (Math.Abs(period - MidiRounding.CentsPerOctave) > PeriodToleranceCents)
        {
            return ScalaImportResult.Fail(ScalaImportFailureReason.NonOctavePeriod,
                $"has a {period:0.###}-cent period (its final pitch line), not the " +
                $"{MidiRounding.CentsPerOctave:0}-cent octave MIDIRestyle requires. Stretched-octave " +
                "and non-octave tunings are out of scope: the per-scale offset model assumes exact " +
                "1200-cent periodicity throughout.");
        }

        // The implicit 1/1 is prepended as 0 cents; the trailing period entry is stripped, since
        // Scale.DegreeCents starts at 0 and excludes the octave.
        var degreeCents = new double[declaredCount];
        degreeCents[0] = 0.0;
        Array.Copy(rawCents, 0, degreeCents, 1, declaredCount - 1);

        string name = description.Length > 0 ? description : "Unnamed Scala import";
        string label = string.IsNullOrWhiteSpace(sourceLabel) ? "Scala import" : sourceLabel.Trim();
        string source = $"Scala .scl import ({label}): {name}";

        try
        {
            var scale = new Scale(
                id: BuildId(label, name),
                name: name,
                tradition: "Imported",
                region: "Imported",
                degreeCents: degreeCents,
                source: source,
                notatable: false,
                spelling: null,
                description: name);

            return ScalaImportResult.Ok(scale);
        }
        catch (ScaleValidationException ex)
        {
            // Scale.MaxDegrees is checked before the ascending-order check in Scale's own
            // validation, so a length overrun always surfaces here as this specific exception -
            // categorise it distinctly so the UI can give the cardinality-cap explanation rather
            // than a generic one.
            var reason = degreeCents.Length > Scale.MaxDegrees
                ? ScalaImportFailureReason.TooManyDegrees
                : ScalaImportFailureReason.Validation;

            return ScalaImportResult.Fail(reason, ex.Reason);
        }
    }

    /// <summary>Reads and parses a <c>.scl</c> file. Files are Latin-1 per the Scala format spec.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be read.</exception>
    public static ScalaImportResult ReadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string content = File.ReadAllText(path, Encoding.Latin1);
        return ReadFromString(content, Path.GetFileName(path));
    }

    private static List<string> SplitIntoContentLines(string content)
    {
        var rawLines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var kept = new List<string>();
        foreach (var raw in rawLines)
        {
            if (raw.TrimStart().StartsWith('!'))
            {
                // A comment line - the spec allows these between pitch lines, not only in the header.
                continue;
            }

            kept.Add(raw);
        }

        // Defensive only: drop blank lines trailing the file (a final newline splits into a
        // trailing ""). Interior blank lines are left alone and will surface as a normal parse
        // error, since a real pitch line is never blank.
        int end = kept.Count - 1;
        while (end >= 0 && string.IsNullOrWhiteSpace(kept[end]))
        {
            end--;
        }

        return kept.Take(end + 1).ToList();
    }

    /// <summary>
    /// Parses one pitch line's leading value token. Internal rather than private so the cents/ratio
    /// rules can be unit-tested directly, alongside the end-to-end <see cref="ReadFromString"/>
    /// tests that exercise the file-level rules.
    /// </summary>
    internal static PitchParseOutcome ParsePitchToken(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return PitchParseOutcome.Fail(ScalaImportFailureReason.InvalidPitchValue, "blank pitch line.");
        }

        // "Anything after a valid pitch value should be ignored" - take the first token, splitting
        // only on the whitespace the spec names: space and horizontal tab.
        string token = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];

        if (token.Contains('.'))
        {
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double centsValue))
            {
                return PitchParseOutcome.Fail(ScalaImportFailureReason.InvalidPitchValue,
                    $"'{token}' has a decimal point but is not a valid cents value.");
            }

            return PitchParseOutcome.Ok(centsValue);
        }

        string numeratorText = token;
        string denominatorText = "1";
        int slash = token.IndexOf('/');
        if (slash >= 0)
        {
            numeratorText = token[..slash];
            denominatorText = token[(slash + 1)..];
        }

        bool numeratorOk = long.TryParse(numeratorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numerator);
        bool denominatorOk = long.TryParse(denominatorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long denominator);

        if (!numeratorOk || !denominatorOk)
        {
            return PitchParseOutcome.Fail(ScalaImportFailureReason.InvalidPitchValue,
                $"'{token}' is neither a cents value (no decimal point found) nor a recognisable ratio.");
        }

        if (numerator <= 0 || denominator <= 0)
        {
            return PitchParseOutcome.Fail(ScalaImportFailureReason.NegativeRatio,
                $"'{token}' is a non-positive ratio. Negative (or zero) ratios are meaningless and are a read error per the Scala spec.");
        }

        double cents = 1200.0 * Math.Log2(numerator / (double)denominator);
        return PitchParseOutcome.Ok(cents);
    }

    private static string BuildId(string label, string description)
    {
        string basis = label != "Scala import" ? Path.GetFileNameWithoutExtension(label) : description;
        if (string.IsNullOrWhiteSpace(basis))
        {
            basis = description;
        }

        char[] slugChars = basis.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        string slug = new(slugChars);
        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        slug = slug.Trim('-');
        if (slug.Length == 0)
        {
            slug = "import";
        }

        return $"scala.import.{slug}";
    }
}

/// <summary>Result of parsing one pitch line's value token, in cents.</summary>
internal readonly record struct PitchParseOutcome(
    bool Success,
    double Cents,
    ScalaImportFailureReason Reason,
    string Message)
{
    public static PitchParseOutcome Ok(double cents) => new(true, cents, default, "");

    public static PitchParseOutcome Fail(ScalaImportFailureReason reason, string message) =>
        new(false, 0.0, reason, message);
}
