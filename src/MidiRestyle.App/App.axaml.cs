using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MidiRestyle.App.Services;
using MidiRestyle.App.Views;

namespace MidiRestyle.App;

public partial class App : Application
{
    /// <summary>
    /// The single <see cref="Services.ThemeService"/> for the app's lifetime. Exposed so any part of
    /// the UI (the View menu, in particular) can read the current preference and call
    /// <see cref="Services.ThemeService.SetTheme"/> without constructing its own instance and
    /// drifting out of sync with what was actually applied.
    /// </summary>
    public ThemeService ThemeService { get; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Apply the persisted preference before the window is created, so the first frame is
        // already in the right theme rather than flashing the XAML default and then switching.
        ThemeService.Apply(this);

        // Whoever changes the preference (a View-menu item) only needs to call SetTheme; re-applying
        // to the live Application is this class's job, not theirs, so the two can never drift apart.
        ThemeService.ThemeChanged += (_, _) => ThemeService.Apply(this);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
