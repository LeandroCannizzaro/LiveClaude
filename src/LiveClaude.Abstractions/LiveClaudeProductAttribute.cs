namespace LiveClaude.Abstractions;

/// <summary>
/// Marks the <see cref="IAgentProduct"/> implementation a product assembly provides. One per
/// assembly; <c>ProductHost</c> reads it instead of scanning every exported type.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class LiveClaudeProductAttribute : Attribute
{
    public LiveClaudeProductAttribute(Type productType)
    {
        ProductType = productType;
    }

    public Type ProductType { get; }
}

/// <summary>Thrown when a product assembly is missing, unusable, or unknown by id.</summary>
public sealed class ProductNotAvailableException : Exception
{
    public ProductNotAvailableException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
