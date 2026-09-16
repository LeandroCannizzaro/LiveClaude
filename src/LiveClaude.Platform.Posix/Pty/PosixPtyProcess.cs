using System.Runtime.InteropServices;
using System.Text;
using LiveClaude.Abstractions;
using LiveClaude.Platform.Posix.Native;

namespace LiveClaude.Platform.Posix.Pty;

/// <summary>
/// A child process hosted in a pty pair, the POSIX counterpart of a Windows pseudo console. The CLI
/// gets a real terminal, so its full-screen prompts — workspace trust, the Remote Control
/// confirmation, <c>/login</c> — render and can be answered.
/// </summary>
public sealed class PosixPtyProcess : IPtyProcess
{
    // ptsname returns a pointer into a static buffer, and macOS never gained the _r variant.
    private static readonly object PtsNameGate = new();

    private readonly int _masterFd;
    private readonly PtyMasterStream _stream;
    private readonly Task<int> _exited;
    private int _disposed;

    private PosixPtyProcess(int pid, int masterFd, PtyMasterStream stream, Task<int> exited)
    {
        ProcessId = pid;
        _masterFd = masterFd;
        _stream = stream;
        _exited = exited;
    }

    public int ProcessId { get; }

    public Stream Output => _stream;

    public Task<int> Exited => _exited;

    public bool HasExited => _exited.IsCompleted;

    public static PosixPtyProcess Start(PtyOptions options)
    {
        var master = Libc.posix_openpt(Libc.O_RDWR | Libc.O_NOCTTY);
        if (master < 0)
            throw new IOException($"posix_openpt failed (errno {Marshal.GetLastPInvokeError()}).");

        try
        {
            if (Libc.grantpt(master) != 0)
                throw new IOException($"grantpt failed (errno {Marshal.GetLastPInvokeError()}).");

            if (Libc.unlockpt(master) != 0)
                throw new IOException($"unlockpt failed (errno {Marshal.GetLastPInvokeError()}).");

            string slavePath;
            lock (PtsNameGate)
            {
                var pointer = Libc.ptsname(master);
                if (pointer == IntPtr.Zero)
                    throw new IOException($"ptsname failed (errno {Marshal.GetLastPInvokeError()}).");

                slavePath = Marshal.PtrToStringUTF8(pointer)
                            ?? throw new IOException("ptsname returned a name that could not be read.");
            }

            var pid = Spawn(options, slavePath);
            SetWindowSize(master, options.Columns, options.Rows);

            var stream = new PtyMasterStream(master);
            return new PosixPtyProcess(pid, master, stream, ChildReaper.Watch(pid));
        }
        catch
        {
            Libc.close(master);
            throw;
        }
    }

    /// <summary>
    /// Starts the child with posix_spawn.
    ///
    /// Deliberately not fork/forkpty: forking a multi-threaded .NET process leaves the child holding
    /// locks no thread will ever release, and anything managed running before exec can deadlock.
    /// posix_spawn does the whole open/dup2/exec sequence inside libc, where no managed code runs.
    ///
    /// SETSID puts the child in a new session; opening the slave as descriptor 0 without O_NOCTTY
    /// then makes it the controlling terminal. That is what makes Ctrl+C work: the 0x03 byte written
    /// to the master reaches the line discipline, which sends SIGINT to the foreground process group.
    /// Without a controlling terminal it would be an ordinary byte and the server would have to be
    /// killed — which is exactly what leaves dead bridge environments behind.
    /// </summary>
    private static int Spawn(PtyOptions options, string slavePath)
    {
        var fileActions = Marshal.AllocHGlobal(Libc.SpawnStateSize);
        var attributes = Marshal.AllocHGlobal(Libc.SpawnStateSize);
        var argv = IntPtr.Zero;
        var envp = IntPtr.Zero;

        // The opaque spawn types are structs on glibc and pointers on macOS; zeroing a generous
        // buffer is valid for both.
        for (var i = 0; i < Libc.SpawnStateSize; i++)
        {
            Marshal.WriteByte(fileActions, i, 0);
            Marshal.WriteByte(attributes, i, 0);
        }

        var actionsReady = false;
        var attributesReady = false;

        try
        {
            Check(Libc.posix_spawn_file_actions_init(fileActions), "posix_spawn_file_actions_init");
            actionsReady = true;

            Check(Libc.posix_spawnattr_init(attributes), "posix_spawnattr_init");
            attributesReady = true;

            Check(Libc.posix_spawnattr_setflags(attributes, Libc.POSIX_SPAWN_SETSID), "posix_spawnattr_setflags");

            // No O_NOCTTY here, on purpose: this open is what acquires the controlling terminal.
            Check(Libc.posix_spawn_file_actions_addopen(fileActions, 0, slavePath, Libc.O_RDWR, 0),
                "posix_spawn_file_actions_addopen");
            Check(Libc.posix_spawn_file_actions_adddup2(fileActions, 0, 1), "posix_spawn_file_actions_adddup2 (stdout)");
            Check(Libc.posix_spawn_file_actions_adddup2(fileActions, 0, 2), "posix_spawn_file_actions_adddup2 (stderr)");

            argv = BuildStringArray([options.ExecutablePath, .. options.Arguments]);
            envp = BuildStringArray(BuildEnvironment(options));

            // posix_spawn has no "working directory" action that is portable, so the directory is
            // changed around the call. The lock keeps two concurrent starts from seeing each other's
            // directory — process-wide state is the price of not forking.
            lock (PtsNameGate)
            {
                var previous = Directory.GetCurrentDirectory();
                var moved = false;

                try
                {
                    if (!string.IsNullOrWhiteSpace(options.WorkingDirectory) && Directory.Exists(options.WorkingDirectory))
                    {
                        Directory.SetCurrentDirectory(options.WorkingDirectory);
                        moved = true;
                    }

                    var result = Libc.posix_spawn(out var pid, options.ExecutablePath, fileActions, attributes, argv, envp);

                    if (result != 0)
                    {
                        ChildReaper.Forget(pid);
                        throw new IOException($"posix_spawn failed for '{options.ExecutablePath}' (errno {result}).");
                    }

                    return pid;
                }
                finally
                {
                    if (moved)
                        Directory.SetCurrentDirectory(previous);
                }
            }
        }
        finally
        {
            if (actionsReady)
                Libc.posix_spawn_file_actions_destroy(fileActions);
            if (attributesReady)
                Libc.posix_spawnattr_destroy(attributes);

            Marshal.FreeHGlobal(fileActions);
            Marshal.FreeHGlobal(attributes);
            FreeStringArray(argv);
            FreeStringArray(envp);
        }
    }

