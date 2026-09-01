using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MidiRestyle.App.ViewModels;
using MidiRestyle.App.Views;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The notices window loads, shows and renders - and does so quickly enough to be worth opening.
/// </summary>
/// <remarks>
/// Same scope as <see cref="AboutWindowRenderTests"/>: it catches anything that throws while the
/// window is built, shown or rendered, which matters because this window sits two clicks deep
/// behind a menu item no other test opens.
///
/// The timing leg is the part that is not boilerplate. The document is ~4,600 lines in a single
/// <c>SelectableTextBlock</c>, and Avalonia 12 virtualises neither that nor a read-only
/// <c>TextBox</c> - so the entire text is laid out on open. That is a real risk of a dialog that
/// appears to hang, and it is invisible to a correctness test. The budget is deliberately loose;
/// it is here to catch a regression into seconds, not to police milliseconds.
/// </remarks>
public class ThirdPartyNoticesWindowRenderTests
{
    [Fact]
    public void TheNoticesWindowLoadsShowsAndRenders() => AvaloniaRenderFixture.Run(() =>
    {
        ThirdPartyNoticesWindow window = new();

        try
        {
            Stopwatch clock = Stopwatch.StartNew();

            window.Show();
            Dispatcher.UIThread.RunJobs();

            using Bitmap frame = window.CaptureRenderedFrame()!;

            clock.Stop();

            window.Width.Should().Be(860);
            window.Height.Should().Be(640);
            frame.PixelSize.Width.Should().Be(860);
            frame.PixelSize.Height.Should().Be(640);

            clock.ElapsedMilliseconds.Should().BeLessThan(4000,
                "the whole document is laid out on open, so a regression here is a dialog that "
                + "looks like it has hung");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void TheWindowShowsTheRealNoticesAndNotAnEmptyBox()
    {
        // Renders nothing, so it needs no UI thread: this asserts the binding source, which is the
        // part a pixel test cannot see. A window that renders perfectly around an empty document
        // satisfies no licence at all.
        ThirdPartyNoticesViewModel viewModel = new();

        viewModel.Text.Should().Contain("SIL OPEN FONT LICENSE Version 1.1");
        viewModel.Text.Should().Contain("Copyright 2020 The Inter Project Authors");
    }
}
