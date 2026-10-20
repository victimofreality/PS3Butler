using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PS3Butler.Views;

public sealed class NativeShutdownSmoke : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Opacity = 0 };
        desktop.MainWindow = window;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var errors = 0;
        var closed = false;
        _ = new WindowShutdownBehavior(window,
            () => ++attempts == 1 ? Task.FromException(new IOException("simulated save failure")) : settled.Task,
            _ => errors++);
        window.Closed += (_, _) => closed = true;
        window.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                window.Close();
                Require(!closed && errors == 1, "save failure keeps native window open");
                window.Close();
                window.Close();
                Require(!closed && attempts == 2, "repeated close waits for one shutdown");
                settled.SetResult();
                // Queue a continuation after the close handler has resumed.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Require(closed, "native window closes after shutdown settles");
                Console.WriteLine("Native-window shutdown smoke checks passed.");
                desktop.Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                desktop.Shutdown(1);
            }
        });
        base.OnFrameworkInitializationCompleted();
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception($"FAILED: {name}");
        Console.WriteLine($"PASS: {name}");
    }
}
