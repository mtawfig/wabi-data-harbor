using WabiInsight.Core.Entities.Organizations;

namespace WabiInsight.Core.Applications;

/// <summary>Application as returned to the browser: never the connection string, only that one is configured plus the display hint the administrator typed.</summary>
public sealed record ApplicationDto(
    Guid ApplicationId,
    string ApplicationCode,
    string ApplicationName,
    string ApplicationNameAr,
    string? Description,
    DatabaseType DatabaseType,
    bool IsBuiltIn,
    bool IsActive,
    bool IsApproved,
    bool ConnectionConfigured,
    string? ConnectionHint,
    DateTimeOffset? LastConnectionTestAt,
    bool? LastConnectionTestSucceeded,
    DateTimeOffset? LastMetadataRefreshAt,
    int ObjectCount,
    int ApprovedObjectCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record SaveApplicationCommand(string ApplicationCode, string ApplicationName, string ApplicationNameAr, string? Description, DatabaseType DatabaseType, string? ConnectionString, string? ConnectionHint);

public sealed record UpdateApplicationCommand(string ApplicationName, string ApplicationNameAr, string? Description, bool IsActive);

/// <summary>Outcome of a connectivity test. Never contains the connection string.</summary>
public sealed record ConnectionTestResult(bool Succeeded, string? ServerVersion, string? DatabaseName, bool? ReadOnlyVerified, IReadOnlyList<string> Warnings, string? Error, double DurationMs);

/// <summary>Organization Administrators manage the applications (data sources) of their own organization (specification §11).</summary>
public interface IApplicationService
{
    Task<IReadOnlyList<ApplicationDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default);

    Task<ApplicationDto?> GetAsync(Guid applicationId, CancellationToken cancellationToken = default);

    Task<ApplicationDto> CreateAsync(SaveApplicationCommand command, CancellationToken cancellationToken = default);

    Task<ApplicationDto> UpdateAsync(Guid applicationId, UpdateApplicationCommand command, CancellationToken cancellationToken = default);

    /// <summary>Replaces the encrypted connection string (and fingerprint). The old value is never returned.</summary>
    Task<ApplicationDto> ReplaceConnectionAsync(Guid applicationId, string connectionString, string? connectionHint, CancellationToken cancellationToken = default);

    /// <summary>Tests a connection string that has not been saved yet.</summary>
    Task<ConnectionTestResult> TestConnectionAsync(DatabaseType databaseType, string connectionString, CancellationToken cancellationToken = default);

    /// <summary>Tests the stored connection of an application and records the outcome on it.</summary>
    Task<ConnectionTestResult> TestStoredConnectionAsync(Guid applicationId, CancellationToken cancellationToken = default);

    Task<ApplicationDto> SetApprovalAsync(Guid applicationId, bool isApproved, CancellationToken cancellationToken = default);
}

/// <summary>Encrypts connection strings with a purpose-specific protector; plain text exists only in server memory.</summary>
public interface IConnectionStringProtector
{
    string Protect(string connectionString);

    string Unprotect(string payload);

    /// <summary>Keyed hash of the normalized connection string, globally unique across organizations (review A7).</summary>
    Task<string> FingerprintAsync(string connectionString, CancellationToken cancellationToken = default);
}

/// <summary>Settings for the Data Protection key ring, bound from the "DataProtection" section.</summary>
public sealed class DataProtectionOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>Directory outside the configuration database and web root. Relative paths resolve against the current directory.</summary>
    public string KeyRingPath { get; set; } = Path.Combine(".artifacts", "keys");

    public int KeyLifetimeDays { get; set; } = 90;
}
