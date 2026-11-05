namespace WabiInsight.Core.Common;

/// <summary>All timestamps are UTC. Injected so tests can control time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
