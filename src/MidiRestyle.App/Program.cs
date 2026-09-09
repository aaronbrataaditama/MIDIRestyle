using Avalonia;
using MidiRestyle.App.Services;
using MidiRestyle.Mcp;

namespace MidiRestyle.App;

internal static class Program
{
    // Avalonia requires the STA apartment on Windows and must not use any Avalonia API before
    // AppMain is called: everything before BuildAvaloniaApp is initialization-order sensitive.
    //
    // `--mcp` and `--version` never reach Avalonia at all - not a window, not a dispatcher, not a
    // single Avalonia type is loaded. The headless MCP server runs on this thread and returns when
    // the host closes stdin; blocking the STA thread there is safe because no SynchronizationContext
    // is installed yet, so the server's continuations run on the thread pool.
    //
    // Anything else - no arguments, a file path, the same switch in second position - is the desktop
    // app being launched and reaches Avalonia untouched, argv included.
    [STAThread]
    public static int Main(string[] args)
    {
        if (McpHost.IsCliInvocation(args))
        {
            return McpHost.RunCli(args, AppVersion.Display);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by the Avalonia previewer/designer by convention - keep the name and signature.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
