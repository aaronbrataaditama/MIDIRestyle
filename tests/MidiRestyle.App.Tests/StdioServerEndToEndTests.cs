using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MidiRestyle.App.Services;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The one test that exercises the real transport: the built WinExe, spawned with redirected pipes the
/// way an MCP host spawns it. A hand-rolled JSON-RPC client rather than the SDK's, because the SDK
/// client owns stdout and the assertion here is about every byte on it.
/// </summary>
/// <remarks>
/// <para>
/// This is the only test in the suite that reaches <c>McpHost.RunServerAsync</c>. <c>--mcp</c> blocks
/// on stdin and <c>StdioServerTransport</c> opens the standard handles directly, so no in-process
/// redirect can stand in for a real child process.
/// </para>
/// <para>
/// stdout is drained by <see cref="Process.BeginOutputReadLine"/> into a channel rather than read with
/// <c>ReadLineAsync(token)</c>. <see cref="Process.StandardOutput"/> wraps a <em>synchronous</em>
/// FileStream, so a cancellation token is only observed before a read starts, never during one - a
/// server that answers nothing would hang the suite forever instead of failing. A channel read is
/// genuinely cancellable, and the child is killed in <see cref="Dispose"/> either way.
/// </para>
/// </remarks>
public sealed class StdioServerEndToEndTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "MIDIRestyle.exe");

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "midirestyle-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    public void Dispose()
    {
        foreach (Process process in _started)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
                // Already reaped.
            }

            process.Dispose();
        }

        try
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private Process Start(string arguments)
    {
        var psi = new ProcessStartInfo(Exe, arguments)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };
        psi.Environment[PathProbe.DataRootOverrideVariable] = _dataRoot;

        Process process = Process.Start(psi)!;
        _started.Add(process);
        return process;
    }

    /// <summary>
    /// True if the process exited within <paramref name="timeout"/>. Never throws and never blocks
    /// longer than that, so a wedged child fails the test rather than stalling the run.
    /// </summary>
    private static async Task<bool> ExitedWithin(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);

            // Parameterless WaitForExit is the documented flush for the async output readers: it
            // returns only once the redirected streams have reached end-of-file.
            process.WaitForExit();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [Fact]
    public async Task InitializeListToolsAndExitCleanlyWhenStdinCloses()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "spawns the Windows exe");
        File.Exists(Exe).Should().BeTrue(
            $"the App ProjectReference copies MIDIRestyle.exe into the test output, but '{Exe}' is not there");

        Process process = Start("--mcp");

        var stderr = new StringBuilder();
        Channel<string> stdout = Channel.CreateUnbounded<string>();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { stdout.Writer.TryComplete(); } else { stdout.Writer.TryWrite(e.Data); }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) { lock (stderr) { stderr.AppendLine(e.Data); } }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        string Stderr() { lock (stderr) { return stderr.ToString(); } }

        var lines = new List<string>();

        async Task<string> ReadUntil(string marker, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            while (true)
            {
                string line;
                try
                {
                    line = await stdout.Reader.ReadAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        $"timed out after {timeout} waiting for '{marker}' on stdout. Lines so far: " +
                        $"[{string.Join(" | ", lines)}]. stderr: {Stderr()}");
                }
                catch (ChannelClosedException)
                {
                    throw new InvalidOperationException(
                        $"stdout closed before '{marker}' arrived. Lines so far: [{string.Join(" | ", lines)}]. " +
                        $"stderr: {Stderr()}");
                }

                lines.Add(line);
                if (line.Contains(marker, StringComparison.Ordinal)) { return line; }
            }
        }

        StreamWriter stdin = process.StandardInput;
        stdin.AutoFlush = true;

        await stdin.WriteAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"e2e","version":"0"}}}""" + "\n");
        string init = await ReadUntil("\"id\":1", TimeSpan.FromSeconds(60));
        init.Should().Contain(McpHostServerName).And.Contain(AppVersion.Display);

        await stdin.WriteAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\n");
        await stdin.WriteAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""" + "\n");
        string tools = await ReadUntil("\"id\":2", TimeSpan.FromSeconds(60));
        tools.Should().ContainAll("list_scales", "describe_scale", "inspect_midi", "restyle_midi", "export_musicxml");

        stdin.Close();
        (await ExitedWithin(process, TimeSpan.FromSeconds(15)))
            .Should().BeTrue($"the server must exit when the host closes stdin. stderr: {Stderr()}");
        process.ExitCode.Should().Be(0, Stderr());

        var trailing = new List<string>();
        while (stdout.Reader.TryRead(out string? extra)) { trailing.Add(extra); }
        trailing.Should().BeEmpty("nothing may reach stdout after the last JSON-RPC response");

        foreach (string line in lines)
        {
            JsonDocument.Parse(line).RootElement.GetProperty("jsonrpc").GetString()
                .Should().Be("2.0", $"stray stdout line: {line}");
        }

        Stderr().Should().Contain("Scale library:",
            "the server logs its start-up to stderr, which is what keeps stdout protocol-only");

        Directory.Exists(Path.Combine(_dataRoot, ScaleLibraryLoader.ScalesFolderName))
            .Should().BeTrue("start-up materialised scales/ under the override root");
        File.Exists(Path.Combine(AppContext.BaseDirectory, ScaleLibraryLoader.ScalesFolderName, "europe.json"))
            .Should().BeFalse("and not into the test output dir");
    }

    [Fact]
    public async Task VersionPrintsTheDisplayVersionAndExits()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "spawns the Windows exe");
        File.Exists(Exe).Should().BeTrue(
            $"the App ProjectReference copies MIDIRestyle.exe into the test output, but '{Exe}' is not there");

        Process process = Start("--version");

        // Read before waiting: the parameterless WaitForExit inside ExitedWithin only returns once the
        // pipes have drained, and nothing here is large enough to fill one.
        CancellationToken token = TestContext.Current.CancellationToken;
        Task<string> output = process.StandardOutput.ReadToEndAsync(token);
        Task<string> error = process.StandardError.ReadToEndAsync(token);

        (await ExitedWithin(process, TimeSpan.FromSeconds(30)))
            .Should().BeTrue("--version must print and exit rather than opening a window");
        process.ExitCode.Should().Be(0, await error);
        (await output).Should().Be(AppVersion.Display + "\n");
    }

    /// <summary>
    /// The server name as it appears on the wire. Stated here rather than referenced from
    /// <c>McpHost</c>: the App test project reaches the Mcp assembly only transitively, and the
    /// assertion is about the bytes an MCP host reads, not about a constant agreeing with itself.
    /// </summary>
    private const string McpHostServerName = "MIDIRestyle";
}
