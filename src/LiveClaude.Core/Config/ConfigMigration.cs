using System.Text.Json;
using LiveClaude.Core.Model;
using LiveClaude.Core.Products;

namespace LiveClaude.Core.Config;

/// <summary>Brings older configuration files up to the current schema.</summary>
public static class ConfigMigration
{
    public const int CurrentVersion = 2;

    /// <summary>
    /// Mutates <paramref name="config"/> in place. <paramref name="rawJson"/> is the file that was
    /// just deserialized, so v1 top-level fields that are no longer on the model can still be read.
    /// </summary>
    public static bool Apply(AppConfig config, string? rawJson = null)
    {
        var changed = false;

        if (config.Version < 2)
        {
            if (!string.IsNullOrWhiteSpace(rawJson))
                ApplyV1Aliases(config, rawJson);

            foreach (var session in config.Sessions)
            {
                if (string.IsNullOrWhiteSpace(session.ProductId))
                {
                    session.ProductId = ProductIds.Claude;
                    changed = true;
                }
            }

            config.Version = 2;
            changed = true;
        }

        foreach (var session in config.Sessions)
        {
            if (string.IsNullOrWhiteSpace(session.ProductId))
            {
                session.ProductId = ProductIds.Claude;
                changed = true;
            }
        }

        return changed;
    }

    private static void ApplyV1Aliases(AppConfig config, string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;

            if (config.Products.Claude.Path is null &&
                root.TryGetProperty("ClaudePath", out var claudePath) &&
                claudePath.ValueKind == JsonValueKind.String)
            {
                config.Products.Claude.Path = claudePath.GetString();
            }

            if (root.TryGetProperty("TrackEnvironments", out var track) &&
                (track.ValueKind is JsonValueKind.True or JsonValueKind.False))
            {
                config.Products.Claude.TrackEnvironments = track.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // best effort — the main deserialize already succeeded
        }
    }
}
