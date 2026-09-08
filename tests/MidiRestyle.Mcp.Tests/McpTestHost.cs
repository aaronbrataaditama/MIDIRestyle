using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MidiRestyle.Mcp.Tests;

/// <summary>
/// The real server over in-memory pipes - same <see cref="McpHost.BuildOptions"/> the exe uses, so a
/// test exercises the shipped registration, schema generation and argument binding rather than calling
/// a tool method directly.
/// </summary>
internal sealed class McpTestHost : IAsyncDisposable
{
    private readonly McpServer _server;
    private readonly CancellationTokenSource _cts = new();
    private Task _run = Task.CompletedTask;

    public McpClient Client { get; private set; } = null!;

    private McpTestHost(McpServer server) => _server = server;

    /// <param name="configure">
    /// Optional hook applied to the options <see cref="McpHost.BuildOptions"/> produced, for tests that
    /// need a primitive the production surface deliberately does not carry. Never used to change what
    /// BuildOptions itself registered.
    /// </param>
    public static async Task<McpTestHost> StartAsync(Action<McpServerOptions>? configure = null)
    {
        Pipe clientToServer = new(), serverToClient = new();
        McpServerOptions options = McpHost.BuildOptions(TestLibrary.Load(), TestLibrary.Probe, "0.0.0-test");
        configure?.Invoke(options);

        var server = McpServer.Create(
            new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
            options);
        var host = new McpTestHost(server);
        host._run = server.RunAsync(host._cts.Token);

        host.Client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
        return host;
    }

    public async Task<CallToolResult> CallAsync(string tool, Dictionary<string, object?>? args = null) =>
        await Client.CallToolAsync(tool, args ?? []);

    /// <summary>The payload parsed as JSON, or a throw carrying the server's own error text.</summary>
    public async Task<JsonElement> CallJsonAsync(string tool, Dictionary<string, object?>? args = null)
    {
        CallToolResult result = await CallAsync(tool, args);
        string text = ToolResults.TextOf(result);
        if (result.IsError == true)
        {
            throw new InvalidOperationException($"{tool} returned an error: {text}");
        }

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _cts.CancelAsync();
        try { await _run; } catch (OperationCanceledException) { }
        await _server.DisposeAsync();
        _cts.Dispose();
    }
}
