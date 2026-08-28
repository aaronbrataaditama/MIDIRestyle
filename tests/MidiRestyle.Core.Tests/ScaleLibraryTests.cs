using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

public class ScaleLibraryTests
{
    private static Scale S(string id, string name, string region = "East Asia", string tradition = "Test") =>
        new(id, name, tradition, region, [0, 200, 400, 700, 900], "Test fixture, 2026");

    [Fact]
    public void BuildMergesEverySource()
    {
        ScaleLibrary library = ScaleLibrary.Build(
            (ScaleOrigin.Generated, [S("a", "A")]),
            (ScaleOrigin.Embedded, [S("b", "B")]),
            (ScaleOrigin.UserDefined, [S("c", "C")]));

        library.Count.Should().Be(3);
        library.Collisions.Should().BeEmpty();
    }

    /// <summary>
    /// Precedence is user &gt; beside-exe &gt; embedded &gt; generated: a user must be able to override
    /// anything the app ships without editing the app.
    /// </summary>
    [Fact]
    public void LaterSourcesWinOnAnIdCollision()
    {
        ScaleLibrary library = ScaleLibrary.Build(
            (ScaleOrigin.Embedded, [S("gong", "Shipped Gong")]),
            (ScaleOrigin.UserDefined, [S("gong", "My Gong")]));

        library.Count.Should().Be(1);
        library.Find("gong")!.Name.Should().Be("My Gong");
        library.OriginOf("gong").Should().Be(ScaleOrigin.UserDefined);
    }

    /// <summary>
    /// Shadowing is a feature; an unintended shadow is a confusing bug. The two are
    /// indistinguishable unless the library says which happened.
    /// </summary>
    [Fact]
    public void CollisionsAreReportedRatherThanResolvedSilently()
    {
        ScaleLibrary library = ScaleLibrary.Build(
            (ScaleOrigin.Embedded, [S("gong", "Shipped")]),
            (ScaleOrigin.UserDefined, [S("gong", "Mine")]));

        library.Collisions.Should().ContainSingle();
        ScaleIdCollision collision = library.Collisions[0];
        collision.Id.Should().Be("gong");
        collision.Winner.Should().Be(ScaleOrigin.UserDefined);
        collision.Loser.Should().Be(ScaleOrigin.Embedded);
        collision.Describe().Should().Contain("gong").And.Contain("UserDefined");
    }

    [Fact]
    public void ShadowingKeepsThePositionOfTheOriginalRatherThanReorderingTheList()
    {
        ScaleLibrary library = ScaleLibrary.Build(
            (ScaleOrigin.Embedded, [S("a", "A"), S("b", "B"), S("c", "C")]),
            (ScaleOrigin.UserDefined, [S("b", "B overridden")]));

        // Equal(params) would swallow a because-string as another expected element, so pass an array.
        library.Entries.Select(e => e.Id).Should().Equal(["a", "b", "c"],
            "an override replaces a scale in place; it does not move it to the end of the list");
        library.Find("b")!.Name.Should().Be("B overridden");
    }

    [Fact]
    public void IdsAreMatchedCaseInsensitively()
    {
        ScaleLibrary library = ScaleLibrary.Build(
            (ScaleOrigin.Embedded, [S("Gong", "Shipped")]),
            (ScaleOrigin.UserDefined, [S("gong", "Mine")]));

        library.Count.Should().Be(1, "an id differing only in case is the same id, not a new scale");
        library.Contains("GONG").Should().BeTrue();
    }

    [Fact]
    public void FindReturnsNullForAnUnknownId() =>
        ScaleLibrary.Build((ScaleOrigin.Embedded, [S("a", "A")])).Find("nope").Should().BeNull();

    [Fact]
    public void AnEmptyLibraryIsUsable()
    {
        ScaleLibrary library = ScaleLibrary.Build();

        library.Count.Should().Be(0);
        library.Scales.Should().BeEmpty();
        library.Search("anything").Should().BeEmpty();
        library.ByRegion().Should().BeEmpty();
    }

    // --- grouping -----------------------------------------------------------------------

