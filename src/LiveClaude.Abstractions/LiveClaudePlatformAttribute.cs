namespace LiveClaude.Abstractions;

/// <summary>
/// Marks the <see cref="IPlatform"/> implementation a platform assembly provides. One per assembly;
/// <see cref="PlatformLoader"/> reads it instead of scanning every exported type.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class LiveClaudePlatformAttribute : Attribute
{
    public LiveClaudePlatformAttribute(Type platformType)
    {
        PlatformType = platformType;
    }

    public Type PlatformType { get; }
}

/// <summary>Thrown when the platform assembly for the running OS is missing or unusable.</summary>
public sealed class PlatformNotAvailableException : Exception
{
    public PlatformNotAvailableException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
