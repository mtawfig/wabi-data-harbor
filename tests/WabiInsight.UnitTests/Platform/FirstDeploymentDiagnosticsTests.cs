using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Common;
using WabiInsight.Infrastructure.Health;
using WabiInsight.Infrastructure.Logging;
using WabiInsight.Infrastructure.Storage;

namespace WabiInsight.UnitTests.Platform;

/// <summary>P13.33: what makes a first deployment diagnosable: the application's log file and the folder checks of "ready".</summary>
public sealed class FirstDeploymentDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wi-diagnostics-tests", Guid.NewGuid().ToString("N"));

    public FirstDeploymentDiagnosticsTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class MovableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 200; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    /// <summary>One log entry through the interface every logger call ends in.</summary>
    private static void Write(ILogger logger, LogLevel level, string message, Exception? exception = null)
        => logger.Log(level, default, message, exception, static (text, _) => text);

    private static ServiceProvider Logging(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddWabiInsightLogFile(configuration);
            })
            .BuildServiceProvider();
    }

    [Fact]
    public void LogFile_ReceivesWhatTheLogLevelSettingsLetThrough_OneEntryPerLine()
    {
        var logs = Path.Combine(_root, "logs");
        var services = Logging(new()
        {
            ["Logging:LogLevel:Default"] = "Information",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
            ["Logging:File:Directory"] = logs,
        });
        var factory = services.GetRequiredService<ILoggerFactory>();
        var email = factory.CreateLogger("WabiInsight.Infrastructure.Email.EmailOutboxWorker");
        var framework = factory.CreateLogger("Microsoft.AspNetCore.Routing");

        Write(email, LogLevel.Information, "Email 42 sent to user@org.test");
        Write(email, LogLevel.Debug, "too detailed for the file");
        Write(framework, LogLevel.Information, "request matched");
        Write(framework, LogLevel.Warning, "slow request");
        Write(email, LogLevel.Information, "a value with\r\na line break");
        Write(email, LogLevel.Error, "Delivery failed for 43", new InvalidOperationException("the mail server refused"));

        // Closing the application writes what is still queued.
        services.Dispose();

        var file = Assert.Single(Directory.GetFiles(logs));
        Assert.Equal($"wabi-insight-{DateTime.UtcNow:yyyyMMdd}.log", Path.GetFileName(file));
        var lines = File.ReadAllLines(file);
        Assert.Matches(@"^﻿?\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z INF \[WabiInsight\.Infrastructure\.Email\.EmailOutboxWorker\] Email 42 sent to user@org\.test$", lines[0]);
        Assert.EndsWith("WRN [Microsoft.AspNetCore.Routing] slow request", lines[1], StringComparison.Ordinal);
        Assert.EndsWith("a value with ⏎ a line break", lines[2], StringComparison.Ordinal);
        Assert.EndsWith("ERR [WabiInsight.Infrastructure.Email.EmailOutboxWorker] Delivery failed for 43", lines[3], StringComparison.Ordinal);
        Assert.StartsWith("    System.InvalidOperationException: the mail server refused", lines[4], StringComparison.Ordinal);
        var text = string.Join('\n', lines);
        Assert.DoesNotContain("too detailed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("request matched", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LogFile_IsOff_UntilAFolderIsNamed()
    {
        using var services = Logging(new() { ["Logging:LogLevel:Default"] = "Information", ["Logging:File:RetentionDays"] = "7" });
        Assert.DoesNotContain(services.GetServices<ILoggerProvider>(), provider => provider is FileLoggerProvider);
    }

    [Fact]
    public async Task LogFile_StartsANewFileEachDay_AndDeletesOnlyItsOwnOldFiles()
    {
        var now = new DateTimeOffset(2026, 11, 10, 23, 59, 0, TimeSpan.Zero);
        var old = Path.Combine(_root, "wabi-insight-20261001.log");
        var recent = Path.Combine(_root, "wabi-insight-20261105.log");
        var deployLog = Path.Combine(_root, "deploy.log");
        foreach (var path in new[] { old, recent, deployLog })
        {
            await File.WriteAllTextAsync(path, "earlier");
        }

        using (var provider = new FileLoggerProvider(new FileLogOptions { Directory = _root, RetentionDays = 30 }, () => now))
        {
            var logger = provider.CreateLogger("Test");
            Write(logger, LogLevel.Information, "before midnight");
            await WaitForAsync(() => File.Exists(Path.Combine(_root, "wabi-insight-20261110.log")), "the first day's file");

            now = new DateTimeOffset(2026, 11, 11, 0, 0, 5, TimeSpan.Zero);
            Write(logger, LogLevel.Information, "after midnight");
            await WaitForAsync(() => File.Exists(Path.Combine(_root, "wabi-insight-20261111.log")), "the second day's file");
        }

        Assert.Contains("before midnight", await File.ReadAllTextAsync(Path.Combine(_root, "wabi-insight-20261110.log")), StringComparison.Ordinal);
        Assert.Contains("after midnight", await File.ReadAllTextAsync(Path.Combine(_root, "wabi-insight-20261111.log")), StringComparison.Ordinal);
        // Older than 30 days: gone. Within 30 days, and files that are not this application's log: kept.
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.Equal("earlier", await File.ReadAllTextAsync(deployLog));
    }

    [Fact]
    public async Task LogFile_ContinuesInANumberedPart_WhenTheDaysFileIsFull()
    {
        var now = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        using (var provider = new FileLoggerProvider(new FileLogOptions { Directory = _root }, () => now) { MaxFileBytes = 300 })
        {
            var logger = provider.CreateLogger("Test");
            Write(logger, LogLevel.Information, new string('a', 400));
            await WaitForAsync(() => File.Exists(Path.Combine(_root, "wabi-insight-20261111.log")), "the day's file");
            Write(logger, LogLevel.Information, "second entry");
            await WaitForAsync(() => File.Exists(Path.Combine(_root, "wabi-insight-20261111-2.log")), "the second part");
        }

        Assert.Contains("second entry", await File.ReadAllTextAsync(Path.Combine(_root, "wabi-insight-20261111-2.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void LogFile_IsWrittenByItsOwnThread_SoABusyThreadPoolCannotDelayOrLoseIt()
    {
        // The writer asks the clock for the day's file name: note which threads ask.
        var writerThreads = new System.Collections.Concurrent.ConcurrentBag<(string? Name, bool Pool)>();
        var test = Environment.CurrentManagedThreadId;
        var provider = new FileLoggerProvider(new FileLogOptions { Directory = _root }, () =>
        {
            if (Environment.CurrentManagedThreadId != test)
            {
                writerThreads.Add((Thread.CurrentThread.Name, Thread.CurrentThread.IsThreadPoolThread));
            }

            return new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
        });
        Write(provider.CreateLogger("Test"), LogLevel.Information, "written at shutdown");

        // Closing right away still writes the entry: nothing has to be scheduled on the thread pool first.
        provider.Dispose();
        provider.Dispose(); // closing twice is harmless

        Assert.Contains("written at shutdown", File.ReadAllText(Path.Combine(_root, "wabi-insight-20261113.log")), StringComparison.Ordinal);
        Assert.NotEmpty(writerThreads);
        Assert.All(writerThreads, thread =>
        {
            Assert.Equal("Wabi Insight log file", thread.Name);
            Assert.False(thread.Pool);
        });
        // Logging after the provider is closed is ignored, not an error.
        Write(provider.CreateLogger("Test"), LogLevel.Information, "too late");
    }

    [Fact]
    public void LogFile_ThatCannotBeWritten_NeverGetsInTheApplicationsWay()
    {
        // A folder "inside" a file cannot be created, on any system.
        var blocker = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(blocker, "x");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using (var provider = new FileLoggerProvider(new FileLogOptions { Directory = Path.Combine(blocker, "logs") }, () => DateTimeOffset.UtcNow) { RetryAfterFailure = TimeSpan.FromMilliseconds(50) })
        {
            var logger = provider.CreateLogger("Test");
            for (var i = 0; i < 1000; i++)
            {
                Write(logger, LogLevel.Warning, "entry");
            }
        }

        // Neither logging nor shutting down waits for the folder.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"took {watch.Elapsed}");
        Assert.Equal("x", File.ReadAllText(blocker));
    }

    [Fact]
    public void FolderCheck_WritesAndRemovesAProbe_AndAsksTheDiskOnceAMinute()
    {
        var clock = new MovableClock();
        var probe = new FolderProbeCache();
        var folder = Path.Combine(_root, "files");

        // The folder is created when missing, and nothing is left in it.
        Assert.Null(probe.Problem(folder, clock.UtcNow));
        Assert.Empty(Directory.GetFileSystemEntries(folder));

        // The folder becomes unusable: within the minute the earlier answer stands, after it the disk is asked again.
        Directory.Delete(folder);
        File.WriteAllText(folder, "now a file");
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        Assert.Null(probe.Problem(folder, clock.UtcNow));
        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        Assert.NotNull(probe.Problem(folder, clock.UtcNow));

        // A failure is never remembered: the moment the folder works again, so does the check.
        File.Delete(folder);
        Assert.Null(probe.Problem(folder, clock.UtcNow));
    }

    [Fact]
    public async Task Ready_NamesTheFolderAndItsSetting_WhenFilesOrKeysCannotBeWritten()
    {
        var clock = new MovableClock();
        var blocker = Path.Combine(_root, "not-a-folder");
        await File.WriteAllTextAsync(blocker, "x");
        var bad = Path.Combine(blocker, "inside");
        var good = Path.Combine(_root, "good");

        var files = await new FileStorageHealthCheck(Options.Create(new StorageOptions { RootPath = good }), new FolderProbeCache(), clock).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, files.Status);
        var keys = await new KeyRingHealthCheck(Options.Create(new DataProtectionOptions { KeyRingPath = good }), new FolderProbeCache(), clock).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, keys.Status);

        var noFiles = await new FileStorageHealthCheck(Options.Create(new StorageOptions { RootPath = bad }), new FolderProbeCache(), clock).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, noFiles.Status);
        Assert.Contains("Storage:RootPath", noFiles.Description, StringComparison.Ordinal);
        Assert.Contains(bad, noFiles.Description, StringComparison.Ordinal);
        var noKeys = await new KeyRingHealthCheck(Options.Create(new DataProtectionOptions { KeyRingPath = bad }), new FolderProbeCache(), clock).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, noKeys.Status);
        Assert.Contains("DataProtection:KeyRingPath", noKeys.Description, StringComparison.Ordinal);

        // Files in Azure Blob Storage: no folder on the server is involved, and none is created.
        var blob = await new FileStorageHealthCheck(Options.Create(new StorageOptions { Provider = StorageProviders.AzureBlob, RootPath = Path.Combine(_root, "unused") }), new FolderProbeCache(), clock).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, blob.Status);
        Assert.False(Directory.Exists(Path.Combine(_root, "unused")));
    }
}
