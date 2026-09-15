using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using LiveClaude.Core.Claude;
using Microsoft.Win32.SafeHandles;
using static LiveClaude.Core.Pty.NativeMethods;

namespace LiveClaude.Core.Pty;

public sealed class PtyOptions
{
    public required string ExecutablePath { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public short Columns { get; init; } = 120;
    public short Rows { get; init; } = 30;
}

/// <summary>
/// A child process hosted in a Windows pseudo console (ConPTY). Unlike plain redirected pipes this
/// gives the CLI a real terminal, so its full-screen prompts — workspace trust, the Remote Control
/// confirmation — render and can be answered, including from a Windows service with no desktop.
/// </summary>
public sealed class PtyProcess : IDisposable
{
    private readonly IntPtr _pseudoConsole;
    private readonly IntPtr _processHandle;
    private readonly IntPtr _threadHandle;
    private readonly SafeFileHandle _inputWrite;
    private readonly SafeFileHandle _outputRead;
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RegisteredWaitHandle? _waitHandle;
    private int _disposed;

    private PtyProcess(IntPtr pseudoConsole, PROCESS_INFORMATION pi, SafeFileHandle inputWrite, SafeFileHandle outputRead)
    {
        _pseudoConsole = pseudoConsole;
        _processHandle = pi.hProcess;
        _threadHandle = pi.hThread;
        ProcessId = pi.dwProcessId;
        _inputWrite = inputWrite;
        _outputRead = outputRead;

        Input = new FileStream(inputWrite, FileAccess.Write, bufferSize: 4096, isAsync: false);
        Output = new FileStream(outputRead, FileAccess.Read, bufferSize: 4096, isAsync: false);

        RegisterExitCallback();
    }

    public int ProcessId { get; }

    /// <summary>Write keystrokes here (UTF-8).</summary>
    public Stream Input { get; }

    /// <summary>Raw terminal output, escape sequences included (UTF-8).</summary>
    public Stream Output { get; }

    public Task<int> Exited => _exited.Task;

    public bool HasExited => _exited.Task.IsCompleted;

    /// <summary>True on Windows 10 1809 and later, where ConPTY exists.</summary>
    public static bool IsSupported { get; } = CheckSupport();

    /// <summary>
    /// True when this process owns a console window. Windows then attaches every child to that
    /// console and ignores the pseudo-console attribute, so the child's output never reaches the
    /// pipes — which is why the supervisor and the app are both built as windowed executables and
    /// only attach to the caller's console for one-shot CLI commands.
    /// </summary>
    public static bool HasOwnConsole => GetConsoleCP() != 0 || GetConsoleWindow() != IntPtr.Zero;

    public static PtyProcess Start(PtyOptions options)
    {
        var attributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = 0,
            lpSecurityDescriptor = IntPtr.Zero
        };

