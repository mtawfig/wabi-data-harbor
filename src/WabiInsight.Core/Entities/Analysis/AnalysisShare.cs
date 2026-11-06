using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Analysis;

/// <summary>Only VIEW exists in this version: a recipient reads stored results and files, never re-runs, exports, edits or deletes.</summary>
public enum ShareAccessLevel
{
    View = 1,
}

/// <summary>
/// A saved analysis shared with another active user of the same organization (specification §24). The share is
/// tenant-owned, carries an optional expiry and is revoked by setting <see cref="RevokedAt"/>; the row stays for audit.
/// Cross-organization sharing does not exist: owner and recipient share one OrganizationId by construction.
/// </summary>
public class AnalysisShare : ITenantOwned
{
    public Guid ShareId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid HistoryId { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid RecipientUserId { get; set; }

    public ShareAccessLevel Permission { get; set; } = ShareAccessLevel.View;

    public string? Message { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? LastViewedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public AnalysisHistory? History { get; set; }
}

public enum EmailDraftEventKind
{
    /// <summary>The model wrote an email draft as part of an answer; nothing left the platform.</summary>
    Drafted = 1,

    /// <summary>The owner sent the analysis by email through the configured provider.</summary>
    Sent = 2,

    /// <summary>A send was refused by the classification rules (Restricted data to an external recipient).</summary>
    Blocked = 3,
}

/// <summary>
/// Append-only record distinguishing email drafts from emails actually sent (specification §12.3, §24, review B11):
/// who, which version, how many recipients (internal and external), the data classification and whether the owner
/// gave the explicit disclosure approval a Confidential send requires. Bodies are never stored.
/// </summary>
public class EmailDraftEvent : ITenantOwned
{
    public Guid EventId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid UserId { get; set; }

    public Guid HistoryId { get; set; }

    public Guid VersionId { get; set; }

    public EmailDraftEventKind Kind { get; set; }

    public DataClassification DataClassification { get; set; }

    public int InternalRecipientCount { get; set; }

    public int ExternalRecipientCount { get; set; }

    /// <summary>Recipient addresses of a sent or blocked email, comma separated, for audit.</summary>
    public string? Recipients { get; set; }

    public bool AttachedReport { get; set; }

    public bool DisclosureApproved { get; set; }

    public string? BlockReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
