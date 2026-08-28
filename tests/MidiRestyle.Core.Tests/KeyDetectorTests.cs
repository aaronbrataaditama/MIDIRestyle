using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="KeyDetector"/> - Krumhansl-Schmuckler key detection.
/// </summary>
/// <remarks>
/// The correlation arithmetic is the easy part. What these tests pin down is the statistics around
/// it: that an undefined correlation is reported as "no key" rather than defaulting to C major, that
/// a margin of 0.0443 on a bare C major scale is reported as ambiguity rather than as an answer, and
/// that a whole-tone input - where six candidates are mathematically identical - still produces the
/// same shortlist in the same order every single run.
/// <para>
/// Every expected figure here was computed independently from this implementation and matches the
/// figures in the plan to four decimal places.
/// </para>
/// </remarks>
public class KeyDetectorTests
{
    private const int C = 0;
    private const int Cs = 1;
    private const int D = 2;
    private const int Ds = 3;
    private const int E = 4;
    private const int F = 5;
    private const int Fs = 6;
    private const int G = 7;
    private const int Gs = 8;
    private const int A = 9;
    private const int As = 10;
    private const int B = 11;

    private static readonly int[] CMajorPitchClasses = [C, D, E, F, G, A, B];
    private static readonly int[] FsMajorPitchClasses = [Fs, Gs, As, B, Cs, Ds, F];
    private static readonly int[] WholeTonePitchClasses = [C, D, E, Fs, Gs, As];

    // ---------------------------------------------------------------- profile construction

    /// <summary>
    /// Builds a one-track project whose notes are the given (pitch class, duration) pairs, all on
    /// the same channel. Octave is irrelevant to the profile, so everything sits in octave 5.
    /// </summary>
    private static MidiProject Project(params (int PitchClass, long Length)[] notes) =>
        ProjectFrom(Track(0, channel: 0, notes));

    private static MidiProject ProjectFrom(params TrackInfo[] tracks) => new()
    {
        Format = MidiFileFormatKind.MultiTrack,
        Division = new TicksPerQuarterNote(480),
        Tracks = tracks,
    };

    private static TrackInfo Track(int index, int channel, params (int PitchClass, long Length)[] notes)
    {
        var built = new List<Note>();
        long start = 0;
        foreach ((int pitchClass, long length) in notes)
        {
            built.Add(new Note(Pitch.FromMidi(60 + pitchClass), start, length, 96));
            start += length == 0 ? 1 : length;
        }

        return new TrackInfo { TrackIndex = index, Channel = channel, Notes = built };
    }

    /// <summary>One note per pitch class, all the same length: the "equal durations" fixtures.</summary>
    private static (int PitchClass, long Length)[] Equal(params int[] pitchClasses) =>
        [.. pitchClasses.Select(pc => (pc, 240L))];

    private static void ShouldBe(KeyEstimate estimate, int pitchClass, bool isMinor)
    {
        estimate.PitchClass.Should().Be(pitchClass);
        estimate.IsMinor.Should().Be(isMinor);
    }

    // ---------------------------------------------------------------- no key detected

    /// <summary>
    /// A project with no notes has nothing to correlate. Defaulting to C major here would be
    /// actively harmful, not merely wrong: the detected tonic also defaults the *target* tonic, so
    /// the user's output would be silently transposed to a key nothing in the file suggested.
    /// </summary>
    [Fact]
    public void EmptyProjectDetectsNoKeyRatherThanDefaultingToCMajor()
    {
        KeyDetectionResult result = KeyDetector.Detect(ProjectFrom());

        result.Outcome.Should().Be(KeyDetectionOutcome.NoKeyDetected);
        result.HasKey.Should().BeFalse();
        result.Best.Should().BeNull();
        result.TopCandidate.Should().BeNull();
        result.Candidates.Should().BeEmpty();
        result.Margin.Should().Be(0);
    }

    /// <summary>
    /// A drums-only file has plenty of notes and no pitches. Channel 9's note numbers select which
    /// drum is struck, so every one of them is excluded - which leaves an empty profile.
    /// </summary>
    [Fact]
    public void DrumsOnlyProjectDetectsNoKey()
    {
        MidiProject project = ProjectFrom(
            Track(0, TrackInfo.DrumChannel, Equal(CMajorPitchClasses)));

        project.TotalNoteCount.Should().Be(7, "the drum notes are really there");
        KeyDetector.Detect(project).Outcome.Should().Be(KeyDetectionOutcome.NoKeyDetected);
    }

