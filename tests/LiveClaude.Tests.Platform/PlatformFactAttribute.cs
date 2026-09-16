using System.Runtime.InteropServices;
using Xunit;

namespace LiveClaude.Tests.Platform;

/// <summary>
/// Marks a test that only means something on some operating systems, and skips it on the others.
///
/// xunit reports a skip, not a pass, so a suite that runs nothing on Linux is visibly a suite that
/// ran nothing — which is the point: a platform test project whose tests all silently vanish off
/// their own platform is worse than not having it.
/// </summary>
public sealed class PlatformFactAttribute : FactAttribute
{
    public PlatformFactAttribute(params string[] platforms)
    {
        var current = Current;

        if (!platforms.Contains(current, StringComparer.OrdinalIgnoreCase))
            Skip = $"Only applies to {string.Join(", ", platforms)}; this is {current}.";
    }

    internal static string Current =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "unknown";
}
