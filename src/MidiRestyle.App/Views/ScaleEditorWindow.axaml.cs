using Avalonia.Controls;
using Avalonia.Interactivity;
using MidiRestyle.App.ViewModels;

namespace MidiRestyle.App.Views;

/// <summary>
/// The custom scale editor, shown as a modal dialog over the main window.
/// </summary>
/// <remarks>
/// Closes with <c>true</c> when a scale was saved or deleted, so the caller knows to reload the
/// library. Saving and deleting both report failure through the view model rather than throwing -
/// an unwritable location is an expected state on read-only media, not an error.
/// </remarks>
public partial class ScaleEditorWindow : Window
{
    private readonly ScaleEditorViewModel _viewModel;

    public ScaleEditorWindow()
        : this(new ScaleEditorViewModel())
    {
    }

    public ScaleEditorWindow(ScaleEditorViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Set when the library needs reloading because something was written.</summary>
    public bool LibraryChanged { get; private set; }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        ScaleEditorSaveResult result = _viewModel.Save();
        if (result.Success)
        {
            LibraryChanged = true;
            Close(true);
            return;
        }

        // Stay open and show why. Closing on a failed save would lose the user's work, which is a
        // far worse outcome than making them read a message.
        ShowFailure(result.Reason);
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        ScaleEditorSaveResult result = _viewModel.Delete();
        if (result.Success)
        {
            LibraryChanged = true;
            Close(true);
            return;
        }

        ShowFailure(result.Reason);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);

    private void OnRemoveDegreeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DegreeEntryViewModel entry })
        {
            _viewModel.RemoveDegree(entry);
        }
    }

    /// <summary>
    /// Surfaces a save or delete failure in the dialog itself.
    /// </summary>
    /// <remarks>
    /// Deliberately not a message box: the reason belongs next to the thing that failed, and a modal
    /// on top of a modal is a poor way to tell someone their USB stick is read-only.
    /// </remarks>
    private void ShowFailure(string reason) => Title = $"Custom scale - {reason}";
}
