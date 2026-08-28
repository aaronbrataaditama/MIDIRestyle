using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Analysis;

/// <summary>
/// One candidate key, with both numbers that matter: the raw correlation and the margin over its
/// closest rival.
/// </summary>
/// <remarks>
/// <para>
/// <b>Confidence shown to a user is <see cref="Margin"/>, never <see cref="R"/>.</b> The raw
/// Krumhansl-Schmuckler correlation is a poor confidence signal and reporting it would misinform.
/// A plain C major scale in equal durations scores C major at r = 0.7564 - which reads as 76%
/// certainty - while A minor sits right behind it at 0.7121. Meanwhile a single sustained pitch
/// class scores C major 0.6845 against C minor 0.6842: two apparently strong readings separated by
/// 0.0003. The correlation says how well the profile fits the template; only the gap to the
/// runner-up says whether the answer was actually determined.
/// </para>
/// <para>
/// The inverse mistake is just as bad: the margin alone reads as 4% certainty on that unambiguous
/// C major scale, because the relative minor shares all seven pitch classes and always scores
/// close. So both numbers are carried, the margin is what the UI presents as confidence, and the
/// correlation is available for anyone who wants to see the fit itself.
/// </para>
/// </remarks>
/// <param name="PitchClass">Tonic pitch class, 0..11, where 0 is C.</param>
/// <param name="IsMinor">Minor when true, major when false.</param>
/// <param name="R">The raw Pearson correlation against this key's rotated Krumhansl-Kessler profile.</param>
/// <param name="Margin">
/// This candidate's <see cref="R"/> minus that of the best <em>other</em> candidate. Positive and
/// equal to <c>r[0] - r[1]</c> for the top-ranked key - the detection confidence; negative for every
/// candidate below it, showing how far behind the leader it sits.
/// </param>
public sealed record KeyEstimate(int PitchClass, bool IsMinor, double R, double Margin)
{
    /// <summary>
    /// The tonic's letter name, spelled with the library's flat-preferring convention - so pitch
    /// class 6 reads <c>Gb</c>, matching how <see cref="DiatonicSpeller"/> spells scales. Display
    /// only; the pitch class is the identity.
    /// </summary>
    public string TonicName => TonicSpelling.FromPitchClass(PitchClass).ToString();

    /// <summary>A display label, e.g. <c>D minor</c>.</summary>
    public string Name => $"{TonicName} {(IsMinor ? "minor" : "major")}";

    public override string ToString() => $"{Name} (r={R:0.####}, margin={Margin:+0.####;-0.####;0})";
}

/// <summary>What key detection concluded. Three outcomes, because two would hide one of them.</summary>
public enum KeyDetectionOutcome
{
    /// <summary>
    /// Nothing to correlate against: no non-drum notes, or a profile whose bins are all equal. The
    /// 24 correlations are all NaN. Never silently substituted with C major - the detected tonic
    /// also defaults the <em>target</em> tonic, so a bogus detection would silently transpose the
    /// user's whole output.
    /// </summary>
    NoKeyDetected = 0,

    /// <summary>
    /// Candidates were ranked, but the leader's margin fell below the threshold. The top candidates
    /// are offered without declaring a winner.
    /// </summary>
    Ambiguous,

    /// <summary>One candidate led by more than the threshold.</summary>
    Detected,
}

/// <summary>
/// The outcome of key detection: a ranked shortlist, and an honest statement of how sure it is.
/// </summary>
/// <remarks>
/// Detection is a suggestion the user can override, never a silent decision, which is why the
/// shortlist is always present even when a winner is declared. Two known weaknesses of the algorithm
/// are surfaced by that shortlist rather than hidden: a relative major and minor share all seven
/// pitch classes and so always score within a few hundredths of each other, and K-S has a documented
/// tendency to pick the dominant as tonic.
/// </remarks>
public sealed record KeyDetectionResult
{
    /// <summary>The number of candidates offered when a ranking was possible.</summary>
    public const int ShortlistSize = 3;

    private KeyDetectionResult(
        KeyDetectionOutcome outcome,
        IReadOnlyList<KeyEstimate> candidates,
        double margin,
        PitchClassProfile profile,
        double ambiguityThreshold)
    {
        Outcome = outcome;
        Candidates = candidates;
        Margin = margin;
        Profile = profile;
        AmbiguityThreshold = ambiguityThreshold;
    }

    public KeyDetectionOutcome Outcome { get; }

    /// <summary>
    /// The top candidates, best first - <see cref="ShortlistSize"/> of them whenever a ranking was
    /// possible, and empty only when <see cref="Outcome"/> is
    /// <see cref="KeyDetectionOutcome.NoKeyDetected"/>.
    /// </summary>
    /// <remarks>
    /// Ordered by <see cref="KeyEstimate.R"/> descending, with correlations within
    /// <c>KeyDetector.TieTolerance</c> of one another treated as equal and broken deterministically:
    /// lower pitch class first, then major before minor. Without that tolerance a whole-tone input,
    /// whose six major candidates are mathematically identical, would be ordered by the last bit of
    /// floating-point noise - stable for a given build, but arbitrary and unexplainable to a user.
    /// </remarks>
    public IReadOnlyList<KeyEstimate> Candidates { get; }

    /// <summary>
    /// The detection confidence: the leading candidate's correlation minus the runner-up's, clamped
    /// at zero. Zero when no key was detected.
    /// </summary>
    public double Margin { get; }

    /// <summary>The margin below which the result is reported as ambiguous.</summary>
    public double AmbiguityThreshold { get; }

    /// <summary>The profile the correlation was run against. Kept for display and diagnosis.</summary>
    public PitchClassProfile Profile { get; }

    /// <summary>Whether any ranking at all was possible.</summary>
    public bool HasKey => Outcome != KeyDetectionOutcome.NoKeyDetected;

    /// <summary>Whether the leader's margin fell below <see cref="AmbiguityThreshold"/>.</summary>
    public bool IsAmbiguous => Outcome == KeyDetectionOutcome.Ambiguous;

    /// <summary>
    /// The declared winner, or <see langword="null"/> when the result is ambiguous or empty. Callers
    /// that want the ranking regardless of confidence - to preselect a row in the shortlist, say -
    /// want <see cref="TopCandidate"/> instead.
    /// </summary>
    public KeyEstimate? Best => Outcome == KeyDetectionOutcome.Detected ? Candidates[0] : null;

    /// <summary>The highest-ranked candidate whatever the confidence, or null when there is none.</summary>
    public KeyEstimate? TopCandidate => Candidates.Count > 0 ? Candidates[0] : null;

    internal static KeyDetectionResult NoKey(PitchClassProfile profile, double ambiguityThreshold) =>
        new(KeyDetectionOutcome.NoKeyDetected, [], 0, profile, ambiguityThreshold);

    internal static KeyDetectionResult Ranked(
        IReadOnlyList<KeyEstimate> candidates,
        double margin,
        PitchClassProfile profile,
        double ambiguityThreshold) =>
        new(
            margin < ambiguityThreshold ? KeyDetectionOutcome.Ambiguous : KeyDetectionOutcome.Detected,
            candidates,
            margin,
            profile,
            ambiguityThreshold);

    public override string ToString() => Outcome switch
    {
        KeyDetectionOutcome.NoKeyDetected => "No key detected",
        KeyDetectionOutcome.Ambiguous =>
            $"Ambiguous (margin {Margin:0.####}): {string.Join(", ", Candidates.Select(c => c.Name))}",
        _ => $"{Candidates[0].Name} (margin {Margin:0.####})",
    };
}
