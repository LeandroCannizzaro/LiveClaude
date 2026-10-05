using LiveClaude.Abstractions;
using LiveClaude.Core.Model;

namespace LiveClaude.Product.Cursor;

/// <summary>Builds <c>agent worker start</c> (My Machines) argv from a session entry.</summary>
public static class CursorArgs
{
    public static IReadOnlyList<string> BuildWorkerStart(SessionConfig session, AppConfig appConfig)
    {
        var options = session.Cursor;
        var args = new List<string> { "worker" };

        // Flags that belong before the subcommand.
        args.Add("--name");
        args.Add(session.Name.Trim());

        args.Add("--worker-dir");
        args.Add(session.Directory.Trim());

        foreach (var dir in options.ExtraWorkerDirs
                     .Where(d => !string.IsNullOrWhiteSpace(d))
                     .Select(d => d.Trim())
                     .Where(d => !string.Equals(d, session.Directory.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            args.Add("--worker-dir");
            args.Add(dir);
        }

        var apiKeyPath = FirstNonEmpty(options.ApiKeyPath, appConfig.Products.Cursor.ApiKeyPath);
        var authTokenFile = FirstNonEmpty(options.AuthTokenFile, appConfig.Products.Cursor.AuthTokenFile);

        if (!string.IsNullOrWhiteSpace(apiKeyPath) && File.Exists(apiKeyPath))
        {
            // Read the key at launch time so the process gets --api-key without persisting the secret
            // inside LiveClaude's config JSON.
            var key = File.ReadAllText(apiKeyPath).Trim();
            if (key.Length > 0)
            {
                args.Add("--api-key");
                args.Add(key);
            }
        }
        else if (!string.IsNullOrWhiteSpace(authTokenFile))
        {
            args.Add("--auth-token-file");
            args.Add(authTokenFile.Trim());
        }

        if (options.Debug)
            args.Add("--debug");

        args.Add("start");

        if (options.Verbose)
            args.Add("--verbose");

        if (!string.IsNullOrWhiteSpace(session.ExtraArgs))
            args.AddRange(CliArguments.Split(session.ExtraArgs));

        return args;
    }

    /// <summary>Builds <c>agent login</c> for the embedded terminal auth flow.</summary>
    public static IReadOnlyList<string> BuildLogin() => ["login"];

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
