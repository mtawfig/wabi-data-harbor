using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;
using WabiInsight.Infrastructure.SupportAccess;

namespace WabiInsight.Infrastructure.Monitoring;

/// <summary>Executes one kind of <see cref="ScheduledJob"/>. Registered scoped; each run gets a fresh scope.</summary>
public interface IScheduledJobHandler
{
    ScheduledJobType JobType { get; }

    Task ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken);
}

/// <summary>
/// The scheduling core (specification §35.4), separated from the hosted service so tests can drive it: provisions
/// the standing per-organization jobs, takes due jobs with an atomic lease (one instance wins, the others see
/// zero rows affected) and runs each in its own scope under the platform scheduler principal.
/// </summary>
public sealed partial class ScheduledJobRunner
{
    public static readonly TenantPrincipal SchedulerPrincipal = SupportAccessExpiryWorker.SchedulerPrincipal;

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;
    private readonly MonitoringOptions _options;
    private readonly ILogger<ScheduledJobRunner> _logger;

    public ScheduledJobRunner(ConfigurationDbContext context, ITenantContext tenantContext, IServiceScopeFactory scopes, IClock clock, IOptions<MonitoringOptions> options, ILogger<ScheduledJobRunner> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _scopes = scopes;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Every organization gets a nightly baselines job (02:15 local), a nightly retention purge (03:30 local) and the
    /// daily alert digest (its digest time); time zones follow the organization. Every active Monitoring Rule has an
    /// enabled job on its own schedule; jobs of paused or deleted rules are disabled.
    /// </summary>
    public async Task<int> EnsureStandingJobsAsync(CancellationToken cancellationToken = default)
    {
        var organizations = await _context.Organizations.AsNoTracking().Select(o => new { o.OrganizationId, o.TimeZone, o.MonitoringDigestTime }).ToListAsync(cancellationToken);
        var existing = await _context.ScheduledJobs.Where(j => j.OrganizationId != null && j.TargetId == null && (j.JobType == ScheduledJobType.AccessBaselines || j.JobType == ScheduledJobType.RetentionPurge || j.JobType == ScheduledJobType.AlertDigest)).ToListAsync(cancellationToken);
        var now = _clock.UtcNow;
        var created = 0;
        foreach (var organization in organizations)
        {
            foreach (var (type, time) in new[] { (ScheduledJobType.AccessBaselines, new TimeOnly(2, 15)), (ScheduledJobType.RetentionPurge, new TimeOnly(3, 30)), (ScheduledJobType.AlertDigest, organization.MonitoringDigestTime) })
            {
                var job = existing.FirstOrDefault(j => j.OrganizationId == organization.OrganizationId && j.JobType == type);
                if (job is null)
                {
                    job = new ScheduledJob
                    {
                        JobId = Ids.New(),
                        JobType = type,
                        OrganizationId = organization.OrganizationId,
                        RecurrenceKind = JobRecurrenceKind.Daily,
                        TimeOfDay = time,
                        TimeZone = organization.TimeZone,
                        CreatedAt = now,
                    };
                    job.NextRunAt = JobRecurrence.NextRun(job, now);
                    _context.ScheduledJobs.Add(job);
                    created++;
                }
                else if (!string.Equals(job.TimeZone, organization.TimeZone, StringComparison.Ordinal) || job.TimeOfDay != time)
                {
                    job.TimeZone = organization.TimeZone;
                    job.TimeOfDay = time;
                    job.NextRunAt = JobRecurrence.NextRun(job, now);
                    job.UpdatedAt = now;
                }
            }
        }

        created += await SyncRuleJobsAsync(organizations.ToDictionary(o => o.OrganizationId, o => o.TimeZone), now, cancellationToken);

        if (_context.ChangeTracker.HasChanges())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return created;
    }

    private async Task<int> SyncRuleJobsAsync(Dictionary<Guid, string> timeZones, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rules = await _context.MonitoringRules.AsPlatformScope(_tenantContext).AsNoTracking()
            .Select(r => new { r.RuleId, r.OrganizationId, r.IsActive, r.RecurrenceKind, r.IntervalMinutes, r.TimeOfDay, r.DayOfWeek })
            .ToListAsync(cancellationToken);
        var jobs = await _context.ScheduledJobs.Where(j => j.JobType == ScheduledJobType.MonitoringRule).ToListAsync(cancellationToken);
        var created = 0;
        foreach (var rule in rules.Where(r => r.IsActive && timeZones.ContainsKey(r.OrganizationId)))
        {
            if (jobs.Any(j => j.TargetId == rule.RuleId && j.OrganizationId == rule.OrganizationId))
            {
                continue;
            }

            var job = new ScheduledJob { JobId = Ids.New(), JobType = ScheduledJobType.MonitoringRule, OrganizationId = rule.OrganizationId, TargetId = rule.RuleId, CreatedAt = now };
            MonitoringSchedules.Apply(job, rule.RecurrenceKind, rule.IntervalMinutes, rule.TimeOfDay, rule.DayOfWeek, timeZones[rule.OrganizationId], true, now);
            _context.ScheduledJobs.Add(job);
            created++;
        }

        var active = rules.Where(r => r.IsActive).Select(r => r.RuleId).ToHashSet();
        foreach (var job in jobs.Where(j => j.IsEnabled && (j.TargetId is not { } target || !active.Contains(target))))
        {
            job.IsEnabled = false;
            job.UpdatedAt = now;
        }

        return created;
    }

    /// <summary>Runs every due job this instance manages to lease. Returns how many ran here.</summary>
    public async Task<int> RunDueJobsAsync(string owner, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var now = _clock.UtcNow;
        var due = await _context.ScheduledJobs.AsNoTracking()
            .Where(j => j.IsEnabled && j.NextRunAt <= now && (j.LeaseUntil == null || j.LeaseUntil < now))
            .OrderBy(j => j.NextRunAt)
            .Select(j => j.JobId)
            .Take(50)
            .ToListAsync(cancellationToken);

        var ran = 0;
        foreach (var jobId in due)
        {
            if (await TryLeaseAsync(jobId, owner, cancellationToken))
            {
                await RunLeasedAsync(jobId, owner, cancellationToken);
                ran++;
            }
        }

        return ran;
    }

    private async Task<bool> TryLeaseAsync(Guid jobId, string owner, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var until = now.AddMinutes(Math.Clamp(_options.LeaseMinutes, 1, 120));
        // Atomic: the WHERE repeats the due/unleased test, so two instances cannot both see one row affected.
        var affected = await _context.ScheduledJobs
            .Where(j => j.JobId == jobId && j.IsEnabled && j.NextRunAt <= now && (j.LeaseUntil == null || j.LeaseUntil < now))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseOwner, owner).SetProperty(j => j.LeaseUntil, until), cancellationToken);
        return affected == 1;
    }

    private async Task RunLeasedAsync(Guid jobId, string owner, CancellationToken cancellationToken)
    {
        var job = await _context.ScheduledJobs.SingleAsync(j => j.JobId == jobId, cancellationToken);
        var watch = Stopwatch.StartNew();
        string? error = null;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(SchedulerPrincipal);
            var handler = scope.ServiceProvider.GetServices<IScheduledJobHandler>().FirstOrDefault(h => h.JobType == job.JobType)
                ?? throw new InvalidOperationException($"No handler for job type {job.JobType}.");
            await handler.ExecuteAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            LogJobFailed(_logger, ex, job.JobType, job.JobId, job.OrganizationId);
        }

        var finished = _clock.UtcNow;
        job.LastRunAt = finished;
        job.LastDurationMs = watch.Elapsed.TotalMilliseconds;
        job.LastOutcome = error is null ? JobOutcome.Succeeded : JobOutcome.Failed;
        job.LastError = error;
        job.ConsecutiveFailures = error is null ? 0 : job.ConsecutiveFailures + 1;
        job.NextRunAt = JobRecurrence.NextRun(job, finished);
        job.LeaseOwner = null;
        job.LeaseUntil = null;
        job.UpdatedAt = finished;
        if (error is not null)
        {
            using var usageScope = _scopes.CreateScope();
            usageScope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(SchedulerPrincipal);
            await usageScope.ServiceProvider.GetRequiredService<IUsageEventWriter>().AppendAndSaveAsync(UsageEventTypes.ScheduledJobFailed, "ScheduledJob", job.JobId.ToString(), succeeded: false,
                metadata: new { jobType = job.JobType.ToString(), job.ConsecutiveFailures, error = error[..Math.Min(error.Length, 200)] }, organizationId: job.OrganizationId, cancellationToken: cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _ = owner;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduled job {JobType} {JobId} (organization {OrganizationId}) failed.")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, ScheduledJobType jobType, Guid jobId, Guid? organizationId);
}

/// <summary>Ticks the runner every <see cref="MonitoringOptions.SchedulerTickSeconds"/>; one per API instance, instances coordinate through leases.</summary>
public sealed partial class JobSchedulerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly MonitoringOptions _options;
    private readonly ILogger<JobSchedulerWorker> _logger;
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}";

    public JobSchedulerWorker(IServiceScopeFactory scopes, IOptions<MonitoringOptions> options, ILogger<JobSchedulerWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.SchedulerEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(_options.SchedulerTickSeconds, 1, 3600)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(ScheduledJobRunner.SchedulerPrincipal);
                var runner = scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>();
                await runner.EnsureStandingJobsAsync(stoppingToken);
                var ran = await runner.RunDueJobsAsync(_owner, stoppingToken);
                if (ran > 0)
                {
                    LogRan(_logger, ran);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogTickFailed(_logger, ex);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduler ran {Count} job(s).")]
    private static partial void LogRan(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduler tick failed.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}

/// <summary>Maps a rule's schedule onto its job (used by the rule service on save and by the standing-job sync).</summary>
public static class MonitoringSchedules
{
    public static void Apply(ScheduledJob job, JobRecurrenceKind kind, int intervalMinutes, TimeOnly timeOfDay, int? dayOfWeek, string timeZone, bool enabled, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.RecurrenceKind = kind;
        job.IntervalMinutes = kind == JobRecurrenceKind.Interval ? intervalMinutes : 0;
        job.TimeOfDay = timeOfDay;
        job.DayOfWeek = kind == JobRecurrenceKind.Weekly ? dayOfWeek : null;
        job.DayOfMonth = null;
        job.TimeZone = timeZone;
        job.IsEnabled = enabled;
        job.NextRunAt = JobRecurrence.NextRun(job, now);
        job.UpdatedAt = now;
    }
}
