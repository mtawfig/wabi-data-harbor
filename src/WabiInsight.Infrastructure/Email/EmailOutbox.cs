using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Email;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Email;

/// <summary>In-process queue shared by the outbox (producer) and the worker (consumer).</summary>
public sealed class EmailDeliveryChannel
{
    public Channel<(Guid Id, EmailMessage Message)> Channel { get; } =
        System.Threading.Channels.Channel.CreateBounded<(Guid, EmailMessage)>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait });
}

/// <summary>Writes the outbox row (metadata only) and hands the message to the background worker.</summary>
public sealed class EmailOutbox : IEmailOutbox
{
    private readonly ConfigurationDbContext _context;
    private readonly EmailDeliveryChannel _queue;
    private readonly ITenantContext _tenantContext;
    private readonly IRequestContext _request;
    private readonly IClock _clock;

    private readonly IUsageEventWriter _usage;

    public EmailOutbox(ConfigurationDbContext context, EmailDeliveryChannel queue, ITenantContext tenantContext, IRequestContext request, IClock clock, IUsageEventWriter usage)
    {
        _context = context;
        _queue = queue;
        _tenantContext = tenantContext;
        _request = request;
        _clock = clock;
        _usage = usage;
    }

    public async Task<Guid> EnqueueAsync(EmailMessage message, string templateCode, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var record = new EmailMessageRecord
        {
            EmailMessageId = Ids.New(),
            OrganizationId = organizationId ?? _tenantContext.OrganizationId,
            RecipientEmail = message.ToEmail,
            RecipientName = message.ToName,
            Subject = message.Subject,
            TemplateCode = templateCode,
            Language = message.Language,
            Status = EmailDeliveryStatus.Queued,
            RequestedByType = _tenantContext.Principal.PrincipalType,
            RequestedById = _tenantContext.Principal.AdministratorId ?? _tenantContext.Principal.UserId,
            CorrelationId = _request.CorrelationId,
            CreatedAt = _clock.UtcNow,
        };

        _context.EmailMessages.Add(record);
        _usage.Append(UsageEventTypes.EmailQueued, "EmailMessage", record.EmailMessageId.ToString(), metadata: new { templateCode }, organizationId: record.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
        await _queue.Channel.Writer.WriteAsync((record.EmailMessageId, message), cancellationToken);
        return record.EmailMessageId;
    }
}

/// <summary>Records delivery outcomes on the outbox rows and the matching usage events (metadata only).</summary>
public sealed class EmailDeliveryLog : IEmailDeliveryLog
{
    private const int AbandonedBatchSize = 200;

    private readonly ConfigurationDbContext _context;
    private readonly IUsageEventWriter _usage;
    private readonly IClock _clock;

    public EmailDeliveryLog(ConfigurationDbContext context, IUsageEventWriter usage, IClock clock)
    {
        _context = context;
        _usage = usage;
        _clock = clock;
    }

