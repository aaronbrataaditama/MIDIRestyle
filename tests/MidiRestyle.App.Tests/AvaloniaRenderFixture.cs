using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Headless;

[assembly: AssemblyFixture(typeof(MidiRestyle.App.Tests.AvaloniaRenderFixture))]

namespace MidiRestyle.App.Tests;

/// <summary>
/// Owns the one thread in this assembly that is allowed to touch Avalonia, and initialises the
/// framework on it before any test runs.
/// </summary>
/// <remarks>
/// <para>
/// Two facts about Avalonia force this shape. Its objects have thread affinity, and
/// <c>Dispatcher.UIThread</c> binds to whichever thread reaches the framework first and stays bound
/// for the life of the process. A lock is not enough: it serialises access without making it the
/// <i>same</i> thread.
/// </para>
/// <para>
/// It is an assembly fixture rather than a per-class one because the race is between classes. With
/// xunit running them in parallel, a class that merely constructs an Avalonia type could bind the
/// dispatcher to a worker thread first, and the renderer would then fail with "a different thread
/// owns it" - but only in a full run, never when the render tests ran alone. An assembly fixture is
/// constructed before any test in the assembly, so the first thread to reach Avalonia is always this
/// one. A module initializer would also run early enough but deadlocks: it holds the module
/// initialisation lock while waiting on a thread that needs the same class's statics.
/// </para>
/// </remarks>
public sealed class AvaloniaRenderFixture : IDisposable
{
    private static readonly BlockingCollection<Action> Queue = [];
    private static readonly ManualResetEventSlim Ready = new();
    private static ExceptionDispatchInfo? _setupFailure;

    public AvaloniaRenderFixture()
    {
        Thread thread = new(Run)
        {
            IsBackground = true,
            Name = "avalonia-render-tests",
        };

        thread.Start();
        Ready.Wait();
    }

    private static void Run()
    {
        // Nothing may escape this method. An exception on a thread with no handler tears down the
        // whole process, which surfaces as every test passing and the run still reporting failure.
        try
        {
            AppBuilder.Configure<Application>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
        catch (Exception ex)
        {
            _setupFailure = ExceptionDispatchInfo.Capture(ex);
            return;
        }
        finally
        {
            Ready.Set();
        }

        try
        {
            foreach (Action work in Queue.GetConsumingEnumerable())
            {
                work();
            }
        }
        catch (Exception)
        {
            // A test's own exceptions are captured where they are queued, so anything arriving here
            // is teardown noise. Letting it escape would crash a run that had already passed.
        }
    }

    /// <summary>Runs <paramref name="action"/> on the render thread and rethrows whatever it threw.</summary>
    public static void Run(Action action)
    {
        Ready.Wait();
        _setupFailure?.Throw();

        using ManualResetEventSlim done = new();
        ExceptionDispatchInfo? failure = null;

        Queue.Add(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        failure?.Throw();
    }

    public void Dispose() => Queue.CompleteAdding();
}
