using System.Reflection;
using Microsoft.Extensions.Logging;
using MidiRestyle.Core.Scales;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MidiRestyle.Mcp;

/// <summary>
/// Assembles the server and owns the command-line entry into it. The desktop exe hands its arguments
/// to <see cref="IsCliInvocation"/> before Avalonia starts; everything else it launches normally.
/// </summary>
public static partial class McpHost
{
    public const string ServerName = "MIDIRestyle";

    /// <summary>
    /// <c>tools/list</c> is answered once per agent session and its whole text lands in that session's
    /// context, before the agent has done anything. Descriptions earn their place against this budget,
    /// which is asserted by test rather than left as an aspiration.
    /// </summary>
    public const int MaxToolListBytes = 12 * 1024;

    /// <summary>The switch that makes the exe run as a stdio MCP server rather than opening a window.</summary>
    /// <remarks>
    /// The one place this string is written. <c>AgentAccessViewModel</c> builds the configuration
    /// snippets the user pastes into an agent host from this same constant, so the dialog cannot
    /// advertise a switch the dispatcher does not accept.
    /// </remarks>
    public const string McpSwitch = "--mcp";

    /// <summary>The switch that prints the version and exits.</summary>
    public const string VersionSwitch = "--version";

    /// <summary>
    /// True only for a leading <c>--mcp</c> or <c>--version</c>. Anything else - a file path, no
    /// arguments, the same switch in second position - is the desktop app being launched and goes to
    /// Avalonia untouched. Ordinal and case-sensitive: a Windows user typing <c>--MCP</c> gets the GUI,
    /// which is visible and correctable, rather than a silent server on a terminal they did not expect.
    /// </summary>
    public static bool IsCliInvocation(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Length >= 1 && (args[0] == McpSwitch || args[0] == VersionSwitch);
    }

    /// <summary>
    /// Runs the CLI mode named by <paramref name="args"/> and returns the process exit code. Call only
    /// when <see cref="IsCliInvocation"/> agreed.
    /// </summary>
    /// <remarks>
    /// <c>--version</c> writes the version and nothing else, with a bare <c>\n</c> so a caller on any
    /// platform can compare the whole of stdout. Note that a WinExe launched from a prompt without
    /// redirection has <see cref="Console.Out"/> bound to <see cref="Stream.Null"/>, so it prints
    /// nothing visible there - this is for scripts, for pipes and for the end-to-end test, not for a
    /// human at a console.
    /// </remarks>
    public static int RunCli(string[] args, string displayVersion)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args[0] == "--version")
        {
            Console.Out.Write(displayVersion + "\n");
            Console.Out.Flush();
            return 0;
        }

        return RunServerAsync(displayVersion).GetAwaiter().GetResult();
    }

    /// <summary>
    /// The stdio server. Everything it has to say about loading the scale library goes to stderr:
    /// stdout carries JSON-RPC and nothing else, which is why the logger factory is the one sink that
    /// cannot be pointed anywhere else. Returns when the host closes stdin.
    /// </summary>
    private static async Task<int> RunServerAsync(string displayVersion)
    {
        using var loggerFactory = new StderrLoggerFactory();
        ILogger log = loggerFactory.CreateLogger(ServerName);

        PathProbe probe = PathProbe.Default();
        ScaleLibraryLoadResult loaded = new ScaleLibraryLoader(probe).Load();
        log.LogInformation("Scale library: {Count} scales. {Reason}", loaded.Library.Count, loaded.Reason);
        foreach (ScaleLoadFailure failure in loaded.Failures)
        {
            log.LogWarning("Scale not loaded: {Id} - {Reason}", failure.Id, failure.Reason);
        }

        foreach (ScaleIdCollision collision in loaded.Collisions)
        {
            log.LogInformation("{Collision}", collision.Describe());
        }

        McpServerOptions options = BuildOptions(loaded.Library, probe, displayVersion);
        var transport = new StdioServerTransport(options, loggerFactory);
        await using McpServer server = McpServer.Create(transport, options, loggerFactory);
        await server.RunAsync().ConfigureAwait(false);
        return 0;
    }

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

        object[] toolOwners = [new ScaleTools(library), new MidiTools(library, probe)];
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
