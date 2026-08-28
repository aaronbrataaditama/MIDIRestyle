using System.ComponentModel;
using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;
using MidiRestyle.Playback;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Guards that computed transport properties are <em>announced</em>, not merely correct.
/// </summary>
/// <remarks>
/// This class exists because of a shipped bug. After loading a file, <c>CanPlay</c> computed to true
/// and the Play button stayed disabled anyway: <c>Adopt</c> raised <c>HasProject</c> and
/// <c>WindowTitle</c> but not <c>CanPlay</c>, so the binding never re-evaluated. Every existing test
/// passed, because they all asserted view-model <em>values</em> and a value is right whether or not
/// anyone was told about it.
/// <para>
/// So these tests subscribe to <see cref="INotifyPropertyChanged"/> and assert on the names raised.
/// Any computed property a control binds its enabled-state to needs one.
/// </para>
/// </remarks>
public class TransportNotificationTests
{
    private static readonly Scale CMajor = new(
        "t.cmajor", "C major", "Western", "Europe & Balkans",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    private static readonly Scale Gong = new(
        "t.gong", "Gong", "Chinese Wusheng", "East Asia",
        [0, 200, 400, 700, 900], "Test fixture, 2026");

    private static MidiProject Project() => new()
    {
        FilePath = @"C:\music\test.mid",
        Format = MidiFileFormatKind.MultiTrack,
        Division = new TicksPerQuarterNote(480),
        Tracks =
        [
            new TrackInfo
            {
                TrackIndex = 0,
                Channel = 0,
                Notes = [.. new[] { 60, 62, 64 }
                    .Select((n, i) => new Note(Pitch.FromMidi(n), i * 480, 480, 90))],
            },
        ],
    };

    private static RestyleSettings Settings() => new()
    {
        TargetScale = Gong,
        TargetTonic = Pitch.FromMidi(60),
        SourceScale = CMajor,
        SourceTonic = Pitch.FromMidi(60),
    };

    /// <summary>Records every property name announced, so a missing notification is visible.</summary>
    private static List<string> Watch(MainWindowViewModel vm)
    {
        List<string> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "?");
        return raised;
    }

    /// <summary>An always-available engine, so the test isolates notification from device presence.</summary>
    private static MainWindowViewModel WithAudio()
    {
        MainWindowViewModel vm = new();
        vm.AttachEngine(new NullPlaybackEngine("test"));
        vm.AudioAvailable = true;
        return vm;
    }

    // --- the regression ------------------------------------------------------------------

    [Fact]
    public void LoadingAFileAnnouncesCanPlay()
    {
        MainWindowViewModel vm = WithAudio();
        List<string> raised = Watch(vm);

        vm.Adopt(Project());

        vm.CanPlay.Should().BeTrue("the value was never the problem");
        raised.Should().Contain(nameof(MainWindowViewModel.CanPlay),
            "the Play button binds its enabled state to this; without the notification it stays "
            + "disabled forever, which is exactly the bug this test exists for");
    }

    [Fact]
    public void LoadingAFileAnnouncesCanCompare()
    {
        MainWindowViewModel vm = WithAudio();
        List<string> raised = Watch(vm);

        vm.Adopt(Project());

        raised.Should().Contain(nameof(MainWindowViewModel.CanCompare));
    }

    [Fact]
    public void LoadingAFileStillAnnouncesHasProjectAndTheTitle()
    {
        MainWindowViewModel vm = WithAudio();
        List<string> raised = Watch(vm);

        vm.Adopt(Project());

        raised.Should().Contain(nameof(MainWindowViewModel.HasProject));
        raised.Should().Contain(nameof(MainWindowViewModel.WindowTitle));
    }

    /// <summary>
    /// The A/B toggle is meaningless until there is a transform, so it must light up when one arrives.
    /// </summary>
    [Fact]
    public void RunningATransformAnnouncesCanCompare()
    {
        MainWindowViewModel vm = WithAudio();
        vm.Adopt(Project());
        List<string> raised = Watch(vm);

        vm.ApplyRestyle(Settings());

        vm.CanCompare.Should().BeTrue();
        raised.Should().Contain(nameof(MainWindowViewModel.CanCompare));
    }

