using System.Runtime.InteropServices;

namespace LiveClaude.Platform.Posix.Native;

/// <summary>
/// The libc calls LiveClaude needs. Everything here is POSIX, but several constants are not: the
/// values of O_NOCTTY, TIOCSWINSZ and POSIX_SPAWN_SETSID differ between Linux and macOS, and using
/// the wrong one fails in ways that look like anything but a wrong constant — a resize that kills the
/// process, or a child that never receives Ctrl+C.
/// </summary>
internal static partial class Libc
{
    private const string Lib = "libc";

    internal static bool IsMacOS => OperatingSystem.IsMacOS();

    // ---- open(2) flags ----

    internal const int O_RDWR = 0x0002;

    /// <summary>Linux: 0400 octal. macOS: 0x20000.</summary>
    internal static int O_NOCTTY => IsMacOS ? 0x20000 : 0x0100;

    // ---- posix_spawn attributes ----

    /// <summary>
    /// Put the child in its own session. This is what makes the pty slave become its controlling
    /// terminal when the file actions open it, and without a controlling terminal writing 0x03 to the
    /// master is just a byte — the child never gets SIGINT, and a Remote Control server is killed
    /// instead of being asked to shut down.
    /// </summary>
    internal static short POSIX_SPAWN_SETSID => IsMacOS ? (short)0x0400 : (short)0x80;

    // ---- ioctl ----

    /// <summary>TIOCSWINSZ: Linux 0x5414, macOS _IOW('t', 103, struct winsize).</summary>
    internal static ulong TIOCSWINSZ => IsMacOS ? 0x80087467UL : 0x5414UL;

    // ---- signals ----

    internal const int SIGINT = 2;
    internal const int SIGTERM = 15;
    internal const int SIGKILL = 9;

    // ---- waitpid ----

    internal const int WNOHANG = 1;

    // ---- errno ----

    internal const int EINTR = 4;

    /// <summary>
    /// Reading a pty master whose slave has been closed gives EIO on Linux, not end of file. Treating
    /// it as an error rather than as EOF is a classic way to end up logging a spurious failure every
    /// time a server stops normally.
    /// </summary>
    internal const int EIO = 5;

    internal const int EAGAIN = 11;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WinSize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int posix_openpt(int flags);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int grantpt(int fd);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int unlockpt(int fd);

    /// <summary>
    /// Returns a pointer into a static buffer, so the name must be copied before anything else on
    /// this process calls it again. macOS never gained ptsname_r, which is why the thread-safe
    /// variant is not used here; callers hold a lock instead.
    /// </summary>
    [LibraryImport(Lib, SetLastError = true)]
    internal static partial IntPtr ptsname(int fd);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int close(int fd);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial nint read(int fd, IntPtr buffer, nuint count);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial nint write(int fd, IntPtr buffer, nuint count);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int ioctl(int fd, ulong request, ref WinSize size);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int kill(int pid, int sig);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int killpg(int pgid, int sig);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int waitpid(int pid, out int status, int options);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int getpgid(int pid);

    [LibraryImport(Lib)]
    internal static partial uint geteuid();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int access(string path, int mode);

    /// <summary>X_OK: can this process execute the file?</summary>
    internal const int X_OK = 1;

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int chmod(string path, uint mode);

    // ---- posix_spawn ----
    //
    // posix_spawn_file_actions_t and posix_spawnattr_t are opaque and differently sized per platform:
    // on glibc they are structs held inline (80 and 336 bytes), on macOS they are a single pointer.
    // Passing a pointer to a generously sized, zeroed buffer satisfies both.

    internal const int SpawnStateSize = 512;

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int posix_spawn_file_actions_init(IntPtr fileActions);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int posix_spawn_file_actions_destroy(IntPtr fileActions);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int posix_spawn_file_actions_addopen(
        IntPtr fileActions, int fileDescriptor, string path, int flags, uint mode);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int posix_spawn_file_actions_adddup2(
        IntPtr fileActions, int fileDescriptor, int newFileDescriptor);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int posix_spawnattr_init(IntPtr attributes);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int posix_spawnattr_destroy(IntPtr attributes);

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int posix_spawnattr_setflags(IntPtr attributes, short flags);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int posix_spawn(
        out int pid,
        string path,
        IntPtr fileActions,
        IntPtr attributes,
        IntPtr argv,
        IntPtr envp);

    // ---- wait status decoding ----
    //
    // The macros from <sys/wait.h>, which C hides and P/Invoke cannot.

    internal static bool WIfExited(int status) => (status & 0x7F) == 0;

    internal static int WExitStatus(int status) => (status >> 8) & 0xFF;

    internal static bool WIfSignaled(int status) => ((sbyte)(((status & 0x7F) + 1) >> 1)) > 0;

    internal static int WTermSig(int status) => status & 0x7F;

    /// <summary>
    /// The exit code to report for a child. A process killed by a signal has no exit status, and
    /// 128 + signal is the convention every shell uses — worth keeping so a log line reading
    /// "exited with code 130" means the same thing here as it does in a terminal.
    /// </summary>
    internal static int ExitCodeFrom(int status) =>
        WIfExited(status) ? WExitStatus(status) :
        WIfSignaled(status) ? 128 + WTermSig(status) : -1;
}
