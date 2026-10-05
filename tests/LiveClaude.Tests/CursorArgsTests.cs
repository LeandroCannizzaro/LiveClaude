using LiveClaude.Core.Model;
using LiveClaude.Product.Cursor;
using Xunit;

namespace LiveClaude.Tests;

public class CursorArgsTests
{
    [Fact]
    public void WorkerStartUsesNameAndPrimaryDirectory()
    {
        var session = new SessionConfig
        {
            ProductId = "cursor",
            Name = "my-devbox",
            Directory = @"C:\repos\app"
        };

        var args = CursorArgs.BuildWorkerStart(session, new AppConfig());

        Assert.Equal("worker", args[0]);
        Assert.Contains("--name", args);
        Assert.Contains("my-devbox", args);
        Assert.Contains("--worker-dir", args);
        Assert.Contains(@"C:\repos\app", args);
        Assert.Contains("start", args);
    }

    [Fact]
    public void ExtraWorkerDirsAreRepeated()
    {
        var session = new SessionConfig
        {
            Name = "box",
            Directory = @"C:\repos\app",
            Cursor = new CursorSessionOptions
            {
                ExtraWorkerDirs = [@"C:\repos\infra"],
                Verbose = true
            }
        };

        var args = CursorArgs.BuildWorkerStart(session, new AppConfig());
        Assert.Equal(2, args.Count(a => a == "--worker-dir"));
        Assert.Contains(@"C:\repos\infra", args);
        Assert.Contains("--verbose", args);
    }
}
