namespace WabiInsight.Core.Audit;

/// <summary>Well-known audit event codes. Stable strings; never localized.</summary>
public static class AuditEvents
{
    public const string BootstrapCompleted = "Bootstrap.Completed";
    public const string LoginSucceeded = "Auth.LoginSucceeded";
    public const string LoginFailed = "Auth.LoginFailed";
    public const string LoginDenied = "Auth.LoginDenied";
    public const string AccountLockedOut = "Auth.LockedOut";
    public const string Logout = "Auth.Logout";
    public const string PasswordChanged = "Auth.PasswordChanged";
    public const string AdministratorPasswordReset = "Administrator.PasswordReset";
    public const string AdministratorCreated = "Administrator.Created";
    public const string EmailQueued = "Email.Queued";
    public const string EmailFailed = "Email.Failed";
}

/// <summary>Appends an audit record attributed to the current principal (or anonymous). Never writes secrets.</summary>
public interface IAuditWriter
{
    /// <summary>Adds the record to the current unit of work; the caller's SaveChanges persists it with the business change.</summary>
    void Append(string eventType, string? entityType = null, string? entityId = null, object? details = null, Guid? organizationId = null);

    /// <summary>Persists a record immediately (for events without an accompanying business change, e.g. failed login).</summary>
    Task AppendAndSaveAsync(string eventType, string? entityType = null, string? entityId = null, object? details = null, Guid? organizationId = null, CancellationToken cancellationToken = default);
}