    public async Task RecordAsync(Guid emailMessageId, int attempts, EmailDeliveryStatus status, string? lastError, CancellationToken cancellationToken = default)
    {
        var record = await _context.EmailMessages.FindAsync([emailMessageId], cancellationToken);
        if (record is null)
        {
            return;
        }

        record.Attempts = attempts;
        record.Status = status;
        record.LastError = lastError is null ? null : lastError[..Math.Min(lastError.Length, 1000)];
        record.SentAt = status == EmailDeliveryStatus.Sent ? _clock.UtcNow : null;

        // Verified delivery outcome for metrics (sent vs failed), attributed to the requesting principal.
        if (status != EmailDeliveryStatus.Queued)
        {
            _usage.Append(
                status == EmailDeliveryStatus.Sent ? UsageEventTypes.EmailSent : UsageEventTypes.EmailFailed,
                "EmailMessage",
                emailMessageId.ToString(),
                succeeded: status == EmailDeliveryStatus.Sent,
                metadata: new { record.TemplateCode, attempts, error = lastError is null ? null : lastError[..Math.Min(lastError.Length, 200)] },
                organizationId: record.OrganizationId,
                actorType: record.RequestedByType,
                actorId: record.RequestedById);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> FailAbandonedAsync(DateTimeOffset queuedBefore, CancellationToken cancellationToken = default)
    {
        const string Reason = EmailFailureReasons.ProcessRestartedBeforeDelivery;
        var marked = 0;
        while (true)
        {
            // Platform scope on purpose: this runs at start-up with no principal and covers the messages of every
            // organization and of the platform itself. Only delivery metadata is read; no body exists to read.
            var batch = await _context.EmailMessages.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.Status == EmailDeliveryStatus.Queued && e.CreatedAt < queuedBefore)
                .OrderBy(e => e.CreatedAt)
                .Select(e => new { e.EmailMessageId, e.OrganizationId, e.TemplateCode, e.Attempts, e.RequestedByType, e.RequestedById })
                .Take(AbandonedBatchSize)
                .ToListAsync(cancellationToken);

            foreach (var row in batch)
            {
                // Conditional update: when several API instances start together exactly one of them marks the row
                // (and writes its usage event); a row another process has just finished is left as that process set it.
                var changed = await _context.EmailMessages.IgnoreQueryFilters()
                    .Where(e => e.EmailMessageId == row.EmailMessageId && e.Status == EmailDeliveryStatus.Queued)
                    .ExecuteUpdateAsync(
                        update => update.SetProperty(e => e.Status, EmailDeliveryStatus.Failed).SetProperty(e => e.LastError, Reason),
                        cancellationToken);
                if (changed == 0)
                {
                    continue;
                }

                marked++;
                _usage.Append(
                    UsageEventTypes.EmailFailed,
                    "EmailMessage",
                    row.EmailMessageId.ToString(),
                    succeeded: false,
                    metadata: new { row.TemplateCode, attempts = row.Attempts, error = Reason },
                    organizationId: row.OrganizationId,
                    actorType: row.RequestedByType,
                    actorId: row.RequestedById);
            }

            await _context.SaveChangesAsync(cancellationToken);
            if (batch.Count < AbandonedBatchSize)
            {
                return marked;
            }
        }
    }
}

/// <summary>
/// Delivers queued messages with bounded retries and records the outcome on the outbox row. Nothing a single
/// message does can end the worker (an escaping exception would stop the whole API). When the API stops, the
/// worker keeps sending what it already holds for <see cref="EmailOptions.ShutdownGraceSeconds"/>; rows a
/// previous process left Queued are marked Failed after start-up, because their content was only in memory.
/// </summary>
public sealed partial class EmailOutboxWorker : BackgroundService
{
    /// <summary>
    /// A Queued row is treated as abandoned only when it is older than this, so a message another API instance
    /// queued a moment ago (and is about to send) is never touched.
    /// </summary>
    internal static readonly TimeSpan DefaultAbandonedSafetyMargin = TimeSpan.FromMinutes(2);

    private readonly EmailDeliveryChannel _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailOutboxWorker> _logger;
    private readonly EmailOptions _options;
    private readonly DateTimeOffset _startedAt;

    public EmailOutboxWorker(EmailDeliveryChannel queue, IServiceScopeFactory scopes, IEmailSender sender, IClock clock, IOptions<EmailOptions> options, ILogger<EmailOutboxWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        _queue = queue;
        _scopes = scopes;
        _sender = sender;
        _options = options.Value;
        _logger = logger;

        // Taken when the host builds its services, before any request or job of this process can queue a message.
        _startedAt = clock.UtcNow;
    }

    internal TimeSpan AbandonedSafetyMargin { get; init; } = DefaultAbandonedSafetyMargin;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recovery = RecoverAbandonedAsync(stoppingToken);

        // Deliveries run on their own token: it is cancelled only when the shutdown grace period is over, so the
        // message being sent when the API stops, and those waiting behind it, still get their chance.
        using var delivery = new CancellationTokenSource();
        using var registration = stoppingToken.Register(() => delivery.CancelAfter(TimeSpan.FromSeconds(_options.ShutdownGraceSeconds)));

        var reader = _queue.Channel.Reader;
        while (!delivery.IsCancellationRequested)
        {
            if (reader.TryRead(out var item))
            {
                await DeliverSafelyAsync(item.Id, item.Message, stoppingToken, delivery.Token);
                continue;
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break; // Stopping and nothing left in memory.
            }

            try
            {
                if (!await reader.WaitToReadAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown: loop once more to send what is already queued.
            }
        }

        if (reader.Count > 0)
        {
            LogLeftUnsent(_logger, reader.Count, _options.ShutdownGraceSeconds);
        }

        await recovery;
    }

    private async Task DeliverSafelyAsync(Guid id, EmailMessage message, CancellationToken stoppingToken, CancellationToken deliveryToken)
    {
        try
        {
            await DeliverAsync(id, message, stoppingToken, deliveryToken);
        }
        catch (OperationCanceledException) when (deliveryToken.IsCancellationRequested)
        {
            // The grace period ended mid-delivery; the row stays Queued and is marked Failed at the next start.
            LogInterrupted(_logger, id);
        }
#pragma warning disable CA1031 // The worker must outlive any failure of one message; an escaping exception stops the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeliveryAborted(_logger, ex, id);
        }
    }

