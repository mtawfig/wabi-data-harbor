using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Platform;

/// <summary>
/// One request to choose a new password through a link sent by e-mail (self-service reset, P13.32). The link carries a
/// random token; only its SHA-256 hash is kept here, so reading this table gives nobody a usable link. A request is
/// good once, until it expires, is replaced by a newer request, or the account's password changes by any other way.
/// Like <see cref="LoginIdentity"/> it is read before anyone is signed in, by the hash, never by organization.
/// </summary>
public class PasswordResetRequest
{
    public Guid PasswordResetRequestId { get; set; }

    public PrincipalType PrincipalType { get; set; }

    /// <summary>AdministratorId or UserId depending on <see cref="PrincipalType"/>.</summary>
    public Guid PrincipalId { get; set; }

    /// <summary>The organization of an organization user; null for a Platform Administrator.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>SHA-256 of the token, in hexadecimal. The token itself exists only in the e-mail.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the link was used to set a new password.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>When a newer request for the same account replaced this one.</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Address the request came from, for the audit trail.</summary>
    public string? RequestedFromIp { get; set; }
}
