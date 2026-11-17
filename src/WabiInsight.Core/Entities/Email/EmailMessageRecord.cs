using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Email;

public enum EmailDeliveryStatus
{
    Queued = 1,
    Sent = 2,
    Failed = 3,

    /// <summary>Not sent on purpose (P13.55): the address cannot receive mail (a sample user's address). Not a failure.</summary>
    Skipped = 4,
}

/// <summary>
/// Outbox/log row for every transactional email. Holds recipient, subject, template and delivery status;
/// bodies (which may carry one-time credentials) are never stored.
/// </summary>
public class EmailMessageRecord
{
    public Guid EmailMessageId { get; set; }

    public Guid? OrganizationId { get; set; }

    public string RecipientEmail { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string TemplateCode { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public EmailDeliveryStatus Status { get; set; } = EmailDeliveryStatus.Queued;

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public PrincipalType RequestedByType { get; set; }

    public Guid? RequestedById { get; set; }

    public string? CorrelationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
