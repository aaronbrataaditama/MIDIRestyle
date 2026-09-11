using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Scales;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MidiRestyle.Mcp.Tests;

/// <summary>
/// The wire contract, pinned. Everything an agent is written against - tool names, annotations,
/// required lists, parameter schemas and the property names of every report payload - is frozen in
/// <c>tools.golden.json</c> and <c>reports.golden.json</c>, which are strict and carry no prose.
/// Descriptions, the server instructions and the prompt script are frozen separately in
/// <c>descriptions.snapshot.txt</c>: they are agent-facing surface too, but they are improved by
/// rewording, and a byte-for-byte pin on prose turns every wording improvement into a red build.
/// </summary>
/// <remarks>
/// To accept an intentional change, run once with <c>MIDIRESTYLE_UPDATE_GOLDEN=1</c>, read the diff,
/// and commit it with the change that caused it. The regeneration writes the source tree, not just
/// the output folder, so the committed file is the one that moves.
/// </remarks>
public class ContractTests
{
    private static readonly string ContractDir = Path.Combine(AppContext.BaseDirectory, "Contract");
    private static readonly JsonSerializerOptions Pretty = new(McpJson.Options) { WriteIndented = true };

    /// <summary>
    /// The report payload types, named here rather than discovered, so that adding a record without
    /// deciding whether it is wire surface cannot silently widen the contract.
    /// </summary>
    private static readonly (string Tool, Type Type)[] ReportTypes =
    [
        ("list_scales", typeof(ScalePage)),
        ("describe_scale", typeof(ScaleDetail)),
        ("inspect_midi", typeof(MidiInspection)),
        ("restyle_midi", typeof(RestyleReport)),
        ("export_musicxml", typeof(MusicXmlReport)),
    ];

    /// <summary>
    /// Every enum whose MEMBER NAMES reach the wire: the four parsed from tool parameters and the four
    /// echoed into reports. Named explicitly for the same reason <see cref="ReportTypes"/> is.
    /// </summary>
    /// <remarks>
    /// The schema cannot carry these. The parameters are declared as <c>string?</c> so that we, not the
    /// SDK binder, own the error text, so they serialise as <c>["string","null"]</c> and the accepted
    /// values appear nowhere in tools.golden.json. RENAMING a member is caught - EnumNamesTests,
    /// McpJsonTests and RestyleRequestResolverTests all pin wire strings - but ADDING one was silent
    /// until this golden existed: a new accepted value would ship with nothing recording it. Verified
    /// by adding a member and watching all 1542 tests stay green.
    /// </remarks>
    private static readonly (string Wire, Type Type)[] WireEnums =
    [
        ("collisions", typeof(CollisionPolicy)),
        ("exportFailureReason", typeof(ExportFailureReason)),
        ("fidelityBadge", typeof(FidelityBadge)),
        ("format", typeof(MidiFileFormatKind)),
        ("keyDetectionOutcome", typeof(KeyDetectionOutcome)),
        ("nonScaleNotes", typeof(NonScaleNotePolicy)),
        ("range", typeof(RangePolicy)),
        ("strategy", typeof(MappingStrategy)),
    ];

    [Fact]
    public void TheAcceptedAndEchoedEnumValuesMatchTheGolden()
    {
        var projection = new JsonObject();
        foreach ((string wire, Type type) in WireEnums.OrderBy(e => e.Wire, StringComparer.Ordinal))
        {
            var values = new JsonArray();
            foreach (string value in ValidValues(type))
            {
                values.Add(value);
            }

            projection[wire] = values;
        }

        AssertMatchesFile("enums.golden.json", projection.ToJsonString(Pretty));
    }

    private static IReadOnlyList<string> ValidValues(Type type) =>
        (IReadOnlyList<string>)typeof(EnumNames)
            .GetMethod(nameof(EnumNames.Valid))!
            .MakeGenericMethod(type)
            .Invoke(null, null)!;

