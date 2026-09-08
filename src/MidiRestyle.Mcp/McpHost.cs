using System.Reflection;
using MidiRestyle.Core.Scales;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MidiRestyle.Mcp;

/// <summary>
/// Assembles the server. Split across files: the options live here, the command-line entry point
/// arrives in a later task.
/// </summary>
public static partial class McpHost
{
    public const string ServerName = "MIDIRestyle";

    /// <summary>
    /// The server's options, shared by the exe and the in-memory test harness. Tools and prompts are
    /// registered by reflection over the attributed methods of the given objects - no DI container and
    /// no hosting package, so nothing but JSON-RPC can reach stdout; each owner instance is
    /// process-lifetime and shared by concurrent calls, so it must hold only immutable state.
    /// </summary>
    /// <remarks>
    /// Every primitive is created with <see cref="McpJson.Options"/>, which is what makes that instance
    /// govern the whole wire contract: the SDK uses it both to generate each tool's input schema and to
    /// bind incoming arguments, so a tool cannot deserialise under different rules from the ones the
    /// payloads are serialised with.
    /// </remarks>
    public static McpServerOptions BuildOptions(ScaleLibrary library, PathProbe probe, string version)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(probe);

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = ServerName, Version = version },
            ServerInstructions = StylePrompts.ServerInstructions,
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability(), Prompts = new PromptsCapability() },
            ToolCollection = [],
            PromptCollection = [],
        };

        // `probe` is carried for the file tools a later task adds (new MidiTools(library, probe));
        // the scale tools need nothing but the library.
        object[] toolOwners = [new ScaleTools(library)];
        foreach (object owner in toolOwners)
        {
            foreach (MethodInfo method in AttributedMethods<McpServerToolAttribute>(owner))
            {
                options.ToolCollection.Add(McpServerTool.Create(method, owner,
                    new McpServerToolCreateOptions { SerializerOptions = McpJson.Options }));
            }
        }

        object[] promptOwners = [new StylePrompts()];
        foreach (object owner in promptOwners)
        {
            foreach (MethodInfo method in AttributedMethods<McpServerPromptAttribute>(owner))
            {
                options.PromptCollection.Add(McpServerPrompt.Create(method, owner,
                    new McpServerPromptCreateOptions { SerializerOptions = McpJson.Options }));
            }
        }

        return options;
    }

    private static IEnumerable<MethodInfo> AttributedMethods<TAttribute>(object owner) where TAttribute : Attribute =>
        owner.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<TAttribute>() is not null);
}
