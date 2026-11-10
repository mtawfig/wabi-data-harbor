using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Email;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Tenancy;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Email;

/// <summary>
/// A message's content lives only in the memory of the process that queued it. Rows a previous process left
/// Queued can never be sent, so the next start marks them Failed instead of leaving them "queued" forever.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class EmailOutboxRecoveryTests
{
    private readonly SqlServerTestDatabase _db;

    public EmailOutboxRecoveryTests(SqlServerTestDatabase db)
    {
        _db = db;
    }

    [Fact]
    public async Task StartUp_MarksQueuedRowsOfAPreviousProcessAsFailed_AndLeavesFreshOnesAlone()
    {
        var now = DateTimeOffset.UtcNow;
        var staleA = await InsertAsync(_db.Data.OrganizationAId, EmailDeliveryStatus.Queued, now.AddMinutes(-30), attempts: 1, lastError: "Connection timed out");
        var staleB = await InsertAsync(_db.Data.OrganizationBId, EmailDeliveryStatus.Queued, now.AddMinutes(-3));
        var stalePlatform = await InsertAsync(null, EmailDeliveryStatus.Queued, now.AddHours(-5));
        var fresh = await InsertAsync(_db.Data.OrganizationAId, EmailDeliveryStatus.Queued, now.AddSeconds(-20));
        var sent = await InsertAsync(_db.Data.OrganizationAId, EmailDeliveryStatus.Sent, now.AddMinutes(-30), attempts: 1);
        try
        {
            // A new API process starts: the worker checks the outbox on its own, with no request and no principal.
            using var factory = new ApiFactory(_db.ConnectionString);
            using var platform = factory.CreateClient();
            await WaitUntilAsync(async () => (await ReadAsync(stalePlatform)).Status == EmailDeliveryStatus.Failed
                && (await ReadAsync(staleA)).Status == EmailDeliveryStatus.Failed
                && (await ReadAsync(staleB)).Status == EmailDeliveryStatus.Failed);

            foreach (var id in new[] { staleA, staleB, stalePlatform })
            {
                var row = await ReadAsync(id);
                Assert.Equal(EmailFailureReasons.ProcessRestartedBeforeDelivery, row.LastError);
                Assert.Null(row.SentAt);
            }

            Assert.Equal("process_restarted_before_delivery", EmailFailureReasons.ProcessRestartedBeforeDelivery);
            Assert.Equal(1, (await ReadAsync(staleA)).Attempts);
            Assert.Equal(EmailDeliveryStatus.Queued, (await ReadAsync(fresh)).Status);
            Assert.Null((await ReadAsync(fresh)).LastError);
            Assert.Equal(EmailDeliveryStatus.Sent, (await ReadAsync(sent)).Status);

            // One failure event per marked row, attributed to the row's organization: the dashboards count it once.
            await using (var context = _db.CreateContext(TestData.SystemPrincipal))
            {
                var events = await context.UsageEvents.AsNoTracking().IgnoreQueryFilters()
                    .Where(e => e.EventType == UsageEventTypes.EmailFailed && e.EntityType == "EmailMessage")
                    .ToListAsync();
                Assert.Equal(_db.Data.OrganizationAId, Assert.Single(events, e => e.EntityId == staleA.ToString()).OrganizationId);
                Assert.Equal(_db.Data.OrganizationBId, Assert.Single(events, e => e.EntityId == staleB.ToString()).OrganizationId);
                var platformEvent = Assert.Single(events, e => e.EntityId == stalePlatform.ToString());
                Assert.Null(platformEvent.OrganizationId);
                Assert.False(platformEvent.Succeeded);
                Assert.DoesNotContain(events, e => e.EntityId == fresh.ToString() || e.EntityId == sent.ToString());
            }

            // The administrator sees them as failures on System Health, with the reason; nothing of a message body exists to show.
            await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
            var report = await (await platform.GetAsync("/api/platform/health")).ReadJsonAsync();
            var outbox = report.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "email-outbox");
            Assert.Equal("Degraded", outbox.GetProperty("status").GetString());
            Assert.True(outbox.GetProperty("data").GetProperty("notSentBeforeRestartLast24h").GetInt64() >= 3);
            Assert.True(outbox.GetProperty("data").GetProperty("failedLast24h").GetInt64() >= 3);
        }
        finally
        {
            await DeleteAsync(staleA, staleB, stalePlatform, fresh, sent);
        }
    }

    [Fact]
    public async Task FailAbandoned_MarksEachRowOnce_SoSeveralInstancesStartingTogetherAreSafe()
    {
        var now = DateTimeOffset.UtcNow;
        // Younger than the worker's own two-minute margin, so the start-up check of this factory leaves both rows to the calls below.
        var stale = await InsertAsync(_db.Data.OrganizationBId, EmailDeliveryStatus.Queued, now.AddSeconds(-90));
        var fresh = await InsertAsync(_db.Data.OrganizationBId, EmailDeliveryStatus.Queued, now.AddSeconds(-10));
        try
        {
            using var factory = new ApiFactory(_db.ConnectionString);
            var cutoff = now.AddSeconds(-60);

            // Two instances run the same check at the same moment, then one of them runs it again.
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                using var scope = factory.Services.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<IEmailDeliveryLog>().FailAbandonedAsync(cutoff);
            }));
            int again;
            using (var scope = factory.Services.CreateScope())
            {
                again = await scope.ServiceProvider.GetRequiredService<IEmailDeliveryLog>().FailAbandonedAsync(cutoff);
            }

            Assert.True(results.Sum() >= 1);
            Assert.Equal(0, again);
            var row = await ReadAsync(stale);
            Assert.Equal(EmailDeliveryStatus.Failed, row.Status);
            Assert.Equal(EmailFailureReasons.ProcessRestartedBeforeDelivery, row.LastError);
            Assert.Equal(EmailDeliveryStatus.Queued, (await ReadAsync(fresh)).Status);

            await using var context = _db.CreateContext(TestData.SystemPrincipal);
            Assert.Equal(1, await context.UsageEvents.IgnoreQueryFilters().CountAsync(e => e.EventType == UsageEventTypes.EmailFailed && e.EntityId == stale.ToString()));
            Assert.Equal(0, await context.UsageEvents.IgnoreQueryFilters().CountAsync(e => e.EventType == UsageEventTypes.EmailFailed && e.EntityId == fresh.ToString()));
        }
        finally
        {
            await DeleteAsync(stale, fresh);
        }
    }

    private async Task<Guid> InsertAsync(Guid? organizationId, EmailDeliveryStatus status, DateTimeOffset createdAt, int attempts = 0, string? lastError = null)
    {
        await using var context = _db.CreateContext(TestData.SystemPrincipal);
        var record = new EmailMessageRecord
        {
            EmailMessageId = Ids.New(),
            OrganizationId = organizationId,
            RecipientEmail = "recipient@org.test",
            RecipientName = "Recipient",
            Subject = "Your Wabi Insight account",
            TemplateCode = EmailTemplateCodes.AccountCreated,
            Status = status,
            Attempts = attempts,
            LastError = lastError,
            RequestedByType = organizationId is null ? PrincipalType.PlatformAdministrator : PrincipalType.OrganizationUser,
            CreatedAt = createdAt,
            SentAt = status == EmailDeliveryStatus.Sent ? createdAt : null,
        };
        context.EmailMessages.Add(record);
        await context.SaveChangesAsync();
        return record.EmailMessageId;
    }

    private async Task<EmailMessageRecord> ReadAsync(Guid id)
    {
        await using var context = _db.CreateContext(TestData.SystemPrincipal);
        return await context.EmailMessages.AsNoTracking().SingleAsync(e => e.EmailMessageId == id);
    }

    /// <summary>Removes the rows this class added so they never count in another test's e-mail figures.</summary>
    private async Task DeleteAsync(params Guid[] ids)
    {
        await using var context = _db.CreateContext(TestData.SystemPrincipal);
        await context.EmailMessages.Where(e => ids.Contains(e.EmailMessageId)).ExecuteDeleteAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(100);
        }
    }
}
