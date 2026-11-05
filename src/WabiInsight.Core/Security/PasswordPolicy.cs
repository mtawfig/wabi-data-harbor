namespace WabiInsight.Core.Security;

/// <summary>
/// Length-based policy (NIST SP 800-63B): 12–128 characters, not equal to the login email, and the
/// known development sample password is rejected unless explicitly allowed (Development environment only).
/// </summary>
public static class PasswordPolicy
{
    public const string SamplePassword = "my_default_password_change_it";

    public static IReadOnlyList<string> Validate(string? password, string? email, SecurityOptions options, bool allowSamplePassword)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("password_required");
            return errors;
        }

        if (password.Length < options.PasswordMinLength)
        {
            errors.Add("password_too_short");
        }

        if (password.Length > options.PasswordMaxLength)
        {
            errors.Add("password_too_long");
        }

        if (!string.IsNullOrEmpty(email) && string.Equals(password.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("password_equals_email");
        }

        if (!allowSamplePassword && string.Equals(password, SamplePassword, StringComparison.Ordinal))
        {
            errors.Add("password_is_sample");
        }

        return errors;
    }
}
