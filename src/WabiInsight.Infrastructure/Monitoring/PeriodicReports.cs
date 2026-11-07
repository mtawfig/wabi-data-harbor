using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Ai.Compilers;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Storage;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Ai;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Persistence;
using WabiInsight.Infrastructure.Reports;

namespace WabiInsight.Infrastructure.Monitoring;

/// <summary>What one recipient gets from one run.</summary>
public sealed record RenderedReport(ReportDeliveryStatus Status, byte[]? Content, string? FileName, string? ContentType, int RowCount, string? ErrorCode);

/// <summary>
/// Renders a saved analysis for one recipient (specification §35.3). Must run in a scope signed in as the
/// <b>recipient</b>: the stored plan is re-validated and re-authorized against the recipient's permissions and row
/// scopes (no model call), compiled and executed read-only, then drawn by the existing PDF/XLSX exporters. The
/// owner's stored figures and AI narrative are never reused; a recipient who may not read the data gets
/// <see cref="ReportDeliveryStatus.NoData"/>.
/// </summary>
public sealed class PeriodicReportRenderer
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IQueryPlanner _planner;
    private readonly ISqlDialectCompilerFactory _compilers;
    private readonly IQueryExecutor _executor;
    private readonly IReportRenderer _pdf;
    private readonly IEnumerable<IAnswerExporter> _exporters;
    private readonly IArtifactStorage _storage;
    private readonly IClock _clock;

    public PeriodicReportRenderer(ConfigurationDbContext context, ITenantContext tenantContext, IQueryPlanner planner, ISqlDialectCompilerFactory compilers, IQueryExecutor executor, IReportRenderer pdf, IEnumerable<IAnswerExporter> exporters, IArtifactStorage storage, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _planner = planner;
        _compilers = compilers;
        _executor = executor;
        _pdf = pdf;
        _exporters = exporters;
        _storage = storage;
        _clock = clock;
    }

    public async Task<RenderedReport> RenderAsync(Guid applicationId, string title, string prompt, AnalysisPlan plan, ExportFormat format, string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_tenantContext.Principal.PrincipalType != PrincipalType.OrganizationUser)
        {
            throw new TenantContextException("Periodic reports render as their recipient.");
        }

        var application = await _context.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.ApplicationId == applicationId, cancellationToken);
        if (application is null || !application.IsApproved || !application.IsActive)
        {
            return Failed("subscription_application_unavailable");
        }

        IReadOnlyList<AuthorizedPlan> authorized;
        try
        {
            authorized = await _planner.AuthorizeStoredAsync(applicationId, plan, cancellationToken);
        }
        catch (PlanRejectedException ex) when (ex.Stage == "authorization")
        {
            return new RenderedReport(ReportDeliveryStatus.NoData, null, null, null, 0, "report_not_permitted");
        }
        catch (PlanRejectedException)
        {
            return Failed("report_plan_invalid");
        }
        catch (DomainException ex) when (ex.Message == "ai_no_permitted_objects")
        {
            return new RenderedReport(ReportDeliveryStatus.NoData, null, null, null, 0, "report_not_permitted");
        }
        catch (DomainException ex)
        {
            return Failed(ex.Message);
        }

        var watch = Stopwatch.StartNew();
        var compiler = _compilers.Resolve(application.DatabaseType);
        var panels = new List<AnalysisPanel>(authorized.Count);
        try
        {
            foreach (var query in authorized)
            {
                var compiled = compiler.Compile(query);
                var result = await _executor.ExecuteAsync(applicationId, compiled, query.Plan.Visualization, cancellationToken);
                panels.Add(new AnalysisPanel(query.Plan.Title, query.Plan, compiled.RedactedPreview, result));
            }
        }
        catch (DomainException ex)
        {
            return Failed(ex.Message);
        }
        catch (AnalysisExecutionException ex)
        {
            return Failed(ex.Code);
        }

        // Panels only: the owner's interpretation and written study describe the owner's figures, not the recipient's.
        var answer = new AskResponse(Guid.Empty, null, 0, plan.Objective, plan.Assumptions, panels, null, null, null, new PlanUsage("none", "none", 0, 0, 0, 0, 0), 0, watch.Elapsed.TotalMilliseconds);
        var content = AnswerReportBuilder.Build(answer, title, prompt, language);
        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == application.OrganizationId, cancellationToken);
        var rtl = language == "ar";
        var organizationName = rtl ? organization.OrganizationNameAr : organization.OrganizationName;
        var applicationName = rtl ? application.ApplicationNameAr : application.ApplicationName;
        var branding = await BrandingAsync(organization, cancellationToken);
        var now = _clock.UtcNow;
        byte[] bytes;
        string contentType;
        string extension;
        if (format == ExportFormat.Pdf)
        {
            bytes = _pdf.RenderPdf(new ReportRenderRequest(content, panels, branding, language, organizationName, applicationName, _tenantContext.Principal.DisplayName ?? organizationName, now));
            contentType = "application/pdf";
            extension = "pdf";
        }
        else
        {
            var exporter = _exporters.FirstOrDefault(e => e.Format == format);
            if (exporter is null)
            {
                return Failed("export_format_unsupported");
            }

            bytes = exporter.Export(new ExportRequest(answer, content, language, organizationName, applicationName, prompt, branding, now));
            contentType = exporter.ContentType;
            extension = exporter.Extension;
        }

        var fileName = $"{Slug(title)}-{now:yyyyMMdd}.{extension}";
        return new RenderedReport(ReportDeliveryStatus.Delivered, bytes, fileName, contentType, panels.Sum(p => p.Result.RowCount), null);
    }

    private async Task<ReportBranding> BrandingAsync(Organization organization, CancellationToken cancellationToken)
    {
        if (organization.LogoArtifactKey is null)
        {
            return new ReportBranding(null, null);
        }

        await using var stream = await _storage.OpenReadAsync(organization.LogoArtifactKey, cancellationToken);
        if (stream is null)
        {
            return new ReportBranding(null, null);
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return new ReportBranding(buffer.ToArray(), organization.LogoContentType);
    }

    private static RenderedReport Failed(string code) => new(ReportDeliveryStatus.Failed, null, null, null, 0, code);

    internal static string Slug(string title)
    {
        var slug = new string(title.Select(c => char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        return slug.Length == 0 ? "report" : slug.Length <= 60 ? slug : slug[..60].TrimEnd('-');
    }
}

public sealed record SubscriptionRunResult(Guid RunId, IReadOnlyList<ReportDelivery> Deliveries, string? ErrorCode);

/// <summary>
/// Runs a subscription: for each active recipient, a child scope signed in as that recipient renders the report;
/// the caller's scope stores the file, emails it through the outbox (attachment + link; or the "no data" note) and
/// writes the delivery log. Three failed scheduled runs in a row pause the subscription and notify its creator.
/// </summary>
public sealed class PeriodicReportRunner
{
    public const int PauseAfterFailures = 3;

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IServiceScopeFactory _scopes;
    private readonly IArtifactStorage _storage;
    private readonly IEmailOutbox _outbox;
    private readonly INotificationDispatcher _notifications;
    private readonly IUsageEventWriter _usage;
    private readonly IClock _clock;
    private readonly PlatformUrlOptions _urls;
    private readonly EmailOptions _email;

    public PeriodicReportRunner(ConfigurationDbContext context, ITenantContext tenantContext, IServiceScopeFactory scopes, IArtifactStorage storage, IEmailOutbox outbox, INotificationDispatcher notifications, IUsageEventWriter usage, IClock clock, IOptions<PlatformUrlOptions> urls, IOptions<EmailOptions> email)
    {
        _context = context;
        _tenantContext = tenantContext;
        _scopes = scopes;
        _storage = storage;
        _outbox = outbox;
        _notifications = notifications;
        _usage = usage;
        _clock = clock;
        _urls = urls.Value;
        _email = email.Value;
    }

    public async Task<SubscriptionRunResult> RunAsync(ReportSubscription subscription, bool manual, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var organizationId = subscription.OrganizationId;
        if (!_tenantContext.IsPlatformAdministrator && _tenantContext.OrganizationId != organizationId)
        {
            throw new TenantContextException("Subscriptions run inside their own organization.");
        }

        IQueryable<T> Org<T>(IQueryable<T> query)
            where T : class, ITenantOwned => RuleRunner.Scope(query, _tenantContext, organizationId);

        var runId = Ids.New();
        var deliveries = new List<ReportDelivery>();
        string? error = null;
        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        var history = await Org(_context.AnalysisHistories).AsNoTracking().SingleOrDefaultAsync(h => h.HistoryId == subscription.HistoryId, cancellationToken);
        AnalysisPlan? plan = null;
        if (organization.Status != OrganizationStatus.Active)
        {
            return new SubscriptionRunResult(runId, deliveries, "organization_inactive");
        }

        if (history is null || history.IsDeleted)
        {
            error = "subscription_analysis_missing";
        }
        else
        {
            var planJson = await Org(_context.AnalysisHistoryVersions).AsNoTracking()
                .Where(v => v.HistoryId == history.HistoryId && v.Status == AnalysisHistoryStatus.Completed)
                .OrderByDescending(v => v.VersionNumber).Select(v => v.QueryPlanJson).FirstOrDefaultAsync(cancellationToken);
            try
            {
                plan = planJson is null ? null : QueryPlanner.Parse(planJson);
            }
            catch (System.Text.Json.JsonException)
            {
                plan = null;
            }

            if (plan is null || plan.Queries.Count == 0)
            {
                error = "subscription_analysis_has_no_plan";
            }
        }

        var recipientIds = await Org(_context.ReportSubscriptionRecipients).AsNoTracking().Where(r => r.SubscriptionId == subscription.SubscriptionId).Select(r => r.UserId).ToListAsync(cancellationToken);
        var recipients = await Org(_context.OrganizationUsers).AsNoTracking().Where(u => recipientIds.Contains(u.UserId) && u.Status == OrganizationUserStatus.Active).ToListAsync(cancellationToken);
        if (error is null && recipients.Count == 0)
        {
            error = "subscription_no_active_recipients";
        }

        if (error is null)
        {
            var link = $"{_urls.BaseUrl.TrimEnd('/')}/app/reports?tab=received";
            foreach (var recipient in recipients)
            {
                deliveries.Add(await DeliverAsync(subscription, history!, plan!, recipient, organization, runId, manual, link, cancellationToken));
            }
        }

        var failed = error is not null || deliveries.All(d => d.Status == ReportDeliveryStatus.Failed);
        var now = _clock.UtcNow;
        var tracked = await Org(_context.ReportSubscriptions).SingleOrDefaultAsync(s => s.SubscriptionId == subscription.SubscriptionId, cancellationToken);
        if (tracked is not null)
        {
            tracked.LastRunAt = now;
            if (!manual)
            {
                tracked.ConsecutiveFailures = failed ? tracked.ConsecutiveFailures + 1 : 0;
                if (failed && tracked.ConsecutiveFailures >= PauseAfterFailures && tracked.IsActive)
                {
                    await PauseAsync(tracked, error ?? deliveries.Select(d => d.ErrorCode).FirstOrDefault(c => c is not null) ?? "subscription_delivery_failed", cancellationToken);
                }
            }
        }

        _usage.Append(failed ? UsageEventTypes.SubscriptionFailed : UsageEventTypes.SubscriptionDelivered, "ReportSubscription", subscription.SubscriptionId.ToString(),
            quantity: deliveries.Count(d => d.Status == ReportDeliveryStatus.Delivered), succeeded: !failed,
            metadata: new { manual, error, delivered = deliveries.Count(d => d.Status == ReportDeliveryStatus.Delivered), noData = deliveries.Count(d => d.Status == ReportDeliveryStatus.NoData), failedDeliveries = deliveries.Count(d => d.Status == ReportDeliveryStatus.Failed), format = subscription.Format.ToString() },
            organizationId: organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return new SubscriptionRunResult(runId, deliveries, error);
    }

    private async Task<ReportDelivery> DeliverAsync(ReportSubscription subscription, AnalysisHistory history, AnalysisPlan plan, OrganizationUser recipient, Organization organization, Guid runId, bool manual, string link, CancellationToken cancellationToken)
    {
        var language = subscription.Language is "en" or "ar" ? subscription.Language : (recipient.PreferredLanguage == "ar" ? "ar" : "en");
        RenderedReport rendered;
        try
        {
            var roles = await AccountResolver.RoleCodesAsync(_context, recipient, cancellationToken);
            await using var scope = _scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(TenantPrincipal.ForOrganizationUser(recipient.OrganizationId, recipient.UserId, recipient.FullName, roles, recipient.DepartmentId, recipient.SectionId));
            rendered = await scope.ServiceProvider.GetRequiredService<PeriodicReportRenderer>()
                .RenderAsync(history.ApplicationId, history.Title, history.OriginalPrompt, plan, subscription.Format, language, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // One recipient's rendering problem (renderer, exporter, source connection) must not stop the others.
            rendered = new RenderedReport(ReportDeliveryStatus.Failed, null, null, null, 0, "report_render_failed");
        }

        var delivery = new ReportDelivery
        {
            DeliveryId = Ids.New(),
            OrganizationId = subscription.OrganizationId,
            SubscriptionId = subscription.SubscriptionId,
            RunId = runId,
            RecipientUserId = recipient.UserId,
            Status = rendered.Status,
            ReportName = subscription.Name,
            AnalysisTitle = history.Title.Length <= 300 ? history.Title : history.Title[..300],
            IsManualRun = manual,
            Language = language,
            RowCount = rendered.RowCount,
            ErrorCode = rendered.ErrorCode,
            CreatedAt = _clock.UtcNow,
        };

        if (rendered.Status != ReportDeliveryStatus.Failed)
        {
            var organizationName = language == "ar" ? organization.OrganizationNameAr : organization.OrganizationName;
            var message = EmailTemplates.PeriodicReport(recipient.Email, recipient.FullName, language, subscription.Name, organizationName, rendered.Status == ReportDeliveryStatus.Delivered,
                subscription.Format.ToString().ToUpperInvariant(), link, _email.ReplyTo);
            if (rendered is { Status: ReportDeliveryStatus.Delivered, Content: { } bytes })
            {
                await using (var stream = new MemoryStream(bytes, writable: false))
                {
                    delivery.StorageKey = await _storage.SaveAsync("report-deliveries", stream, cancellationToken);
                }

                delivery.FileName = rendered.FileName;
                delivery.ContentType = rendered.ContentType;
                delivery.SizeBytes = bytes.LongLength;
                message = message with { Attachments = [new EmailAttachment(rendered.FileName!, rendered.ContentType!, bytes)] };
            }

            // The outbox saves on enqueue, so the (append-only) delivery row is added afterwards with the message id.
            delivery.EmailMessageId = await _outbox.EnqueueAsync(message, EmailTemplateCodes.PeriodicReport, subscription.OrganizationId, cancellationToken);
            _context.ReportDeliveries.Add(delivery);
        }
        else
        {
            _context.ReportDeliveries.Add(delivery);
        }

        return delivery;
    }

    private async Task PauseAsync(ReportSubscription subscription, string reason, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        subscription.IsActive = false;
        subscription.PausedReason = reason.Length <= 100 ? reason : reason[..100];
        foreach (var job in await _context.ScheduledJobs.Where(j => j.OrganizationId == subscription.OrganizationId && j.JobType == ScheduledJobType.ReportSubscription && j.TargetId == subscription.SubscriptionId).ToListAsync(cancellationToken))
        {
            job.IsEnabled = false;
            job.UpdatedAt = now;
        }

        var creator = await RuleRunner.Scope(_context.OrganizationUsers, _tenantContext, subscription.OrganizationId).AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == subscription.CreatedByUserId && u.Status == OrganizationUserStatus.Active, cancellationToken);
        if (creator is not null)
        {
            _notifications.Notify([new NotificationRecipient(PrincipalType.OrganizationUser, creator.UserId, subscription.OrganizationId, creator.Email, creator.FullName, creator.PreferredLanguage)],
                NotificationTypes.ReportSubscriptionPaused, "ReportSubscription", subscription.SubscriptionId.ToString(), new { subscriptionId = subscription.SubscriptionId, name = subscription.Name, reason }, subscription.OrganizationId);
        }
    }
}

/// <summary>Scheduled run of one subscription (<see cref="ScheduledJob.TargetId"/> is the subscription).</summary>
public sealed class ReportSubscriptionJob : IScheduledJobHandler
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly PeriodicReportRunner _runner;

    public ReportSubscriptionJob(ConfigurationDbContext context, ITenantContext tenantContext, PeriodicReportRunner runner)
    {
        _context = context;
        _tenantContext = tenantContext;
        _runner = runner;
    }

    public ScheduledJobType JobType => ScheduledJobType.ReportSubscription;

    public async Task ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var organizationId = job.OrganizationId ?? throw new InvalidOperationException("Subscriptions are per organization.");
        var subscription = await RuleRunner.Scope(_context.ReportSubscriptions, _tenantContext, organizationId).AsNoTracking().SingleOrDefaultAsync(s => s.SubscriptionId == job.TargetId, cancellationToken);
        if (subscription is null || !subscription.IsActive)
        {
            return;
        }

        await _runner.RunAsync(subscription, manual: false, cancellationToken);
    }
}