    public void Resize(short columns, short rows)
    {
        if (_disposed != 0 || HasExited)
            return;

        SetWindowSize(_masterFd, columns, rows);
    }

    public void Write(string text)
    {
        if (HasExited)
            return;

        var bytes = Encoding.UTF8.GetBytes(text);
        _stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Asks the server to stop the way a person would.
    ///
    /// Writing 0x03 goes through the line discipline, which signals the foreground process group —
    /// the right thing, because the CLI may have started children of its own. If the terminal has
    /// somehow been put in raw mode the byte would be delivered as data instead, so the process
    /// group is signalled directly as well; a process that already took the first route is gone by
    /// then and the second call simply fails.
    /// </summary>
    public void SendInterrupt()
    {
        if (HasExited)
            return;

        try
        {
            Write("\x03");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // the master is already closed
        }

        var group = Libc.getpgid(ProcessId);
        if (group > 0)
            Libc.killpg(group, Libc.SIGINT);
    }

    public void Kill()
    {
        if (_disposed != 0 || HasExited)
            return;

        // The whole group: the CLI spawns children, and killing only the leader orphans them.
        var group = Libc.getpgid(ProcessId);

        if (group > 0)
            Libc.killpg(group, Libc.SIGKILL);
        else
            Libc.kill(ProcessId, Libc.SIGKILL);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (!HasExited)
        {
            var group = Libc.getpgid(ProcessId);
            if (group > 0)
                Libc.killpg(group, Libc.SIGKILL);
            else
                Libc.kill(ProcessId, Libc.SIGKILL);
        }

        _stream.Dispose();
    }

    private static void SetWindowSize(int fd, short columns, short rows)
    {
        var size = new Libc.WinSize
        {
            ws_col = (ushort)Math.Max((short)20, columns),
            ws_row = (ushort)Math.Max((short)5, rows)
        };

        Libc.ioctl(fd, Libc.TIOCSWINSZ, ref size);
    }

    private static IEnumerable<string> BuildEnvironment(PtyOptions options)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                merged[key] = value;
        }

        if (options.Environment is not null)
        {
            foreach (var (key, value) in options.Environment)
                merged[key] = value;
        }

        // Terminal-shaped defaults so the CLI renders colours and boxes the way it does in a terminal.
        merged.TryAdd("TERM", "xterm-256color");
        merged.TryAdd("COLORTERM", "truecolor");

        return merged.Select(pair => $"{pair.Key}={pair.Value}");
    }

    /// <summary>Marshals a string list into the NULL-terminated char*[] the exec family takes.</summary>
    private static IntPtr BuildStringArray(IEnumerable<string> values)
    {
        var items = values.ToArray();
        var array = Marshal.AllocHGlobal(IntPtr.Size * (items.Length + 1));

        for (var i = 0; i < items.Length; i++)
            Marshal.WriteIntPtr(array, i * IntPtr.Size, Marshal.StringToHGlobalAnsi(items[i]));

        Marshal.WriteIntPtr(array, items.Length * IntPtr.Size, IntPtr.Zero);
        return array;
    }

    private static void FreeStringArray(IntPtr array)
    {
        if (array == IntPtr.Zero)
            return;

        for (var i = 0; ; i++)
        {
            var item = Marshal.ReadIntPtr(array, i * IntPtr.Size);
            if (item == IntPtr.Zero)
                break;

            Marshal.FreeHGlobal(item);
        }

        Marshal.FreeHGlobal(array);
    }

    private static void Check(int result, string call)
    {
        // These return an errno directly rather than setting the global one.
        if (result != 0)
            throw new IOException($"{call} failed (errno {result}).");
    }
}

/// <summary>Starts pty-hosted processes on Linux and macOS.</summary>
public sealed class PosixPtyFactory : IPtyFactory
{
    /// <summary>Every supported POSIX system has ptys; there is no version to check for.</summary>
    public bool IsSupported => true;

    /// <summary>
    /// Always false. The Windows rule this exists for — a console owner hands its console to every
    /// child, and the pseudo console is then ignored — has no POSIX equivalent: the pty slave becomes
    /// the child's controlling terminal whether or not the parent has one.
    /// </summary>
    public bool HostOwnsConsole => false;

    public IPtyProcess Start(PtyOptions options) => PosixPtyProcess.Start(options);
}
