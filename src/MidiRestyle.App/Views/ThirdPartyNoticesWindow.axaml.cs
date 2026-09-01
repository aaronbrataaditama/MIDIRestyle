using Avalonia.Controls;
using Avalonia.Interactivity;
using MidiRestyle.App.ViewModels;

namespace MidiRestyle.App.Views;

/// <summary>
/// The third-party licence notices, shown as a modal dialog over the About box.
/// </summary>
/// <remarks>
/// Shaped like <see cref="AboutWindow"/> - constructed with its view model, shown with
/// <c>ShowDialog</c> - so every dialog in the app works the same way. Unlike the About box this
/// one resizes: it holds a long fixed-width document, and a licence a reader cannot widen is a
/// licence they will not read.
/// </remarks>
public partial class ThirdPartyNoticesWindow : Window
{
    public ThirdPartyNoticesWindow()
        : this(new ThirdPartyNoticesViewModel())
    {
    }

    public ThirdPartyNoticesWindow(ThirdPartyNoticesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
