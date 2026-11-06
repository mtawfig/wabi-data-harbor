namespace WabiInsight.Core.Email;

/// <summary>A message ready to send. Bodies may contain one-time credentials and are never persisted.</summary>
public sealed record EmailMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string TextBody,
    string Language)
{
    public string? ReplyTo { get; init; }

    /// <summary>Files to attach (a shared report). Kept in memory with the message; never persisted by the outbox.</summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Stable template codes recorded on EmailMessages for metrics (drafts vs sent, specification §12.3).</summary>
public static class EmailTemplateCodes
{
    public const string AccountCreated = "Account.Created";
    public const string PasswordReset = "Account.PasswordReset";
    public const string SupportNotification = "Support.Notification";
    public const string AnalysisShared = "Analysis.Shared";
    public const string AnalysisEmailed = "Analysis.Emailed";
}

/// <summary>
/// Queues a message for background delivery. The outbox row stores recipient, subject, template and
/// status only; the body stays in memory until sent so temporary passwords never reach the database.
/// </summary>
public interface IEmailOutbox
{
    Task<Guid> EnqueueAsync(EmailMessage message, string templateCode, Guid? organizationId = null, CancellationToken cancellationToken = default);
}
