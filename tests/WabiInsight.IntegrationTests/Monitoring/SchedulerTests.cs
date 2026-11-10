using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Monitoring;
using WabiInsight.Infrastructure.Persistence;
using WabiInsight.Infrastructure.Usage;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Monitoring;

/// <summary>
/// Phase 14 scheduler (specification §35.4): standing jobs per organization, atomic leases so two instances never
/// run one job twice, next run strictly after now, baselines computed per user and role, retention purge per
/// organization with its own months.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SchedulerTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ServiceProvider _services;
    private readonly MonitoringOptions _options = new() { SchedulerEnabled = false, LeaseMinutes = 10, BaselineWindowDays = 28, DefaultRetentionMonths = 24, MaxRetentionMonths = 60 };

    public SchedulerTests(SqlServerTestDatabase db)
    {
        _db = db;
        var services = new ServiceCollection();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(Options.Create(_options));
        services.AddDbContext<ConfigurationDbContext>(options => options.UseSqlServer(db.ConnectionString));
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextInitializer>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<IRequestContext, NullRequestContext>();
        services.AddScoped<IUsageEventWriter, UsageEventWriter>();
        services.AddScoped<IScheduledJobHandler, AccessBaselineJob>();
        services.AddScoped<IScheduledJobHandler, RetentionPurgeJob>();
        services.AddScoped<IScheduledJobHandler, FlakyDigestHandler>();
        services.AddSingleton<Core.Storage.IArtifactStorage, NoFilesStorage>();
        services.AddScoped(sp => new ScheduledJobRunner(sp.GetRequiredService<ConfigurationDbContext>(), sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<IOptions<MonitoringOptions>>(), NullLogger<ScheduledJobRunner>.Instance));
        _services = services.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private AsyncServiceScope SchedulerScope()
    {
        var scope = _services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(ScheduledJobRunner.SchedulerPrincipal);
        return scope;
    }

    [Fact]
    public async Task StandingJobs_ExistPerOrganization_AndOnlyOneInstanceWinsALease()
    {
        await using (var scope = SchedulerScope())
        {
            await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().EnsureStandingJobsAsync();
        }

        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            foreach (var organizationId in new[] { _db.Data.OrganizationAId, _db.Data.OrganizationBId })
            {
                Assert.Single(await context.ScheduledJobs.Where(j => j.OrganizationId == organizationId && j.JobType == ScheduledJobType.AccessBaselines).ToListAsync());
                Assert.Single(await context.ScheduledJobs.Where(j => j.OrganizationId == organizationId && j.JobType == ScheduledJobType.RetentionPurge).ToListAsync());
            }

            var job = await context.ScheduledJobs.SingleAsync(j => j.OrganizationId == _db.Data.OrganizationAId && j.JobType == ScheduledJobType.AccessBaselines);
            Assert.True(job.NextRunAt > DateTimeOffset.UtcNow);
            Assert.Equal(new TimeOnly(2, 15), job.TimeOfDay);
        }

        // Make organization A's baseline job due, then race two runners (two API instances) for it.
        var jobId = await MakeDueAsync(_db.Data.OrganizationAId, ScheduledJobType.AccessBaselines);
        await using var scope1 = SchedulerScope();
        await using var scope2 = SchedulerScope();
        var results = await Task.WhenAll(
            scope1.ServiceProvider.GetRequiredService<ScheduledJobRunner>().RunDueJobsAsync("instance-1"),
            scope2.ServiceProvider.GetRequiredService<ScheduledJobRunner>().RunDueJobsAsync("instance-2"));
        Assert.Equal(1, results.Sum());

        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            var job = await context.ScheduledJobs.SingleAsync(j => j.JobId == jobId);
            Assert.Equal(JobOutcome.Succeeded, job.LastOutcome);
            Assert.NotNull(job.LastRunAt);
            Assert.Null(job.LeaseOwner);
            Assert.Null(job.LeaseUntil);
            Assert.True(job.NextRunAt > DateTimeOffset.UtcNow, "the next run is strictly in the future (missed slots collapse to one)");
            Assert.Equal(0, job.ConsecutiveFailures);
        }
    }

    [Fact]
    public async Task AWorkerTick_ReleasesTheLease_SoAJobMadeDueAgainRunsAtOnce()
    {
        // The worker ensures the standing jobs and runs the due ones in one scope, so the job rows are already tracked
        // when the lease is taken with a set-based update. The lease must still be released when the run ends.
        await using (var scope = SchedulerScope())
        {
            await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().EnsureStandingJobsAsync();
        }

        // Retention only removes rows older than the organization's months, so running it here changes nothing other tests use.
        var jobId = await MakeDueAsync(_db.Data.OrganizationBId, ScheduledJobType.RetentionPurge);
        for (var run = 0; run < 2; run++)
        {
            await using (var scope = SchedulerScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>();
                await runner.EnsureStandingJobsAsync();
                Assert.Equal(1, await runner.RunDueJobsAsync("worker-tick"));
            }

            await using var context = _db.CreateContext(TestData.SystemPrincipal);
            var job = await context.ScheduledJobs.SingleAsync(j => j.JobId == jobId);
            Assert.Equal(JobOutcome.Succeeded, job.LastOutcome);
            Assert.Null(job.LeaseOwner);
            Assert.Null(job.LeaseUntil);
            if (run == 0)
            {
                // Due again right away (an edited rule, "make due" in a demo): it must not wait for the old lease to expire.
                job.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                await context.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Baselines_AreComputedPerUserAndRole_FromTheLedger_InsideOneOrganization()
    {
        var today = DateTimeOffset.UtcNow;
        var objectA = await SeedObjectAsync(_db.Data.OrganizationAId);
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            // Alice (organization A): three active days of 2, 4 and 6 analyses, one restricted touch; Bob (organization B): one day.
            foreach (var (daysAgo, count) in new[] { (3, 2), (2, 4), (1, 6) })
            {
                for (var i = 0; i < count; i++)
                {
                    context.AnalysisAccessFacts.Add(Fact(_db.Data.OrganizationAId, _db.Data.UserA1Id, objectA, today.AddDays(-daysAgo).AddMinutes(i), "OrganizationAdmin", restricted: daysAgo == 1 && i == 0));
                }
            }

            context.AnalysisAccessFacts.Add(Fact(_db.Data.OrganizationBId, _db.Data.UserB1Id, null, today.AddDays(-1), "Management"));
            await context.SaveChangesAsync();
        }

        await RunAsync(await MakeDueAsync(_db.Data.OrganizationAId, ScheduledJobType.AccessBaselines));

        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            var baselines = await context.AccessBaselines.IgnoreQueryFilters().Where(b => b.OrganizationId == _db.Data.OrganizationAId).ToListAsync();
            var alice = Assert.Single(baselines, b => b.SubjectKind == BaselineSubjectKind.User && b.UserId == _db.Data.UserA1Id);
            Assert.Equal(3, alice.ActiveDays);
            Assert.Equal(4, alice.QueriesPerDayMedian);
            Assert.Equal(5.8, alice.QueriesPerDayP95, 6);
            Assert.Equal(0, alice.RestrictedTouchesPerDayMedian);
            Assert.Equal(0.9, alice.RestrictedTouchesPerDayP95, 6);
            var adminRole = Assert.Single(baselines, b => b.SubjectKind == BaselineSubjectKind.Role && b.RoleCode == "OrganizationAdmin");
            Assert.Equal(4, adminRole.QueriesPerDayMedian);
            // Organization B's facts never leak into A's baselines, and B has none computed yet.
            Assert.DoesNotContain(baselines, b => b.UserId == _db.Data.UserB1Id);
            Assert.Empty(await context.AccessBaselines.IgnoreQueryFilters().Where(b => b.OrganizationId == _db.Data.OrganizationBId).ToListAsync());
        }
    }

    [Fact]
    public async Task RetentionPurge_UsesEachOrganizationsOwnMonths_AndTheRecorderNeverTouchesOtherOrganizations()
    {
        var now = DateTimeOffset.UtcNow;
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            var organizationB = await context.Organizations.SingleAsync(o => o.OrganizationId == _db.Data.OrganizationBId);
            organizationB.MonitoringRetentionMonths = 1;
            context.AnalysisAccessFacts.AddRange(
                Fact(_db.Data.OrganizationAId, _db.Data.UserA1Id, null, now.AddMonths(-2), "OrganizationAdmin"),   // kept: A keeps 24 months
                Fact(_db.Data.OrganizationAId, _db.Data.UserA1Id, null, now.AddMonths(-30), "OrganizationAdmin"),  // purged
                Fact(_db.Data.OrganizationBId, _db.Data.UserB1Id, null, now.AddMonths(-2), "OrganizationAdmin"),   // purged: B keeps 1 month
                Fact(_db.Data.OrganizationBId, _db.Data.UserB1Id, null, now.AddDays(-3), "OrganizationAdmin"));   // kept
            await context.SaveChangesAsync();
        }

        await RunAsync(await MakeDueAsync(_db.Data.OrganizationAId, ScheduledJobType.RetentionPurge));
        await RunAsync(await MakeDueAsync(_db.Data.OrganizationBId, ScheduledJobType.RetentionPurge));

        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            var a = await context.AnalysisAccessFacts.IgnoreQueryFilters().Where(f => f.OrganizationId == _db.Data.OrganizationAId && f.ObjectName == "RetentionProbe").ToListAsync();
            var b = await context.AnalysisAccessFacts.IgnoreQueryFilters().Where(f => f.OrganizationId == _db.Data.OrganizationBId && f.ObjectName == "RetentionProbe").ToListAsync();
            Assert.Single(a);
            Assert.True(a[0].AccessedAt > now.AddMonths(-3));
            Assert.Single(b);
            Assert.True(b[0].AccessedAt > now.AddMonths(-1));
            var organizationB = await context.Organizations.SingleAsync(o => o.OrganizationId == _db.Data.OrganizationBId);
            organizationB.MonitoringRetentionMonths = 24;
            await context.SaveChangesAsync();
        }

        // The platform maximum caps what an organization may configure.
        Assert.Equal(60, RetentionPurgeJob.EffectiveRetentionMonths(new Core.Entities.Platform.Organization { MonitoringRetentionMonths = 120 }, _options));
        Assert.Equal(24, RetentionPurgeJob.EffectiveRetentionMonths(new Core.Entities.Platform.Organization { MonitoringRetentionMonths = 0 }, _options));
    }

    private async Task RunAsync(Guid jobId)
    {
        await using var scope = SchedulerScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().RunDueJobsAsync("test-instance"));
        await using var context = _db.CreateContext(TestData.SystemPrincipal);
        var job = await context.ScheduledJobs.SingleAsync(j => j.JobId == jobId);
        Assert.True(job.LastOutcome == JobOutcome.Succeeded, job.LastError);
    }

    [Fact]
    public async Task AFailedRun_IsRetriedSoon_ThenBackOnSchedule_AndOnlySuccessMovesTheSuccessMark()
    {
        var jobId = await MakeDueAsync(_db.Data.OrganizationBId, ScheduledJobType.AlertDigest);
        DateTimeOffset? successBefore;
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            successBefore = (await context.ScheduledJobs.AsNoTracking().SingleAsync(j => j.JobId == jobId)).LastSuccessAt;
        }

        async Task<ScheduledJob> RunAsync(bool fail)
        {
            FlakyDigestHandler.Fail = fail;
            await using (var context = _db.CreateContext(TestData.SystemPrincipal))
            {
                var due = await context.ScheduledJobs.SingleAsync(j => j.JobId == jobId);
                due.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                await context.SaveChangesAsync();
            }

            await using (var scope = SchedulerScope())
            {
                Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().RunDueJobsAsync("retry-test"));
            }

            await using var read = _db.CreateContext(TestData.SystemPrincipal);
            return await read.ScheduledJobs.AsNoTracking().SingleAsync(j => j.JobId == jobId);
        }

        try
        {
            // Three failures in a row: retried after 5, 15 and 60 minutes, each time counted, the success mark untouched.
            foreach (var (attempt, minutes) in new[] { (1, 5), (2, 15), (3, 60) })
            {
                var failed = await RunAsync(fail: true);
                Assert.Equal(JobOutcome.Failed, failed.LastOutcome);
                Assert.Equal(attempt, failed.ConsecutiveFailures);
                Assert.Equal(FlakyDigestHandler.Error, failed.LastError);
                Assert.Equal(successBefore, failed.LastSuccessAt);
                Assert.Null(failed.LeaseOwner);
                var regular = JobRecurrence.NextRun(failed, failed.LastRunAt!.Value);
                var retry = failed.LastRunAt.Value.AddMinutes(minutes);
                Assert.Equal(retry < regular ? retry : regular, failed.NextRunAt); // never later than the regular run
            }

            // A fourth failure in a row waits for the regular schedule: a source that stays down is not hammered.
            var fourth = await RunAsync(fail: true);
            Assert.Equal(4, fourth.ConsecutiveFailures);
            Assert.Equal(JobRecurrence.NextRun(fourth, fourth.LastRunAt!.Value), fourth.NextRunAt);
            Assert.Equal(successBefore, fourth.LastSuccessAt);

            // Success: the count is cleared, the mark moves, the regular schedule resumes.
            var succeeded = await RunAsync(fail: false);
            Assert.Equal(JobOutcome.Succeeded, succeeded.LastOutcome);
            Assert.Equal(0, succeeded.ConsecutiveFailures);
            Assert.Null(succeeded.LastError);
            Assert.Equal(succeeded.LastRunAt, succeeded.LastSuccessAt);
            Assert.Equal(JobRecurrence.NextRun(succeeded, succeeded.LastRunAt!.Value), succeeded.NextRunAt);
            Assert.Equal(successBefore, FlakyDigestHandler.SeenSuccessMarks[0]); // what the digest would have counted from, on every attempt
            Assert.All(FlakyDigestHandler.SeenSuccessMarks, mark => Assert.Equal(successBefore, mark));
        }
        finally
        {
            FlakyDigestHandler.Fail = false;
            FlakyDigestHandler.SeenSuccessMarks.Clear();
        }
    }

    [Fact]
    public async Task OneJobPerOrganizationTypeAndTarget_EvenWhenTwoInstancesProvisionAtOnce()
    {
        await using (var scope = SchedulerScope())
        {
            await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().EnsureStandingJobsAsync();
        }

        // The database refuses a second job for the same organization, type and target (none, for a standing job).
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            context.ScheduledJobs.Add(new ScheduledJob { JobId = Ids.New(), JobType = ScheduledJobType.RetentionPurge, OrganizationId = _db.Data.OrganizationBId, TimeZone = "UTC", NextRunAt = DateTimeOffset.UtcNow.AddDays(1), CreatedAt = DateTimeOffset.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        // Two instances start together and both find the job missing: one row results, and neither tick fails.
        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            await context.ScheduledJobs.Where(j => j.OrganizationId == _db.Data.OrganizationBId && j.JobType == ScheduledJobType.RetentionPurge).ExecuteDeleteAsync();
        }

        await using (var scope1 = SchedulerScope())
        await using (var scope2 = SchedulerScope())
        {
            await Task.WhenAll(
                scope1.ServiceProvider.GetRequiredService<ScheduledJobRunner>().EnsureStandingJobsAsync(),
                scope2.ServiceProvider.GetRequiredService<ScheduledJobRunner>().EnsureStandingJobsAsync());
        }

        await using (var scope = SchedulerScope())
        {
            await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().EnsureStandingJobsAsync(); // the tick after, whoever lost the race
        }

        await using (var context = _db.CreateContext(TestData.SystemPrincipal))
        {
            Assert.Single(await context.ScheduledJobs.AsNoTracking().Where(j => j.OrganizationId == _db.Data.OrganizationBId && j.JobType == ScheduledJobType.RetentionPurge).ToListAsync());
            Assert.Single(await context.ScheduledJobs.AsNoTracking().Where(j => j.OrganizationId == _db.Data.OrganizationAId && j.JobType == ScheduledJobType.RetentionPurge).ToListAsync());
        }
    }

    [Fact]
    public async Task ARunLongerThanItsLease_KeepsTheLease_SoNoSecondInstanceStartsIt()
    {
        var jobId = await MakeDueAsync(_db.Data.OrganizationAId, ScheduledJobType.AlertDigest);
        _options.LeaseMinutes = 1; // renewed every 20 seconds
        FlakyDigestHandler.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var scope = SchedulerScope();
            var running = scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().RunDueJobsAsync("long-run");
            DateTimeOffset firstLease;
            await using (var context = _db.CreateContext(TestData.SystemPrincipal))
            {
                ScheduledJob? leased = null;
                for (var i = 0; i < 100 && leased?.LeaseUntil is null; i++)
                {
                    await Task.Delay(100);
                    leased = await context.ScheduledJobs.AsNoTracking().SingleAsync(j => j.JobId == jobId);
                }

                firstLease = leased!.LeaseUntil!.Value;
                Assert.Equal("long-run", leased.LeaseOwner);
            }

            // While it runs, the lease moves forward; a second instance finds nothing to take.
            DateTimeOffset renewed = firstLease;
            for (var i = 0; i < 300 && renewed == firstLease; i++)
            {
                await Task.Delay(100);
                await using var context = _db.CreateContext(TestData.SystemPrincipal);
                renewed = (await context.ScheduledJobs.AsNoTracking().SingleAsync(j => j.JobId == jobId)).LeaseUntil ?? firstLease;
            }

            Assert.True(renewed > firstLease, "the lease was extended while the job was still running");
            await using (var other = SchedulerScope())
            {
                Assert.Equal(0, await other.ServiceProvider.GetRequiredService<ScheduledJobRunner>().RunDueJobsAsync("second-instance"));
            }

            FlakyDigestHandler.Hold.SetResult();
            Assert.Equal(1, await running);
            await using var after = _db.CreateContext(TestData.SystemPrincipal);
            var done = await after.ScheduledJobs.AsNoTracking().SingleAsync(j => j.JobId == jobId);
            Assert.Equal(JobOutcome.Succeeded, done.LastOutcome);
            Assert.Null(done.LeaseOwner);
            Assert.Null(done.LeaseUntil);
        }
        finally
        {
            FlakyDigestHandler.Hold?.TrySetResult();
            FlakyDigestHandler.Hold = null;
            FlakyDigestHandler.SeenSuccessMarks.Clear();
            _options.LeaseMinutes = 10;
        }
    }

    /// <summary>Stands in for the digest job (not registered in this class): fails or waits on demand, and notes the success mark it was given.</summary>
    private sealed class FlakyDigestHandler : IScheduledJobHandler
    {
        public const string Error = "source_unavailable";

        public static bool Fail { get; set; }

        public static TaskCompletionSource? Hold { get; set; }

        public static List<DateTimeOffset?> SeenSuccessMarks { get; } = [];

        public ScheduledJobType JobType => ScheduledJobType.AlertDigest;

        public async Task ExecuteAsync(ScheduledJob job, CancellationToken cancellationToken)
        {
            SeenSuccessMarks.Add(job.LastSuccessAt);
            if (Hold is { } hold)
            {
                await hold.Task.WaitAsync(cancellationToken);
            }

            if (Fail)
            {
                throw new ScheduledJobFailedException(Error);
            }
        }
    }

    private async Task<Guid> MakeDueAsync(Guid organizationId, ScheduledJobType type)
    {
        await using (var scope = SchedulerScope())
        {
            await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().EnsureStandingJobsAsync();
        }

        await using var context = _db.CreateContext(TestData.SystemPrincipal);
        // Only this job is due: every other job stays in the future so RunDueJobsAsync counts are exact.
        var job = await context.ScheduledJobs.SingleAsync(j => j.OrganizationId == organizationId && j.JobType == type);
        job.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        job.LeaseOwner = null;
        job.LeaseUntil = null;
        await context.SaveChangesAsync();
        return job.JobId;
    }

    private async Task<Guid> SeedObjectAsync(Guid organizationId)
    {
        await using var context = _db.CreateContext(TestData.SystemPrincipal);
        var application = await context.Applications.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.OrganizationId == organizationId);
        if (application is null)
        {
            application = new Core.Entities.Organizations.Application { ApplicationId = Ids.New(), OrganizationId = organizationId, ApplicationCode = $"LED{Guid.NewGuid():N}"[..12], ApplicationName = "Ledger", ApplicationNameAr = "Ledger", DatabaseType = Core.Entities.Organizations.DatabaseType.SqlServer, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            context.Applications.Add(application);
        }

        var dbObject = new DatabaseObject { ObjectId = Ids.New(), OrganizationId = organizationId, ApplicationId = application.ApplicationId, SchemaName = "dbo", ObjectName = $"Ledger{Guid.NewGuid():N}"[..20], ObjectType = DatabaseObjectType.Table, IsApproved = true, DiscoveredAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        context.DatabaseObjects.Add(dbObject);
        await context.SaveChangesAsync();
        return dbObject.ObjectId;
    }

    private static AnalysisAccessFact Fact(Guid organizationId, Guid userId, Guid? objectId, DateTimeOffset at, string roles, bool restricted = false) => new()
    {
        FactId = Ids.New(),
        OrganizationId = organizationId,
        UserId = userId,
        RoleCodes = roles,
        ApplicationId = Guid.NewGuid(),
        MetricId = Ids.New(),
        ObjectId = objectId,
        SchemaName = "dbo",
        ObjectName = objectId is null ? "RetentionProbe" : "LedgerTable",
        IsPrimaryObject = true,
        AccessedAt = at,
        RowCount = 10,
        HighestClassification = restricted ? DataClassification.Restricted : DataClassification.Internal,
        Outcome = AccessOutcome.Succeeded,
    };
}

/// <summary>The scheduler tests store no files; retention only asks to delete them.</summary>
internal sealed class NoFilesStorage : Core.Storage.IArtifactStorage
{
    public Task<string> SaveAsync(string category, Stream content, CancellationToken cancellationToken = default) => Task.FromResult($"{category}/{Guid.NewGuid():N}");

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
