namespace WabiInsight.Core.Tenancy;

/// <summary>
/// Account category of the current principal. Platform Administrators and Organization Users are
/// stored in separate tables and must always be distinguishable (specification §5, §8).
/// </summary>
public enum PrincipalType
{
    Anonymous = 0,
    PlatformAdministrator = 1,
    OrganizationUser = 2,
}