    /// <summary>
    /// Twelve equally weighted bins have zero variance, so the Pearson denominator is zero and all
    /// 24 correlations are NaN. The detector must recognise that as "no key", not sort a list of
    /// NaNs - comparisons against NaN are all false, so the resulting order would be whatever the
    /// sort's pivot choices happened to produce.
    /// </summary>
    [Fact]
    public void ChromaticScaleInEqualDurationsHasZeroVarianceAndDetectsNoKey()
    {
        MidiProject project = Project(Equal([.. Enumerable.Range(0, 12)]));

        PitchClassProfile profile = PitchClassProfile.FromProject(project);
        profile.IsEmpty.Should().BeFalse();
        profile.HasVariance.Should().BeFalse();
        double r = KeyDetector.Correlation(profile.Weights, KeyDetector.ProfileFor(C, isMinor: false));
        double.IsNaN(r).Should().BeTrue("the Pearson denominator is zero");

        KeyDetector.Detect(project).Outcome.Should().Be(KeyDetectionOutcome.NoKeyDetected);
    }

    [Fact]
    public void RankAllReturnsNothingForAnUnusableProfile() =>
        KeyDetector.RankAll(PitchClassProfile.Empty).Should().BeEmpty();

    // ---------------------------------------------------------------- the C major scale

    /// <summary>
    /// The case that motivates the whole design. C major wins, but by 0.0443 - and the runner-up is
    /// its relative minor, which shares all seven pitch classes. Reporting r = 0.7564 would claim
    /// 76% certainty; reporting the margin claims 4% on an input a musician would call obvious.
    /// Both numbers are therefore carried, and the margin is the one shown as confidence.
    /// </summary>
    [Fact]
    public void CMajorScaleInEqualDurationsRanksCMajorFirstAndAMinorSecond()
    {
        KeyDetectionResult result = KeyDetector.Detect(Project(Equal(CMajorPitchClasses)));

        result.HasKey.Should().BeTrue();
        ShouldBe(result.Candidates[0], C, isMinor: false);
        ShouldBe(result.Candidates[1], A, isMinor: true);
        result.Candidates[0].R.Should().BeApproximately(0.7564, 1e-4);
        result.Candidates[1].R.Should().BeApproximately(0.7121, 1e-4);
        result.Margin.Should().BeApproximately(0.0443, 1e-4);
    }

    /// <summary>
    /// And because that margin is below the 0.05 threshold, the bare scale is reported as ambiguous
    /// with no winner declared. This is the intended behaviour, not a shortcoming: with no metrical
    /// or harmonic emphasis, those seven notes really are C major and A minor equally.
    /// </summary>
    [Fact]
    public void CMajorScaleInEqualDurationsIsReportedAmbiguousBecauseTheRelativeMinorIsThatClose()
    {
        KeyDetectionResult result = KeyDetector.Detect(Project(Equal(CMajorPitchClasses)));

        result.IsAmbiguous.Should().BeTrue();
        result.Best.Should().BeNull("an ambiguous result declares no winner");
        result.TopCandidate.Should().NotBeNull("but the ranking is still offered");
    }

    /// <summary>Emphasise the tonic and dominant and the same seven notes become unambiguous.</summary>
    [Fact]
    public void CMajorWithTonicAndDominantEmphasisBecomesUnambiguous()
    {
        KeyDetectionResult result = KeyDetector.Detect(Project(
            (C, 1600), (G, 1200), (E, 800), (D, 400), (F, 400), (A, 400), (B, 400)));

        result.Outcome.Should().Be(KeyDetectionOutcome.Detected);
        result.Best.Should().NotBeNull();
        ShouldBe(result.Best!, C, isMinor: false);
        result.Margin.Should().BeGreaterThan(KeyDetector.DefaultAmbiguityThreshold);
    }

    // ---------------------------------------------------------------- duration weighting

