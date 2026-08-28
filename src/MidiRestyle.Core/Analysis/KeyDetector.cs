using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Analysis;

/// <summary>
/// Krumhansl-Schmuckler key detection: a duration-weighted pitch-class profile correlated against
/// the 24 rotations of the Krumhansl-Kessler major and minor key profiles.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm itself is four lines of arithmetic. Everything interesting here is what surrounds
/// it, because <b>the raw correlation is a poor confidence signal</b> and reporting it would
/// misinform on both sides. Measured behaviour of this exact implementation:
/// </para>
/// <list type="bullet">
///   <item>A plain C major scale in equal durations: C major 0.7564, A minor 0.7121 - a margin of
///   0.0443 on a completely unambiguous input, because the relative minor shares all seven pitch
///   classes.</item>
///   <item>A whole-tone scale: six major candidates identical to fifteen significant figures at
///   0.0680. There is no answer, and any one of them would look like a 0.07 answer.</item>
///   <item>A single sustained pitch class: C major 0.6845 against C minor 0.6842, a margin of
///   0.0003. Two strong-looking correlations, and nothing whatsoever distinguishing them.</item>
///   <item>No non-drum notes: the Pearson denominator is zero, so all 24 correlations are NaN.</item>
/// </list>
/// <para>
/// Hence: both numbers are reported, the margin is the confidence, a margin below
/// <see cref="DefaultAmbiguityThreshold"/> is reported as ambiguity rather than as an answer, and an
/// undefined correlation returns <see cref="KeyDetectionOutcome.NoKeyDetected"/> rather than
/// defaulting to C major. That last one is not fastidiousness: the detected tonic also defaults the
/// <em>target</em> tonic, so a silent default would silently transpose the user's entire output.
/// </para>
/// <para>
/// NaN is handled defensively at every step. A NaN never reaches a comparison - it fails every
/// ordering predicate, so a sort containing one produces an arbitrary and input-order-dependent
/// result rather than an error.
/// </para>
/// </remarks>
public static class KeyDetector
{
    /// <summary>
    /// The Krumhansl-Kessler major key profile, C-rooted.
    /// </summary>
    /// <remarks>
    /// From Krumhansl, <i>Cognitive Foundations of Musical Pitch</i> (1990), pp. 37 and 81-96; the
    /// 1982 Krumhansl-Kessler paper gives them only as a figure. Verified against Humdrum's
    /// <c>keycor</c> and music21. Do not "correct" these values.
    /// </remarks>
    public static readonly IReadOnlyList<double> MajorProfile =
        [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];

    /// <summary>The Krumhansl-Kessler minor key profile, C-rooted. Same source as the major.</summary>
    public static readonly IReadOnlyList<double> MinorProfile =
        [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    /// <summary>
    /// The margin below which a result is reported as ambiguous rather than as an answer. A plain
    /// C major scale clears it only by losing - it scores 0.0443 - which is exactly the point: on a
    /// bare scale with no metrical or harmonic emphasis, C major and A minor genuinely are the same
    /// seven notes, and the honest report is both of them.
    /// </summary>
    public const double DefaultAmbiguityThreshold = 0.05;

    /// <summary>
    /// Correlations within this of each other are treated as equal for ranking, and broken by the
    /// deterministic rule instead: lower pitch class first, then major before minor.
    /// </summary>
    /// <remarks>
    /// A whole-tone profile makes six major candidates mathematically identical, but they emerge
    /// from the floating-point summation differing in the last bit or two - around 1e-16. Sorting on
    /// those bits is repeatable, but it is repeatable noise: it would put F# major at the head of the
    /// list for no reason a user could ever be told. Quantising to this tolerance before comparing
    /// makes the documented tie-break rule actually reachable, and keeps the shortlist stable if the
    /// summation order ever changes.
    /// </remarks>
    public const double TieTolerance = 1e-9;

    /// <summary>The number of candidates correlated: twelve tonics, major and minor.</summary>
    public const int CandidateCount = MidiRounding.SemitonesPerOctave * 2;

    /// <summary>Detects the key of a project. Drum channels are excluded from the profile.</summary>
    public static KeyDetectionResult Detect(
        MidiProject project,
        double ambiguityThreshold = DefaultAmbiguityThreshold)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Detect(PitchClassProfile.FromProject(project), ambiguityThreshold);
    }

    /// <summary>Detects the key described by an already-built pitch-class profile.</summary>
    public static KeyDetectionResult Detect(
        PitchClassProfile profile,
        double ambiguityThreshold = DefaultAmbiguityThreshold)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfNegative(ambiguityThreshold);

        // Checked before correlating rather than after, so the "no key" case is a stated condition
        // of the input instead of a NaN discovered downstream.
        if (!profile.IsUsable)
        {
            return KeyDetectionResult.NoKey(profile, ambiguityThreshold);
        }

        List<Scored> ranked = RankInternal(profile);
        if (ranked.Count < 2)
        {
            // Unreachable while the profile is usable - kept because the margin below assumes a
            // runner-up exists, and a silent IndexOutOfRange would be a worse way to learn otherwise.
            return KeyDetectionResult.NoKey(profile, ambiguityThreshold);
        }

        // Clamped: the tie tolerance can seat a candidate whose R is lower by ~1e-16 at the head of
        // the list, and a negative "lead" is float noise, not a finding.
        double margin = Math.Max(0, ranked[0].R - ranked[1].R);