    [Fact]
    public async Task ToolNamesSchemasAndAnnotationsMatchTheGolden()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        IList<McpClientTool> tools = await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var projection = new JsonArray();
        foreach (McpClientTool tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            JsonElement schema = tool.ProtocolTool.InputSchema;
            var properties = new JsonObject();
            if (schema.TryGetProperty("properties", out JsonElement props))
            {
                foreach (JsonProperty p in props.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    // The whole parameter schema, not merely its type: an agent writes against the
                    // enum values and the nested shape of `exclude` as much as against the type name.
                    // Descriptions are stripped recursively - they belong in the snapshot.
                    properties[p.Name] = WithoutProse(JsonNode.Parse(p.Value.GetRawText()));
                }
            }

            projection.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["readOnly"] = tool.ProtocolTool.Annotations?.ReadOnlyHint,
                ["destructive"] = tool.ProtocolTool.Annotations?.DestructiveHint,
                ["idempotent"] = tool.ProtocolTool.Annotations?.IdempotentHint,
                ["openWorld"] = tool.ProtocolTool.Annotations?.OpenWorldHint,
                ["required"] = schema.TryGetProperty("required", out JsonElement req) ? JsonNode.Parse(req.GetRawText()) : new JsonArray(),
                ["properties"] = properties,
            });
        }

        AssertMatchesFile("tools.golden.json", projection.ToJsonString(Pretty));
    }

    /// <summary>
    /// The other half of the wire, which <c>tools/list</c> does not describe at all: each tool's
    /// success payload is serialised into a text block, so its property names reach the agent without
    /// ever appearing in a schema. Walked through <see cref="McpJson.Options"/> so the pinned names
    /// are the ones the naming policy actually produces.
    /// </summary>
    [Fact]
    public void ReportPayloadShapesMatchTheGolden()
    {
        var projection = new JsonObject();
        foreach ((string tool, Type type) in ReportTypes)
        {
            projection[tool] = ShapeOf(type);
        }

        AssertMatchesFile("reports.golden.json", projection.ToJsonString(Pretty));
    }

    /// <summary>
    /// Every word of prose an agent reads: the standing instructions, the prompt and its arguments,
    /// each tool's description and each parameter's. Regenerable by design - this snapshot exists so
    /// that a wording change is reviewed, not so that it is forbidden.
    /// </summary>
    [Fact]
    public async Task DescriptionsInstructionsAndThePromptMatchTheSnapshot()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        IList<McpClientTool> tools = await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var sb = new StringBuilder();
        sb.Append("# instructions\n").Append(host.Client.ServerInstructions).Append("\n\n");

        foreach (McpClientPrompt prompt in (await host.Client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken))
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            sb.Append("# prompt ").Append(prompt.Name).Append('\n').Append(prompt.ProtocolPrompt.Description).Append('\n');
            foreach (PromptArgument argument in prompt.ProtocolPrompt.Arguments ?? [])
            {
                sb.Append("- ").Append(argument.Name).Append(" (required=").Append(argument.Required == true)
                    .Append("): ").Append(argument.Description).Append('\n');
            }

            // The script with no path given: the branch an agent that invokes the prompt cold gets,
            // and the only one that does not carry a caller's path through it.
            GetPromptResult script = await host.Client.GetPromptAsync(
                prompt.Name, new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);
            foreach (PromptMessage message in script.Messages)
            {
                sb.Append((message.Content as TextContentBlock)?.Text).Append('\n');
            }

            sb.Append('\n');
        }

        foreach (McpClientTool tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            sb.Append("# tool ").Append(tool.Name).Append('\n').Append(tool.Description).Append('\n');
            if (tool.ProtocolTool.InputSchema.TryGetProperty("properties", out JsonElement props))
            {
                foreach (JsonProperty p in props.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    string d = p.Value.TryGetProperty("description", out JsonElement de) ? de.GetString() ?? "" : "";
                    sb.Append("- ").Append(p.Name).Append(": ").Append(d).Append('\n');
                }
            }

            sb.Append('\n');
        }

        AssertMatchesFile("descriptions.snapshot.txt", sb.ToString());
    }

    [Fact]
    public async Task TheToolListFitsInTheContextBudget()
    {
        await using McpTestHost host = await McpTestHost.StartAsync();
        IList<McpClientTool> tools = await host.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        string compact = JsonSerializer.Serialize(tools.Select(t => t.ProtocolTool), McpJsonUtilities.DefaultOptions);

        Encoding.UTF8.GetByteCount(compact).Should().BeLessThanOrEqualTo(McpHost.MaxToolListBytes,
            "tools/list lands in every agent session's context");
    }

    /// <summary>
    /// A golden that rewrites itself on every run pins nothing, and the environment variable that
    /// makes it do so is one stray <c>setx</c> away from being permanently on. Asserted rather than
    /// assumed: if this fails, the other three tests in this class are meaningless.
    /// </summary>
    [Fact]
    public void TheGoldenIsNotRegeneratingItselfOnAnOrdinaryRun()
    {
        Environment.GetEnvironmentVariable(UpdateVariable).Should().NotBe("1",
            $"{UpdateVariable} rewrites the golden to match whatever the code currently does, so a run with it set proves nothing");
    }

    private const string UpdateVariable = "MIDIRESTYLE_UPDATE_GOLDEN";

    /// <summary>Strips every <c>description</c>, at any depth, so the strict golden carries no prose.</summary>
    private static JsonNode? WithoutProse(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                var copy = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> pair in o.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (pair.Key != "description")
                    {
                        copy[pair.Key] = WithoutProse(pair.Value?.DeepClone());
                    }
                }

                return copy;
            case JsonArray a:
                var items = new JsonArray();
                foreach (JsonNode? item in a)
                {
                    items.Add(WithoutProse(item?.DeepClone()));
                }

                return items;
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>
    /// The serialised shape of a payload type: property names exactly as the naming policy renders
    /// them, and a type word for each leaf. <c>?</c> marks a property that may be absent, since
    /// <see cref="McpJson.Options"/> omits nulls rather than writing them.
    /// </summary>
    private static JsonNode ShapeOf(Type type)
    {
        Type bare = Nullable.GetUnderlyingType(type) ?? type;
        if (bare == typeof(string))
        {
            return "string";
        }

        JsonTypeInfo info = McpJson.Options.GetTypeInfo(bare);
        switch (info.Kind)
        {
            case JsonTypeInfoKind.Object:
                var shape = new JsonObject();
                foreach (JsonPropertyInfo property in info.Properties)
                {
                    Type propertyType = property.PropertyType;
                    bool optional = Nullable.GetUnderlyingType(propertyType) is not null
                        || (!propertyType.IsValueType && IsNullableReference(property));
                    shape[property.Name + (optional ? "?" : "")] = ShapeOf(propertyType);
                }

                return shape;
            case JsonTypeInfoKind.Enumerable:
                return new JsonArray(ShapeOf(info.ElementType!));
            default:
                return Leaf(bare);
        }
    }

    /// <summary>
    /// Nullability of a reference-typed property, read from the declaring record's own annotation.
    /// Not available through <c>JsonPropertyInfo</c>, so the constructor parameter or property is
    /// asked directly; unknown is reported as nullable, which is the safer thing to freeze.
    /// </summary>
    private static bool IsNullableReference(JsonPropertyInfo property)
    {
        if (property.AttributeProvider is System.Reflection.PropertyInfo pi)
        {
            return new System.Reflection.NullabilityInfoContext().Create(pi).ReadState != System.Reflection.NullabilityState.NotNull;
        }

        return true;
    }

    private static string Leaf(Type type) => type switch
    {
        _ when type == typeof(string) => "string",
        _ when type == typeof(bool) => "boolean",
        _ when type == typeof(int) || type == typeof(long) => "integer",
        _ when type == typeof(double) || type == typeof(float) || type == typeof(decimal) => "number",
        _ when type.IsEnum => "string",
        _ => type.Name,
    };

    private static void AssertMatchesFile(string fileName, string actual)
    {
        string path = Path.Combine(ContractDir, fileName);
        actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            // Write the source tree, not only the output dir: the committed file is the contract.
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MIDIRestyle.slnx"))) { dir = dir.Parent; }
            string source = Path.Combine(dir!.FullName, "tests", "MidiRestyle.Mcp.Tests", "Contract", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(ContractDir);
            File.WriteAllText(source, actual, new UTF8Encoding(false));
            File.WriteAllText(path, actual, new UTF8Encoding(false));
        }

        File.Exists(path).Should().BeTrue($"{fileName} is missing; run once with {UpdateVariable}=1 to create it");
        string expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        actual.Should().Be(expected, $"{fileName} differs; if the change is intended, rerun with {UpdateVariable}=1 and review the diff");
    }
}
