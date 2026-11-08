using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Organizations;

public enum DatabaseType
{
    SqlServer = 1,
    Oracle = 2,
    PlatformInsights = 3,
}

/// <summary>
/// A business application (data source) owned by one organization. The built-in Platform Insights
/// application is one tenant-owned row per organization with no connection string (specification §11, §12).
/// </summary>
public class Application : AuditableEntity, ITenantOwned
{
    public const string PlatformInsightsCode = "WI-INSIGHTS";

    public Guid ApplicationId { get; set; }

    public Guid OrganizationId { get; set; }

    public string ApplicationName { get; set; } = string.Empty;

    public string ApplicationNameAr { get; set; } = string.Empty;

    public string ApplicationCode { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DatabaseType DatabaseType { get; set; }

    /// <summary>Data Protection payload (P6.1). Always null for the built-in application.</summary>
    public string? EncryptedConnectionString { get; set; }

    /// <summary>Keyed hash of the normalized connection string; globally unique so two organizations cannot register the same source.</summary>
    public string? ConnectionFingerprint { get; set; }

    public bool IsBuiltIn { get; set; }

    public string? BuiltInCode { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsApproved { get; set; }

    public Guid? CreatedByUserId { get; set; }

    /// <summary>Server/database hint typed by the administrator; shown instead of the connection string.</summary>
    public string? ConnectionHint { get; set; }

    /// <summary>
    /// Example questions shown on the Ask page for this application, written by the organization's administrators
    /// (JSON array of <c>{ "en": …, "ar": … }</c>, see <see cref="WabiInsight.Core.Applications.ApplicationExamples"/>). The product ships none
    /// for business applications: every organization writes questions its own data answers.
    /// </summary>
    public string ExampleQuestionsJson { get; set; } = "[]";

    public DateTimeOffset? LastConnectionTestAt { get; set; }

    public bool? LastConnectionTestSucceeded { get; set; }

    public DateTimeOffset? LastMetadataRefreshAt { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}
