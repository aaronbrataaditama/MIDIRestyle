using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using MidiRestyle.Core.Scales;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MidiRestyle.Mcp.Tests;

// Shares a collection with StderrLoggerFactoryTests: both redirect the process-wide Console.Out.
[Collection(ConsoleCollection.Name)]
public class McpHostTests
{
    private static McpServerOptions Build() =>
        McpHost.BuildOptions(TestLibrary.Load(), TestLibrary.Probe, "0.0.0-test");

    [Fact]
    public async Task TheServerIntroducesItselfAndAdvertisesItsTools()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();

        host.Client.ServerInfo.Name.Should().Be(McpHost.ServerName);
        host.Client.ServerInfo.Version.Should().Be("0.0.0-test", "the version is passed in, never restated here");
        host.Client.ServerInstructions.Should().Be(StylePrompts.ServerInstructions);
        host.Client.ServerCapabilities.Tools.Should().NotBeNull();
        host.Client.ServerCapabilities.Prompts.Should().NotBeNull("the capability is declared alongside tools, and choose_a_style fills it");

        IList<McpClientTool> tools = await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        tools.Select(t => t.Name).Should().BeEquivalentTo(
            ["list_scales", "describe_scale", "inspect_midi", "restyle_midi", "export_musicxml"]);

        McpClientTool list = tools.Single(t => t.Name == "list_scales");
        list.Description.Should().Contain("Search and filter");
        list.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue("both scale tools only read");
        list.JsonSchema.GetProperty("properties").GetProperty("maxDeviationCents").GetProperty("description")
            .GetString().Should().Contain("cents", "each parameter's [Description] reaches the schema");
    }

    /// <summary>
    /// Carried finding from Task 8: nothing had yet confirmed that the SDK binds tool arguments with
    /// <see cref="McpJson.Options"/> rather than its own <c>McpJsonUtilities.DefaultOptions</c>. This is
    /// the identity half - the instance BuildOptions hands over is the instance the created function
    /// marshals with. It reaches an SDK-internal property deliberately; if a future SDK renames it the
    /// test fails loudly, which is the right outcome for an upgrade that could silently swap the
    /// options instance back to the default.
    /// </summary>
    [Fact]
    public void EveryToolMarshalsWithTheProjectSerializerOptions()
    {
        McpServerOptions options = Build();
        options.ToolCollection.Should().NotBeNullOrEmpty();

        foreach (McpServerTool tool in options.ToolCollection!)
        {
            PropertyInfo? property = tool.GetType().GetProperty("AIFunction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            property.Should().NotBeNull($"{tool.ProtocolTool.Name} should expose the AIFunction the SDK invokes it through");

            var function = (AIFunction)property!.GetValue(tool)!;
            function.JsonSerializerOptions.Should().BeSameAs(McpJson.Options,
                $"{tool.ProtocolTool.Name} must bind arguments with our options, not the SDK's default");
        }
    }

    /// <summary>
    /// The behavioural half of the same finding, over a live server. The SDK's own default options are
    /// also <c>Web</c>-flavoured with a string-enum converter, so almost nothing distinguishes them -
    /// except the naming policy on that converter: ours renders <c>ScaleOrigin.BesideExe</c> as
    /// <c>besideExe</c>, the SDK's default as <c>BesideExe</c>. A camelCase enum in the generated schema
    /// therefore proves the options we passed governed schema generation and argument binding, and not a
    /// default instance. The tool is registered by the test because the production surface deliberately
    /// takes no enum parameter (see <see cref="EnumNames"/>); it is created exactly as
    /// <see cref="McpHost.BuildOptions"/> creates one.
    /// </summary>
    [Fact]
    public async Task ToolArgumentsAreBoundWithTheProjectSerializerOptions()
    {
        var owner = new EnumProbeTool();
        await using McpTestHost host = await McpTestHost.StartAsync(options =>
            options.ToolCollection!.Add(McpServerTool.Create(
                typeof(EnumProbeTool).GetMethod(nameof(EnumProbeTool.Echo))!,
                owner,
                new McpServerToolCreateOptions { SerializerOptions = McpJson.Options })));

        McpClientTool probe = (await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Single(t => t.Name == "enum_probe");
        string[] allowed = [.. probe.JsonSchema.GetProperty("properties").GetProperty("origin")
            .GetProperty("enum").EnumerateArray().Select(e => e.GetString()!)];
        allowed.Should().Contain("besideExe")
            .And.NotContain("BesideExe", "the SDK's default options would spell the enum PascalCase");

        CallToolResult echoed = await host.CallAsync("enum_probe", new() { ["origin"] = "besideExe" });
        echoed.IsError.Should().NotBe(true, "the camelCase name our options define must bind");
        ToolResults.TextOf(echoed).Should().Be("BesideExe");
    }

    /// <summary>
    /// The one decision the exe's entry point makes before Avalonia starts. Only a leading switch
    /// counts: a file path, no arguments at all, or a switch in any other position is the desktop app
    /// being launched normally, and must reach Avalonia untouched.
    /// </summary>
    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "tune.mid" }, false)]
    [InlineData(new[] { "--mcp" }, true)]
    [InlineData(new[] { "--version" }, true)]
    [InlineData(new[] { "--mcp", "extra" }, true)]
    [InlineData(new[] { "--MCP" }, false)]
    [InlineData(new[] { "-mcp" }, false)]
    [InlineData(new[] { "--mcp-server" }, false)]
    [InlineData(new[] { "x", "--mcp" }, false)]
    [InlineData(new[] { "", "--mcp" }, false)]
    public void OnlyALeadingMcpOrVersionSwitchIsACliInvocation(string[] args, bool expected)
    {
        McpHost.IsCliInvocation(args).Should().Be(expected);
    }

    /// <summary>
    /// <c>--version</c> is for scripts and for the end-to-end test that proves the exe answers at all,
    /// so it writes the version and nothing else: no banner, no trailing prose, and a bare "\n" rather
    /// than the platform's line ending, so a caller on either platform can compare the whole of stdout.
    /// </summary>
    [Fact]
    public void VersionPrintsNothingButTheDisplayVersionAndExitsZero()
    {
        TextWriter saved = Console.Out;
        var buffer = new StringWriter();
        int exit;
        try
        {
            Console.SetOut(buffer);
            exit = McpHost.RunCli(["--version"], "9.8.7");
        }
        finally
        {
            Console.SetOut(saved);
        }

        exit.Should().Be(0);
        buffer.ToString().Should().Be("9.8.7\n", "the version is passed in, and stdout carries it alone");
    }

    /// <summary>
    /// The version is whatever the caller passes, never restated inside the host - the same rule
    /// <see cref="TheServerIntroducesItselfAndAdvertisesItsTools"/> pins for the server handshake. A
    /// second value would let the About box and the command line disagree about what was built.
    /// </summary>
    [Fact]
    public void VersionEchoesWhateverItWasGivenRatherThanAConstant()
    {
        TextWriter saved = Console.Out;
        var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            McpHost.RunCli(["--version"], "1.2.3-rc.4+build");
        }
        finally
        {
            Console.SetOut(saved);
        }

        buffer.ToString().Should().Be("1.2.3-rc.4+build\n");
    }

    [McpServerToolType]
    internal sealed class EnumProbeTool
    {
        [McpServerTool(Name = "enum_probe", ReadOnly = true)]
        [Description("Test-only: echoes the enum value it was given.")]
        public CallToolResult Echo([Description("A scale origin.")] ScaleOrigin origin) =>
            new() { Content = [new TextContentBlock { Text = origin.ToString() }] };
    }
}