    /// <summary>
    /// Duration weighting is not a detail. Here the *most frequent* notes spell C major - 130 short
    /// notes of it - while the *longest* notes spell F# major in only seven. Count-weighting gives
    /// C major; duration-weighting gives F# major, which is the answer a listener would give, since
    /// the F# material occupies 1300 ticks against C major's 130.
    /// </summary>
    [Fact]
    public void TheLongestNotesWinOverTheMostFrequentOnes()
    {
        MidiProject project = ProjectFrom(Track(0, channel: 0, DurationVersusFrequencyNotes()));

        KeyDetectionResult result = KeyDetector.Detect(project);

        result.Outcome.Should().Be(KeyDetectionOutcome.Detected);
        ShouldBe(result.Best!, Fs, isMinor: false);
        result.Best!.R.Should().BeApproximately(0.9703, 1e-4);
        result.Margin.Should().BeApproximately(0.3117, 1e-4);
    }

    /// <summary>
    /// The control for the test above: the very same notes, counted rather than timed, give C major
    /// just as decisively. Without this the previous test could pass on an implementation that
    /// happened to favour F# for some unrelated reason.
    /// </summary>
    [Fact]
    public void TheSameMaterialCountedRatherThanTimedGivesTheOtherKey()
    {
        double[] counts = new double[PitchClassProfile.BinCount];
        foreach ((int pitchClass, long _) in DurationVersusFrequencyNotes())
        {
            counts[pitchClass] += 1;
        }

        KeyDetectionResult counted = KeyDetector.Detect(PitchClassProfile.FromWeights(counts));

        ShouldBe(counted.Best!, C, isMinor: false);
        counted.Margin.Should().BeApproximately(0.3107, 1e-4);
    }

    /// <summary>
    /// 130 short C major notes with the tonic and dominant favoured, plus seven long F# major notes.
    /// </summary>
    private static (int PitchClass, long Length)[] DurationVersusFrequencyNotes()
    {
        var notes = new List<(int, long)>();
        foreach ((int pitchClass, int repeats) in
                 new[] { (C, 40), (G, 30), (E, 20), (D, 10), (F, 10), (A, 10), (B, 10) })
        {
            for (int i = 0; i < repeats; i++)
            {
                notes.Add((pitchClass, 1L));
            }
        }

        notes.AddRange([(Fs, 400L), (Cs, 300L), (As, 200L), (Gs, 100L), (B, 100L), (Ds, 100L), (F, 100L)]);
        return [.. notes];
    }

    // ---------------------------------------------------------------- ambiguity

    /// <summary>
    /// A whole-tone scale has no tonic, and the arithmetic says so: six major candidates correlate
    /// at 0.0680 and agree to fifteen significant figures. Any single answer here would be a
    /// fabrication.
    /// </summary>
    [Fact]
    public void WholeToneInputIsReportedAmbiguousWithNoWinner()
    {
        KeyDetectionResult result = KeyDetector.Detect(Project(Equal(WholeTonePitchClasses)));

        result.Outcome.Should().Be(KeyDetectionOutcome.Ambiguous);
        result.Best.Should().BeNull();
        result.Candidates.Should().HaveCount(3);
        result.Margin.Should().BeLessThan(1e-9, "the top candidates are numerically identical");
        result.Candidates[0].R.Should().BeApproximately(0.0680, 1e-4);
    }

    /// <summary>
    /// The six tied candidates differ only in the last bit or two of the floating-point sum, so
    /// ordering on the raw doubles would seat F# major at the head of the list for no reason anyone
    /// could explain. The documented rule - lower pitch class first, then major before minor -
    /// applies within the tie tolerance, so the shortlist reads C, D, E major.
    /// </summary>
    [Fact]
    public void WholeToneTiesBreakByLowerPitchClassThenMajorBeforeMinor()
    {
        KeyDetectionResult result = KeyDetector.Detect(Project(Equal(WholeTonePitchClasses)));

        ShouldBe(result.Candidates[0], C, isMinor: false);
        ShouldBe(result.Candidates[1], D, isMinor: false);
        ShouldBe(result.Candidates[2], E, isMinor: false);
    }

    /// <summary>Repeated runs over freshly built inputs must not reorder the shortlist.</summary>
    [Fact]
    public void WholeToneShortlistIsIdenticalAcrossRepeatedRuns()
    {
        string First() => string.Join(
            " | ",
            KeyDetector.Detect(Project(Equal(WholeTonePitchClasses))).Candidates.Select(c => c.Name));

        string expected = First();
        for (int run = 0; run < 20; run++)
        {
            First().Should().Be(expected);
        }

        expected.Should().Be("C major | D major | E major");
    }

