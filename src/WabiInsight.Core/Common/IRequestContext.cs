namespace WabiInsight.Core.Common;

/// <summary>Request metadata for audit records, supplied by the host (HTTP) or a null implementation (CLI, tests).</summary>
public interface IRequestContext
{
    string? CorrelationId { get; }

    string? IpAddress { get; }
}

public sealed class NullRequestContext : IRequestContext
{
    public string? CorrelationId => null;

    public string? IpAddress => null;
}
