using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using MidiRestyle.App.ViewModels;

namespace MidiRestyle.App.Views;

/// <summary>
/// Shows how to point an MCP-capable agent at this exe, as a modal dialog over the main window.
/// </summary>
/// <remarks>
/// Shaped like <see cref="AboutWindow"/> - constructed with its view model, shown with
/// <c>ShowDialog</c> - so both Help dialogs work the same way. All three snippets come from the
/// view model, which holds no Avalonia types; the clipboard is the only thing this class adds, and
/// it is the only part that could not be tested headlessly.
/// </remarks>
public partial class AgentAccessWindow : Window
{
    private readonly AgentAccessViewModel _viewModel;

    public AgentAccessWindow()
        : this(new AgentAccessViewModel())
    {
    }

    public AgentAccessWindow(AgentAccessViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnCopyClaudeCode(object? sender, RoutedEventArgs e) =>
        await CopyAsync(_viewModel.ClaudeCodeCommand).ConfigureAwait(true);

    private async void OnCopyClaudeDesktop(object? sender, RoutedEventArgs e) =>
        await CopyAsync(_viewModel.ClaudeDesktopJson).ConfigureAwait(true);

    private async void OnCopyGeneric(object? sender, RoutedEventArgs e) =>
        await CopyAsync(_viewModel.GenericJson).ConfigureAwait(true);

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard and says whether it worked.
    /// </summary>
    /// <remarks>
    /// The catch is not defensive noise. The clipboard is a shared OS resource that another process
    /// can hold open, so <c>SetTextAsync</c> genuinely fails on real machines - and an unhandled
    /// exception out of an <c>async void</c> Click handler is unobserved by anything and brings the
    /// application down. The failure message names the manual way out, because every snippet is
    /// sitting in a selectable text box a few pixels away.
    /// </remarks>
    private async Task CopyAsync(string text)
    {
        try
        {
            IClipboard? clipboard = Clipboard;

            if (clipboard is null)
            {
                Report("No clipboard is available - select the text and press Ctrl+C.");
                return;
            }

            await clipboard.SetTextAsync(text).ConfigureAwait(true);
            Report("Copied to the clipboard.");
        }
        catch (Exception ex)
        {
            Report($"Could not copy - {ex.Message}. Select the text and press Ctrl+C.");
        }
    }

    private void Report(string message)
    {
        CopyStatus.Text = message;
        CopyStatus.IsVisible = true;
    }
}
