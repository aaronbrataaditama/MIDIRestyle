using System.ComponentModel;
using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The staff and degree views, the view-tab strip and the MusicXML menu entry all bind to computed
/// properties. A computed property whose dependency never announces itself is the bug that shipped
/// the Play button permanently disabled - every test passed, because they all asserted values. So
/// these subscribe to <see cref="INotifyPropertyChanged"/> and assert on the names actually raised.
/// </summary>
public class NotationViewNotificationTests
{
    private static readonly Scale CMajor = new(
        "t.cmajor", "C major", "Western", "Europe & Balkans",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    private static readonly Scale Slendro = new(
        "t.slendro", "Slendro", "Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

    private static MidiProject Project() => new()
    {
        FilePath = @"C:\music\test.mid",
        Format = MidiFileFormatKind.MultiTrack,
        Division = new TicksPerQuarterNote(480),
        TimeSignatures = [new TimeSignatureChange(0, 4, 4)],
        Tracks =
        [
            new TrackInfo
            {
                TrackIndex = 0,
                Channel = 0,
                ProgramNumber = 40,
                Notes = [.. new[] { 60, 62, 64, 65 }
                    .Select((n, i) => new Note(Pitch.FromMidi(n), i * 480, 480, 90))],
            },
        ],
    };

    private static RestyleSettings Settings(Scale target) => new()
    {
        TargetScale = target,
        TargetTonic = Pitch.FromMidi(60),
        SourceScale = CMajor,
        SourceTonic = Pitch.FromMidi(60),
    };

    /// <summary>A library carrying the Ionian id the panel falls back to before key detection.</summary>
    private static ScaleLibrary Library() => ScaleLibrary.Build(
        (ScaleOrigin.Embedded,
            new[]
            {
                new Scale(
                    StylePanelViewModel.MajorSourceScaleId, "Ionian", "Western Church Modes",
                    "Europe & Balkans", [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026"),
                new Scale(
                    "t.gong", "Gong", "Chinese Wusheng", "East Asia",
                    [0, 200, 400, 700, 900], "Test fixture, 2026"),
            }));

    private static List<string> Watch(MainWindowViewModel vm)
    {
        List<string> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "?");
        return raised;
    }

    private static MainWindowViewModel Loaded(Scale target)
    {
        MainWindowViewModel vm = new() { Project = Project() };
        vm.ApplyRestyle(Settings(target));
        return vm;
    }

    [Fact]
    public void SwitchingTheCentreViewAnnouncesEveryTabFlag()
    {
        MainWindowViewModel vm = new();
        var raised = Watch(vm);

        vm.ShowStaffCommand.Execute(null);

        raised.Should().Contain(nameof(MainWindowViewModel.IsStaffView));
        raised.Should().Contain(nameof(MainWindowViewModel.IsPianoRollView));
        raised.Should().Contain(nameof(MainWindowViewModel.IsDegreesView));
        vm.IsStaffView.Should().BeTrue();
        vm.IsPianoRollView.Should().BeFalse();
    }

    [Fact]
    public void EachViewCommandSelectsItsOwnView()
    {
        MainWindowViewModel vm = new();

        vm.ShowDegreesCommand.Execute(null);
        vm.CentreView.Should().Be(CentreView.Degrees);

        vm.ShowStaffCommand.Execute(null);
        vm.CentreView.Should().Be(CentreView.Staff);

        vm.ShowPianoRollCommand.Execute(null);
        vm.CentreView.Should().Be(CentreView.PianoRoll);
    }

    [Fact]
    public void RestylingAnnouncesTheExportGateAndItsReason()
    {
        MainWindowViewModel vm = new() { Project = Project() };
        var raised = Watch(vm);

        vm.ApplyRestyle(Settings(CMajor));

        raised.Should().Contain(nameof(MainWindowViewModel.Score));
        raised.Should().Contain(nameof(MainWindowViewModel.CanExportMusicXml));
        raised.Should().Contain(nameof(MainWindowViewModel.MusicXmlMenuHeader));
        raised.Should().Contain(nameof(MainWindowViewModel.StaffUnavailableReason));
    }

    [Fact]
    public void ANotatableScaleEnablesTheStaffAndTheExport()
    {
        var vm = Loaded(CMajor);

        vm.HasScore.Should().BeTrue();
        vm.StaffUnavailableReason.Should().BeNull();
        vm.CanExportMusicXml.Should().BeTrue();
        vm.MusicXmlMenuHeader.Should().NotContain("(");
    }

    [Fact]
    public void ANonNotatableScaleExplainsItselfAndNamesTheScale()
    {
        // Slendro's degrees are evenly spaced; writing them on a staff would misrepresent the
        // tuning. The view has to say so, and say which scale it is talking about.
        var vm = Loaded(Slendro);

        vm.StaffUnavailableReason.Should().NotBeNull();
        vm.StaffUnavailableReason.Should().Contain("Slendro");
        vm.StaffUnavailableReason.Should().Contain("Degrees view");
        vm.CanExportMusicXml.Should().BeFalse();
    }

    [Fact]
    public void TheDisabledExportMenuStatesItsReasonRatherThanJustGreyingOut()
    {
        // A greyed entry with no explanation reads as a broken app - the same lesson as the
        // "No MIDI device" notice.
        var vm = Loaded(Slendro);

        vm.MusicXmlMenuHeader.Should().Contain("Slendro");
        vm.MusicXmlMenuHeader.Should().Contain("no staff spelling");
    }

    [Fact]
    public void WithNothingLoadedTheStaffSaysWhatToDoNext()
    {
        MainWindowViewModel vm = new();

        vm.HasScore.Should().BeFalse();
        vm.CanExportMusicXml.Should().BeFalse();
        vm.StaffUnavailableReason.Should().Contain("Load a MIDI file");
        vm.MusicXmlMenuHeader.Should().Contain("nothing to export");
    }

    [Fact]
    public void TheScoreCoversEveryPitchedTrackAndCarriesTheScale()
    {
        var vm = Loaded(CMajor);

        vm.Score.Should().NotBeNull();
        vm.Score!.Parts.Should().ContainSingle();
        vm.Score.ScaleName.Should().Be("C major");
        vm.NotationScale.Should().Be(CMajor);
        vm.NotationTonic.Should().Be(Pitch.FromMidi(60));
    }

    [Fact]
    public void SwitchingScaleRebuildsTheScoreAndFlipsTheExportGate()
    {
        MainWindowViewModel vm = new() { Project = Project() };

        vm.ApplyRestyle(Settings(CMajor));
        vm.CanExportMusicXml.Should().BeTrue();

        vm.ApplyRestyle(Settings(Slendro));
        vm.CanExportMusicXml.Should().BeFalse("the transform re-runs rather than being undone");
        vm.Score.Should().NotBeNull("the degree view still needs a score for a non-notatable scale");
    }

    [Fact]
    public void TheTabStripIsShownNowThatThereAreThreeRealViews() =>
        MainWindowViewModel.ShowViewTabs.Should().BeTrue();

    // --- notation must not sit empty beside a full piano roll ---------------------------------

    [Fact]
    public void OpeningAFileNotatesItBeforeAnyTargetScaleIsChosen()
    {
        // The bug this pins: Score was only ever built by ApplyRestyle, which needs a target scale.
        // So on opening a file the piano roll filled with the source notes while the staff and
        // degree views stayed blank - which reads as a broken view, not as an empty one.
        MainWindowViewModel vm = new() { Project = Project() };
        StylePanelViewModel panel = new(Library());

        vm.StylePanel = panel;
        vm.ReapplyFromStylePanel();

        vm.Score.Should().NotBeNull("a loaded file is notatable before anything is restyled");
        vm.Score!.Parts.Should().NotBeEmpty();
        vm.HasScore.Should().BeTrue();
    }

    [Fact]
    public void TheSourceNotationIsSpelledAgainstTheSourceKeyNotATarget()
    {
        MainWindowViewModel vm = new() { Project = Project() };
        StylePanelViewModel panel = new(Library());

        vm.StylePanel = panel;
        vm.ReapplyFromStylePanel();

        vm.NotationScale.Should().NotBeNull();
        vm.NotationTonic.Should().Be(panel.SourceTonic);
    }

    [Fact]
    public void ChoosingATargetScaleReplacesTheSourceNotation()
    {
        MainWindowViewModel vm = new() { Project = Project() };
        vm.StylePanel = new StylePanelViewModel(Library());
        vm.ReapplyFromStylePanel();

        vm.ApplyRestyle(Settings(Slendro));

        vm.NotationScale.Should().Be(Slendro, "the target replaces the source reading");
    }

    [Fact]
    public void OpeningASecondFileDoesNotLeaveTheFirstFilesNotationOnScreen()
    {
        // Adopt clears the restyle and the roll notes; leaving a stale Score behind would show one
        // piece's notation over another's piano roll.
        MainWindowViewModel vm = new() { Project = Project() };
        vm.ApplyRestyle(Settings(CMajor));
        vm.Score.Should().NotBeNull();

        vm.Adopt(Project());

        vm.Score.Should().BeNull("a new file invalidates the previous notation");
    }

    // --- the score follows the A/B "Hearing" toggle -------------------------------------------

    [Fact]
    public void PickingATargetScaleSwitchesTheScoreToTheRestyledReading()
    {
        MainWindowViewModel vm = new() { Project = Project() };
        vm.StylePanel = new StylePanelViewModel(Library());
        vm.ReapplyFromStylePanel();

        vm.NotationScale.Should().NotBe(Slendro, "nothing has been restyled yet");

        vm.ApplyRestyle(Settings(Slendro));

        vm.NotationScale.Should().Be(Slendro,
            "choosing a scale shows it - that is the thing the user just asked to see");
    }

    [Fact]
    public void TheAbSideMovesTheScoreWithTheSound()
    {
        MainWindowViewModel vm = new() { Project = Project() };
        vm.StylePanel = new StylePanelViewModel(Library());
        vm.ReapplyFromStylePanel();
        vm.ApplyRestyle(Settings(Slendro));

        var original = vm.Score;
        vm.NotationScale.Should().Be(Slendro);

        vm.ShowRestyledScore = false;
        vm.NotationScale.Should().NotBe(Slendro, "hearing the original means seeing the original");
        vm.Score.Should().NotBeSameAs(original);

        vm.ShowRestyledScore = true;
        vm.NotationScale.Should().Be(Slendro);
        vm.Score.Should().BeSameAs(original, "switching back returns the same cached reading");
    }

    [Fact]
    public void SwitchingSideWithNothingRestyledLeavesTheScoreAlone()
    {
        // Blanking the staff because the toggle moved, when there is no comparison to move to,
        // would read as the view breaking.
        MainWindowViewModel vm = new() { Project = Project() };
        vm.StylePanel = new StylePanelViewModel(Library());
        vm.ReapplyFromStylePanel();

        var before = vm.Score;
        vm.ShowRestyledScore = true;

        vm.Score.Should().BeSameAs(before);
    }

    [Fact]
    public void TheOriginalReadingIsBuiltOnceAndReusedAcrossScaleChanges()
    {
        // The scale list is arrow-key browsable. The original reading depends only on the file and
        // the source key, so rebuilding it per keystroke would double the cost of every keypress
        // for an identical result.
        MainWindowViewModel vm = new() { Project = Project() };
        vm.StylePanel = new StylePanelViewModel(Library());
        vm.ReapplyFromStylePanel();

        vm.ApplyRestyle(Settings(CMajor));
        vm.ShowRestyledScore = false;
        var firstOriginal = vm.Score;

        vm.ShowRestyledScore = true;
        vm.ApplyRestyle(Settings(Slendro));
        vm.ShowRestyledScore = false;

        vm.Score.Should().BeSameAs(firstOriginal, "the source reading did not change, so nor should the object");
    }
}
