using LiveClaude.Abstractions;
using LiveClaude.Platform.Posix.Native;

namespace LiveClaude.Platform.Posix;

/// <summary>
/// Where the IPC socket lives.
///
/// A Unix domain socket path has a hard length limit baked into <c>struct sockaddr_un</c>: 104 bytes
/// on macOS, 108 on Linux, including the terminator. That is small enough that the obvious answers
/// are wrong — <c>~/Library/Application Support/LiveClaude/run/</c> is already 50 characters before
/// the file name, and a long user name pushes it over on its own.
///
/// So the socket goes in a short, per-user directory the way tmux does it: <c>/tmp/liveclaude-&lt;uid&gt;</c>,
/// created 0700. The sticky bit on /tmp keeps other users from removing or replacing it, and a
/// directory nobody else can traverse is what actually protects the socket — the earlier concern
/// about /tmp was about a world-readable socket sitting directly in it, not about /tmp itself.
/// </summary>
public static class PosixRuntimeDirectory
{
    /// <summary>The longest a socket path may be. macOS is the tighter of the two, so it sets the bar.</summary>
    public const int MaxSocketPathLength = 104;

    /// <summary>
    /// The short per-user directory. Used by macOS always, and by Linux when XDG_RUNTIME_DIR is not
    /// set — under a system unit, or over ssh.
    /// </summary>
    public static string Shared => $"/tmp/liveclaude-{PosixUser.EffectiveUserId}";

    /// <summary>
    /// Creates the directory and makes sure it is ours and private.
    ///
    /// The symlink check is the point: anything in /tmp is a place another user may have got to
    /// first, and following a planted symlink would put the socket somewhere they can read.
    /// </summary>
    public static void Prepare(string directory)
    {
        if (Directory.Exists(directory) &&
            new DirectoryInfo(directory).ResolveLinkTarget(returnFinalTarget: false) is not null)
        {
            throw new IOException(
                $"{directory} is a symbolic link. Refusing to put the supervisor socket there — remove it and try again.");
        }

        Directory.CreateDirectory(directory);
        Libc.chmod(directory, Convert.ToUInt32("700", 8));
    }

    /// <summary>
    /// Rejects a path the kernel would truncate, with the length in the message. The alternative is
    /// an ArgumentOutOfRangeException naming a parameter, which says nothing about what to change.
    /// </summary>
    public static void ValidateSocketPath(string path)
    {
        if (path.Length <= MaxSocketPathLength)
            return;

        throw new IOException(
            $"The supervisor socket path is {path.Length} characters, and a Unix domain socket allows " +
            $"at most {MaxSocketPathLength}: {path}. Set {PathLayout.RootOverrideVariable} to a shorter directory.");
    }
}
