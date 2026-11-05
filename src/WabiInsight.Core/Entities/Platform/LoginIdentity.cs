using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Platform;

/// <summary>
/// Registry that makes a normalized login email unique across Administrators and OrganizationUsers.
/// Inserted in the same transaction as the account it points to (specification §8).
/// </summary>
public class LoginIdentity
{
    public string NormalizedEmail { get; set; } = string.Empty;

    public PrincipalType PrincipalType { get; set; }

    /// <summary>AdministratorId or UserId depending on <see cref="PrincipalType"/>.</summary>
    public Guid PrincipalId { get; set; }

    /// <summary>Set for organization users so support routing can find the organization without a join.</summary>
    public Guid? OrganizationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
