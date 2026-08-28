using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Tests;

public class MainWindowViewModelTests
{
    private static MidiProject Project(params TrackInfo[] tracks) => new()
    {
        FilePath = @"C:\music\sakura-theme.mid",
        Format = MidiFileFormatKind.MultiTrack,
        Division = new TicksPerQuarterNote(480),
        Tracks = tracks,
        TempoMap = [new TempoChange(0, 500_000)],
        TimeSignatures = [new TimeSignatureChange(0, 4, 4)],
    };

    private static TrackInfo Track(int channel, int trackIndex = 0, int noteCount = 4) => new()
    {
        TrackIndex = trackIndex,
        Channel = channel,
        Notes = [.. Enumerable.Range(0, noteCount)
            .Select(i => new Note(Pitch.FromMidi(60 + i), i * 480, 480, 90))],
    };

    [Fact]
    public void StartsEmpty()
    {
        MainWindowViewModel vm = new();

        vm.HasProject.Should().BeFalse();
        vm.Tracks.Should().BeEmpty();
        vm.Metadata.Should().BeNull();
        vm.WindowTitle.Should().Be("MIDIRestyle");
    }

    [Fact]
    public void AdoptingAProjectPopulatesTracksAndMetadata()
    {
        MainWindowViewModel vm = new();

        vm.Adopt(Project(Track(0), Track(1, 1), Track(TrackInfo.DrumChannel, 2)));

        vm.HasProject.Should().BeTrue();
        vm.Tracks.Should().HaveCount(3);
        vm.Metadata!.FileName.Should().Be("sakura-theme.mid");
        vm.WindowTitle.Should().Be("sakura-theme.mid - MIDIRestyle");
    }

    [Fact]
    public void DrumsAreExcludedFromTheRestyleSelectionByDefault()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project(Track(0), Track(TrackInfo.DrumChannel, 1)));

        vm.RestylableSelection.Should().HaveCount(1);
        vm.Tracks.Single(t => t.IsLocked).Channel().Should().Be(TrackInfo.DrumChannel);
    }

    [Fact]
    public void AdoptingReplacesThePreviousProjectRatherThanAppending()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project(Track(0), Track(1, 1)));
        vm.Adopt(Project(Track(0)));

        vm.Tracks.Should().HaveCount(1);
    }

    [Fact]
    public void ExistingPitchBendIsReportedInTheStatusBar()
    {
        MainWindowViewModel vm = new();
        TrackInfo bent = Track(0) with { Name = "Guitar", HasExistingPitchBend = true };

        vm.Adopt(Project(bent));

        vm.Status.PitchBendNotice.Should().NotBeNullOrWhiteSpace();
        vm.Status.PitchBendNotice.Should().Contain("Guitar");
        vm.Status.HasNotices.Should().BeTrue();
    }

    [Fact]
    public void NoPitchBendMeansNoNotice()
    {
        MainWindowViewModel vm = new();

        vm.Adopt(Project(Track(0)));

        vm.Status.PitchBendNotice.Should().BeNull();
        vm.Status.HasNotices.Should().BeFalse();
    }

    /// <summary>
    /// A failed load must not discard what is already open. Losing the user's loaded file because
    /// the next one was corrupt would be its own bug.
    /// </summary>
    [Fact]
    public void AFailedLoadLeavesThePreviousProjectIntact()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project(Track(0)));

        vm.Load(Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.mid"));

        vm.HasProject.Should().BeTrue();
        vm.Metadata!.FileName.Should().Be("sakura-theme.mid");
        vm.Status.Severity.Should().Be(StatusSeverity.Error);
    }

    [Fact]
    public async Task OpenWithoutAFilePickerReportsRatherThanThrowing()
    {
        MainWindowViewModel vm = new();

        await vm.OpenCommand.ExecuteAsync(null);

        vm.Status.Severity.Should().Be(StatusSeverity.Error);
    }

    [Fact]
    public async Task CancellingTheFilePickerChangesNothing()
    {
        MainWindowViewModel vm = new(() => Task.FromResult<string?>(null));
        vm.Adopt(Project(Track(0)));

        await vm.OpenCommand.ExecuteAsync(null);

        vm.Tracks.Should().HaveCount(1);
        vm.Status.Severity.Should().Be(StatusSeverity.Info);
    }

    /// <summary>
    /// The strip was suppressed while the staff and degree views were still deferred, on the
    /// grounds that a tab which does nothing is worse than no tab. Both views now exist, so it
    /// appears - and the three tabs each do something.
    /// </summary>
    [Fact]
    public void TheViewTabStripAppearsNowThatAllThreeViewsExist() =>
        MainWindowViewModel.ShowViewTabs.Should().BeTrue();
}

file static class TrackViewModelChannelExtensions
{
    public static int Channel(this TrackViewModel vm) => vm.Track.Channel;
}
