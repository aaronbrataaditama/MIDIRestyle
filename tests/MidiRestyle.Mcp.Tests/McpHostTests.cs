using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using MidiRestyle.Core.Scales;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MidiRestyle.Mcp.Tests;

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

    [McpServerToolType]
    internal sealed class EnumProbeTool
    {
        [McpServerTool(Name = "enum_probe", ReadOnly = true)]
        [Description("Test-only: echoes the enum value it was given.")]
        public CallToolResult Echo([Description("A scale origin.")] ScaleOrigin origin) =>
            new() { Content = [new TextContentBlock { Text = origin.ToString() }] };
    }
}
