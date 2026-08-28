using System.ComponentModel;
using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The right rail's behaviour. Several of these are rules a plausible implementation gets
/// backwards - the contextual fidelity badge above all - so they are pinned here rather than left
/// to a manual pass.
/// </summary>
public class StylePanelViewModelTests
{
    private const string Citation = "Test fixture. Cents chosen to exercise a stated threshold.";

    // Exactly on the 12-TET grid: the badge must never escalate, in any output mode.
    private static Scale Gong() => new(
        "eastasia.chinese.gong", "Gong", "Chinese Pentatonic", "East Asia",
        [0, 200, 400, 700, 900], Citation);

    private static Scale Shang() => new(
        "eastasia.chinese.shang", "Shang", "Chinese Pentatonic", "East Asia",
        [0, 200, 500, 700, 1000], Citation);

    private static Scale Yu() => new(
        "eastasia.chinese.yu", "Yu", "Chinese Pentatonic", "East Asia",
        [0, 300, 500, 700, 1000], Citation);

    /// <summary>
    /// Five near-equal 240-cent steps: 40 cents from 12-TET at the worst degree, and not notatable.
    /// The scale the warning rule exists for.
    /// </summary>
    private static Scale Slendro() => new(
        "seasia.gamelan.slendro", "Slendro", "Javanese Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], Citation, notatable: false);

    private static Scale Pelog() => new(
        "seasia.gamelan.pelog", "Pelog Bem", "Javanese Gamelan", "Southeast Asia",
        [0, 120, 270, 670, 785], Citation, notatable: false);

    /// <summary>
    /// Turkish AEU Rast: worst degree ~15.6 cents out, so <em>Close</em> - never a warning, however
    /// the output mode is set.
    /// </summary>
    private static Scale TurkishRast() => new(
        "turkish.makam.rast", "Rast (AEU)", "Turkish Makam", "Turkey",
        [0, 203.91, 384.36, 498.04, 701.96, 905.87, 1086.31], Citation);

    private static Scale Aeolian() => new(
        StylePanelViewModel.MinorSourceScaleId, "Aeolian", "Western Church Modes", "Europe",
        [0, 200, 300, 500, 700, 800, 1000], Citation);

    private static ScaleLibrary Library() => ScaleLibrary.Build(
        (ScaleOrigin.Embedded,
            new[] { Gong(), Shang(), Yu(), Slendro(), Pelog(), TurkishRast(), Aeolian() }));

    private static StylePanelViewModel Panel() => new(Library());

    private static Scale Find(StylePanelViewModel vm, string id) =>
        vm.FilteredScales.Single(s => s.Id == id);

    // ------------------------------------------------------------------ the scale list

    [Fact]
    public void TheListIsGroupedByRegionLargestFirstAndHoldsEveryScaleExactlyOnce()
    {
        StylePanelViewModel vm = Panel();

        string[] headers = [.. vm.Items.Where(i => i.IsHeader).Select(i => i.Region)];

        // East Asia 3, Southeast Asia 2, then the two singletons alphabetically.
        headers.Should().Equal("East Asia", "Southeast Asia", "Europe", "Turkey");

        vm.FilteredScales.Select(s => s.Id).Should().BeEquivalentTo(
            ["eastasia.chinese.gong", "eastasia.chinese.shang", "eastasia.chinese.yu",
             "seasia.gamelan.slendro", "seasia.gamelan.pelog", "turkish.makam.rast",
             StylePanelViewModel.MinorSourceScaleId]);

        vm.FilteredScales.Should().OnlyHaveUniqueItems();
        vm.MatchCount.Should().Be(7);
        vm.TotalScaleCount.Should().Be(7);
    }

    [Fact]
    public void TheUnfilteredListReproducesTheLibrarysOwnRegionGrouping()
    {
        ScaleLibrary library = Library();
        StylePanelViewModel vm = new(library);

        // Pinned so the panel and ScaleLibrary.ByRegion cannot drift apart on ordering.
        string[] expected = [.. library.ByRegion().SelectMany(g => g).Select(s => s.Id)];
        vm.FilteredScales.Select(s => s.Id).Should().Equal(expected);
    }

    [Fact]
    public void EveryScaleRowCarriesItsOwnFidelityBadgeAndEveryHeaderCarriesNone()
    {
        StylePanelViewModel vm = Panel();

        vm.Items.Where(i => i.IsHeader).Should().OnlyContain(i => i.Fidelity == null);
        vm.Items.Where(i => !i.IsHeader).Should().OnlyContain(i => i.Fidelity != null);

        vm.Items.Single(i => i.Scale?.Id == "eastasia.chinese.gong").FidelityLabel.Should().Be("Exact");
        vm.Items.Single(i => i.Scale?.Id == "seasia.gamelan.slendro").FidelityLabel.Should().Be("Approximate");
    }

    [Fact]
    public void HeadersAreNotSelectableSoArrowKeyBrowsingNeverStopsOnOne()
    {
        StylePanelViewModel vm = Panel();

        vm.Items.Where(i => i.IsHeader).Should().OnlyContain(i => !i.IsSelectable);
        vm.Items.Where(i => !i.IsHeader).Should().OnlyContain(i => i.IsSelectable);

        vm.SelectNext().Should().BeTrue();
        vm.SelectedScale.Should().Be(vm.FilteredScales[0]);

        vm.SelectNext().Should().BeTrue();
        vm.SelectedScale.Should().Be(vm.FilteredScales[1]);

        vm.SelectPrevious().Should().BeTrue();
        vm.SelectedScale.Should().Be(vm.FilteredScales[0]);
    }

    // ------------------------------------------------------------------ search

    [Fact]
    public void SearchFiltersLiveAndClearingItRestoresEverything()
    {
        StylePanelViewModel vm = Panel();

        vm.SearchText = "slendro";

        vm.FilteredScales.Should().ContainSingle(s => s.Id == "seasia.gamelan.slendro");
        vm.MatchCount.Should().Be(1);
        vm.MatchSummary.Should().Be("1 of 7");

        vm.SearchText = string.Empty;

        vm.MatchCount.Should().Be(7);
        vm.MatchSummary.Should().Be("7 scales");
    }

    [Theory]
    // name, tradition, region and id are all searchable - the four fields ScaleLibrary.Search reads.
    [InlineData("shang", 1)]          // name
    [InlineData("gamelan", 2)]        // tradition
    [InlineData("turkey", 1)]         // region, and no other field of any scale contains it
    [InlineData("eastasia.", 3)]      // id prefix, which no region or name contains
    public void SearchMatchesNameTraditionRegionAndId(string query, int expected)
    {
        StylePanelViewModel vm = Panel();

        vm.SearchText = query;

        vm.MatchCount.Should().Be(expected);
    }

    [Fact]
    public void MultipleTermsNarrowRatherThanWiden()
    {
        StylePanelViewModel vm = Panel();

        vm.SearchText = "gamelan";
        vm.MatchCount.Should().Be(2);

        vm.SearchText = "gamelan pelog";
        vm.MatchCount.Should().Be(1);
        vm.FilteredScales.Single().Id.Should().Be("seasia.gamelan.pelog");
    }

    [Fact]
    public void FilteredResultsStayGroupedByRegion()
    {
        StylePanelViewModel vm = Panel();

        vm.SearchText = "chinese";

        vm.Items.Where(i => i.IsHeader).Select(i => i.Region).Should().Equal("East Asia");
        vm.Items.Count(i => !i.IsHeader).Should().Be(3);
    }

    [Fact]
    public void AFilterThatHidesTheSelectionDoesNotChangeIt()
    {
        // Typing in the search box must not silently change what is playing.
        StylePanelViewModel vm = Panel();
        vm.SelectedScale = Find(vm, "seasia.gamelan.slendro");

        vm.SearchText = "chinese";

        vm.FilteredScales.Should().NotContain(vm.SelectedScale!);
        vm.SelectedScale!.Id.Should().Be("seasia.gamelan.slendro");
    }

    // ------------------------------------------------------------------ the fidelity badge

    [Theory]
    [InlineData(OutputMode.TwelveTet, true)]
    [InlineData(OutputMode.Microtonal, false)]
    [InlineData(OutputMode.Auto, false)]
    public void AScaleBeyondTwentyFiveCentsWarnsOnlyUnderTwelveTet(OutputMode mode, bool expected)
    {
        StylePanelViewModel vm = Panel();
        vm.SelectedScale = Find(vm, "seasia.gamelan.slendro");
        vm.OutputMode = mode;

        vm.Fidelity!.MaxDeviationCents.Should().BeApproximately(40, 0.01);
        vm.IsFidelityWarning.Should().Be(expected);
        (vm.FidelityWarning is null).Should().Be(!expected);

        // Neutral information is shown calmly in every mode, warning or not.
        vm.FidelityDescription.Should().NotBeNullOrWhiteSpace();
        vm.FidelityLabel.Should().Be("Approximate");
    }

    [Theory]
    [InlineData(OutputMode.TwelveTet)]
    [InlineData(OutputMode.Microtonal)]
    [InlineData(OutputMode.Auto)]
    public void AScaleWithinTwentyFiveCentsIsNeverAWarning(OutputMode mode)
    {
        StylePanelViewModel vm = Panel();
        vm.SelectedScale = Find(vm, "turkish.makam.rast");
        vm.OutputMode = mode;

        vm.Fidelity!.MaxDeviationCents.Should().BeInRange(5, 25);
        vm.FidelityLabel.Should().Be("Close");
        vm.IsFidelityWarning.Should().BeFalse();
        vm.FidelityWarning.Should().BeNull();
    }

    [Theory]
    [InlineData(OutputMode.TwelveTet)]
    [InlineData(OutputMode.Microtonal)]
    [InlineData(OutputMode.Auto)]
    public void AnExactScaleIsNeverAWarning(OutputMode mode)
    {
        StylePanelViewModel vm = Panel();
        vm.SelectedScale = Find(vm, "eastasia.chinese.gong");
        vm.OutputMode = mode;

        vm.FidelityLabel.Should().Be("Exact");
        vm.IsFidelityWarning.Should().BeFalse();
        vm.FidelityWarning.Should().BeNull();
    }

    [Fact]
    public void WithNothingSelectedTheBadgeIsSilentRatherThanWrong()
    {
        StylePanelViewModel vm = Panel();
        vm.OutputMode = OutputMode.TwelveTet;

        vm.Fidelity.Should().BeNull();
        vm.FidelityLabel.Should().BeEmpty();
        vm.IsFidelityWarning.Should().BeFalse();
    }

    // ------------------------------------------------------------------ the policies disclosure

    [Fact]
    public void ThePoliciesDisclosureIsClosedByDefault()
    {
        Panel().IsPoliciesExpanded.Should().BeFalse();
    }

    [Fact]
    public void TheDisclosureHoldsExactlyTheFivePoliciesPlusTheBendTolerance()
    {
        StylePanelViewModel.DisclosureControls.Should().Equal(
            PolicyControl.MappingStrategy,
            PolicyControl.NonScaleNotes,
            PolicyControl.Collisions,
            PolicyControl.Range,
            PolicyControl.OutputMode,
            PolicyControl.BendTolerance);
    }

    [Fact]
    public void TheTargetTonicIsNotInTheDisclosure()
    {
        // It is a per-file setting that defaults from the detected key, not a set-once policy, so it
        // sits outside as a peer of the scale list - and stays reachable while the disclosure is shut.
        StylePanelViewModel.DisclosureControls
            .Should().NotContain(c => c.ToString().Contains("Tonic", StringComparison.OrdinalIgnoreCase));

        Enum.GetNames<PolicyControl>()
            .Should().NotContain(n => n.Contains("Tonic", StringComparison.OrdinalIgnoreCase));

        StylePanelViewModel vm = Panel();
        vm.IsPoliciesExpanded.Should().BeFalse();

        vm.TargetTonicPitchClass = 5;

        vm.TargetTonicName.Should().Be("F4");
        vm.TargetTonicMidiNote.Should().Be(65);
    }

    [Fact]
    public void PolicyDefaultsMatchTheEnginesOwnDefaults()
    {
        StylePanelViewModel vm = Panel();

        vm.Strategy.Should().Be(MappingStrategy.ScaleDegree);
        vm.NonScaleNotes.Should().Be(NonScaleNotePolicy.SnapToNearestSourceDegree);
        vm.Collisions.Should().Be(CollisionPolicy.Merge);
        vm.Range.Should().Be(RangePolicy.ShiftIntoRange);
        vm.OutputMode.Should().Be(OutputMode.Auto);
        vm.ToleranceCents.Should().Be(OffsetClusterer.DefaultToleranceCents);
    }

    // ------------------------------------------------------------------ source controls

    [Fact]
    public void UnderNearestPitchTheSourceKeyAndScaleAreDimmedWithAStatedReason()
    {
        StylePanelViewModel vm = Panel();

        vm.Strategy = MappingStrategy.NearestPitch;

        vm.IsSourceKeyEnabled.Should().BeFalse();
        vm.IsSourceScaleEnabled.Should().BeFalse();
        vm.SourceControlsDisabledReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void UnderDegreeMappingTheSourceKeyAndScaleAreLive()
    {
        StylePanelViewModel vm = Panel();

        vm.Strategy = MappingStrategy.ScaleDegree;

        vm.IsSourceKeyEnabled.Should().BeTrue();
        vm.IsSourceScaleEnabled.Should().BeTrue();
        vm.SourceControlsDisabledReason.Should().BeNull();
    }

    [Fact]
    public void SwitchingStrategyRaisesTheDimmingProperties()
    {
        StylePanelViewModel vm = Panel();
        List<string> raised = Watch(vm);

        vm.Strategy = MappingStrategy.NearestPitch;

        raised.Should().Contain(nameof(StylePanelViewModel.IsSourceKeyEnabled))
            .And.Contain(nameof(StylePanelViewModel.IsSourceScaleEnabled))
            .And.Contain(nameof(StylePanelViewModel.SourceControlsDisabledReason));
    }

    // ------------------------------------------------------------------ the target tonic

    [Fact]
    public void TheTonicKeepsItsLetterSpellingRatherThanJustAPitchClass()
    {
        StylePanelViewModel vm = Panel();

        // MIDI 61 is C# here, not Db, and the letter has to survive - every letter downstream
        // follows from which one the user picked.
        vm.TargetTonicSpelling = new TonicSpelling(0, 1);

        vm.TargetTonicPitchClass.Should().Be(1);
        vm.TargetTonicMidiNote.Should().Be(61);
        vm.TargetTonicName.Should().Be("C#4");

        // Choosing a pitch class instead falls back to the library's flat-preferring convention.
        vm.TargetTonicPitchClass = 6;
        vm.TargetTonicSpelling.Should().Be(new TonicSpelling(4, -1));
        vm.TargetTonicName.Should().Be("Gb4");
        vm.TargetTonicMidiNote.Should().Be(66);
    }

    [Fact]
    public void TheDetectedKeyDefaultsTheTargetTonicAndSeedsTheSourceScale()
    {
        StylePanelViewModel vm = Panel();
        KeyDetectionResult detection = KeyDetector.Detect(DMinorProject());

        vm.ApplyDetectedKey(detection);

        KeyEstimate top = detection.TopCandidate!;
        vm.SourceTonicPitchClass.Should().Be(top.PitchClass);
        vm.TargetTonicPitchClass.Should().Be(top.PitchClass);
        vm.SourceKeySummary.Should().NotBeNullOrWhiteSpace();

        // Only the minor seed exists in this fixture library; a missing id must leave the choice alone.
        if (top.IsMinor)
        {
            vm.SourceScale!.Id.Should().Be(StylePanelViewModel.MinorSourceScaleId);
        }
        else
        {
            vm.SourceScale.Should().BeNull();
        }
    }

    [Fact]
    public void NoDetectedKeyChangesNothing()
    {
        // Substituting C major for "we could not tell" would silently transpose the whole output.
        StylePanelViewModel vm = Panel();
        vm.TargetTonicPitchClass = 7;

        vm.ApplyDetectedKey(null);

        vm.TargetTonicPitchClass.Should().Be(7);
        vm.SourceScale.Should().BeNull();
    }

    // ------------------------------------------------------------------ settings round-trip

    [Fact]
    public void SettingsRoundTripEveryChoice()
    {
        StylePanelViewModel vm = Panel();

        vm.SelectedScale = Find(vm, "turkish.makam.rast");
        vm.TargetTonicSpelling = new TonicSpelling(1, -1);   // Db
        vm.TargetTonicOctave = 3;
        vm.SourceScale = Find(vm, "eastasia.chinese.gong");
        vm.SourceTonicPitchClass = 2;
        vm.Strategy = MappingStrategy.ScaleDegree;
        vm.NonScaleNotes = NonScaleNotePolicy.Drop;
        vm.Collisions = CollisionPolicy.DisplaceOctave;
        vm.Range = RangePolicy.FoldOctave;
        vm.OutputMode = OutputMode.Microtonal;
        vm.ToleranceCents = 12.5;

        RestyleSettings settings = vm.BuildSettings();

        settings.TargetScale.Id.Should().Be("turkish.makam.rast");
        settings.TargetTonic.Should().Be(Pitch.FromMidi(49));
        settings.TonicSpelling.Should().Be(new TonicSpelling(1, -1));
        settings.SourceScale!.Id.Should().Be("eastasia.chinese.gong");
        settings.SourceTonic.Should().Be(Pitch.FromMidi(62));
        settings.Mapping.Strategy.Should().Be(MappingStrategy.ScaleDegree);
        settings.Mapping.NonScaleNotes.Should().Be(NonScaleNotePolicy.Drop);
        settings.Mapping.Collisions.Should().Be(CollisionPolicy.DisplaceOctave);
        settings.Mapping.Range.Should().Be(RangePolicy.FoldOctave);
        settings.OutputMode.Should().Be(OutputMode.Microtonal);
        settings.ToleranceCents.Should().Be(12.5);
        settings.Excluded.Should().BeEmpty();
    }

    [Fact]
    public void TrackExclusionsArePassedThroughBecauseTheyBelongToTheLeftRail()
    {
        StylePanelViewModel vm = Panel();
        vm.SelectedScale = Find(vm, "eastasia.chinese.gong");
        vm.SourceScale = Find(vm, StylePanelViewModel.MinorSourceScaleId);

        RestyleSettings settings = vm.BuildSettings(new HashSet<(int, int)> { (1, 3) });

        settings.Excluded.Should().BeEquivalentTo([(1, 3)]);
    }

    [Fact]
    public void DegreeMappingWithoutASourceScaleIsBlockedWithAReasonRatherThanThrowingLater()
    {
        StylePanelViewModel vm = Panel();
        vm.SelectedScale = Find(vm, "eastasia.chinese.gong");

        vm.CanRestyle.Should().BeFalse();
        vm.RestyleBlockedReason.Should().NotBeNullOrWhiteSpace();
        vm.Invoking(v => v.BuildSettings()).Should().Throw<InvalidOperationException>();

        // Nearest-pitch reads no source scale, so the same state is perfectly restylable there.
        vm.Strategy = MappingStrategy.NearestPitch;

        vm.CanRestyle.Should().BeTrue();
        vm.BuildSettings().TargetScale.Id.Should().Be("eastasia.chinese.gong");
    }

    // ------------------------------------------------------------------ notification and browsing

    [Fact]
    public void ChangingTheSelectedScaleRaisesTheSelectionAndTheFidelityBadge()
    {
        StylePanelViewModel vm = Panel();
        List<string> raised = Watch(vm);

        vm.SelectedScale = Find(vm, "seasia.gamelan.slendro");

        raised.Should().Contain(nameof(StylePanelViewModel.SelectedScale))
            .And.Contain(nameof(StylePanelViewModel.SelectedItem))
            .And.Contain(nameof(StylePanelViewModel.Fidelity))
            .And.Contain(nameof(StylePanelViewModel.FidelityLabel))
            .And.Contain(nameof(StylePanelViewModel.FidelityDescription))
            .And.Contain(nameof(StylePanelViewModel.IsFidelityWarning))
            .And.Contain(nameof(StylePanelViewModel.FidelityWarning));
    }

    [Fact]
    public void ChangingTheOutputModeRaisesTheFidelityBadgeBecauseItsSeverityDependsOnIt()
    {
        StylePanelViewModel vm = Panel();
        vm.SelectedScale = Find(vm, "seasia.gamelan.slendro");
        List<string> raised = Watch(vm);

        vm.OutputMode = OutputMode.TwelveTet;

        raised.Should().Contain(nameof(StylePanelViewModel.IsFidelityWarning));
        vm.IsFidelityWarning.Should().BeTrue();
    }

    [Fact]
    public void SelectingARowSelectsItsScaleAndSelectingAScaleSelectsItsRow()
    {
        StylePanelViewModel vm = Panel();

        vm.SelectedItem = vm.Items.Single(i => i.Scale?.Id == "eastasia.chinese.yu");
        vm.SelectedScale!.Id.Should().Be("eastasia.chinese.yu");

        vm.SelectedScale = Find(vm, "turkish.makam.rast");
        vm.SelectedItem!.Scale!.Id.Should().Be("turkish.makam.rast");
    }

    [Fact]
    public void ADebounceIntervalIsPublishedAndSane()
    {
        // The host owns the timer; the interval is published so the two cannot disagree about it.
        StylePanelViewModel.SelectionDebounce.Should().BePositive();
        StylePanelViewModel.SelectionDebounce.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(50));
        StylePanelViewModel.SelectionDebounce.Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(500));
    }