    private async Task DeliverAsync(Guid id, EmailMessage message, CancellationToken stoppingToken, CancellationToken deliveryToken)
    {
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            Exception? failure = null;
            try
            {
                await _sender.SendAsync(message, deliveryToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !deliveryToken.IsCancellationRequested)
            {
                failure = ex;
            }

            if (failure is null)
            {
                LogSent(_logger, id, message.ToEmail, attempt);

                // Outside the send's try block: when recording fails, the message is not sent a second time.
                await RecordAsync(id, attempt, EmailDeliveryStatus.Sent, null, deliveryToken);
                return;
            }

            var final = attempt >= _options.MaxAttempts;
            LogFailed(_logger, failure, id, message.ToEmail, attempt, final);
            if (final)
            {
                await RecordAsync(id, attempt, EmailDeliveryStatus.Failed, failure.Message, deliveryToken);
                return;
            }

            try
            {
                await RecordAsync(id, attempt, EmailDeliveryStatus.Queued, failure.Message, deliveryToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !deliveryToken.IsCancellationRequested)
            {
                // The attempt count of a message that is still being retried is informational; keep retrying.
                LogProgressNotRecorded(_logger, ex, id, attempt);
            }

            try
            {
                // Ends early when the API starts stopping: the remaining attempts then run back to back, inside the grace period.
                await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                deliveryToken.ThrowIfCancellationRequested();
            }
        }
    }

    private async Task RecordAsync(Guid id, int attempts, EmailDeliveryStatus status, string? error, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IEmailDeliveryLog>().RecordAsync(id, attempts, status, error, cancellationToken);
    }

    /// <summary>
    /// Two passes. At start-up: rows older than the safety margin. Once the margin has passed: rows queued before
    /// this process started. A restart usually takes seconds, so the rows the previous process dropped are still
    /// younger than the margin during the first pass. Rows this process queued itself are never touched.
    /// </summary>
    private async Task RecoverAbandonedAsync(CancellationToken stoppingToken)
    {
        await FailAbandonedSafelyAsync(_startedAt - AbandonedSafetyMargin, stoppingToken);
        try
        {
            await Task.Delay(AbandonedSafetyMargin, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await FailAbandonedSafelyAsync(_startedAt, stoppingToken);
    }

    private async Task FailAbandonedSafelyAsync(DateTimeOffset queuedBefore, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var marked = await scope.ServiceProvider.GetRequiredService<IEmailDeliveryLog>().FailAbandonedAsync(queuedBefore, stoppingToken);
            if (marked > 0)
            {
                LogAbandonedMarked(_logger, marked);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping; the next start repeats the check.
        }
#pragma warning disable CA1031 // A database that is unreachable (or not migrated yet) at start-up must not stop the API.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRecoveryFailed(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {EmailMessageId} sent to {Recipient} (attempt {Attempt}).")]
    private static partial void LogSent(ILogger logger, Guid emailMessageId, string recipient, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {EmailMessageId} to {Recipient} failed (attempt {Attempt}, final: {Final}).")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid emailMessageId, string recipient, int attempt, bool final);

    [LoggerMessage(Level = LogLevel.Error, Message = "Email {EmailMessageId}: the delivery outcome could not be recorded, so the outbox row may still show Queued. The worker continues with the next message.")]
    private static partial void LogDeliveryAborted(ILogger logger, Exception exception, Guid emailMessageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {EmailMessageId}: attempt {Attempt} could not be recorded on the outbox row; the delivery is retried.")]
    private static partial void LogProgressNotRecorded(ILogger logger, Exception exception, Guid emailMessageId, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {EmailMessageId} was interrupted by shutdown; it will be marked Failed at the next start.")]
    private static partial void LogInterrupted(ILogger logger, Guid emailMessageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} email(s) were not sent within the {GraceSeconds}s shutdown grace period; they will be marked Failed at the next start.")]
    private static partial void LogLeftUnsent(ILogger logger, int count, int graceSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} email(s) left Queued by a previous process were marked Failed (process_restarted_before_delivery); they have to be sent again.")]
    private static partial void LogAbandonedMarked(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Emails left Queued by a previous process could not be checked.")]
    private static partial void LogRecoveryFailed(ILogger logger, Exception exception);
}