    /// <summary>
    /// Note order must not affect the outcome either - the profile is a histogram, and a histogram
    /// built in a different order is the same histogram.
    /// </summary>
    [Fact]
    public void ShortlistDoesNotDependOnTheOrderTheNotesArriveIn()
    {
        int[] shuffled = [Gs, C, As, E, Fs, D];

        KeyDetector.Detect(Project(Equal(shuffled))).Candidates.Select(c => c.Name)
            .Should().Equal(
                KeyDetector.Detect(Project(Equal(WholeTonePitchClasses))).Candidates.Select(c => c.Name));
    }

    /// <summary>
    /// One sustained pitch class scores C major 0.6845 and C minor 0.6842 - two correlations that
    /// both look strong, separated by 0.0003. It is the clearest demonstration that r is not a
    /// confidence: nothing in the input distinguishes major from minor, and only the margin says so.
    /// </summary>
    [Fact]
    public void ASingleSustainedPitchClassIsAmbiguousBetweenMajorAndMinor()
    {
        KeyDetectionResult result = KeyDetector.Detect(Project((C, 9600)));

        result.Outcome.Should().Be(KeyDetectionOutcome.Ambiguous);
        ShouldBe(result.Candidates[0], C, isMinor: false);
        ShouldBe(result.Candidates[1], C, isMinor: true);
        result.Candidates[0].R.Should().BeApproximately(0.6845, 1e-4);
        result.Candidates[1].R.Should().BeApproximately(0.6842, 1e-4);
        result.Margin.Should().BeApproximately(0.000282, 1e-6);
        result.Margin.Should().BeLessThan(KeyDetector.DefaultAmbiguityThreshold);
    }

    // ---------------------------------------------------------------- drum exclusion

    /// <summary>
    /// Channel 10 (0-indexed 9) is excluded from the pitch-class profile. The drum track here is
    /// deliberately overwhelming - seven long F# major notes against a quiet C major part - and it
    /// must change the detected key by exactly nothing.
    /// </summary>
    [Fact]
    public void ABusyDrumTrackSpellingAnotherKeyChangesNothing()
    {
        (int, long)[] pitched = [(C, 1600), (G, 1200), (E, 800), (D, 400), (F, 400), (A, 400), (B, 400)];
        (int, long)[] drums =
            [.. FsMajorPitchClasses.Select(pc => (pc, 100_000L))];

        KeyDetectionResult without = KeyDetector.Detect(ProjectFrom(Track(0, 0, pitched)));
        KeyDetectionResult with = KeyDetector.Detect(ProjectFrom(
            Track(0, 0, pitched),
            Track(1, TrackInfo.DrumChannel, drums)));

        with.Profile.Weights.Should().Equal(without.Profile.Weights);
        with.Outcome.Should().Be(without.Outcome);
        with.Candidates.Should().Equal(without.Candidates);
        ShouldBe(with.Best!, C, isMinor: false);
    }

    /// <summary>The same exclusion, stated directly against the profile.</summary>
    [Fact]
    public void DrumNotesContributeNoWeightToTheProfile()
    {
        MidiProject project = ProjectFrom(
            Track(0, 0, (C, 480)),
            Track(1, TrackInfo.DrumChannel, (Fs, 480), (As, 480)));

        PitchClassProfile profile = PitchClassProfile.FromProject(project);

        profile.Total.Should().Be(480);
        profile[Fs].Should().Be(0);
        profile[As].Should().Be(0);
    }

    // ---------------------------------------------------------------- clear keys

    /// <summary>
    /// A D minor part with the tonic and dominant held longest. Note that D natural minor in *equal*
    /// durations would rank F major first at 0.7564 - the relative-major trap - so the emphasis is
    /// what makes this a clear case rather than an ambiguous one.
    /// </summary>
    [Fact]
    public void AClearDMinorPartDetectsDMinor()
    {
        KeyDetectionResult result = KeyDetector.Detect(Project(
            (D, 1920), (A, 1440), (F, 960), (E, 480), (G, 480), (As, 480), (C, 480)));

        result.Outcome.Should().Be(KeyDetectionOutcome.Detected);
        ShouldBe(result.Best!, D, isMinor: true);
        result.Best!.R.Should().BeApproximately(0.9471, 1e-4);
        result.Margin.Should().BeApproximately(0.2846, 1e-4);
    }

