using LiveClaude.Core.Model;

namespace LiveClaude.Core.Supervision;

/// <summary>Exponential restart backoff that resets once an instance has stayed up long enough.</summary>
public sealed class BackoffPolicy
{
    private readonly BackoffSettings _settings;
    private int _consecutiveFailures;

    public BackoffPolicy(BackoffSettings settings) => _settings = settings;

    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>Records an exit and returns the delay before the next start attempt.</summary>
    public TimeSpan OnExit(TimeSpan uptime)
    {
        if (uptime >= TimeSpan.FromSeconds(Math.Max(1, _settings.ResetAfterHealthySeconds)))
            _consecutiveFailures = 0;

        _consecutiveFailures++;

        var seconds = _settings.InitialSeconds * Math.Pow(
            Math.Max(1.0, _settings.Multiplier),
            Math.Max(0, _consecutiveFailures - 1));

        return TimeSpan.FromSeconds(Math.Min(seconds, Math.Max(_settings.InitialSeconds, _settings.MaxSeconds)));
    }

    /// <summary>True when the instance has exhausted its restart budget.</summary>
    public bool GiveUp => _settings.MaxRestarts > 0 && _consecutiveFailures >= _settings.MaxRestarts;

    public void Reset() => _consecutiveFailures = 0;
}
