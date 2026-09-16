using System.Runtime.InteropServices;

namespace LiveClaude.Platform.Windows;

/// <summary>
/// The supervisor is a windowed executable on purpose: a process that owns a console hands its own
/// console to every child it starts, which makes Windows ignore the pseudo-console attribute and
/// breaks the embedded terminal. For one-shot CLI commands (install, status, …) it borrows the
/// caller's console instead, so output still lands in the terminal the user typed in.
/// </summary>
public static class ConsoleBridge
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    public static bool IsAttached { get; private set; }

    /// <summary>Attaches to the calling terminal, or opens a console when there is none.</summary>
    public static void AttachToParent(bool allocateIfMissing = false)
    {
        if (GetConsoleWindow() != IntPtr.Zero)
        {
            IsAttached = true;
            return;
        }

        if (!AttachConsole(AttachParentProcess) && allocateIfMissing)
            AllocConsole();

        IsAttached = GetConsoleWindow() != IntPtr.Zero;
        if (!IsAttached)
            return;

        // The runtime captured its standard handles before the console existed; reopen them.
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        Console.SetOut(stdout);
        Console.SetError(stderr);
    }
}
