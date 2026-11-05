namespace WabiInsight.Core.Bootstrap;

public enum BootstrapOutcome
{
    Completed = 0,
    AlreadyCompleted = 1,
    Disabled = 2,
    Unauthorized = 3,
    InvalidConfiguration = 4,
    SchemaNotCurrent = 5,
}

public sealed record BootstrapStatus(
    bool BootstrapCompleted,
    DateTimeOffset? BootstrapCompletedAt,
    string? BootstrapAdministratorEmail,
    string? SchemaVersion,
    bool PendingMigrations,
    bool BootstrapEnabled);

public sealed record BootstrapResult(BootstrapOutcome Outcome, IReadOnlyList<string> Errors, Guid? AdministratorId = null);

public interface IBootstrapService
{
    Task<BootstrapStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the first Platform Administrator once. Requires the initialization secret (the CLI passes
    /// the configured one). Never applies migrations; fails if the schema is not current.
    /// </summary>
    Task<BootstrapResult> InitializeAsync(string? providedSecret, CancellationToken cancellationToken = default);
}
