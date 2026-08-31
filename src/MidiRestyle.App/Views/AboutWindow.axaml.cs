using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MidiRestyle.App.ViewModels;

namespace MidiRestyle.App.Views;

/// <summary>
/// The About box, shown as a modal dialog over the main window.
/// </summary>
/// <remarks>
/// Carries no logic beyond opening two links, which is why its view model holds nothing but the
/// text and a failure message. Shaped like <see cref="ScaleEditorWindow"/> - constructed with its
/// view model, shown with <c>ShowDialog</c> - so both dialogs in the app work the same way.
/// </remarks>
public partial class AboutWindow : Window
{
    private readonly AboutViewModel _viewModel;

    public AboutWindow()
        : this(new AboutViewModel())
    {
    }

    public AboutWindow(AboutViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnLicenseClicked(object? sender, RoutedEventArgs e) =>
        OpenInBrowser(AboutViewModel.LicenseUrl);

    private void OnDonateClicked(object? sender, RoutedEventArgs e) =>
        OpenInBrowser(AboutViewModel.DonationUrl);

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Hands a URL to whatever the system has registered to open it.
    /// </summary>
    /// <remarks>
    /// The same shape as the Scales menu's "Open scales folder", deliberately: <c>UseShellExecute</c>
    /// is what makes the shell resolve the association, and without it .NET tries to execute the
    /// string as a program and throws. Both URLs are compile-time constants on the view model, so
    /// nothing user-supplied ever reaches the shell.
    ///
    /// The catch is not defensive noise. A machine with no default browser is a real state, and an
    /// unhandled exception out of a Click handler brings the whole application down - a spectacular
    /// way to fail at showing someone a web address they could have typed themselves.
    /// </remarks>
    private void OpenInBrowser(string url)
    {
        try
        {
            _viewModel.LaunchError = null;
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _viewModel.LaunchError = $"Could not open {url} - {ex.Message}";
        }
    }
}
