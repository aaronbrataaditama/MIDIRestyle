using System.Text.Json;

namespace MidiRestyle.Mcp.Tests;

public class ScaleToolsTests
{
    [Fact]
    public async Task ListScalesFiltersAndPages()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        JsonElement all = await host.CallJsonAsync("list_scales", new() { ["limit"] = 200 });
        all.GetProperty("totalMatches").GetInt32().Should().Be(171);
        all.GetProperty("truncated").GetBoolean().Should().BeFalse();

        JsonElement page = await host.CallJsonAsync("list_scales", new() { ["limit"] = 10, ["offset"] = 165 });
        page.GetProperty("scales").GetArrayLength().Should().Be(6);
        page.GetProperty("truncated").GetBoolean().Should().BeFalse();

        JsonElement neutral = await host.CallJsonAsync("list_scales", new() { ["third"] = "neutral", ["region"] = "Middle East" });
        neutral.GetProperty("scales").EnumerateArray().Select(s => s.GetProperty("id").GetString())
            .Should().Contain("middleeast.arabic.maqam-rast");

        JsonElement twelve = await host.CallJsonAsync("list_scales", new() { ["fitsTwelveTet"] = true, ["notatable"] = true, ["degreeCount"] = 7, ["query"] = "ionian" });
        twelve.GetProperty("scales").EnumerateArray().Select(s => s.GetProperty("id").GetString())
            .Should().Contain("europe.churchmodes.ionian");