    /// <summary>The same shape a tritone away, to prove nothing is anchored to C.</summary>
    [Fact]
    public void AClearFSharpMajorPartDetectsFSharpMajor()
    {
        KeyDetectionResult result = KeyDetector.Detect(Project(
            (Fs, 1920), (Cs, 1440), (As, 960), (Gs, 480), (B, 480), (Ds, 480), (F, 480)));

        result.Outcome.Should().Be(KeyDetectionOutcome.Detected);
        ShouldBe(result.Best!, Fs, isMinor: false);
        result.Best!.R.Should().BeApproximately(0.9756, 1e-4);
        result.Margin.Should().BeApproximately(0.3032, 1e-4);
    }

    /// <summary>
    /// D natural minor in equal durations, kept as a regression note: K-S ranks the relative major
    /// first here. The shortlist is what saves the user, which is why it is always offered.
    /// </summary>
    [Fact]
    public void DNaturalMinorInEqualDurationsRanksItsRelativeMajorFirstAndSaysSoAmbiguously()
    {
        KeyDetectionResult result = KeyDetector.Detect(Project(Equal([D, E, F, G, A, As, C])));

        result.Outcome.Should().Be(KeyDetectionOutcome.Ambiguous);
        ShouldBe(result.Candidates[0], F, isMinor: false);
        ShouldBe(result.Candidates[1], D, isMinor: true);
        result.Margin.Should().BeApproximately(0.0443, 1e-4);
    }

    // ---------------------------------------------------------------- shortlist shape

    [Fact]
    public void ADetectedKeyAlwaysComesWithThreeCandidatesInDescendingOrder()
    {
        foreach (KeyDetectionResult result in new[]
                 {
                     KeyDetector.Detect(Project(Equal(CMajorPitchClasses))),
                     KeyDetector.Detect(Project((D, 1920), (A, 1440), (F, 960), (C, 480))),
                     KeyDetector.Detect(Project(Equal(WholeTonePitchClasses))),
                     KeyDetector.Detect(Project((C, 9600))),
                 })
        {
            result.HasKey.Should().BeTrue();
            result.Candidates.Should().HaveCount(KeyDetectionResult.ShortlistSize);

            for (int i = 1; i < result.Candidates.Count; i++)
            {
                // Within the tie tolerance, ordering is by pitch class rather than by the last bit
                // of the correlation - so descending is asserted to that same tolerance.
                result.Candidates[i].R.Should()
                    .BeLessThanOrEqualTo(result.Candidates[i - 1].R + KeyDetector.TieTolerance);
            }
        }
    }

    [Fact]
    public void RankAllReturnsEveryOneOfTheTwentyFourCandidatesOnce()
    {
        IReadOnlyList<KeyEstimate> all =
            KeyDetector.RankAll(PitchClassProfile.FromProject(Project(Equal(CMajorPitchClasses))));

        all.Should().HaveCount(KeyDetector.CandidateCount);
        all.Select(e => (e.PitchClass, e.IsMinor)).Should().OnlyHaveUniqueItems();
        all.Should().AllSatisfy(e => double.IsFinite(e.R).Should().BeTrue());
    }

    /// <summary>
    /// The leader's margin is its lead over the runner-up; every other candidate reports a negative
    /// gap to the leader, so a UI can never read a trailing candidate's margin as confidence.
    /// </summary>
    [Fact]
    public void OnlyTheLeadingCandidateHasAPositiveMargin()
    {
        KeyDetectionResult result = KeyDetector.Detect(Project(
            (D, 1920), (A, 1440), (F, 960), (E, 480), (G, 480), (As, 480), (C, 480)));

        result.Candidates[0].Margin.Should().Be(result.Margin).And.BePositive();
        result.Candidates[1].Margin.Should()
            .BeApproximately(result.Candidates[1].R - result.Candidates[0].R, 1e-12)
            .And.BeNegative();
        result.Candidates[2].Margin.Should().BeNegative();
    }

    // ---------------------------------------------------------------- the numbers themselves

    /// <summary>
    /// The Krumhansl-Kessler profiles, guarded against well-meaning "corrections". These are from
    /// Krumhansl 1990, pp. 37 and 81-96, and match Humdrum's keycor and music21.
    /// </summary>
    [Fact]
    public void TheKrumhanslKesslerProfilesAreTheKrumhansl1990Values()
    {
        KeyDetector.MajorProfile.Should().Equal(
            6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88);
        KeyDetector.MinorProfile.Should().Equal(
            6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17);
    }

