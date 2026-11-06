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
    public const string AdministratorActivated = "Administrator.Activated";
    public const string AdministratorDeactivated = "Administrator.Deactivated";
    public const string OrganizationCreated = "Organization.Created";
    public const string OrganizationUpdated = "Organization.Updated";

    public const string HistoryDeleted = "History.Deleted";
    public const string OrganizationStatusChanged = "Organization.StatusChanged";
    public const string OrganizationUserCreated = "OrganizationUser.Created";
    public const string OrganizationUserPasswordReissued = "OrganizationUser.PasswordReissued";
    public const string OrganizationUserUpdated = "OrganizationUser.Updated";
    public const string OrganizationUserStatusChanged = "OrganizationUser.StatusChanged";
    public const string OrganizationUserRolesChanged = "OrganizationUser.RolesChanged";
    public const string DepartmentSaved = "Department.Saved";
    public const string SectionSaved = "Section.Saved";
    public const string RoleSaved = "Role.Saved";
    public const string RoleDeleted = "Role.Deleted";
    public const string RoleTemplateSaved = "RoleTemplate.Saved";
    public const string EmailQueued = "Email.Queued";
    public const string EmailFailed = "Email.Failed";
    public const string TicketCreated = "SupportTicket.Created";
    public const string TicketViewedByPlatform = "SupportTicket.ViewedByPlatform";
    public const string TicketStatusChanged = "SupportTicket.StatusChanged";
    public const string TicketAssigned = "SupportTicket.Assigned";
    public const string TicketCommentAdded = "SupportTicket.CommentAdded";
    public const string TicketAttachmentAdded = "SupportTicket.AttachmentAdded";
    public const string TicketAttachmentDownloaded = "SupportTicket.AttachmentDownloaded";
    public const string SupportAccessProposed = "SupportAccess.Proposed";
    public const string SupportAccessApproved = "SupportAccess.Approved";
    public const string SupportAccessDeclined = "SupportAccess.Declined";
    public const string SupportAccessRevoked = "SupportAccess.Revoked";
    public const string SupportAccessExpired = "SupportAccess.Expired";
    public const string SupportAccessCompleted = "SupportAccess.Completed";
    public const string SupportSessionStarted = "SupportAccess.SessionStarted";
    public const string SupportSessionEnded = "SupportAccess.SessionEnded";
    public const string SupportOperationExecuted = "SupportAccess.OperationExecuted";
    public const string SupportOperationDenied = "SupportAccess.OperationDenied";
    public const string ApplicationCreated = "Application.Created";
    public const string ApplicationUpdated = "Application.Updated";
    public const string ApplicationConnectionReplaced = "Application.ConnectionReplaced";
    public const string ApplicationConnectionTested = "Application.ConnectionTested";
    public const string ApplicationApprovalChanged = "Application.ApprovalChanged";
    public const string MetadataRefreshed = "Metadata.Refreshed";
    public const string MetadataObjectUpdated = "Metadata.ObjectUpdated";
    public const string MetadataColumnUpdated = "Metadata.ColumnUpdated";
    public const string PermissionChanged = "Permission.Changed";
    public const string RowMappingChanged = "Permission.RowMappingChanged";
    public const string InsightsCatalogSynced = "Insights.CatalogSynced";
    public const string InsightsObjectUpdated = "Insights.ObjectUpdated";
    public const string InsightsColumnUpdated = "Insights.ColumnUpdated";
    public const string InsightsMetricChanged = "Insights.MetricChanged";
    public const string InsightsMetricQueried = "Insights.MetricQueried";
    public const string InsightsMetricGrantChanged = "Insights.MetricGrantChanged";
    public const string AiProviderChanged = "Ai.ProviderChanged";
    public const string AiProviderSelected = "Ai.ProviderSelected";
    public const string AiAllowListChanged = "Ai.AllowListChanged";
    public const string ShareCreated = "Share.Created";
    public const string ShareRevoked = "Share.Revoked";
    public const string ShareEmailSent = "Share.EmailSent";
    public const string ShareEmailBlocked = "Share.EmailBlocked";
}

/// <summary>Appends an audit record attributed to the current principal (or anonymous). Never writes secrets.</summary>
public interface IAuditWriter
{
    /// <summary>Adds the record to the current unit of work; the caller's SaveChanges persists it with the business change.</summary>
    void Append(string eventType, string? entityType = null, string? entityId = null, object? details = null, Guid? organizationId = null);

    /// <summary>Persists a record immediately (for events without an accompanying business change, e.g. failed login).</summary>
    Task AppendAndSaveAsync(string eventType, string? entityType = null, string? entityId = null, object? details = null, Guid? organizationId = null, CancellationToken cancellationToken = default);
}
