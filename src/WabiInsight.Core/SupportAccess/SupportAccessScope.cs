using WabiInsight.Core.Entities.Support;

namespace WabiInsight.Core.SupportAccess;

/// <summary>Pure scope and lifecycle rules of a grant; the authorizer and the service apply them. Unit-tested.</summary>
public static class SupportAccessScope
{
    public const string NewRoleReference = "new";
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

        if (definition.TargetType == SupportTargetType.None)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(targetReference))
        {
            return "support_target_required";
        }

        return entry.TargetReferences.Any(t => string.Equals(t, targetReference.Trim(), StringComparison.OrdinalIgnoreCase)) ? null : "support_target_not_granted";
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

            if (definition.TargetType != SupportTargetType.None && entry.TargetReferences.Count == 0)
            {
                errors.Add("support_target_required");
            }

            if (definition.TargetType == SupportTargetType.None && entry.TargetReferences.Count > 0)
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