    [Fact]
    public void ClearingTheTransformAnnouncesCanCompareAgain()
    {
        MainWindowViewModel vm = WithAudio();
        vm.Adopt(Project());
        vm.ApplyRestyle(Settings());
        List<string> raised = Watch(vm);

        vm.ClearRestyle();

        vm.CanCompare.Should().BeFalse();
        raised.Should().Contain(nameof(MainWindowViewModel.CanCompare));
    }

    [Fact]
    public void AttachingAnEngineAnnouncesTheTransportState()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project());
        List<string> raised = Watch(vm);

        vm.AttachEngine(new NullPlaybackEngine("no device"));

        raised.Should().Contain(nameof(MainWindowViewModel.CanPlay));
        raised.Should().Contain(nameof(MainWindowViewModel.CanCompare));
    }

    [Fact]
    public void AudioBecomingAvailableAnnouncesCanPlay()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project());
        List<string> raised = Watch(vm);

        vm.AudioAvailable = true;

        raised.Should().Contain(nameof(MainWindowViewModel.CanPlay));
    }

    // --- the state itself ---------------------------------------------------------------

    [Fact]
    public void WithNoAudioDevicePlayStaysDisabledAndSaysWhy()
    {
        MainWindowViewModel vm = new();
        vm.AttachEngine(new NullPlaybackEngine("No MIDI output device was found."));
        vm.Adopt(Project());

        vm.AudioAvailable.Should().BeFalse();
        vm.CanPlay.Should().BeFalse("there is nothing to play through");
        // A notice, not a message: loading a file overwrites the message, and the reason a control
        // is disabled has to outlive that or the app just looks broken.
        vm.Status.AudioNotice.Should().Contain("MIDI output device");
        vm.Status.HasNotices.Should().BeTrue();
        vm.PlayDisabledReason.Should().Contain("MIDI output device",
            "a disabled button with no explanation reads as a broken app");
    }

    [Fact]
    public void WithNoFileLoadedPlayIsDisabledAndSaysSo()
    {
        MainWindowViewModel vm = WithAudio();

        vm.CanPlay.Should().BeFalse();
        vm.CanCompare.Should().BeFalse();
        vm.PlayDisabledReason.Should().Contain("Open a MIDI file");
    }

    /// <summary>
    /// The reason for a disabled control must survive the next thing that happens, or it is useless.
    /// </summary>
    [Fact]
    public void TheAudioNoticeSurvivesLoadingAFile()
    {
        MainWindowViewModel vm = new();
        vm.AttachEngine(new NullPlaybackEngine("No MIDI output device was found."));

        vm.Adopt(Project());

        vm.Status.AudioNotice.Should().Contain("MIDI output device",
            "loading a file must not erase the explanation for the greyed-out Play button");
    }

    [Fact]
    public void WithAudioAndAFileNothingIsDisabledSoThereIsNoReason()
    {
        MainWindowViewModel vm = WithAudio();
        vm.Adopt(Project());

        vm.PlayDisabledReason.Should().BeNull();
        vm.CompareDisabledReason.Should().Contain("target scale",
            "playback is fine; it is the comparison that has nothing to compare");
    }

    [Fact]
    public void TheAbToggleNeedsATransformNotJustAFile()
    {
        MainWindowViewModel vm = WithAudio();
        vm.Adopt(Project());

        vm.CanPlay.Should().BeTrue();
        vm.CanCompare.Should().BeFalse(
            "there is nothing to compare the original against until a transform has run");
        vm.CompareDisabledReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TheLabelsFollowTheState()
    {
        MainWindowViewModel vm = WithAudio();
        List<string> raised = Watch(vm);

        vm.PlayPauseLabel.Should().Be("Play");
        vm.AbLabel.Should().Contain("original");

        vm.IsPlaying = true;
        vm.HearingRestyled = true;

        vm.PlayPauseLabel.Should().Be("Pause");
        vm.AbLabel.Should().Contain("restyled");
        raised.Should().Contain(nameof(MainWindowViewModel.PlayPauseLabel));
        raised.Should().Contain(nameof(MainWindowViewModel.AbLabel));
    }
}