        JsonElement none = await host.CallJsonAsync("list_scales", new() { ["query"] = "slendro", ["notatable"] = true });
        none.GetProperty("totalMatches").GetInt32().Should().Be(0, "Slendro is authored not notatable");
    }

    [Fact]
    public async Task ListScalesRejectsBadPagingAndThird()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        var result = await host.CallAsync("list_scales", new() { ["limit"] = 0 });
        result.IsError.Should().Be(true);
        ToolResults.TextOf(result).Should().Contain("limit");

        result = await host.CallAsync("list_scales", new() { ["offset"] = -1 });
        result.IsError.Should().Be(true);
        ToolResults.TextOf(result).Should().Contain("offset");

        result = await host.CallAsync("list_scales", new() { ["third"] = "augmented" });
        result.IsError.Should().Be(true);
        ToolResults.TextOf(result).Should().ContainAll("major", "minor", "neutral", "none");
    }

    /// <summary>
    /// The page is a window over the matches, and <c>truncated</c> says whether more follow it -
    /// so it is true only when the window stops short of the end.
    /// </summary>
    [Fact]
    public async Task ListScalesReportsTruncationAndTheDefaultPageSize()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        JsonElement first = await host.CallJsonAsync("list_scales", new() { ["limit"] = 5 });
        first.GetProperty("scales").GetArrayLength().Should().Be(5);
        first.GetProperty("offset").GetInt32().Should().Be(0);
        first.GetProperty("truncated").GetBoolean().Should().BeTrue("166 matches follow the first five");

        JsonElement defaulted = await host.CallJsonAsync("list_scales");
        defaulted.GetProperty("scales").GetArrayLength().Should().Be(ScaleTools.DefaultLimit);
        defaulted.GetProperty("totalMatches").GetInt32().Should().Be(171);

        JsonElement past = await host.CallJsonAsync("list_scales", new() { ["offset"] = 500 });
        past.GetProperty("scales").GetArrayLength().Should().Be(0);
        past.GetProperty("truncated").GetBoolean().Should().BeFalse("there is nothing after the end to page to");
    }

    [Fact]
    public async Task DescribeScaleReturnsDetailOrSuggestions()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        JsonElement rast = await host.CallJsonAsync("describe_scale", new() { ["scaleId"] = "MIDDLEEAST.arabic.maqam-rast" });
        rast.GetProperty("summary").GetProperty("id").GetString().Should().Be("middleeast.arabic.maqam-rast", "canonical id is echoed");
        rast.GetProperty("bendClustersAtDefaultTolerance").GetInt32().Should().Be(2);
        rast.GetProperty("source").GetString().Should().NotBeNullOrWhiteSpace();
        rast.GetProperty("origin").GetString().Should().BeOneOf("embedded", "besideExe");

        JsonElement mela = await host.CallJsonAsync("describe_scale", new() { ["scaleId"] = "southasia.carnatic.melakarta.1-kanakangi" });
        mela.GetProperty("origin").GetString().Should().Be("generated");

        var missing = await host.CallAsync("describe_scale", new() { ["scaleId"] = "maqam rast" });
        missing.IsError.Should().Be(true);
        ToolResults.TextOf(missing).Should().Contain("middleeast.arabic.maqam-rast");
    }

    /// <summary>
    /// The Task 13/14 review found that six of the eight filters could each be replaced with
    /// <c>.Where(s =&gt; true)</c> without failing a test: <c>Contain</c> is satisfied by any superset,
    /// so the original test pinned inclusion but never exclusion. These compare against an expected
    /// set computed independently from the unfiltered page, which no pass-through mutation survives.
    /// </summary>
    private sealed record Summary(string Id, string? Region, string? Tradition, int DegreeCount,
        bool Notatable, bool FitsTwelveTet, double? MaxDeviationCents, string? Third);

    private static List<Summary> Summaries(JsonElement page) =>
        [.. page.GetProperty("scales").EnumerateArray().Select(s => new Summary(
            s.GetProperty("id").GetString()!,
            s.GetProperty("region").GetString(),
            s.GetProperty("tradition").GetString(),
            s.GetProperty("degreeCount").GetInt32(),
            s.GetProperty("notatable").GetBoolean(),
            s.GetProperty("fitsTwelveTet").GetBoolean(),
            s.GetProperty("maxDeviationCents").ValueKind == JsonValueKind.Null
                ? null
                : s.GetProperty("maxDeviationCents").GetDouble(),
            s.GetProperty("third").GetString()))];

    private static async Task<List<Summary>> AllScales(McpTestHost host) =>
        Summaries(await host.CallJsonAsync("list_scales", new() { ["limit"] = 200 }));

    private async Task AssertFilterExcludes(string field, object value, Func<Summary, bool> predicate)
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        List<Summary> all = await AllScales(host);
        string[] expected = [.. all.Where(predicate).Select(s => s.Id).Order()];

        JsonElement filtered = await host.CallJsonAsync("list_scales", new() { [field] = value, ["limit"] = 200 });
        string[] actual = [.. Summaries(filtered).Select(s => s.Id).Order()];

        expected.Should().NotBeEmpty($"the {field} filter must match something for this to prove anything");
        expected.Length.Should().BeLessThan(all.Count, $"the {field} filter must exclude something too");
        actual.Should().Equal(expected, $"the {field} filter must return exactly the scales that satisfy it");
        filtered.GetProperty("totalMatches").GetInt32().Should().Be(expected.Length);
    }

    [Fact]
    public Task RegionFilterExcludes() =>
        AssertFilterExcludes("region", "Middle East", s => string.Equals(s.Region, "Middle East", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task TraditionFilterExcludes()
    {
        // Taken from the library rather than hard-coded, so this cannot rot when a scale is added.
        await using McpTestHost probe = await McpTestHost.StartAsync();
        string tradition = (await AllScales(probe)).First(s => s.Tradition is not null).Tradition!;

        await AssertFilterExcludes("tradition", tradition,
            s => string.Equals(s.Tradition, tradition, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public Task DegreeCountFilterExcludes() =>
        AssertFilterExcludes("degreeCount", 5, s => s.DegreeCount == 5);

    [Fact]
    public Task NotatableFilterExcludes() =>
        AssertFilterExcludes("notatable", false, s => !s.Notatable);

    [Fact]
    public Task FitsTwelveTetFilterExcludes() =>
        AssertFilterExcludes("fitsTwelveTet", false, s => !s.FitsTwelveTet);

    [Fact]
    public Task ThirdFilterExcludes() =>
        AssertFilterExcludes("third", "neutral", s => string.Equals(s.Third, "neutral", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public Task MaxDeviationCentsFilterExcludes() =>
        // A scale with no finite deviation satisfies no ceiling, which is the branch most likely to
        // be got wrong: null must fail the filter, not pass it unmeasured.
        AssertFilterExcludes("maxDeviationCents", 5.0, s => s.MaxDeviationCents <= 5.0);
}
