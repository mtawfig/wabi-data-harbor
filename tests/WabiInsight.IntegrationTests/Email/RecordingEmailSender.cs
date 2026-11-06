using System.Collections.Concurrent;
using WabiInsight.Core.Email;

namespace WabiInsight.IntegrationTests.Email;

/// <summary>Captures messages the outbox worker delivers so tests can read temporary passwords from the email, never from the API.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyCollection<EmailMessage> Messages => _messages.ToArray();

    /// <summary>Set to make deliveries fail (to exercise retries and Failed status).</summary>
    public bool FailDeliveries { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (FailDeliveries)
        {
            throw new InvalidOperationException("Simulated SMTP failure.");
        }

        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task<EmailMessage> WaitForAsync(string toEmail, TimeSpan? timeout = null) => WaitForAsync(toEmail, _ => true, timeout);

    public async Task<EmailMessage> WaitForAsync(string toEmail, Func<EmailMessage, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var match = _messages.FirstOrDefault(m => string.Equals(m.ToEmail, toEmail, StringComparison.OrdinalIgnoreCase) && predicate(m));
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"No email delivered to {toEmail} within the timeout.");
    }

    /// <summary>Extracts the temporary password from the text body ("Temporary password: xxx" / Arabic equivalent).</summary>
    public static string TemporaryPasswordOf(EmailMessage message)
    {
        foreach (var line in message.TextBody.Split('\n'))
        {
            var index = line.IndexOf(": ", StringComparison.Ordinal);
            if (index > 0 && (line.StartsWith("Temporary password", StringComparison.Ordinal) || line.StartsWith("كلمة المرور المؤقتة", StringComparison.Ordinal)))
            {
                return line[(index + 2)..].Trim();
            }
        }

        throw new InvalidOperationException("Temporary password not found in the email body.");
    }
}