    /// <summary>
    /// Largest region first. Alphabetical would bury the 82 South Asian scales below Africa's ten
    /// and make scrolling the list feel arbitrary.
    /// </summary>
    [Fact]
    public void RegionsAreOrderedBySizeLargestFirst()
    {
        ScaleLibrary library = ScaleLibrary.Build((ScaleOrigin.Embedded,
        [
            S("x1", "X1", region: "Africa"),
            S("y1", "Y1", region: "South Asia"),
            S("y2", "Y2", region: "South Asia"),
            S("y3", "Y3", region: "South Asia"),
            S("z1", "Z1", region: "Europe"),
            S("z2", "Z2", region: "Europe"),
        ]));

        library.ByRegion().Select(g => g.Key).Should().Equal("South Asia", "Europe", "Africa");
    }

    // --- search -------------------------------------------------------------------------

    [Fact]
    public void AnEmptySearchReturnsEverything() =>
        ScaleLibrary.Build((ScaleOrigin.Embedded, [S("a", "A"), S("b", "B")]))
            .Search("   ").Should().HaveCount(2);

    [Fact]
    public void SearchMatchesNameTraditionRegionAndId()
    {
        ScaleLibrary library = ScaleLibrary.Build((ScaleOrigin.Embedded,
        [
            S("middleeast.arabic.rast", "Maqam Rast", region: "Middle East", tradition: "Arabic maqam"),
            S("eastasia.china.gong", "Gong", region: "East Asia", tradition: "Chinese Wusheng"),
        ]));

        library.Search("maqam").Should().ContainSingle();          // tradition
        library.Search("wusheng").Should().ContainSingle();         // tradition
        library.Search("eastasia").Should().ContainSingle();        // id
        library.Search("Rast").Should().ContainSingle();            // name
        library.Search("east asia").Should().ContainSingle()        // region, across two terms
            .Which.Name.Should().Be("Gong");
        library.Search("middle").Should().ContainSingle();          // region, partial
    }

    /// <summary>Every whitespace-separated term must match, so "gong pyth" narrows rather than widens.</summary>
    [Fact]
    public void AllSearchTermsMustMatch()
    {
        ScaleLibrary library = ScaleLibrary.Build((ScaleOrigin.Embedded,
        [
            S("a", "Gong"),
            S("b", "Gong (Pythagorean)"),
        ]));

        library.Search("gong").Should().HaveCount(2);
        library.Search("gong pyth").Should().ContainSingle()
            .Which.Name.Should().Be("Gong (Pythagorean)");
    }

    /// <summary>
    /// Typing a scale's exact name must offer that scale first, not the twenty whose text mentions it.
    /// </summary>
    [Fact]
    public void ExactNameMatchesRankAheadOfPartialOnes()
    {
        ScaleLibrary library = ScaleLibrary.Build((ScaleOrigin.Embedded,
        [
            S("c", "Rast Panjgah"),
            S("b", "Maqam Rast"),
            S("a", "Rast"),
        ]));

        library.Search("Rast").Select(s => s.Name).First().Should().Be("Rast");
    }

    [Fact]
    public void SearchIsCaseInsensitive() =>
        ScaleLibrary.Build((ScaleOrigin.Embedded, [S("a", "Hijaz")]))
            .Search("HIJAZ").Should().ContainSingle();

    // --- integration with the generated melakarta ------------------------------------------

    [Fact]
    public void TheGeneratedMelakartaMergeInWithUniqueIds()
    {
        ScaleLibrary library = ScaleLibrary.Build(
            (ScaleOrigin.Generated, MelakartaGenerator.GenerateAll()));

        library.Count.Should().Be(72);
        library.Collisions.Should().BeEmpty("every generated mela id must be distinct");
        library.ByRegion().Should().ContainSingle().Which.Key.Should().Be("South Asia");
    }

    [Fact]
    public void AUserScaleCanShadowAGeneratedMelakarta()
    {
        Scale mela15 = MelakartaGenerator.Generate(15);
        Scale mine = new(mela15.Id, "My Mayamalavagowla", mela15.Tradition, mela15.Region,
            [0, 100, 400, 500, 700, 800, 1100], "User-defined, 2026");

        ScaleLibrary library = ScaleLibrary.Build(
            (ScaleOrigin.Generated, MelakartaGenerator.GenerateAll()),
            (ScaleOrigin.UserDefined, [mine]));

        library.Count.Should().Be(72);
        library.Find(mela15.Id)!.Name.Should().Be("My Mayamalavagowla");
        library.Collisions.Should().ContainSingle();
    }
}
