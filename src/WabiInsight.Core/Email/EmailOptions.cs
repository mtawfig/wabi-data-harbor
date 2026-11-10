using System.ComponentModel.DataAnnotations;

namespace WabiInsight.Core.Email;

/// <summary>Email delivery settings, bound from the "Email" section (specification B8).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Smtp, File (writes .eml files, development), or None (log only).</summary>
    [Required]
    public string Provider { get; set; } = "None";

    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    /// <summary>STARTTLS on 587 (or implicit TLS on 465). Plain connections are refused when true.</summary>
    public bool EnableSsl { get; set; } = true;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string FromEmail { get; set; } = "no-reply@localhost";

    public string FromName { get; set; } = "Wabi Insight";

    public string ReplyTo { get; set; } = string.Empty;

    /// <summary>Reserved for DKIM signing (requires a private key; not applied in v1).</summary>
    public string DkimSelector { get; set; } = string.Empty;

    /// <summary>Directory for the File provider; defaults to ./.artifacts/mail.</summary>
    public string FileSinkDirectory { get; set; } = ".artifacts/mail";

    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 3;

    [Range(1, 3600)]
    public int RetryDelaySeconds { get; set; } = 30;

    /// <summary>
    /// When the API stops, how long the outbox worker keeps sending the messages it already holds before
    /// giving up. What is still unsent after that is marked Failed at the next start.
    /// </summary>
    [Range(0, 300)]
    public int ShutdownGraceSeconds { get; set; } = 20;
}
