using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

/// <summary>
/// <see cref="ScaleJsonStore"/> loads and saves the fixed <c>midirestyle-scales-v1</c> schema.
/// Malformed input is user input, not programmer error, so loading never throws for bad JSON, a
/// wrong schema, or an invalid scale - and one bad scale among several good ones must not drop the
/// good ones.
/// </summary>
public class ScaleJsonStoreTests
{
    // Gong pentatonic, the worked example from the format contract.
    private const string GongJson = """
        {
          "id": "eastasia.china.gong",
          "name": "Gong",
          "tradition": "Chinese Wusheng",
          "region": "East Asia",
          "degreeCents": [0, 200, 400, 700, 900],
          "notatable": true,
          "source": "Author Year, Title, p.N"
        }
        """;

    private static string Wrap(params string[] scaleEntries) =>
        $$"""
        {
          "schema": "midirestyle-scales-v1",
          "scales": [ {{string.Join(",", scaleEntries)}} ]
        }
        """;

    private static Scale MakeScale(
        string id = "test.scale",
        double[]? degreeCents = null,
        string source = "Unit test fixture, ScaleJsonStoreTests",
        bool notatable = true,
        IReadOnlyList<DegreeSpelling>? spelling = null,
        string? description = null) =>
        new(
            id: id,
            name: "Test scale",
            tradition: "Test tradition",
            region: "Test region",
            degreeCents: degreeCents ?? [0, 200, 400, 700, 900],
            source: source,
            notatable: notatable,
            spelling: spelling,
            description: description);

    // ---- Round trip and basic loading ---------------------------------------------------

    [Fact]
    public void RoundTrip_SaveThenLoad_YieldsEqualScales_IncludingSpellingAndDescription()
    {
        var spelling = new DegreeSpelling[]
        {
            new(DiatonicStep: 0, Alter: 0),
            new(DiatonicStep: 1, Alter: 0),
            new(DiatonicStep: 2, Alter: 0),
            new(DiatonicStep: 4, Alter: 0),
            new(DiatonicStep: 5, Alter: 0),
        };

        var original = MakeScale(
            id: "eastasia.china.gong",
            degreeCents: [0, 200, 400, 700, 900],
            spelling: spelling,
            description: "Chinese Gong pentatonic mode.");

        string json = ScaleJsonStore.SaveToString([original]);
        var result = ScaleJsonStore.LoadFromString(json);

        result.FileError.Should().BeNull();
        result.Failures.Should().BeEmpty();
        result.Scales.Should().HaveCount(1);

        var reloaded = result.Scales[0];
        reloaded.Id.Should().Be(original.Id);
        reloaded.Name.Should().Be(original.Name);
        reloaded.Tradition.Should().Be(original.Tradition);
        reloaded.Region.Should().Be(original.Region);
        reloaded.Source.Should().Be(original.Source);
        reloaded.Notatable.Should().Be(original.Notatable);
        reloaded.Description.Should().Be(original.Description);
        reloaded.DegreeCents.Should().Equal(original.DegreeCents);
        reloaded.Spelling.Should().NotBeNull();
        reloaded.Spelling.Should().Equal(original.Spelling);
    }

    [Fact]
    public void MinimalScaleWithoutOptionalFields_Loads()
    {
        var result = ScaleJsonStore.LoadFromString(Wrap(GongJson));

        result.FileError.Should().BeNull();
        result.Failures.Should().BeEmpty();
        result.Scales.Should().HaveCount(1);

        var scale = result.Scales[0];
        scale.Id.Should().Be("eastasia.china.gong");
        scale.Description.Should().BeNull();
        scale.Spelling.Should().BeNull();
    }

    [Fact]
    public void NotatableFalse_YieldsNullSpelling_EvenWhenSpellingArrayIsPresent()
    {
        const string json = """
            {
              "id": "seasia.gamelan.slendro",
              "name": "Slendro",
              "tradition": "Gamelan",
              "region": "Southeast Asia",
              "degreeCents": [0, 240, 480, 720, 960],
              "notatable": false,
              "source": "Author Year, Title, p.N",
              "spelling": [
                { "step": 0, "alter": 0 },
                { "step": 1, "alter": 0 },
                { "step": 2, "alter": 0.5 },
                { "step": 4, "alter": 0 },
                { "step": 5, "alter": 0.5 }
              ]
            }
            """;

        var result = ScaleJsonStore.LoadFromString(Wrap(json));

        result.Failures.Should().BeEmpty();
        result.Scales.Should().HaveCount(1);
        result.Scales[0].Notatable.Should().BeFalse();
        result.Scales[0].Spelling.Should().BeNull();
    }

