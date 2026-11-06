using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Storage;

namespace WabiInsight.Infrastructure.Storage;

/// <summary>Attachment scanning settings, bound from the "AttachmentScanning" section.</summary>
public sealed class AttachmentScanningOptions
{
    public const string SectionName = "AttachmentScanning";

    /// <summary>"None" accepts every file (development); "ClamAv" streams each upload to a clamd daemon.</summary>
    public string Provider { get; set; } = "None";

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 3310;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>When false (default) an unreachable scanner rejects the upload instead of letting it through.</summary>
    public bool FailOpen { get; set; }
}

/// <summary>
/// Real virus scanning through ClamAV's clamd INSTREAM protocol (P12.1, specification §15.10): the file is streamed
/// in length-prefixed chunks over TCP, the daemon answers "stream: OK" or "stream: &lt;signature&gt; FOUND". The scanner
/// fails closed by default: a connection failure, timeout or unexpected reply rejects the upload.
/// </summary>
public sealed partial class ClamAvAttachmentScanner : IAttachmentScanner
{
    private const int ChunkSize = 64 * 1024;
    private readonly AttachmentScanningOptions _options;
    private readonly ILogger<ClamAvAttachmentScanner> _logger;

    public ClamAvAttachmentScanner(IOptions<AttachmentScanningOptions> options, ILogger<ClamAvAttachmentScanner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AttachmentScanResult> ScanAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
            var buffer = new byte[ChunkSize];
            var prefix = new byte[4];
            int read;
            while ((read = await content.ReadAsync(buffer, timeout.Token)) > 0)
            {
                BinaryPrimitives.WriteInt32BigEndian(prefix, read);
                await stream.WriteAsync(prefix, timeout.Token);
                await stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            BinaryPrimitives.WriteInt32BigEndian(prefix, 0);
            await stream.WriteAsync(prefix, timeout.Token);
            await stream.FlushAsync(timeout.Token);

            var reply = await ReadReplyAsync(stream, timeout.Token);
            return Interpret(fileName, reply);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(_logger, ex, fileName, _options.Host, _options.Port);
            return _options.FailOpen ? AttachmentScanResult.Clean : new AttachmentScanResult(false, "scanner_unavailable");
        }
    }

    internal AttachmentScanResult Interpret(string fileName, string reply)
    {
        var text = reply.Trim('\0', '\r', '\n', ' ');
        if (text.EndsWith(" OK", StringComparison.Ordinal))
        {
            return AttachmentScanResult.Clean;
        }

        if (text.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            var signature = text["stream: ".Length..^" FOUND".Length];
            LogInfected(_logger, fileName, signature);
            return new AttachmentScanResult(false, "infected");
        }

        LogUnexpected(_logger, fileName, text.Length > 200 ? text[..200] : text);
        return _options.FailOpen ? AttachmentScanResult.Clean : new AttachmentScanResult(false, "scanner_error");
    }

    private static async Task<string> ReadReplyAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new byte[256];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (buffer.AsSpan(0, read).Contains((byte)0) || builder.Length > 4096)
            {
                break;
            }
        }

        return builder.ToString();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "ClamAV scan of {FileName} failed: clamd at {Host}:{Port} unavailable.")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string fileName, string host, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ClamAV rejected {FileName}: {Signature}.")]
    private static partial void LogInfected(ILogger logger, string fileName, string signature);

    [LoggerMessage(Level = LogLevel.Error, Message = "ClamAV returned an unexpected reply for {FileName}: {Reply}.")]
    private static partial void LogUnexpected(ILogger logger, string fileName, string reply);
}
