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
}