    [Fact]
    public void UnknownProperties_AreIgnored()
    {
        const string json = """
            {
              "id": "eastasia.china.gong",
              "name": "Gong",
              "tradition": "Chinese Wusheng",
              "region": "East Asia",
              "degreeCents": [0, 200, 400, 700, 900],
              "notatable": true,
              "source": "Author Year, Title, p.N",
              "someFutureField": "unexpected",
              "extra": { "nested": [1, 2, 3] }
            }
            """;

        var result = ScaleJsonStore.LoadFromString(Wrap(json));

        result.FileError.Should().BeNull();
        result.Failures.Should().BeEmpty();
        result.Scales.Should().ContainSingle(s => s.Id == "eastasia.china.gong");
    }

    // ---- Spelling length mismatch --------------------------------------------------------

    [Fact]
    public void SpellingLengthMismatch_IsRejectedWithReasonNamingTheId()
    {
        const string json = """
            {
              "id": "bad.spelling.length",
              "name": "Bad Spelling",
              "tradition": "Test",
              "region": "Test",
              "degreeCents": [0, 200, 400, 700, 900],
              "notatable": true,
              "source": "Author Year, Title, p.N",
              "spelling": [ { "step": 0, "alter": 0 }, { "step": 1, "alter": 0 } ]
            }
            """;

        var result = ScaleJsonStore.LoadFromString(Wrap(json));

        result.Scales.Should().BeEmpty();
        result.Failures.Should().ContainSingle();
        result.Failures[0].Id.Should().Be("bad.spelling.length");
        result.Failures[0].Reason.Should().Contain("bad.spelling.length");
        result.Failures[0].Reason.Should().Contain("2").And.Contain("5");
    }

    // ---- Scale constructor validation failures, surfaced verbatim by id ------------------

    [Fact]
    public void MissingSource_IsRejected()
    {
        const string json = """
            {
              "id": "missing.source",
              "name": "No Source",
              "tradition": "Test",
              "region": "Test",
              "degreeCents": [0, 200, 400, 700, 900],
              "notatable": true,
              "source": ""
            }
            """;

        var result = ScaleJsonStore.LoadFromString(Wrap(json));

        result.Scales.Should().BeEmpty();
        result.Failures.Should().ContainSingle();
        result.Failures[0].Id.Should().Be("missing.source");
        result.Failures[0].Reason.Should().Contain("missing.source").And.Contain("needs a real Source");
    }

    [Fact]
    public void PlaceholderSourceTodo_IsRejected()
    {
        const string json = """
            {
              "id": "todo.source",
              "name": "Todo Source",
              "tradition": "Test",
              "region": "Test",
              "degreeCents": [0, 200, 400, 700, 900],
              "notatable": true,
              "source": "TODO"
            }
            """;

        var result = ScaleJsonStore.LoadFromString(Wrap(json));

        result.Scales.Should().BeEmpty();
        result.Failures.Should().ContainSingle();
        result.Failures[0].Id.Should().Be("todo.source");
        result.Failures[0].Reason.Should().Contain("todo.source").And.Contain("needs a real Source");
    }

    [Fact]
    public void NonAscendingDegrees_IsRejected()
    {
        const string json = """
            {
              "id": "non.ascending",
              "name": "Non Ascending",
              "tradition": "Test",
              "region": "Test",
              "degreeCents": [0, 400, 200, 700, 900],
              "notatable": true,
              "source": "Author Year, Title, p.N"
            }
            """;

        var result = ScaleJsonStore.LoadFromString(Wrap(json));

        result.Scales.Should().BeEmpty();
        result.Failures.Should().ContainSingle();
        result.Failures[0].Id.Should().Be("non.ascending");
        result.Failures[0].Reason.Should().Contain("non.ascending").And.Contain("strictly ascend");
    }

