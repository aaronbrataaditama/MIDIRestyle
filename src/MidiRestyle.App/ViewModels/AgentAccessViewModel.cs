using System.Text.Json;
using System.Text.Json.Nodes;

namespace MidiRestyle.App.ViewModels;

/// <summary>
/// Ready-to-paste MCP client configuration for this very exe.
/// </summary>
/// <remarks>
/// <para>
/// The path is the substance of this window. MIDIRestyle ships as a single portable exe with no
/// installer, so it has no canonical location and no guaranteed filename - a user may keep it on a
/// USB stick, in their Downloads folder, or renamed. A snippet naming an assumed path would look
/// authoritative and fail the moment the agent tried to launch it, which is why every snippet is
/// built from <see cref="Environment.ProcessPath"/>: the path of the process the user is reading
/// this window in.
/// </para>
/// <para>
/// Every property is computed once in the constructor and exposed read-only. There is no mutable
/// state and therefore no <c>INotifyPropertyChanged</c>: nothing here can change while the dialog
/// is open, so a binding has nothing to be notified about. (Contrast <c>AboutViewModel</c>, which
/// does implement it, because a link can fail to open and that failure has to reach the view.)
/// </para>
/// <para>
/// It holds no Avalonia types on purpose. Clipboard access lives in the window, so the whole of
/// this - the quoting, the JSON escaping, the fallback - is testable headlessly.
/// </para>
/// </remarks>
public sealed class AgentAccessViewModel
{
    /// <summary>The name the server is registered under in an MCP host's configuration.</summary>
    public const string ServerKey = "midirestyle";

    /// <summary>The switch that makes the exe run as a stdio MCP server rather than opening a window.</summary>
    /// <remarks>
    /// Restated here rather than shared with <c>McpHost</c>, which owns the dispatch. The two are
    /// tied together by a test that feeds these very snippets back through the real dispatcher, so
    /// they cannot drift apart silently.
    /// </remarks>
    public const string McpSwitch = "--mcp";

    /// <summary>The filename assumed only when the runtime will not say what is running.</summary>
    private const string FallbackExeName = "MIDIRestyle.exe";

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>Builds the snippets for <paramref name="exePath"/>, or for the running process.</summary>
    /// <param name="exePath">
    /// An explicit path, for tests. Left null - as the window leaves it - the path comes from the
    /// running process.
    /// </param>
    public AgentAccessViewModel(string? exePath = null)
    {
        ExePath = ResolveExePath(exePath, Environment.ProcessPath, AppContext.BaseDirectory);

        ClaudeCodeCommand = $"claude mcp add {ServerKey} -- \"{ExePath}\" {McpSwitch}";

        // Built as JSON nodes and serialised, never assembled by string concatenation: the path is
        // full of backslashes and may contain any character Windows allows in a filename, and a
        // hand-written snippet would emit those raw and produce a config file the host cannot parse.
        ClaudeDesktopJson = new JsonObject
        {
            ["mcpServers"] = new JsonObject { [ServerKey] = ServerEntry(ExePath) },
        }.ToJsonString(Pretty);

        GenericJson = ServerEntry(ExePath).ToJsonString(Pretty);
    }

    /// <summary>Where this exe actually is.</summary>
    public string ExePath { get; }

    /// <summary>The one-liner that registers the server with Claude Code.</summary>
    /// <remarks>
    /// The path is quoted because it routinely contains spaces - <c>C:\Program Files</c>,
    /// <c>C:\Users\First Last\Downloads</c> - and unquoted it would be read as several arguments.
    /// The bare <c>--</c> is Claude Code's own separator: it stops the CLI from claiming
    /// <c>--mcp</c> as one of its options and hands it to the command instead.
    /// </remarks>
    public string ClaudeCodeCommand { get; }

    /// <summary>The <c>claude_desktop_config.json</c> fragment, wrapper included.</summary>
    public string ClaudeDesktopJson { get; }

    /// <summary>The bare server entry, for a host that asks for one server at a time.</summary>
    public string GenericJson { get; }

    /// <summary>What this window is offering, in one paragraph.</summary>
    /// <remarks>
    /// Says that nothing listens on the network because that is the question a user should ask
    /// before pasting an executable path into an agent's configuration, and because it is a claim
    /// about this build rather than about MCP in general: the transport is stdin/stdout, and the
    /// exe opens no socket.
    /// </remarks>
    public string Explanation =>
        "Any MCP-capable AI agent can drive MIDIRestyle without this window. It starts this exe with " +
        $"{McpSwitch} and talks to it over stdin and stdout - nothing listens on the network, and no " +
        "second copy of the app is installed. Paste one of these into your agent's MCP configuration; " +
        "the path below is where this exe is right now, so re-open this window if you move or rename it.";

    /// <summary>Decides which path the snippets should name.</summary>
    /// <remarks>
    /// Separated from the constructor so the fallback can be tested. <see cref="Environment.ProcessPath"/>
    /// is documented as nullable - a native host that started the runtime itself has no managed
    /// entry executable - and while that cannot happen to the shipping exe, guessing a name is
    /// still better than showing snippets with an empty command in them.
    /// </remarks>
    internal static string ResolveExePath(string? exePath, string? processPath, string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            return exePath;
        }

        return string.IsNullOrWhiteSpace(processPath)
            ? Path.Combine(baseDirectory, FallbackExeName)
            : processPath;
    }

    private static JsonObject ServerEntry(string exePath) => new()
    {
        ["command"] = exePath,
        ["args"] = new JsonArray(McpSwitch),
    };
}
