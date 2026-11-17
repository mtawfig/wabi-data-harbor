using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using WabiInsight.Core.Email;

namespace WabiInsight.Infrastructure.Email;

/// <summary>SMTP delivery through MailKit. Port 465 uses implicit TLS, otherwise STARTTLS is required when EnableSsl is true.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    public bool DeliversToRealMailboxes => true;

    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            throw new InvalidOperationException("Email:Host is not configured.");
        }

        using var mime = BuildMessage(message, _options);
        using var client = new SmtpClient();

        var socket = _options.EnableSsl
            ? (_options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
            : SecureSocketOptions.None;

        await client.ConnectAsync(_options.Host, _options.Port, socket, cancellationToken);
        if (!string.IsNullOrEmpty(_options.UserName))
        {
            await client.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    internal static MimeMessage BuildMessage(EmailMessage message, EmailOptions options)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromEmail));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
        var replyTo = message.ReplyTo ?? (string.IsNullOrWhiteSpace(options.ReplyTo) ? null : options.ReplyTo);
        if (replyTo is not null)
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        }

        mime.Subject = message.Subject;
        mime.Headers.Add("X-Wabi-Insight-Language", message.Language);
        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        foreach (var attachment in message.Attachments)
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mime.Body = body.ToMessageBody();
        return mime;
    }
}
