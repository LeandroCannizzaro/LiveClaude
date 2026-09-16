using System.Collections.Concurrent;
using LiveClaude.Platform.Posix.Native;

namespace LiveClaude.Platform.Posix.Pty;

/// <summary>
/// Collects children started with posix_spawn.
///
/// The .NET runtime reaps only the processes it started itself, so a child spawned here is nobody's
/// responsibility: without this it stays a zombie for the life of the supervisor, and every restart
/// leaves another one behind. <see cref="System.Diagnostics.Process"/> cannot help — it refuses to
/// wait on a process it did not start.
///
/// One background thread polls every child with WNOHANG. Polling rather than a SIGCHLD handler
/// because installing a signal handler in a .NET process fights with the runtime's own, and the
/// handful of children involved here makes the difference immeasurable.
/// </summary>
internal static class ChildReaper
{
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<int>> Watched = new();
    private static readonly object Gate = new();
    private static Thread? _thread;

    /// <summary>Starts watching a pid. The task completes with the child's exit code.</summary>
    public static Task<int> Watch(int pid)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Watched[pid] = completion;
        EnsureRunning();
        return completion.Task;
    }

    /// <summary>Stops watching a pid that was never spawned, so a failed start leaks nothing.</summary>
    public static void Forget(int pid) => Watched.TryRemove(pid, out _);

    private static void EnsureRunning()
    {
        lock (Gate)
        {
            if (_thread is not null)
                return;

            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "LiveClaude child reaper"
            };

            _thread.Start();
        }
    }

    private static void Loop()
    {
        while (true)
        {
            foreach (var (pid, completion) in Watched)
            {
                var result = Libc.waitpid(pid, out var status, Libc.WNOHANG);

                // 0 means alive; a negative result means it is not ours to wait for any more, which
                // in practice only happens if something else already reaped it.
                if (result == 0)
                    continue;

                Watched.TryRemove(pid, out _);
                completion.TrySetResult(result > 0 ? Libc.ExitCodeFrom(status) : -1);
            }

            Thread.Sleep(Watched.IsEmpty ? 250 : 50);
        }
    }
}
