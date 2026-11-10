using WabiInsight.Core.Entities.Support;

namespace WabiInsight.Core.SupportAccess;

/// <summary>Pure scope and lifecycle rules of a grant; the authorizer and the service apply them. Unit-tested.</summary>
public static class SupportAccessScope
{
    public const string NewRoleReference = "new";

    /// <summary>
    /// Prefix of a target reference that names the e-mail address of a user the operation may create (P13.26). The
    /// organization approves the address with the grant; execution refuses any other one.
    /// </summary>
    public const string EmailReferencePrefix = "email:";
    public const int MaxNewUserEmails = 20;

    public static bool IsEmailReference(string? reference)
        => reference is not null && reference.TrimStart().StartsWith(EmailReferencePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The address inside an e-mail reference, trimmed; null for any other reference.</summary>
    public static string? EmailOf(string? reference)
        => IsEmailReference(reference) ? reference!.TrimStart()[EmailReferencePrefix.Length..].Trim() : null;

    /// <summary>The reference under which an address is stored in a grant: one spelling per address.</summary>
    public static string EmailReference(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return EmailReferencePrefix + email.Trim().ToLowerInvariant();
    }

    /// <summary>The addresses a grant entry names for the user(s) it may create.</summary>
    public static IReadOnlyList<string> EmailsOf(SupportOperationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.TargetReferences.Select(EmailOf).Where(e => e is not null).Select(e => e!).ToList();
    }

    /// <summary>True for an operation that creates a user, so its grant has to name the address(es).</summary>
    public static bool NamesNewUser(SupportOperationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return string.Equals(definition.Code, SupportOperations.CreateOrganizationUser, StringComparison.Ordinal);
    }

    /// <summary>True when the requested address is one of the addresses the grant names (compared as sign-in e-mails are).</summary>
    public static bool IsEmailGranted(IEnumerable<string> grantedEmails, string? requestedEmail)
    {
        ArgumentNullException.ThrowIfNull(grantedEmails);
        if (string.IsNullOrWhiteSpace(requestedEmail))
        {
            return false;
        }

        var wanted = Security.EmailNormalizer.Normalize(requestedEmail);
        return grantedEmails.Any(e => string.Equals(Security.EmailNormalizer.Normalize(e), wanted, StringComparison.Ordinal));
    }
    public const int DefaultExpiryHours = 4;
    public const int MaxExpiryHours = 72;

    public static bool IsUsable(SupportAccessGrantStatus status, DateTimeOffset? expiresAt, DateTimeOffset now)
        => status is SupportAccessGrantStatus.Approved or SupportAccessGrantStatus.Active && expiresAt is { } e && e > now;

    public static bool HasExpired(SupportAccessGrantStatus status, DateTimeOffset? expiresAt, DateTimeOffset now)
        => status is SupportAccessGrantStatus.Approved or SupportAccessGrantStatus.Active && expiresAt is { } e && e <= now;

    /// <summary>Returns null when the operation and target are inside the grant, otherwise a stable error code.</summary>
    public static string? Validate(IReadOnlyList<SupportOperationEntry> allowed, string operation, string? targetReference)
    {
        var definition = SupportOperations.Find(operation);
        if (definition is null)
        {
            return "support_operation_unknown";
        }

        var entry = allowed.FirstOrDefault(a => string.Equals(a.Operation, operation, StringComparison.Ordinal));
        if (entry is null)
        {
            return "support_operation_not_granted";
        }

        if (!definition.Executable)
        {
            return "support_operation_not_available";
        }

        if (!definition.RequiresTarget)
        {
            // Nothing to name per call. Roles an operation may assign are checked separately (RolesOutsideGrant).
            return null;
        }

        if (string.IsNullOrWhiteSpace(targetReference))
        {
            return "support_target_required";
        }

        return entry.TargetReferences.Any(t => string.Equals(t, targetReference.Trim(), StringComparison.OrdinalIgnoreCase)) ? null : "support_target_not_granted";
    }

    /// <summary>
    /// For an operation whose targets are the roles it may assign (<see cref="SupportTargetType.AssignableRole"/>):
    /// the requested role codes that are not among the granted roles. Empty means every requested role was approved;
    /// a grant that lists no role approves none.
    /// </summary>
    public static IReadOnlyList<string> RolesOutsideGrant(IEnumerable<string> grantedRoleCodes, IEnumerable<string> requestedRoleCodes)
    {
        ArgumentNullException.ThrowIfNull(grantedRoleCodes);
        ArgumentNullException.ThrowIfNull(requestedRoleCodes);
        var granted = grantedRoleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requestedRoleCodes.Where(code => !granted.Contains(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Validates a proposal: known operations, targets where required, no duplicates.</summary>
    public static IReadOnlyList<string> ValidateProposal(IReadOnlyList<SupportOperationEntry> entries, int? expiryHours)
    {
        var errors = new List<string>();
        if (entries.Count == 0)
        {
            errors.Add("support_operations_required");
        }

        if (entries.Select(e => e.Operation).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            errors.Add("support_operations_duplicate");
        }

        foreach (var entry in entries)
        {
            var definition = SupportOperations.Find(entry.Operation);
            if (definition is null)
            {
                errors.Add("support_operation_unknown");
                continue;
            }

            if (definition.RequiresTarget && entry.TargetReferences.Count == 0)
            {
                errors.Add("support_target_required");
            }

            if (definition.TargetType == SupportTargetType.None && entry.TargetReferences.Count > 0)
            {
                errors.Add("support_target_not_applicable");
            }

            // The address of a user to create is part of what the organization approves (P13.26).
            var emails = EmailsOf(entry);
            if (NamesNewUser(definition))
            {
                if (emails.Count == 0)
                {
                    errors.Add("support_new_user_email_required");
                }
                else if (emails.Count > MaxNewUserEmails || emails.Any(e => !Security.EmailNormalizer.IsPlausible(e)))
                {
                    errors.Add("support_new_user_email_invalid");
                }
            }
            else if (emails.Count > 0)
            {
                errors.Add("support_target_not_applicable");
            }
        }

        if (expiryHours is { } hours && (hours < 1 || hours > MaxExpiryHours))
        {
            errors.Add("support_expiry_out_of_range");
        }

        return errors.Distinct(StringComparer.Ordinal).ToList();
    }
}
