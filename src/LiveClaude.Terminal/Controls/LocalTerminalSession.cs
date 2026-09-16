using System.IO;
using System.Text;
using System.Windows.Threading;
using LiveClaude.Abstractions;

namespace LiveClaude.Terminal.Controls;

/// <summary>
/// Runs a command in a pseudo console and wires it to a <see cref="TerminalView"/>. Used by the app's
/// Terminal tab for the interactive one-off runs: accepting workspace trust for a new directory,
/// answering the Remote Control confirmation, or signing in with /login.
/// </summary>
public sealed class LocalTerminalSession : IDisposable
{
    private readonly TerminalView _view;
    private readonly Dispatcher _dispatcher;
    private IPtyProcess? _pty;
    private CancellationTokenSource? _cts;

    public LocalTerminalSession(TerminalView view)
    {
        _view = view;
        _dispatcher = view.Dispatcher;
        _view.Input += OnInput;
        _view.GridSizeChanged += OnGridSizeChanged;
    }

    public bool IsRunning => _pty is { HasExited: false };

    public event Action<int>? Exited;

    public void Start(string executablePath, IReadOnlyList<string> arguments, string workingDirectory)
    {
        Stop();

        _pty = PlatformLoader.Current.Pty.Start(new PtyOptions
        {
            ExecutablePath = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            Columns = (short)Math.Max(20, _view.Columns),
            Rows = (short)Math.Max(5, _view.Rows)
        });

        _cts = new CancellationTokenSource();
        var pty = _pty;
        var token = _cts.Token;

        _ = Task.Run(() => PumpAsync(pty, token), token);
        _ = pty.Exited.ContinueWith(t =>
        {
            var code = t.IsCompletedSuccessfully ? t.Result : -1;
            _dispatcher.BeginInvoke(() =>
            {
                _view.Write($"\r\n\x1b[90m[process exited with code {code}]\x1b[0m\r\n");
                Exited?.Invoke(code);
            });
        }, TaskScheduler.Default);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _pty?.Dispose();
        _pty = null;
    }

    private async Task PumpAsync(IPtyProcess pty, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[buffer.Length * 2];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await pty.Output.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (read <= 0)
                    break;

                var count = decoder.GetChars(buffer, 0, read, chars, 0);
                if (count == 0)
                    continue;

                var text = new string(chars, 0, count);
                await _dispatcher.InvokeAsync(() => _view.Write(text), DispatcherPriority.Render);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // the pseudo console closed with the process
        }
    }

    private void OnInput(string text)
    {
        try
        {
            _pty?.Write(text);
        }
        catch (IOException)
        {
            // process gone
        }
    }

    private void OnGridSizeChanged(int columns, int rows) =>
        _pty?.Resize((short)columns, (short)rows);

    public void Dispose()
    {
        _view.Input -= OnInput;
        _view.GridSizeChanged -= OnGridSizeChanged;
        Stop();
    }
}
