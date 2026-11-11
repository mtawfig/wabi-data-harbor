using System.ComponentModel.DataAnnotations;

namespace WabiInsight.Core.Security;

/// <summary>Platform security defaults (specification §8). Bound from the "Security" configuration section.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    [Range(8, 128)]
    public int PasswordMinLength { get; set; } = 12;

    [Range(16, 1024)]
    public int PasswordMaxLength { get; set; } = 128;

    [Range(1, 100)]
    public int MaxFailedLoginAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;

    [Range(1, 1440)]
    public int SessionIdleMinutes { get; set; } = 30;

    [Range(1, 168)]
    public int SessionAbsoluteHours { get; set; } = 12;

    /// <summary>How often a session's security stamp is re-checked against the database. 0 = every request.</summary>
    [Range(0, 60)]
    public int SecurityStampValidationMinutes { get; set; } = 5;

    [Range(1, 100000)]
    public int LoginRateLimitPerMinute { get; set; } = 10;

    [Range(1, 100000)]
    public int BootstrapRateLimitPerMinute { get; set; } = 3;

    /// <summary>How long a password reset link stays usable (self-service reset).</summary>
    [Range(5, 1440)]
    public int PasswordResetLinkMinutes { get; set; } = 30;

    /// <summary>Reset links one account may ask for in an hour; further requests are ignored without a word.</summary>
    [Range(1, 100)]
    public int PasswordResetRequestsPerHour { get; set; } = 3;

    /// <summary>Requests per minute per client address on the reset endpoints (ask for a link, check it, use it).</summary>
    [Range(1, 100000)]
    public int PasswordResetRateLimitPerMinute { get; set; } = 10;

    /// <summary>Requests per minute per signed-in principal (or client address when anonymous) across the whole API.</summary>
    [Range(1, 1000000)]
    public int ApiRateLimitPerMinute { get; set; } = 600;

    /// <summary>Analysis and planning requests per minute per user: each one costs model tokens and source queries.</summary>
    [Range(1, 100000)]
    public int AiRateLimitPerMinute { get; set; } = 30;

    /// <summary>Default request body limit for endpoints that do not declare their own (uploads do).</summary>
    [Range(1024, 1024L * 1024 * 1024)]
    public long MaxRequestBodyBytes { get; set; } = 1024 * 1024;
}