        if (!CreatePipe(out var inputRead, out var inputWrite, ref attributes, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (input) failed.");
        if (!CreatePipe(out var outputRead, out var outputWrite, ref attributes, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (output) failed.");

        var size = new COORD { X = Math.Max((short)20, options.Columns), Y = Math.Max((short)5, options.Rows) };
        var hr = CreatePseudoConsole(size, inputRead, outputWrite, 0, out var pseudoConsole);
        if (hr != 0)
            throw new Win32Exception(hr, "CreatePseudoConsole failed.");

        var attributeList = IntPtr.Zero;
        var environmentBlock = IntPtr.Zero;

        try
        {
            attributeList = CreateAttributeList(pseudoConsole);

            var startupInfo = new STARTUPINFOEX
            {
                StartupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFOEX>() },
                lpAttributeList = attributeList
            };

            environmentBlock = CreateEnvironmentBlock(options.Environment);

            var commandLine = ClaudeArgs.ToCommandLine(options.ExecutablePath, options.Arguments);

            var created = CreateProcessW(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                environmentBlock,
                string.IsNullOrWhiteSpace(options.WorkingDirectory) ? null : options.WorkingDirectory,
                ref startupInfo,
                out var processInfo);

            if (!created)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcess failed for: {commandLine}");

            // The child owns these ends now; closing them here is what lets the output stream see EOF.
            inputRead.Dispose();
            outputWrite.Dispose();

            return new PtyProcess(pseudoConsole, processInfo, inputWrite, outputRead);
        }
        catch
        {
            ClosePseudoConsole(pseudoConsole);
            inputRead.Dispose();
            inputWrite.Dispose();
            outputRead.Dispose();
            outputWrite.Dispose();
            throw;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (environmentBlock != IntPtr.Zero)
                Marshal.FreeHGlobal(environmentBlock);
        }
    }

    public void Resize(short columns, short rows)
    {
        if (_disposed != 0 || HasExited)
            return;

        var size = new COORD { X = Math.Max((short)20, columns), Y = Math.Max((short)5, rows) };
        ResizePseudoConsole(_pseudoConsole, size);
    }

    public void Write(string text)
    {
        if (HasExited)
            return;

        var bytes = Encoding.UTF8.GetBytes(text);
        Input.Write(bytes, 0, bytes.Length);
        Input.Flush();
    }

    public void Kill()
    {
        if (_disposed != 0 || HasExited)
            return;

        TerminateProcess(_processHandle, 1);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (!HasExited)
                TerminateProcess(_processHandle, 1);
        }
        catch (Exception ex) when (ex is Win32Exception or ObjectDisposedException)
        {
            // ignored: the process may have exited between the check and the call
        }

        _waitHandle?.Unregister(null);
        ClosePseudoConsole(_pseudoConsole);

        try { Input.Dispose(); } catch (IOException) { }
        try { Output.Dispose(); } catch (IOException) { }

        _inputWrite.Dispose();
        _outputRead.Dispose();

        if (_threadHandle != IntPtr.Zero)
            CloseHandle(_threadHandle);
        if (_processHandle != IntPtr.Zero)
            CloseHandle(_processHandle);

        _exited.TrySetResult(-1);
    }

    private void RegisterExitCallback()
    {
        var handle = new ManualResetEvent(false)
        {
            SafeWaitHandle = new SafeWaitHandle(_processHandle, ownsHandle: false)
        };

        _waitHandle = ThreadPool.RegisterWaitForSingleObject(
            handle,
            (_, _) =>
            {
                GetExitCodeProcess(_processHandle, out var code);
                _exited.TrySetResult((int)code);
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);
    }

    private static IntPtr CreateAttributeList(IntPtr pseudoConsole)
    {
        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);

        var list = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(list, 1, 0, ref size))
        {
            Marshal.FreeHGlobal(list);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed.");
        }

        if (!UpdateProcThreadAttribute(
                list,
                0,
                PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                pseudoConsole,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
        {
            DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed.");
        }

        return list;
    }

    private static IntPtr CreateEnvironmentBlock(IReadOnlyDictionary<string, string>? extra)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                merged[key] = value;
        }

        if (extra is not null)
        {
            foreach (var (key, value) in extra)
                merged[key] = value;
        }

        // Terminal-shaped defaults so the CLI renders colours and boxes the way it does in a console.
        merged.TryAdd("TERM", "xterm-256color");
        merged.TryAdd("COLORTERM", "truecolor");

        var sb = new StringBuilder();
        foreach (var (key, value) in merged)
            sb.Append(key).Append('=').Append(value).Append('\0');
        sb.Append('\0');

        return Marshal.StringToHGlobalUni(sb.ToString());
    }

    private static bool CheckSupport()
    {
        try
        {
            // Probing the export is enough: it exists from Windows 10 1809 onwards.
            var size = new COORD { X = 1, Y = 1 };
            using var dummyRead = new SafeFileHandle(IntPtr.Zero, false);
            using var dummyWrite = new SafeFileHandle(IntPtr.Zero, false);
            CreatePseudoConsole(size, dummyRead, dummyWrite, 0, out var handle);
            if (handle != IntPtr.Zero)
                ClosePseudoConsole(handle);
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
