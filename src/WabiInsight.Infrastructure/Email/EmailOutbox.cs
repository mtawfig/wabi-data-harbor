using System.Threading.Channels;
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

/// <summary>Delivers queued messages with bounded retries and records the outcome on the outbox row.</summary>
public sealed partial class EmailOutboxWorker : BackgroundService
{
    private readonly EmailDeliveryChannel _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly IEmailSender _sender;
    private readonly IClock _clock;
    private readonly ILogger<EmailOutboxWorker> _logger;
    private readonly EmailOptions _options;

    public EmailOutboxWorker(EmailDeliveryChannel queue, IServiceScopeFactory scopes, IEmailSender sender, IClock clock, IOptions<EmailOptions> options, ILogger<EmailOutboxWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _sender = sender;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (id, message) in _queue.Channel.Reader.ReadAllAsync(stoppingToken))
        {
            await DeliverAsync(id, message, stoppingToken);
        }
    }

    private async Task DeliverAsync(Guid id, EmailMessage message, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            try
            {
                await _sender.SendAsync(message, cancellationToken);
                await UpdateAsync(id, attempt, EmailDeliveryStatus.Sent, null, cancellationToken);
                LogSent(_logger, id, message.ToEmail, attempt);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var final = attempt >= _options.MaxAttempts;
                await UpdateAsync(id, attempt, final ? EmailDeliveryStatus.Failed : EmailDeliveryStatus.Queued, ex.Message, cancellationToken);
                LogFailed(_logger, ex, id, message.ToEmail, attempt, final);
                if (final)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), cancellationToken);
            }
        }
    }

    private async Task UpdateAsync(Guid id, int attempts, EmailDeliveryStatus status, string? error, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        var record = await context.EmailMessages.FindAsync([id], cancellationToken);
        if (record is null)
        {
            return;
        }

        record.Attempts = attempts;
        record.Status = status;
        record.LastError = error is null ? null : error[..Math.Min(error.Length, 1000)];
        record.SentAt = status == EmailDeliveryStatus.Sent ? _clock.UtcNow : null;

        // Verified delivery outcome for metrics (sent vs failed), attributed to the requesting principal.
        if (status != EmailDeliveryStatus.Queued)
        {
            var usage = scope.ServiceProvider.GetRequiredService<IUsageEventWriter>();
            usage.Append(
                status == EmailDeliveryStatus.Sent ? UsageEventTypes.EmailSent : UsageEventTypes.EmailFailed,
                "EmailMessage",
                id.ToString(),
                succeeded: status == EmailDeliveryStatus.Sent,
                metadata: new { record.TemplateCode, attempts, error = error is null ? null : error[..Math.Min(error.Length, 200)] },
                organizationId: record.OrganizationId,
                actorType: record.RequestedByType,
                actorId: record.RequestedById);
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {EmailMessageId} sent to {Recipient} (attempt {Attempt}).")]
    private static partial void LogSent(ILogger logger, Guid emailMessageId, string recipient, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {EmailMessageId} to {Recipient} failed (attempt {Attempt}, final: {Final}).")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid emailMessageId, string recipient, int attempt, bool final);
}