    [Fact]
    public void DegreeAt1200_IsRejected()
    {
        const string json = """
            {
              "id": "degree.at.1200",
              "name": "Degree At 1200",
              "tradition": "Test",
              "region": "Test",
              "degreeCents": [0, 200, 400, 700, 1200],
              "notatable": true,
              "source": "Author Year, Title, p.N"
            }
            """;

        var result = ScaleJsonStore.LoadFromString(Wrap(json));

        result.Scales.Should().BeEmpty();
        result.Failures.Should().ContainSingle();
        result.Failures[0].Id.Should().Be("degree.at.1200");
        result.Failures[0].Reason.Should().Contain("degree.at.1200").And.Contain("1200");
    }

    [Fact]
    public void FewerThanTwoDegrees_IsRejected()
    {
        const string json = """
            {
              "id": "too.few.degrees",
              "name": "Too Few",
              "tradition": "Test",
              "region": "Test",
              "degreeCents": [0],
              "notatable": true,
              "source": "Author Year, Title, p.N"
            }
            """;

        var result = ScaleJsonStore.LoadFromString(Wrap(json));

        result.Scales.Should().BeEmpty();
        result.Failures.Should().ContainSingle();
        result.Failures[0].Id.Should().Be("too.few.degrees");
        result.Failures[0].Reason.Should().Contain("too.few.degrees").And.Contain("at least 2 degrees");
    }

    [Fact]
    public void MoreThanTwelveDegrees_IsRejected()
    {
        string degrees = string.Join(", ", Enumerable.Range(0, 13).Select(i => (i * 90).ToString()));
        string json = $$"""
            {
              "id": "too.many.degrees",
              "name": "Too Many",
              "tradition": "Test",
              "region": "Test",
              "degreeCents": [{{degrees}}],
              "notatable": true,
              "source": "Author Year, Title, p.N"
            }
            """;

        var result = ScaleJsonStore.LoadFromString(Wrap(json));

        result.Scales.Should().BeEmpty();
        result.Failures.Should().ContainSingle();
        result.Failures[0].Id.Should().Be("too.many.degrees");
        result.Failures[0].Reason.Should().Contain("too.many.degrees").And.Contain("13").And.Contain("12");
    }

    // ---- One bad scale among several good ones --------------------------------------------

    [Fact]
    public void OneBadScaleAmongSeveralGoodOnes_GoodOnesLoad_BadOneIsReported()
    {
        const string goodOne = """
            {
              "id": "good.one",
              "name": "Good One",
              "tradition": "Test",
              "region": "Test",
              "degreeCents": [0, 200, 400, 700, 900],
              "notatable": true,
              "source": "Author Year, Title, p.N"
            }
            """;

        const string badOne = """
            {
              "id": "bad.one",
              "name": "Bad One",
              "tradition": "Test",
              "region": "Test",
              "degreeCents": [0, 200, 400, 700, 900],
              "notatable": true,
              "source": "TODO"
            }
            """;

        const string goodTwo = """
            {
              "id": "good.two",
              "name": "Good Two",
              "tradition": "Test",
              "region": "Test",
              "degreeCents": [0, 300, 500, 700, 1000],
              "notatable": true,
              "source": "Author Year, Title, p.N"
            }
            """;

        var result = ScaleJsonStore.LoadFromString(Wrap(goodOne, badOne, goodTwo));

        result.FileError.Should().BeNull();
        result.Scales.Should().HaveCount(2);
        result.Scales.Select(s => s.Id).Should().BeEquivalentTo(["good.one", "good.two"]);
        result.Failures.Should().ContainSingle();
        result.Failures[0].Id.Should().Be("bad.one");
    }

    // ---- Whole-file failures ---------------------------------------------------------------

    [Fact]
    public void MalformedJson_ReturnsStatedReason_AndDoesNotThrow()
    {
        const string malformed = "{ this is not valid json";

        var act = () => ScaleJsonStore.LoadFromString(malformed);

        var result = act.Should().NotThrow().Which;
        result.FileError.Should().NotBeNull();
        result.Scales.Should().BeEmpty();
        result.Failures.Should().BeEmpty();
    }

    [Fact]
    public void WrongSchemaValue_IsReported()
    {
        const string json = """
            {
              "schema": "some-other-schema-v9",
              "scales": []
            }
            """;

        var result = ScaleJsonStore.LoadFromString(json);

        result.FileError.Should().NotBeNull();
        result.FileError.Should().Contain("some-other-schema-v9");
        result.Scales.Should().BeEmpty();
    }
}
