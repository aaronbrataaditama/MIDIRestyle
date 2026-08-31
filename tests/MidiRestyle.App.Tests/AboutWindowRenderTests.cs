using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MidiRestyle.App.Views;

namespace MidiRestyle.App.Tests;

/// <summary>
/// A smoke test: the About window loads its markup, shows, and renders a frame without throwing.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately modest, and the scope is stated here because it was measured rather than assumed.
/// Avalonia's XAML compiler already rejects a bad <c>x:Static</c> or a bad compiled binding at build
/// time (AVLN2000), so those never reach this test. A <c>{DynamicResource}</c> naming a key that
/// does not exist is NOT caught either - Avalonia falls back silently, by design, and the test
/// passes. Nor does it pin <c>SizeToContent</c>: removing it entirely still leaves the window tall
/// enough for these assertions.
/// </para>
/// <para>
/// What it does catch is anything that throws while the window is being built, shown or rendered -
/// verified by pointing the window's <c>Icon</c> at a resource that does not exist, which fails it
/// with a <c>FileNotFoundException</c>. That is the class of defect worth guarding here, because
/// this window sits behind a menu item that no other test opens: without this, it could throw on
/// every click and the suite would stay green.
/// </para>
/// </remarks>
public class AboutWindowRenderTests
{
    [Fact]
    public void TheAboutWindowLoadsShowsAndRenders() => AvaloniaRenderFixture.Run(() =>
    {
        AboutWindow window = new();

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Width.Should().Be(480, "the About box is a fixed-width dialog");
            window.Height.Should().BeGreaterThan(300, "the window must not render collapsed");

            using Bitmap frame = window.CaptureRenderedFrame()!;
            frame.PixelSize.Width.Should().Be(480);
            frame.PixelSize.Height.Should().Be((int)window.Height);
        }
        finally
        {
            // Closed even on failure: a window left showing outlives this test, and the next render
            // test in the assembly would inherit it.
            window.Close();
        }
    });
}