        IReadOnlyList<KeyEstimate> candidates =
            BuildEstimates(ranked, Math.Min(KeyDetectionResult.ShortlistSize, ranked.Count));

        return KeyDetectionResult.Ranked(candidates, margin, profile, ambiguityThreshold);
    }

    /// <summary>
    /// All 24 candidates in ranked order, for diagnosis and for a UI that wants to show more than
    /// the shortlist. Empty when the profile cannot be correlated.
    /// </summary>
    public static IReadOnlyList<KeyEstimate> RankAll(PitchClassProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsUsable)
        {
            return [];
        }

        List<Scored> ranked = RankInternal(profile);
        return BuildEstimates(ranked, ranked.Count);
    }

    /// <summary>
    /// Turns the top <paramref name="count"/> of a ranked list into estimates, giving each one its
    /// gap to the best <em>other</em> candidate: the detection confidence for the leader, and a
    /// negative figure for everyone below showing how far behind the leader they sit.
    /// </summary>
    private static IReadOnlyList<KeyEstimate> BuildEstimates(List<Scored> ranked, int count)
    {
        var estimates = new List<KeyEstimate>(count);
        for (int i = 0; i < count; i++)
        {
            double gap = ranked.Count < 2
                ? 0
                : i == 0
                    ? Math.Max(0, ranked[0].R - ranked[1].R)
                    : ranked[i].R - ranked[0].R;

            estimates.Add(new KeyEstimate(ranked[i].PitchClass, ranked[i].IsMinor, ranked[i].R, gap));
        }

        return estimates;
    }

    /// <summary>
    /// The Krumhansl-Kessler profile for one key: the C-rooted template rotated so that
    /// <paramref name="pitchClass"/> is its tonic.
    /// </summary>
    public static IReadOnlyList<double> ProfileFor(int pitchClass, bool isMinor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pitchClass);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pitchClass, PitchClassProfile.BinCount);

        IReadOnlyList<double> template = isMinor ? MinorProfile : MajorProfile;
        double[] rotated = new double[PitchClassProfile.BinCount];
        for (int i = 0; i < rotated.Length; i++)
        {
            // Positive modulo. i - pitchClass goes negative for every bin below the tonic, and C#'s
            // % keeps the sign of the dividend.
            int index = (i - pitchClass) % PitchClassProfile.BinCount;
            rotated[i] = template[index < 0 ? index + PitchClassProfile.BinCount : index];
        }

        return rotated;
    }

    /// <summary>
    /// Pearson product-moment correlation. Returns NaN when either input has zero variance, which is
    /// the honest answer: the correlation is genuinely undefined, and substituting zero would rank an
    /// undefined fit alongside a measured one.
    /// </summary>
    public static double Correlation(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Count != y.Count)
        {
            throw new ArgumentException(
                $"Correlation needs equal-length series; got {x.Count} and {y.Count}.",
                nameof(y));
        }

        int n = x.Count;
        if (n == 0)
        {
            return double.NaN;
        }

        double meanX = 0;
        double meanY = 0;
        for (int i = 0; i < n; i++)
        {
            meanX += x[i];
            meanY += y[i];
        }

        meanX /= n;
        meanY /= n;

        double covariance = 0;
        double varianceX = 0;
        double varianceY = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - meanX;
            double dy = y[i] - meanY;
            covariance += dx * dy;
            varianceX += dx * dx;
            varianceY += dy * dy;
        }

        double denominator = Math.Sqrt(varianceX) * Math.Sqrt(varianceY);
        return denominator > 0 ? covariance / denominator : double.NaN;
    }

    private static List<Scored> RankInternal(PitchClassProfile profile)
    {
        var scored = new List<Scored>(CandidateCount);
        for (int pitchClass = 0; pitchClass < PitchClassProfile.BinCount; pitchClass++)
        {
            for (int mode = 0; mode < 2; mode++)
            {
                bool isMinor = mode == 1;
                double r = Correlation(profile.Weights, ProfileFor(pitchClass, isMinor));

                // A NaN or infinite candidate is dropped rather than sorted. Comparisons against NaN
                // are all false, so leaving one in the list makes the whole ordering depend on the
                // sort's internal pivot choices.
                if (double.IsFinite(r))
                {
                    scored.Add(new Scored(pitchClass, isMinor, r));
                }
            }
        }

        scored.Sort(CompareByRank);
        return scored;
    }

    /// <summary>
    /// Descending by quantised correlation, then lower pitch class, then major before minor.
    /// Quantising first is what makes the documented tie-break rule reachable; comparing the raw
    /// doubles would let a 1e-16 difference decide, which is noise rather than a ranking.
    /// </summary>
    private static int CompareByRank(Scored a, Scored b)
    {
        int byCorrelation = Quantise(b.R).CompareTo(Quantise(a.R));
        if (byCorrelation != 0)
        {
            return byCorrelation;
        }

        int byPitchClass = a.PitchClass.CompareTo(b.PitchClass);
        return byPitchClass != 0 ? byPitchClass : a.IsMinor.CompareTo(b.IsMinor);
    }

    // Rounding to a fixed grid rather than comparing with an epsilon: an epsilon comparison is not
    // transitive, and List.Sort with an intransitive comparer has no defined result at all.
    private static double Quantise(double r) => Math.Round(r / TieTolerance, MidiRounding.Mode);

    private readonly record struct Scored(int PitchClass, bool IsMinor, double R);
}
