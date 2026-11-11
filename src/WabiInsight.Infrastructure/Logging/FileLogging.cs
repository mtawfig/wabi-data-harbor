using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WabiInsight.Infrastructure.Logging;

/// <summary>The application's own log file, bound from "Logging:File". Off while <see cref="Directory"/> is empty.</summary>
public sealed class FileLogOptions
{
    public const string SectionName = "Logging:File";

    /// <summary>Folder for the log files, for example C:\wabitex-apps\wabi-insight\logs. Empty: no log file is written.</summary>
    public string? Directory { get; set; }

    /// <summary>Log files older than this many days are deleted when a new day's file is opened.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>A day's file that reaches this size is continued in a numbered part (…-2.log).</summary>
    public int MaxFileMegabytes { get; set; } = 50;
}

/// <summary>
/// Writes the application's log to one file a day (<c>wabi-insight-yyyyMMdd.log</c>, UTC), for servers where nothing
/// collects the console output: under IIS the process has no console, and only warnings reach the Windows Event Log,
/// so e-mail deliveries, scheduled runs and start-up messages would be written nowhere (P13.33).
/// Logging never gets in the application's way: lines are queued and written by one background task, a full queue
/// drops lines instead of waiting, and a folder that cannot be written is retried quietly.
/// What is written follows the usual "Logging:LogLevel" settings ("Logging:File:LogLevel" overrides them for the file).
/// </summary>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    internal const string FilePrefix = "wabi-insight-";
    private const int QueueCapacity = 20_000;

    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly string _directory;
    private readonly int _retentionDays;
    private readonly long _maxFileBytes;
    private readonly Func<DateTimeOffset> _now;
    private readonly Task _writer;
    private readonly CancellationTokenSource _stop = new();

    public FileLoggerProvider(IOptions<FileLogOptions> options)
        : this(options.Value, () => DateTimeOffset.UtcNow)
    {
    }

    internal FileLoggerProvider(FileLogOptions options, Func<DateTimeOffset> now)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Directory))
        {
            throw new ArgumentException("Logging:File:Directory is empty.", nameof(options));
        }

        _directory = Path.GetFullPath(options.Directory);
        _retentionDays = Math.Max(1, options.RetentionDays);
        _maxFileBytes = Math.Max(1, options.MaxFileMegabytes) * 1024L * 1024L;
        _now = now;
        _writer = Task.Run(WriteAsync);
    }

    /// <summary>For tests: a size limit in bytes.</summary>
    internal long MaxFileBytes { get; init; }

    /// <summary>How long the writer waits before it tries a folder again that could not be written.</summary>
    internal TimeSpan RetryAfterFailure { get; init; } = TimeSpan.FromSeconds(5);

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try
        {
            // What is queued gets written; a folder that cannot be written does not hold the shutdown up.
            if (!_writer.Wait(TimeSpan.FromSeconds(5)))
            {
                _stop.Cancel();
                _writer.Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch (AggregateException)
        {
            // The writer never throws by design; nothing useful can be done here if it did.
        }

        _stop.Dispose();
    }

    internal void Enqueue(string line) => _queue.Writer.TryWrite(line);

    /// <summary>The single writer: drains the queue in batches, appends to the day's file, rolls it by day and size.</summary>
    private async Task WriteAsync()
    {
        var reader = _queue.Reader;
        var batch = new StringBuilder();
        string? purgedFor = null;
        try
        {
            while (await reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Length < 256 * 1024 && reader.TryRead(out var line))
                {
                    batch.Append(line);
                }

                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        var day = _now().UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                        Directory.CreateDirectory(_directory);
                        if (purgedFor != day)
                        {
                            Purge();
                            purgedFor = day;
                        }

                        await File.AppendAllTextAsync(CurrentFile(day), batch.ToString(), Encoding.UTF8, _stop.Token).ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                    {
                        // The folder is missing, full or not ours to write: keep the batch and try again shortly.
                        if (reader.Completion.IsCompleted)
                        {
                            return; // shutting down: do not hold the process for a folder that does not work
                        }

                        await Task.Delay(RetryAfterFailure, _stop.Token).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    /// <summary>The day's file, or its next numbered part once the limit is reached.</summary>
    private string CurrentFile(string day)
    {
        var limit = MaxFileBytes > 0 ? MaxFileBytes : _maxFileBytes;
        for (var part = 1; ; part++)
        {
            var path = Path.Combine(_directory, part == 1 ? $"{FilePrefix}{day}.log" : $"{FilePrefix}{day}-{part.ToString(CultureInfo.InvariantCulture)}.log");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < limit)
            {
                return path;
            }
        }
    }

    /// <summary>Deletes this application's log files older than the retention; other files in the folder are never touched.</summary>
    private void Purge()
    {
        var oldest = _now().UtcDateTime.Date.AddDays(-_retentionDays);
        foreach (var path in Directory.EnumerateFiles(_directory, FilePrefix + "*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..];
            if (name.Length >= 8
                && DateTime.TryParseExact(name[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day)
                && day < oldest)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use or protected: left for the next day.
                }
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = new StringBuilder(256);
            line.Append(_provider._now().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
                .Append(' ').Append(Label(logLevel))
                .Append(" [").Append(_category).Append("] ")
                .Append(OneLine(formatter(state, exception)))
                .Append('\n');
            if (exception is not null)
            {
                foreach (var part in exception.ToString().Split('\n'))
                {
                    line.Append("    ").Append(part.TrimEnd('\r')).Append('\n');
                }
            }

            _provider.Enqueue(line.ToString());
        }

        /// <summary>A message stays on its own line, so a value with a line break cannot pass for another entry.</summary>
        private static string OneLine(string message) => message.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ⏎ ", StringComparison.Ordinal);

        private static string Label(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}

public static class FileLoggingExtensions
{
    /// <summary>Adds the log file when "Logging:File:Directory" names a folder; does nothing otherwise.</summary>
    public static ILoggingBuilder AddWabiInsightLogFile(this ILoggingBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(FileLogOptions.SectionName);
        if (string.IsNullOrWhiteSpace(section[nameof(FileLogOptions.Directory)]))
        {
            return builder;
        }

        builder.Services.AddOptions<FileLogOptions>().Bind(section);
        builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
        return builder;
    }
}
