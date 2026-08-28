using Avalonia;

namespace MidiRestyle.App;

internal static class Program
{
    // Avalonia requires the STA apartment on Windows and must not use any Avalonia API before
    // AppMain is called: everything before BuildAvaloniaApp is initialization-order sensitive.
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Referenced by the Avalonia previewer/designer by convention - keep the name and signature.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
