using WabiInsight.Core.Entities.Email;

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

    /// <summary>
    /// The organization this message is sent for, set by the outbox; null for a message about the platform itself (to a
    /// Platform Administrator). <see cref="IEmailRoute"/> chooses the mail server from it.
    /// </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Files to attach (a shared report). Kept in memory with the message; never persisted by the outbox.</summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// The one place that decides which mail server a message leaves through. Version 1 has one server, the platform's
/// (the "Email" settings), for everyone. When an organization can set its own server (Version 2, G5), its messages are
/// routed here, and every kind of message follows without a change where it is written: temporary passwords, reset
/// links, shared analyses, digests, Periodic Reports.
/// </summary>
public interface IEmailRoute
{
    /// <summary>The server that sends messages for this organization; the platform's when it has none of its own, or for null.</summary>
    IEmailSender SenderFor(Guid? organizationId);
}

/// <summary>Stable template codes recorded on EmailMessages for metrics (drafts vs sent, specification §12.3).</summary>
public static class EmailTemplateCodes
{
    public const string AccountCreated = "Account.Created";
    public const string PasswordReset = "Account.PasswordReset";
    public const string PasswordResetLink = "Account.PasswordResetLink";
    public const string PasswordChangedNotice = "Account.PasswordChanged";
    public const string SupportNotification = "Support.Notification";
    public const string AnalysisShared = "Analysis.Shared";
    public const string AnalysisEmailed = "Analysis.Emailed";
    public const string MonitoringDigest = "Monitoring.Digest";
    public const string PeriodicReport = "Reports.Periodic";
}

/// <summary>Stable reason codes written to <c>EmailMessages.LastError</c> when no delivery error text exists.</summary>
public static class EmailFailureReasons
{
    /// <summary>The API stopped or restarted while the message was still waiting; its content was held in memory only and is gone.</summary>
    public const string ProcessRestartedBeforeDelivery = "process_restarted_before_delivery";
}

/// <summary>
/// Queues a message for background delivery. The outbox row stores recipient, subject, template and
/// status only; the body stays in memory until sent so temporary passwords never reach the database.
/// </summary>
public interface IEmailOutbox
{
    Task<Guid> EnqueueAsync(EmailMessage message, string templateCode, Guid? organizationId = null, CancellationToken cancellationToken = default);
}

/// <summary>Writes delivery outcomes to the outbox rows (metadata only; never a body).</summary>
public interface IEmailDeliveryLog
{
    /// <summary>Records the state of one message after a delivery attempt.</summary>
    Task RecordAsync(Guid emailMessageId, int attempts, EmailDeliveryStatus status, string? lastError, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks rows still Queued and created before <paramref name="queuedBefore"/> as Failed with
    /// <see cref="EmailFailureReasons.ProcessRestartedBeforeDelivery"/>: the process that held their content is gone,
    /// so they can never be sent. Returns how many rows were marked.
    /// </summary>
    Task<int> FailAbandonedAsync(DateTimeOffset queuedBefore, CancellationToken cancellationToken = default);
}
