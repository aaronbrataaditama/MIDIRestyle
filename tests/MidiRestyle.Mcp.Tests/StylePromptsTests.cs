using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MidiRestyle.Mcp.Tests;

/// <summary>
/// The prompt and the standing instructions are the only prose an agent reads before it starts
/// calling tools, so they are tested like an interface rather than like documentation: a tool name
/// that does not exist is a broken instruction, and a tool the script never mentions is one the
/// agent will not think to reach for.
/// </summary>
public partial class StylePromptsTests
{
    [GeneratedRegex(@"\b[a-z]+_[a-z_]+\b")]
    private static partial Regex SnakeCaseWords();

    private static string TextOf(GetPromptResult result) =>
        string.Join("\n", result.Messages.Select(m => (m.Content as TextContentBlock)?.Text));

    private static HashSet<string> NamesIn(string text) =>
        [.. SnakeCaseWords().Matches(text).Select(m => m.Value)];

    [Fact]
    public async Task ThePromptIsListedAndEveryToolItNamesExists()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        IList<McpClientPrompt> prompts = await host.Client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);
        prompts.Select(p => p.Name).Should().Equal(["choose_a_style"]);

        GetPromptResult withPath = await host.Client.GetPromptAsync(
            "choose_a_style",
            new Dictionary<string, object?> { ["midiPath"] = @"C:\music\tune.mid" },
            cancellationToken: TestContext.Current.CancellationToken);

        string text = TextOf(withPath);
        text.Should().Contain(@"C:\music\tune.mid", "a path the caller already knows is threaded into the script");

        HashSet<string> tools = [.. (await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(t => t.Name)];
        HashSet<string> named = NamesIn(text + "\n" + StylePrompts.ServerInstructions);

        // Without this the loop below passes by iterating nothing, which is precisely how four
        // cannot-fail assertions reached this branch.
        named.Should().NotBeEmpty("the prose names tools in snake_case, so the scan must find some");

        foreach (string word in named)
        {
            tools.Should().Contain(word, $"'{word}' reads as a tool name and must exist");
        }

        tools.Should().BeSubsetOf(NamesIn(text), "an unmentioned tool is one the agent will never reach for");
    }

    /// <summary>
    /// The other branch of the script. An agent that invokes the prompt cold has no path to give it,
    /// and a script that assumed one would open by calling inspect_midi on nothing. Blank and
    /// whitespace are covered too, deliberately: the brief branched on null alone, so an agent that
    /// passed an empty string got `inspect_midi on ""` - a null check reads as sufficient here and
    /// is not.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithoutAUsablePathTheScriptOpensByAskingForOne(string? midiPath)
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        Dictionary<string, object?> args = midiPath is null ? [] : new() { ["midiPath"] = midiPath };
        GetPromptResult result = await host.Client.GetPromptAsync(
            "choose_a_style", args, cancellationToken: TestContext.Current.CancellationToken);

        string text = TextOf(result);
        text.Should().Contain("absolute path", "the script has to open by asking for the file");
        NamesIn(text).Should().Contain("inspect_midi",
            "the cold-start branch is how most agents arrive, and it used to skip inspection entirely - leaving question 5 leaning on a key it never fetched");
        text.Should().NotContain("inspect_midi on \"", "no blank path is interpolated into an opening call");
        NamesIn(text).Should().Contain("list_scales", "the questions still have to land on a tool");
    }

    [Fact]
    public void ServerInstructionsFitTheBudget()
    {
        Encoding.UTF8.GetByteCount(StylePrompts.ServerInstructions).Should().BeLessThanOrEqualTo(1536);
        // All FIVE, not three. Pinning a subset let describe_scale and export_musicxml be removed from
        // the instructions with every StylePromptsTests case still green.
        StylePrompts.ServerInstructions.Should().ContainAll(
            "inspect_midi", "list_scales", "describe_scale", "restyle_midi", "export_musicxml");
    }

    /// <summary>
    /// The instructions are the only place an agent learns that a scale it cannot notate is not a
    /// failure, and CLAUDE.md records that the notatable flag is the decision agents get wrong by
    /// default: they reach for export_musicxml on a scale the speller cannot write.
    /// </summary>
    [Fact]
    public void ServerInstructionsSteerTheTwoDecisionsAnAgentGetsWrong()
    {
        // The FILTER form, not the bare word. "notatable" also appears in the flow paragraph, so
        // asserting the word alone let a mutation strip the filter guidance and still pass.
        StylePrompts.ServerInstructions.Should().Contain("notatable=true", "an agent has to learn it is a list_scales filter, not just a property");
        StylePrompts.ServerInstructions.Should().Contain("fitsTwelveTet=true", "the other filter an agent cannot guess the name of");
        StylePrompts.ServerInstructions.Should().Contain("absolute", "every path argument is absolute");
    }
}
