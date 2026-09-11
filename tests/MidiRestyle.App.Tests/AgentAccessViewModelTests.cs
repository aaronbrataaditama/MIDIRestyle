using System.Text.Json;
using MidiRestyle.App.ViewModels;
using MidiRestyle.Mcp;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Guards the ready-to-paste MCP configuration shown by Help > Agent access.
/// </summary>
/// <remarks>
/// <para>
/// The path is the whole point of this dialog. MIDIRestyle ships as a single portable exe that the
/// user is free to rename and drop anywhere, so a snippet naming an assumed filename or an assumed
/// folder is worse than no snippet at all: it looks authoritative and fails at the point where the
/// agent tries to launch it. Every assertion here is therefore about a path that is *not* the one a
/// hard-coded string would produce, and about that path surviving two different escaping schemes -
/// Windows command-line quoting and JSON string escaping.
/// </para>
/// <para>
/// The JSON is checked by parsing it back and comparing the round-tripped string to the original
/// path, rather than by eyeballing a literal with doubled backslashes in it. A literal expectation
/// would have to encode the escaping by hand, which is the very thing under test.
/// </para>
/// </remarks>
public class AgentAccessViewModelTests
{
    /// <summary>A path that is nothing like the one an assumed name would produce.</summary>
    /// <remarks>
    /// Spaces, a renamed exe, and a folder depth of two. Spaces are the load-bearing part: they are
    /// what an unquoted command line and a naive JSON writer both get wrong, and
    /// <c>C:\Program Files</c> makes them the common case rather than an exotic one.
    /// </remarks>
    private const string PathWithSpaces = @"D:\Portable Tools\My MIDIRestyle.exe";

    [Fact]
    public void TheClaudeCodeCommandQuotesThePathSoSpacesSurvive()
    {
        AgentAccessViewModel vm = new(PathWithSpaces);

        // Asserted whole. A "contains the path" check passes on an unquoted command line, which is
        // exactly the failure this test exists to catch: `claude mcp add midirestyle -- D:\Portable
        // Tools\My MIDIRestyle.exe --mcp` would run "D:\Portable" with three arguments after it.
        vm.ClaudeCodeCommand.Should().Be(
            @"claude mcp add midirestyle -- ""D:\Portable Tools\My MIDIRestyle.exe"" --mcp");
    }

    [Fact]
    public void TheClaudeDesktopJsonCarriesThePathUnderMcpServers()
    {
        AgentAccessViewModel vm = new(PathWithSpaces);

        using JsonDocument parsed = JsonDocument.Parse(vm.ClaudeDesktopJson);
        JsonElement server = parsed.RootElement.GetProperty("mcpServers").GetProperty("midirestyle");

        // Round-tripped, not eyeballed: whatever escaping the writer chose, a reader must get the
        // original path back character for character or the agent launches the wrong file.
        server.GetProperty("command").GetString().Should().Be(PathWithSpaces);

        server.GetProperty("args").GetArrayLength().Should().Be(1);
        server.GetProperty("args")[0].GetString().Should().Be("--mcp");
    }

    [Fact]
    public void TheGenericJsonCarriesThePathAtTheRoot()
    {
        AgentAccessViewModel vm = new(PathWithSpaces);

        using JsonDocument parsed = JsonDocument.Parse(vm.GenericJson);

        // No mcpServers wrapper: this snippet is the server entry itself, for a host that asks for
        // one server at a time. Pinned negatively as well, because pasting a wrapped object into a
        // host expecting a bare one fails with a schema error the user cannot act on.
        parsed.RootElement.TryGetProperty("mcpServers", out _).Should().BeFalse(
            "the generic snippet is the server entry itself, not a whole config file");

        parsed.RootElement.GetProperty("command").GetString().Should().Be(PathWithSpaces);
        parsed.RootElement.GetProperty("args")[0].GetString().Should().Be("--mcp");
    }

    [Fact]
    public void TheJsonEscapesBackslashesRatherThanEmittingThemRaw()
    {
        AgentAccessViewModel vm = new(PathWithSpaces);

        // The parse-back tests above would also fail on raw backslashes, but only by throwing a
        // JsonException, which reads as "the test is broken". This says what is actually required.
        vm.ClaudeDesktopJson.Should().Contain(@"D:\\Portable Tools\\My MIDIRestyle.exe");
        vm.GenericJson.Should().Contain(@"D:\\Portable Tools\\My MIDIRestyle.exe");
    }

    [Fact]
    public void BothJsonSnippetsArePrettyPrintedSoTheyCanBePastedAndRead()
    {
        AgentAccessViewModel vm = new(PathWithSpaces);

        // A user is going to paste these into a config file by hand and then edit around them. One
        // long line is legal JSON and useless for that.
        vm.ClaudeDesktopJson.Should().Contain("\n");
        vm.GenericJson.Should().Contain("\n");
    }

    [Fact]
    public void TheDefaultIsThePathOfTheRunningProcess()
    {
        // Environment.ProcessPath is the test host here, which is precisely what makes this
        // assertion able to fail: it is not AppContext.BaseDirectory + an assumed name, and it is
        // not the App assembly's location either.
        Environment.ProcessPath.Should().NotBeNullOrWhiteSpace(
            "this test asserts against the running process's path and cannot run without one");

        new AgentAccessViewModel().ExePath.Should().Be(Environment.ProcessPath);
    }

    [Fact]
    public void WithNoProcessPathItFallsBackToTheExeNameBesideTheBaseDirectory()
    {
        // Environment.ProcessPath is documented as nullable (a native host that started the runtime
        // itself). The fallback is the documented behaviour, so it is asserted rather than assumed.
        AgentAccessViewModel.ResolveExePath(null, null, @"C:\Some Folder\")
            .Should().Be(@"C:\Some Folder\MIDIRestyle.exe");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnAbsentExplicitPathDefersToTheProcessPath(string? absent)
    {
        AgentAccessViewModel.ResolveExePath(absent, @"E:\Real\MIDIRestyle.exe", @"C:\Base\")
            .Should().Be(@"E:\Real\MIDIRestyle.exe");
    }

    [Fact]
    public void AnExplicitPathWinsOverBoth()
    {
        AgentAccessViewModel.ResolveExePath(PathWithSpaces, @"E:\Real\MIDIRestyle.exe", @"C:\Base\")
            .Should().Be(PathWithSpaces);
    }

    [Fact]
    public void TheArgumentsInTheSnippetsAreTheOnesTheExeActuallyDispatchesOn()
    {
        // The dialog restates "--mcp"; McpHost owns the dispatch and restates it too. Two literals
        // for one contract drift silently, so the snippet's own argument list is fed back through
        // the real dispatcher. Rename the switch on either side and this reddens.
        using JsonDocument parsed = JsonDocument.Parse(new AgentAccessViewModel(PathWithSpaces).GenericJson);

        string[] args = [.. parsed.RootElement.GetProperty("args").EnumerateArray().Select(a => a.GetString()!)];

        McpHost.IsCliInvocation(args).Should().BeTrue(
            "the exe must recognise the very arguments this window tells the user to configure");
    }

    [Fact]
    public void TheExplanationSaysHowAnAgentConnectsAndThatNothingListens()
    {
        string explanation = new AgentAccessViewModel(PathWithSpaces).Explanation;

        // The two facts a reader needs before pasting an exe path into an agent's config: what the
        // agent will actually do with it, and that it opens no port. "Nothing listens on the
        // network" is a security claim about this build - if the transport ever changes, this line
        // must change with it, so it is pinned rather than left to prose drift.
        explanation.Should().Contain("--mcp");
        explanation.Should().Contain("network");
    }
}