    /// <summary>
    /// Rotation needs a positive modulo: every bin below the tonic gives a negative index under C#'s
    /// remainder operator, which would throw rather than wrap.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(11, true)]
    public void RotatingAProfilePutsTheTemplateTonicOnTheRequestedPitchClass(int pitchClass, bool isMinor)
    {
        IReadOnlyList<double> rotated = KeyDetector.ProfileFor(pitchClass, isMinor);
        IReadOnlyList<double> template = isMinor ? KeyDetector.MinorProfile : KeyDetector.MajorProfile;

        rotated.Should().HaveCount(12);
        for (int i = 0; i < 12; i++)
        {
            rotated[(pitchClass + i) % 12].Should().Be(template[i]);
        }
    }

    [Fact]
    public void CorrelationOfASeriesWithItselfIsOne() =>
        KeyDetector.Correlation(KeyDetector.MajorProfile, KeyDetector.MajorProfile)
            .Should().BeApproximately(1.0, 1e-12);

    [Fact]
    public void CorrelationOfAConstantSeriesIsNaNRatherThanZero() =>
        double.IsNaN(KeyDetector.Correlation(
            [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1], KeyDetector.MajorProfile)).Should().BeTrue();

    // ---------------------------------------------------------------- profile behaviour

    /// <summary>
    /// Zero-length notes are legal MIDI and the loader keeps them. A file made entirely of them has
    /// notes but no sounding time, so the duration-weighted histogram would be empty and detection
    /// would report "no key" on a file that plainly has a key. Occurrence counts stand in, and only
    /// then.
    /// </summary>
    [Fact]
    public void AFileOfZeroLengthNotesFallsBackToOccurrenceCounts()
    {
        MidiProject project = Project(
            [.. CMajorPitchClasses.Select(pc => (pc, 0L)), (C, 0L), (G, 0L)]);

        PitchClassProfile profile = PitchClassProfile.FromProject(project);

        profile[C].Should().Be(2);
        profile[G].Should().Be(2);
        profile[D].Should().Be(1);
        KeyDetector.Detect(project).Candidates[0].PitchClass.Should().Be(C);
    }

    /// <summary>But a single sounding note is enough to make it duration-weighted again.</summary>
    [Fact]
    public void AnyRealDurationSuppressesTheCountFallback()
    {
        PitchClassProfile profile =
            PitchClassProfile.FromProject(Project((C, 0), (C, 0), (C, 0), (G, 480)));

        profile[C].Should().Be(0);
        profile[G].Should().Be(480);
    }

    [Fact]
    public void ProfilesRejectAWeightVectorOfTheWrongLength()
    {
        Action act = () => PitchClassProfile.FromWeights([1, 2, 3]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProfilesRejectNonFiniteWeightsRatherThanLettingNaNReachTheRanking()
    {
        double[] weights = new double[12];
        weights[0] = double.NaN;

        Action act = () => PitchClassProfile.FromWeights(weights);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheEmptyProfileIsEmptyAndUnusable()
    {
        PitchClassProfile.Empty.IsEmpty.Should().BeTrue();
        PitchClassProfile.Empty.HasVariance.Should().BeFalse();
        PitchClassProfile.Empty.IsUsable.Should().BeFalse();
    }

    /// <summary>
    /// The threshold is a parameter, not a constant carved into the detector - the plan's 0.05 is a
    /// default. Raising it above the C major scale's 0.0443 must turn that answer into ambiguity.
    /// </summary>
    [Theory]
    [InlineData(0.0, KeyDetectionOutcome.Detected)]
    [InlineData(0.04, KeyDetectionOutcome.Detected)]
    [InlineData(0.05, KeyDetectionOutcome.Ambiguous)]
    [InlineData(0.5, KeyDetectionOutcome.Ambiguous)]
    public void TheAmbiguityThresholdIsAdjustable(double threshold, KeyDetectionOutcome expected) =>
        KeyDetector.Detect(Project(Equal(CMajorPitchClasses)), threshold).Outcome.Should().Be(expected);

    [Fact]
    public void ANegativeAmbiguityThresholdIsRejected()
    {
        Action act = () => KeyDetector.Detect(PitchClassProfile.Empty, -0.1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void KeyEstimatesNameThemselvesReadably()
    {
        new KeyEstimate(D, true, 0.9, 0.3).Name.Should().Be("D minor");
        new KeyEstimate(As, false, 0.9, 0.3).Name.Should().Be("Bb major");
    }
}
