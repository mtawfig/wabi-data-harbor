namespace WabiInsight.Core.Tenancy;

/// <summary>
/// Read-only view of the current principal for services and the DbContext.
/// OrganizationId comes only from here, never from the request (specification §4.3).
/// </summary>
public interface ITenantContext
{
    TenantPrincipal Principal { get; }

    bool IsAuthenticated => Principal.PrincipalType != PrincipalType.Anonymous;

    bool IsPlatformAdministrator => Principal.PrincipalType == PrincipalType.PlatformAdministrator;

    bool IsOrganizationUser => Principal.PrincipalType == PrincipalType.OrganizationUser;

    Guid? OrganizationId => Principal.OrganizationId;

    /// <summary>Returns the organization of the current organization user or throws for any other principal.</summary>
    Guid RequireOrganizationId()
    {
        return Principal.PrincipalType == PrincipalType.OrganizationUser && Principal.OrganizationId is { } id
            ? id
            : throw new TenantContextException("The current principal is not an organization user.");
    }
}

/// <summary>Allows the authentication middleware (P1.3) and tests to set the principal once per scope.</summary>
public interface ITenantContextInitializer
{
    void Initialize(TenantPrincipal principal);
}

public sealed class TenantContextException : InvalidOperationException
{
    public TenantContextException()
    {
    }

    public TenantContextException(string message)
        : base(message)
    {
    }

    public TenantContextException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
