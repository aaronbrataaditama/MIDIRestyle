using System.Text.Json;
using MidiRestyle.Core.Mapping;
using MidiRestyle.Mcp;
using ModelContextProtocol.Protocol;

namespace MidiRestyle.Mcp.Tests;

public class McpJsonTests
{
    private sealed record Sample(string OutputPath, double MaxDeviationCents, MappingStrategy Strategy, string? Missing);

    [Fact]
    public void PropertiesAreCamelCaseEnumsAreCamelCaseStringsAndNullsAreOmitted()
    {
        string json = JsonSerializer.Serialize(new Sample("x", 349.99, MappingStrategy.ScaleDegree, null), McpJson.Options);

        json.Should().Be("""{"outputPath":"x","maxDeviationCents":349.99,"strategy":"scaleDegree"}""");
    }

    [Fact]
    public void OkWrapsPayloadJsonAsOneTextBlockAndErrorSetsIsError()
    {
        CallToolResult ok = ToolResults.Ok(new { answer = 42 });
        ok.IsError.Should().NotBe(true);
        ToolResults.TextOf(ok).Should().Be("""{"answer":42}""");

        CallToolResult error = ToolResults.Error("bad input");
        error.IsError.Should().Be(true);
        ToolResults.TextOf(error).Should().Be("bad input");
    }
}
