using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Email;
using WabiInsight.Infrastructure.Email;

namespace WabiInsight.UnitTests.Email;

/// <summary>
/// The outbox worker must outlive every failure of a single message (an exception escaping a hosted service
/// stops the API), keep sending during the shutdown grace period, and report rows a previous process left behind.
/// </summary>
public sealed class EmailOutboxWorkerTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StatusUpdateFailure_DoesNotEndTheWorker_AndTheNextMessageIsDelivered()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var sender = new FakeSender();
        var log = new FakeDeliveryLog { FailRecordingFor = first };
        using var harness = new Harness(sender, log);
        await harness.EnqueueAsync(first, "first@org.test");
        await harness.EnqueueAsync(second, "second@org.test");

        await harness.Worker.StartAsync(CancellationToken.None);
        await log.WaitForAsync(r => r.Id == second && r.Status == EmailDeliveryStatus.Sent);

        Assert.Equal(["first@org.test", "second@org.test"], sender.Delivered);
        Assert.False(harness.Worker.ExecuteTask!.IsCompleted);
        Assert.DoesNotContain(log.Records, r => r.Id == first);

        // Still alive: a message queued afterwards is delivered too.
        var third = Guid.NewGuid();
        await harness.EnqueueAsync(third, "third@org.test");
        await log.WaitForAsync(r => r.Id == third && r.Status == EmailDeliveryStatus.Sent);

        await harness.Worker.StopAsync(CancellationToken.None);
        Assert.True(harness.Worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DeliveryFailure_WhoseOutcomeCannotBeRecorded_DoesNotEndTheWorker()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var sender = new FakeSender { FailFor = "first@org.test" };
        var log = new FakeDeliveryLog { FailRecordingFor = first };
        using var harness = new Harness(sender, log, new EmailOptions { MaxAttempts = 2, RetryDelaySeconds = 0 });
        await harness.EnqueueAsync(first, "first@org.test");
        await harness.EnqueueAsync(second, "second@org.test");

        await harness.Worker.StartAsync(CancellationToken.None);
        await log.WaitForAsync(r => r.Id == second && r.Status == EmailDeliveryStatus.Sent);

        Assert.Equal(2, sender.Attempts.Count(a => a == "first@org.test"));
        Assert.Equal(["second@org.test"], sender.Delivered);
        Assert.False(harness.Worker.ExecuteTask!.IsCompleted);
        await harness.Worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AMessageToASampleAddress_IsSkippedWithoutAnAttempt_ByARealMailServer_AndTakenByAFileSink()
    {
        var sample = Guid.NewGuid();
        var real = Guid.NewGuid();
        var sender = new FakeSender { DeliversToRealMailboxes = true };
        var log = new FakeDeliveryLog();
        using (var harness = new Harness(sender, log))
        {
            await harness.EnqueueAsync(sample, "khalid.alsuwaidi@gulfaviation.example");
            await harness.EnqueueAsync(real, "owner@wabitex.com");
            await harness.Worker.StartAsync(CancellationToken.None);
            await log.WaitForAsync(r => r.Id == real && r.Status == EmailDeliveryStatus.Sent);
            await harness.Worker.StopAsync(CancellationToken.None);
        }

        // Never handed to the server, recorded as skipped (not failed) with the reason and no attempt.
        Assert.Equal(["owner@wabitex.com"], sender.Attempts);
        var skipped = Assert.Single(log.Records, r => r.Id == sample);
        Assert.Equal(EmailDeliveryStatus.Skipped, skipped.Status);
        Assert.Equal(0, skipped.Attempts);
        Assert.Equal(EmailFailureReasons.RecipientAddressReserved, skipped.Error);

        // The development file sink writes every message, sample addresses included.
        var sink = new FakeSender();
        var sinkLog = new FakeDeliveryLog();
        using var local = new Harness(sink, sinkLog);
        await local.EnqueueAsync(sample, "khalid.alsuwaidi@gulfaviation.example");
        await local.Worker.StartAsync(CancellationToken.None);
        await sinkLog.WaitForAsync(r => r.Id == sample && r.Status == EmailDeliveryStatus.Sent);
        await local.Worker.StopAsync(CancellationToken.None);
        Assert.Equal(["khalid.alsuwaidi@gulfaviation.example"], sink.Delivered);
    }

    [Fact]
    public async Task FailedDelivery_IsRetried_AndRecordedAsFailedAfterTheLastAttempt()
    {
        var id = Guid.NewGuid();
        var sender = new FakeSender { FailFor = "unreachable@org.test" };
        var log = new FakeDeliveryLog();
        using var harness = new Harness(sender, log, new EmailOptions { MaxAttempts = 3, RetryDelaySeconds = 0 });
        await harness.EnqueueAsync(id, "unreachable@org.test");

        await harness.Worker.StartAsync(CancellationToken.None);
        await log.WaitForAsync(r => r.Id == id && r.Status == EmailDeliveryStatus.Failed);
        await harness.Worker.StopAsync(CancellationToken.None);

        Assert.Equal(
            [(1, EmailDeliveryStatus.Queued), (2, EmailDeliveryStatus.Queued), (3, EmailDeliveryStatus.Failed)],
            log.Records.Where(r => r.Id == id).Select(r => (r.Attempts, r.Status)));
        Assert.All(log.Records, r => Assert.Equal("Simulated SMTP failure.", r.Error));
    }

    [Fact]
    public async Task Shutdown_DeliversTheMessagesAlreadyQueued_DuringTheGracePeriod()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var sender = new FakeSender { HoldFirstSend = true };
        var log = new FakeDeliveryLog();
        using var harness = new Harness(sender, log, new EmailOptions { ShutdownGraceSeconds = 20 });
        for (var i = 0; i < ids.Length; i++)
        {
            await harness.EnqueueAsync(ids[i], $"user{i}@org.test");
        }

        await harness.Worker.StartAsync(CancellationToken.None);
        await sender.FirstSendStarted.Task.WaitAsync(Timeout);

        // The API stops while the first message is being sent and two more are waiting in memory.
        var stop = harness.Worker.StopAsync(CancellationToken.None);
        Assert.False(stop.IsCompleted);
        sender.ReleaseFirstSend.SetResult();
        await stop.WaitAsync(Timeout);

        Assert.Equal(["user0@org.test", "user1@org.test", "user2@org.test"], sender.Delivered);
        Assert.All(ids, id => Assert.Contains(log.Records, r => r.Id == id && r.Status == EmailDeliveryStatus.Sent));
        Assert.True(harness.Worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Shutdown_RunsTheRemainingAttemptsWithoutTheRetryDelay()
    {
        var id = Guid.NewGuid();
        var sender = new FakeSender { FailFor = "unreachable@org.test" };
        var log = new FakeDeliveryLog();
        using var harness = new Harness(sender, log, new EmailOptions { MaxAttempts = 3, RetryDelaySeconds = 3600, ShutdownGraceSeconds = 20 });
        await harness.EnqueueAsync(id, "unreachable@org.test");

        await harness.Worker.StartAsync(CancellationToken.None);
        await log.WaitForAsync(r => r.Id == id && r.Attempts == 1);
        await harness.Worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        var last = log.Records.Last(r => r.Id == id);
        Assert.Equal((3, EmailDeliveryStatus.Failed), (last.Attempts, last.Status));
    }

    [Fact]
    public async Task Shutdown_GivesUpWhenTheGracePeriodIsOver_WithoutFailing()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var sender = new FakeSender { HoldFirstSend = true };
        var log = new FakeDeliveryLog();
        using var harness = new Harness(sender, log, new EmailOptions { ShutdownGraceSeconds = 1 });
        await harness.EnqueueAsync(first, "slow@org.test");
        await harness.EnqueueAsync(second, "waiting@org.test");

        await harness.Worker.StartAsync(CancellationToken.None);
        await sender.FirstSendStarted.Task.WaitAsync(Timeout);
        await harness.Worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        // Neither message was sent; their rows stay Queued until the next start marks them Failed.
        Assert.True(harness.Worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Empty(sender.Delivered);
        Assert.Empty(log.Records);
    }

    [Fact]
    public async Task StartUp_MarksRowsLeftByAPreviousProcess_OlderThanTheSafetyMargin_ThenThoseQueuedBeforeThisStart()
    {
        var log = new FakeDeliveryLog();
        using var harness = new Harness(new FakeSender(), log, margin: TimeSpan.FromMilliseconds(100));

        await harness.Worker.StartAsync(CancellationToken.None);
        await log.WaitForAbandonedChecksAsync(2);
        await harness.Worker.StopAsync(CancellationToken.None);

        Assert.Equal([StartedAt - TimeSpan.FromMilliseconds(100), StartedAt], log.AbandonedChecks);
    }

    [Fact]
    public async Task StartUp_UsesATwoMinuteSafetyMargin_ByDefault()
    {
        var log = new FakeDeliveryLog();
        using var harness = new Harness(new FakeSender(), log);

        await harness.Worker.StartAsync(CancellationToken.None);
        await log.WaitForAbandonedChecksAsync(1);
        await harness.Worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal([StartedAt.AddMinutes(-2)], log.AbandonedChecks);
    }

    [Fact]
    public async Task StartUp_CheckThatFails_DoesNotEndTheWorker()
    {
        var id = Guid.NewGuid();
        var sender = new FakeSender();
        var log = new FakeDeliveryLog { FailAbandonedChecks = true };
        using var harness = new Harness(sender, log, margin: TimeSpan.FromMilliseconds(50));

        await harness.Worker.StartAsync(CancellationToken.None);
        await log.WaitForAbandonedChecksAsync(2);
        await harness.EnqueueAsync(id, "user@org.test");
        await log.WaitForAsync(r => r.Id == id && r.Status == EmailDeliveryStatus.Sent);

        Assert.False(harness.Worker.ExecuteTask!.IsCompleted);
        await harness.Worker.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(20, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void ShutdownGraceSeconds_IsValidatedLikeTheOtherEmailOptions(int seconds, bool valid)
    {
        var options = new EmailOptions { ShutdownGraceSeconds = seconds };

        var result = new DataAnnotationValidateOptions<EmailOptions>(null).Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
        Assert.Equal(20, new EmailOptions().ShutdownGraceSeconds);
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly EmailDeliveryChannel _channel = new();

        public Harness(FakeSender sender, FakeDeliveryLog log, EmailOptions? options = null, TimeSpan? margin = null)
        {
            _services = new ServiceCollection().AddScoped<IEmailDeliveryLog>(_ => log).BuildServiceProvider();
            Worker = new EmailOutboxWorker(
                _channel,
                _services.GetRequiredService<IServiceScopeFactory>(),
                new PlatformEmailRoute(sender),
                new FixedClock(),
                Options.Create(options ?? new EmailOptions()),
                NullLogger<EmailOutboxWorker>.Instance)
            {
                AbandonedSafetyMargin = margin ?? EmailOutboxWorker.DefaultAbandonedSafetyMargin,
            };
        }

        public EmailOutboxWorker Worker { get; }

        public ValueTask EnqueueAsync(Guid id, string to) =>
            _channel.Channel.Writer.WriteAsync((id, new EmailMessage(to, "User", "Subject", "<p>Body</p>", "Body", "en")));

        public void Dispose()
        {
            Worker.Dispose();
            _services.Dispose();
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => StartedAt;
    }

    private sealed class FakeSender : IEmailSender
    {
        private readonly ConcurrentQueue<string> _attempts = new();
        private readonly ConcurrentQueue<string> _delivered = new();
        private int _sends;

        public string? FailFor { get; init; }

        /// <summary>A real mail server (true) or a sink that takes anything (false, the default).</summary>
        public bool DeliversToRealMailboxes { get; init; }

        /// <summary>The first send waits until <see cref="ReleaseFirstSend"/> is set (or the delivery is cancelled).</summary>
        public bool HoldFirstSend { get; init; }

        public TaskCompletionSource FirstSendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Attempts => _attempts.ToArray();

        public IReadOnlyList<string> Delivered => _delivered.ToArray();

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            _attempts.Enqueue(message.ToEmail);
            if (Interlocked.Increment(ref _sends) == 1 && HoldFirstSend)
            {
                FirstSendStarted.SetResult();
                await ReleaseFirstSend.Task.WaitAsync(cancellationToken);
            }

            if (message.ToEmail == FailFor)
            {
                throw new InvalidOperationException("Simulated SMTP failure.");
            }

            _delivered.Enqueue(message.ToEmail);
        }
    }

    private sealed class FakeDeliveryLog : IEmailDeliveryLog
    {
        private readonly ConcurrentQueue<(Guid Id, int Attempts, EmailDeliveryStatus Status, string? Error)> _records = new();
        private readonly ConcurrentQueue<DateTimeOffset> _abandonedChecks = new();

        /// <summary>Recording the outcome of this message throws, as when the configuration database is unavailable.</summary>
        public Guid? FailRecordingFor { get; init; }

        public bool FailAbandonedChecks { get; init; }

        public IReadOnlyList<(Guid Id, int Attempts, EmailDeliveryStatus Status, string? Error)> Records => _records.ToArray();

        public IReadOnlyList<DateTimeOffset> AbandonedChecks => _abandonedChecks.ToArray();

        public Task RecordAsync(Guid emailMessageId, int attempts, EmailDeliveryStatus status, string? lastError, CancellationToken cancellationToken = default)
        {
            if (emailMessageId == FailRecordingFor)
            {
                throw new InvalidOperationException("The configuration database is unavailable.");
            }

            _records.Enqueue((emailMessageId, attempts, status, lastError));
            return Task.CompletedTask;
        }

        public Task<int> FailAbandonedAsync(DateTimeOffset queuedBefore, CancellationToken cancellationToken = default)
        {
            _abandonedChecks.Enqueue(queuedBefore);
            return FailAbandonedChecks ? throw new InvalidOperationException("The configuration database is unavailable.") : Task.FromResult(0);
        }

        public Task WaitForAsync(Func<(Guid Id, int Attempts, EmailDeliveryStatus Status, string? Error), bool> predicate) =>
            WaitUntilAsync(() => _records.Any(predicate));

        public Task WaitForAbandonedChecksAsync(int count) => WaitUntilAsync(() => _abandonedChecks.Count >= count);

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("Condition not met in time.");
                }

                await Task.Delay(10);
            }
        }
    }
}