    // ------------------------------------------------------------------ non-notatable scales

    [Fact]
    public void SelectingANonNotatableScaleWarnsAboutNothingAndBlocksNothing()
    {
        // Notatability affects the v1.1 staff view and MusicXML export, nothing else. Slendro is an
        // ordinary choice and the panel must not discourage it in any way.
        StylePanelViewModel vm = Panel();
        vm.SourceScale = Find(vm, StylePanelViewModel.MinorSourceScaleId);

        vm.SelectedScale = Find(vm, "seasia.gamelan.slendro");

        vm.SelectedScaleIsNotatable.Should().BeFalse();
        vm.IsFidelityWarning.Should().BeFalse();
        vm.FidelityWarning.Should().BeNull();
        vm.CanRestyle.Should().BeTrue();
        vm.RestyleBlockedReason.Should().BeNull();
        vm.BuildSettings().TargetScale.Id.Should().Be("seasia.gamelan.slendro");
    }

    // ------------------------------------------------------------------ empty library

    [Fact]
    public void AnEmptyLibraryYieldsAnEmptyListWithoutThrowing()
    {
        StylePanelViewModel vm = new(ScaleLibrary.Build());

        vm.Items.Should().BeEmpty();
        vm.FilteredScales.Should().BeEmpty();
        vm.MatchCount.Should().Be(0);
        vm.HasMatches.Should().BeFalse();
        vm.SelectNext().Should().BeFalse();
        vm.SelectPrevious().Should().BeFalse();
        vm.Fidelity.Should().BeNull();
        vm.CanRestyle.Should().BeFalse();

        vm.SearchText = "rast";

        vm.Items.Should().BeEmpty();
        vm.MatchSummary.Should().Be("0 of 0");
    }

    // ------------------------------------------------------------------ helpers

    private static List<string> Watch(INotifyPropertyChanged vm)
    {
        List<string> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        return raised;
    }

    /// <summary>A D natural-minor melody, for key detection. Which key wins is not asserted.</summary>
    private static MidiProject DMinorProject()
    {
        int[] degrees = [62, 64, 65, 67, 69, 70, 72, 74];
        Note[] notes = [.. degrees.Select((n, i) => new Note(Pitch.FromMidi(n), i * 480, 480, 90))];

        return new MidiProject
        {
            FilePath = @"C:\music\fixture.mid",
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            Tracks = [new TrackInfo { TrackIndex = 0, Channel = 0, Notes = notes }],
            TempoMap = [new TempoChange(0, 500_000)],
            TimeSignatures = [new TimeSignatureChange(0, 4, 4)],
        };
    }
}
