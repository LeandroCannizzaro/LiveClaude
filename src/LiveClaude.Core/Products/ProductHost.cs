using System.Reflection;
using System.Runtime.Loader;
using LiveClaude.Abstractions;

namespace LiveClaude.Core.Products;

/// <summary>
/// Finds and activates every <c>LiveClaude.Product.*.dll</c> next to the application, the same way
/// <see cref="PlatformLoader"/> loads the OS platform — no compile-time reference from Core.
/// </summary>
public static class ProductHost
{
    private static readonly object Gate = new();
    private static IReadOnlyList<IAgentProduct>? _products;
    private static int _resolverInstalled;

    /// <summary>Every product shipped with this build, ordered by display name.</summary>
    public static IReadOnlyList<IAgentProduct> All
    {
        get
        {
            if (_products is not null)
                return _products;

            lock (Gate)
                return _products ??= LoadAll();
        }
    }

    /// <summary>Installs an explicit catalogue. Used by tests.</summary>
    public static void Use(IEnumerable<IAgentProduct> products)
    {
        lock (Gate)
            _products = products.ToList();
    }

    public static IAgentProduct? TryGet(string? productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
            productId = ProductIds.Claude;

        return All.FirstOrDefault(p => string.Equals(p.Id, productId, StringComparison.OrdinalIgnoreCase));
    }

    public static IAgentProduct Get(string? productId) =>
        TryGet(productId) ?? throw new ProductNotAvailableException(
            $"No agent product is registered for id '{productId ?? ProductIds.Claude}'.");

    private static IReadOnlyList<IAgentProduct> LoadAll()
    {
        InstallSiblingResolver();

        var products = new List<IAgentProduct>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directory = AppContext.BaseDirectory;

        foreach (var path in Directory.EnumerateFiles(directory, "LiveClaude.Product.*.dll"))
        {
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                var attribute = assembly.GetCustomAttribute<LiveClaudeProductAttribute>();
                if (attribute is null)
                    continue;

                if (Activator.CreateInstance(attribute.ProductType) is not IAgentProduct product)
                {
                    throw new ProductNotAvailableException(
                        $"{attribute.ProductType.FullName} in {assembly.GetName().Name} does not implement IAgentProduct.");
                }

                if (!seen.Add(product.Id))
                    continue;

                products.Add(product);
            }
            catch (Exception ex) when (ex is not ProductNotAvailableException)
            {
                throw new ProductNotAvailableException(
                    $"Failed to load product assembly '{path}': {ex.Message}", ex);
            }
        }

        // Development / trimmed publishes may not have the files on disk yet — fall back to
        // Assembly.Load by well-known names so a single-file build still works when the product
        // assemblies were bundled.
        if (products.Count == 0)
        {
            foreach (var name in new[] { "LiveClaude.Product.Claude", "LiveClaude.Product.Cursor" })
            {
                try
                {
                    var assembly = Assembly.Load(new AssemblyName(name));
                    var attribute = assembly.GetCustomAttribute<LiveClaudeProductAttribute>();
                    if (attribute is null)
                        continue;

                    if (Activator.CreateInstance(attribute.ProductType) is IAgentProduct product && seen.Add(product.Id))
                        products.Add(product);
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    // optional
                }
            }
        }

        return products
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void InstallSiblingResolver()
    {
        if (Interlocked.Exchange(ref _resolverInstalled, 1) != 0)
            return;

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name is not { } simpleName || !simpleName.StartsWith("LiveClaude.", StringComparison.Ordinal))
                return null;

            var candidate = Path.Combine(AppContext.BaseDirectory, simpleName + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
    }
}
