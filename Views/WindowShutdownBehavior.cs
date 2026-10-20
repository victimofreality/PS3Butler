using Avalonia.Controls;
using System;
using System.Threading.Tasks;

namespace PS3Butler.Views;

public sealed class WindowShutdownBehavior
{
    private readonly Window _window;
    private readonly Func<Task> _stop;
    private readonly Action<Exception> _reportError;
    private bool _allowClose;
    private bool _isClosing;

    public WindowShutdownBehavior(Window window, Func<Task> stop, Action<Exception> reportError)
    {
        _window = window;
        _stop = stop;
        _reportError = reportError;
        window.Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_isClosing) return;
        _isClosing = true;
        try
        {
            await _stop();
            _allowClose = true;
            _window.Close();
        }
        catch (Exception ex)
        {
            _reportError(ex);
        }
        finally
        {
            _isClosing = false;
        }
    }
}
