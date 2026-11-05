using Microsoft.Extensions.Options;
using MimeKit;
using WabiInsight.Core.Email;

namespace WabiInsight.Infrastructure.Email;

/// <summary>Development sink: writes each message as an .eml file instead of sending it.</summary>
public sealed class FileEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public FileEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var directory = Path.GetFullPath(_options.FileSinkDirectory);
        Directory.CreateDirectory(directory);
        using var mime = SmtpEmailSender.BuildMessage(message, _options);
        var path = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.eml");
        await using var stream = File.Create(path);
        await mime.WriteToAsync(stream, cancellationToken);
    }
}

/// <summary>No delivery (Provider = None). The outbox still records the attempt as failed so nothing is silently lost.</summary>
public sealed class NullEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Email delivery is disabled (Email:Provider = None).");
    }
}
