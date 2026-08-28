using MidiRestyle.App.Services;
using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Every test points a <see cref="PathProbe"/> at unique temp directories, exactly like
/// <c>ScaleLibraryServiceTests</c>, so nothing here ever touches the real beside-the-exe folder or
/// the user's actual %APPDATA%.
/// </summary>
public sealed class ScaleEditorViewModelTests : IDisposable
{
    private const string Citation = "Unit test fixture, ScaleEditorViewModelTests.";

    private readonly string _tempRoot;
    private readonly string _besideExe;
    private readonly string _appData;

    public ScaleEditorViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "midirestyle-scaleeditor-tests-" + Guid.NewGuid().ToString("N"));
        _besideExe = Path.Combine(_tempRoot, "beside-exe");
        _appData = Path.Combine(_tempRoot, "appdata");
        Directory.CreateDirectory(_besideExe);
        Directory.CreateDirectory(_appData);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private ScaleEditorViewModel Editor() => new(new PathProbe(_besideExe, _appData));

    private string UserScalesPath => Path.Combine(_besideExe, ScaleLibraryService.UserScalesFileName);

    private static Scale ShippedGong() => new(
        "eastasia.china.gong", "Gong", "Chinese Pentatonic", "East Asia",
        [0, 200, 400, 700, 900], Citation);

    /// <summary>Fills in the minimum fields needed for a save attempt to succeed, via a pentatonic scale.</summary>
    private static void FillValidGongLikeScale(ScaleEditorViewModel vm, string slug = "my-gong")
    {
        vm.IdSlug = slug;
        vm.Name = "My Gong";
        vm.Tradition = "Test tradition";
        vm.Region = "Test region";
        vm.Degrees[0].Text = "0";
        vm.Degrees[1].Text = "200";
        vm.AddDegreeCommand.Execute(null);
        vm.Degrees[2].Text = "400";
        vm.AddDegreeCommand.Execute(null);
        vm.Degrees[3].Text = "700";
        vm.AddDegreeCommand.Execute(null);
        vm.Degrees[4].Text = "900";
    }

    // ------------------------------------------------------------------ cents/ratio parsing

    [Theory]
    [InlineData("5/4")]
    [InlineData("386.31")]
    public void CentsAndRatioEntryProduceTheSameCentsValue(string text)
    {
        var expected = 1200.0 * Math.Log2(5.0 / 4.0);
        var entry = new DegreeEntryViewModel { Text = text };

        entry.Status.Should().Be(DegreeEntryStatus.Complete);
        entry.Cents.Should().BeApproximately(expected, 0.01);
    }

    [Fact]
    public void RatioAndCentsEntriesAgreeWithEachOtherWithinSmallTolerance()
    {
        var ratio = new DegreeEntryViewModel { Text = "5/4" };
        var cents = new DegreeEntryViewModel { Text = "386.31" };

        ratio.Cents.Should().BeApproximately(cents.Cents!.Value, 0.01);
    }

    [Fact]
    public void ABareIntegerIsCentsNotAScalaStyleRatio()
    {
        // Deliberately different from ScalaFileReader: a live text field's bare "700" means 700
        // cents to the person typing it, not the ratio 700/1 (~11,344 cents).
        var entry = new DegreeEntryViewModel { Text = "700" };

        entry.Status.Should().Be(DegreeEntryStatus.Complete);
        entry.Cents.Should().Be(700.0);
    }

    [Fact]
    public void ANegativeOrZeroRatioIsARealErrorNotAMidEditState()
    {
        var entry = new DegreeEntryViewModel { Text = "-5/4" };

        entry.Status.Should().Be(DegreeEntryStatus.Error);
        entry.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    // ------------------------------------------------------------------ mid-edit is not an error

    [Theory]
    [InlineData("")]
    [InlineData("5/")]
    [InlineData("-")]
    [InlineData("386.")]
    public void APartiallyTypedDegreeIsPendingOrEmptyNeverAnError(string text)
    {
        var entry = new DegreeEntryViewModel { Text = text };

        entry.Status.Should().NotBe(DegreeEntryStatus.Error);
    }

    [Fact]
    public void APartiallyTypedDegreeListDoesNotPresentAsAnErrorMidEdit()
    {
        // "Mid-edit" here means: at least one degree row is Empty or Pending - a prefix of a valid
        // token, not yet a finished one. Scale's constructor must never even run in that state, so
        // the message must not carry its rejection wording, only a neutral "still being entered".
        ScaleEditorViewModel vm = Editor();
        vm.IdSlug = "partial";
        vm.Name = "Partial";
        vm.Degrees[0].Text = "0";
        vm.Degrees[1].Text = "5/"; // still typing a ratio

        vm.IsValid.Should().BeFalse();
        vm.Preview.Should().BeNull();
        vm.ValidationMessage.Should().NotBeNullOrWhiteSpace();
        vm.ValidationMessage.Should().Contain("still being entered");
        vm.ValidationMessage.Should().NotContain("invalid", "that is Scale's own rejection wording, which must not appear while merely mid-typed");
    }

    // ------------------------------------------------------------------ Scale's own validation, surfaced verbatim

    [Fact]
    public void TooFewDegreesIsRefusedWithScalesOwnReason()
    {
        ScaleEditorViewModel vm = Editor();
        vm.IdSlug = "one-degree";
        vm.Name = "One Degree";

        // Drop to a single row - RemoveDegree refuses below MinDegrees, so remove via direct
        // collection manipulation to reach the invalid state Scale itself must reject.
        vm.Degrees.RemoveAt(1);
        vm.Degrees[0].Text = "0";

        vm.IsValid.Should().BeFalse();
        // Below Scale.MinDegrees the editor's own guard fires before Scale's constructor is even
        // reached - it is still Scale's own threshold, just reported one step earlier.
        vm.ValidationMessage.Should().Contain("2");
    }

    [Fact]
    public void TooManyDegreesIsRefusedWithScalesOwnReason()
    {
        ScaleEditorViewModel vm = Editor();
        vm.IdSlug = "many-degrees";
        vm.Name = "Many Degrees";
        vm.Degrees[0].Text = "0";
        vm.Degrees[1].Text = "100";

        // AddDegreeCommand refuses past Scale.MaxDegrees by design, so reach 13 rows - one past the
        // cap - via the collection directly, the same way a JSON import with too many degrees would
        // still need to be refused.
        for (int i = 2; i < 13; i++)
        {
            vm.Degrees.Add(new DegreeEntryViewModel { Text = (i * 100).ToString() });
        }

        vm.Degrees.Count.Should().Be(13);
        vm.IsValid.Should().BeFalse();
        vm.Preview.Should().BeNull();
        vm.ValidationMessage.Should().Contain("12");
    }

    [Fact]
    public void AFirstDegreeNotAtZeroIsRefusedWithScalesOwnReason()
    {
        ScaleEditorViewModel vm = Editor();
        FillValidGongLikeScale(vm);
        vm.Degrees[0].Text = "50";

        vm.IsValid.Should().BeFalse();
        vm.ValidationMessage.Should().Contain("0 cents");
    }

    [Fact]
    public void NonAscendingDegreesAreRefusedWithScalesOwnReason()
    {
        ScaleEditorViewModel vm = Editor();
        FillValidGongLikeScale(vm);
        vm.Degrees[2].Text = "100"; // below degree 1's 200 - breaks ascending order

        vm.IsValid.Should().BeFalse();
        vm.ValidationMessage.Should().Contain("ascend");
    }

    [Fact]
    public void DuplicateDegreesAreRefusedWithScalesOwnReason()
    {
        ScaleEditorViewModel vm = Editor();
        FillValidGongLikeScale(vm);
        vm.Degrees[2].Text = "200"; // exact duplicate of degree 1

        vm.IsValid.Should().BeFalse();
        vm.ValidationMessage.Should().Contain("ascend");
    }

    [Fact]
    public void ADegreeAt1200CentsIsRefusedWithScalesOwnReason()
    {
        ScaleEditorViewModel vm = Editor();
        FillValidGongLikeScale(vm);
        vm.Degrees[^1].Text = "1200";

        vm.IsValid.Should().BeFalse();
        vm.ValidationMessage.Should().Contain("1200");
    }

    // ------------------------------------------------------------------ Source default

    [Fact]
    public void SourceDefaultsToANonPlaceholderValueTheConstructorAccepts()
    {
        ScaleEditorViewModel vm = Editor();

        vm.Source.Should().Be("user-defined");

        FillValidGongLikeScale(vm);

        vm.IsValid.Should().BeTrue(vm.ValidationMessage);
        vm.Preview!.Source.Should().Be("user-defined");
    }

    // ------------------------------------------------------------------ Notatable derived guess + override

    [Fact]
    public void NotatableIsDerivedByDefaultForAScaleThatSpellsCleanly()
    {
        ScaleEditorViewModel vm = Editor();
        FillValidGongLikeScale(vm); // 0,200,400,700,900 - spells cleanly as C D E G A

        vm.IsNotatableManualOverride.Should().BeFalse();
        vm.Notatable.Should().BeTrue();
        vm.Preview!.Spelling.Should().NotBeNull();
    }

    [Fact]
    public void NotatableIsDerivedFalseByDefaultForAScaleThatCannotBeSpelled()
    {
        ScaleEditorViewModel vm = Editor();
        vm.IdSlug = "eight-degrees";
        vm.Name = "Eight Degrees";
        vm.Degrees[0].Text = "0";
        vm.Degrees[1].Text = "150";
        double[] rest = [300, 450, 600, 750, 900, 1050];
        foreach (double cents in rest)
        {
            vm.AddDegreeCommand.Execute(null);
            vm.Degrees[^1].Text = cents.ToString();
        }

        vm.Degrees.Count.Should().Be(8, "more than 7 degrees cannot be spelled - DiatonicSpeller's own rule");
        vm.IsNotatableManualOverride.Should().BeFalse();
        vm.Notatable.Should().BeFalse();
        vm.Preview!.Spelling.Should().BeNull();
    }

    [Fact]
    public void OverridingNotatableIsStickyAcrossFurtherDegreeEdits()
    {
        ScaleEditorViewModel vm = Editor();
        FillValidGongLikeScale(vm); // derives true

        vm.Notatable = false;
        vm.IsNotatableManualOverride.Should().BeTrue();

        vm.Degrees[1].Text = "210"; // touch a degree - must not silently flip the override back
        vm.Notatable.Should().BeFalse();
    }

    [Fact]
    public void OverridingNotatableToFalseNullsTheSpellingEvenThoughOneWouldOtherwiseDerive()
    {
        ScaleEditorViewModel vm = Editor();
        FillValidGongLikeScale(vm);
        vm.Preview!.Spelling.Should().NotBeNull("sanity check: this scale spells cleanly by default");

        vm.Notatable = false;

        vm.IsValid.Should().BeTrue(vm.ValidationMessage);
        vm.Preview!.Notatable.Should().BeFalse();
        vm.Preview!.Spelling.Should().BeNull();
    }

    [Fact]
    public void ResettingToTheDerivedGuessClearsTheManualOverride()
    {
        ScaleEditorViewModel vm = Editor();
        FillValidGongLikeScale(vm);
        vm.Notatable = false;

        vm.UseDerivedNotatableCommand.Execute(null);

        vm.IsNotatableManualOverride.Should().BeFalse();
        vm.Notatable.Should().BeTrue();
    }

    // ------------------------------------------------------------------ id namespacing

    [Fact]
    public void IdsAreNamespacedUnderUserRegardlessOfWhatWasTyped()
    {
        ScaleEditorViewModel vm = Editor();
        vm.IdSlug = "my-scale";

        vm.Id.Should().Be("user.my-scale");
        vm.Id.Should().StartWith("user.");
    }

    [Fact]
    public void SavingCannotSilentlyOverwriteAnUnrelatedExistingUserScale()
    {
        ScaleEditorViewModel first = Editor();
        FillValidGongLikeScale(first, slug: "shared-name");
        ScaleEditorSaveResult firstResult = first.Save();
        firstResult.Success.Should().BeTrue(firstResult.Reason);

        ScaleEditorViewModel second = Editor();
        FillValidGongLikeScale(second, slug: "shared-name");
        second.Name = "A completely different scale";
        ScaleEditorSaveResult secondResult = second.Save();

        secondResult.Success.Should().BeFalse();
        secondResult.Reason.Should().Contain("user.shared-name");

        ScaleJsonLoadResult onDisk = ScaleJsonStore.LoadFromFile(UserScalesPath);
        onDisk.Scales.Should().ContainSingle(s => s.Id == "user.shared-name");
        onDisk.Scales.Single(s => s.Id == "user.shared-name").Name.Should().Be("My Gong",
            "the second, colliding save must not have overwritten the first scale's data");
    }

    // ------------------------------------------------------------------ save/reload round trip

    [Fact]
    public void AValidScaleSavesAndReloadsRoundTrippingEveryField()
    {
        ScaleEditorViewModel vm = Editor();
        vm.IdSlug = "roundtrip";
        vm.Name = "Round Trip";
        vm.Tradition = "Test Tradition";
        vm.Region = "Test Region";
        vm.Source = "Hand-authored for this test, 2026";
        vm.Description = "A description that must survive the round trip.";
        vm.Degrees[0].Text = "0";
        vm.Degrees[1].Text = "5/4"; // exercise ratio entry as part of the round trip too
        vm.AddDegreeCommand.Execute(null);
        vm.Degrees[2].Text = "700";
        vm.Notatable = true;

        ScaleEditorSaveResult result = vm.Save();
        result.Success.Should().BeTrue(result.Reason);

        ScaleJsonLoadResult reloaded = ScaleJsonStore.LoadFromFile(UserScalesPath);
        reloaded.Failures.Should().BeEmpty();
        Scale saved = reloaded.Scales.Should().ContainSingle().Subject;

        saved.Id.Should().Be("user.roundtrip");
        saved.Name.Should().Be("Round Trip");
        saved.Tradition.Should().Be("Test Tradition");
        saved.Region.Should().Be("Test Region");
        saved.Source.Should().Be("Hand-authored for this test, 2026");
        saved.Description.Should().Be("A description that must survive the round trip.");
        saved.DegreeCents[0].Should().Be(0);
        saved.DegreeCents[1].Should().BeApproximately(1200.0 * Math.Log2(5.0 / 4.0), 0.0001);
        saved.DegreeCents[2].Should().Be(700);
        saved.Notatable.Should().BeTrue();
    }

    // ------------------------------------------------------------------ copy-on-edit

    [Fact]
    public void EditingAShippedScaleProducesANewUserScaleAndLeavesTheOriginalPresent()
    {
        Scale shipped = ShippedGong();
        ScaleEditorViewModel vm = Editor();

        vm.LoadForEdit(shipped, ScaleOrigin.Embedded);

        vm.IsCopyOnEdit.Should().BeTrue();
        vm.IdSlug.Should().Be("eastasia-china-gong");

        vm.Name = "My Gong Variant";
        vm.Degrees[1].Text = "210"; // change one degree so the copy is visibly different

        ScaleEditorSaveResult result = vm.Save();

        result.Success.Should().BeTrue(result.Reason);
        result.Scale!.Id.Should().Be("user.eastasia-china-gong");
        result.Scale!.Id.Should().NotBe(shipped.Id);

        // The original shipped scale is untouched - nothing here ever wrote to it, and it was never
        // even read from disk in the first place.
        shipped.DegreeCents.Should().Equal(0, 200, 400, 700, 900);
        shipped.Name.Should().Be("Gong");

        ScaleJsonLoadResult onDisk = ScaleJsonStore.LoadFromFile(UserScalesPath);
        onDisk.Scales.Should().ContainSingle(s => s.Id == "user.eastasia-china-gong");
        onDisk.Scales.Should().NotContain(s => s.Id == shipped.Id);
    }

    [Fact]
    public void EditingAnExistingUserScaleUpdatesItRatherThanAddingADuplicate()
    {
        ScaleEditorViewModel first = Editor();
        FillValidGongLikeScale(first, slug: "editable");
        first.Save().Success.Should().BeTrue();

        ScaleEditorViewModel second = Editor();
        Scale saved = ScaleJsonStore.LoadFromFile(UserScalesPath).Scales.Single();
        second.LoadForEdit(saved, ScaleOrigin.UserDefined);

        second.IsCopyOnEdit.Should().BeFalse();
        second.Name = "Renamed";
        second.Degrees[1].Text = "205";

        ScaleEditorSaveResult result = second.Save();

        result.Success.Should().BeTrue(result.Reason);
        result.Scale!.Id.Should().Be("user.editable");

        ScaleJsonLoadResult onDisk = ScaleJsonStore.LoadFromFile(UserScalesPath);
        onDisk.Scales.Should().ContainSingle("editing an existing user scale must update it, not add a duplicate entry");
        Scale updated = onDisk.Scales.Single();
        updated.Id.Should().Be("user.editable");
        updated.Name.Should().Be("Renamed");
        updated.DegreeCents[1].Should().Be(205);
    }

    // ------------------------------------------------------------------ delete

    [Fact]
    public void DeletingAUserScaleRemovesItAndLeavesTheShippedLibraryIntact()
    {
        ScaleEditorViewModel keep = Editor();
        FillValidGongLikeScale(keep, slug: "keep-me");
        keep.Save().Success.Should().BeTrue();

        ScaleEditorViewModel toDelete = Editor();
        FillValidGongLikeScale(toDelete, slug: "delete-me");
        toDelete.Save().Success.Should().BeTrue();

        ScaleEditorSaveResult deleteResult = toDelete.Delete();

        deleteResult.Success.Should().BeTrue(deleteResult.Reason);
        toDelete.CanDelete.Should().BeFalse();

        ScaleJsonLoadResult onDisk = ScaleJsonStore.LoadFromFile(UserScalesPath);
        onDisk.Scales.Should().ContainSingle(s => s.Id == "user.keep-me");
        onDisk.Scales.Should().NotContain(s => s.Id == "user.delete-me");

        // Shipped scales live in embedded assets and the beside-exe scales/ folder, neither of which
        // this type ever touches - deleting a user scale cannot possibly reach them. Asserted here by
        // construction: this test's only writable-root file is user.scales.json itself.
        Directory.EnumerateFiles(_besideExe, "*.json", SearchOption.AllDirectories)
            .Should().ContainSingle().Which.Should().Be(UserScalesPath);
    }

    [Fact]
    public void ANewUnsavedScaleCannotBeDeleted()
    {
        ScaleEditorViewModel vm = Editor();
        FillValidGongLikeScale(vm);

        vm.CanDelete.Should().BeFalse();
        ScaleEditorSaveResult result = vm.Delete();

        result.Success.Should().BeFalse();
    }

    // ------------------------------------------------------------------ unwritable save location

    [Fact]
    public void AnUnwritableSaveLocationReportsFailureRatherThanThrowing()
    {
        Directory.Delete(_besideExe);
        File.WriteAllText(_besideExe, "blocking file"); // makes both candidate roots unwritable
        Directory.Delete(_appData);
        File.WriteAllText(_appData, "blocking file");

        ScaleEditorViewModel vm = Editor();
        FillValidGongLikeScale(vm);

        var act = () => vm.Save();

        ScaleEditorSaveResult result = act.Should().NotThrow().Subject;
        result.Success.Should().BeFalse();
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }
}
