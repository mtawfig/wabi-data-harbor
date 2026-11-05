using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.IntegrationTests.Database;

/// <summary>
/// Two organizations, one user each, one platform administrator. Every tenant-isolation test uses both
/// organizations (specification §30: never test isolation with a single organization).
/// </summary>
public sealed class TestData
{
    public static readonly TenantPrincipal SystemPrincipal =
        TenantPrincipal.ForPlatformAdministrator(Guid.Parse("00000000-0000-0000-0000-000000000001"), "system");

    public Guid OrganizationAId { get; } = Ids.New();

    public Guid OrganizationBId { get; } = Ids.New();

    public Guid UserA1Id { get; } = Ids.New();

    public Guid UserB1Id { get; } = Ids.New();

    public Guid AdministratorId { get; } = Ids.New();

    public TenantPrincipal OrganizationAUser => TenantPrincipal.ForOrganizationUser(OrganizationAId, UserA1Id, "Alice A");

    public TenantPrincipal OrganizationBUser => TenantPrincipal.ForOrganizationUser(OrganizationBId, UserB1Id, "Bob B");

    public TenantPrincipal PlatformAdministrator => TenantPrincipal.ForPlatformAdministrator(AdministratorId, "Platform Admin");

    public async Task SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext(PlatformAdministrator);

        context.Organizations.AddRange(
            NewOrganization(OrganizationAId, "ORG-A", "Organization A", "المؤسسة أ"),
            NewOrganization(OrganizationBId, "ORG-B", "Organization B", "المؤسسة ب"));

        context.Administrators.Add(new Administrator
        {
            AdministratorId = AdministratorId,
            Email = "admin@wabitex.test",
            NormalizedEmail = "ADMIN@WABITEX.TEST",
            FullName = "Platform Admin",
            PasswordHash = "not-a-real-hash",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsBootstrapAdministrator = true,
        });

        context.OrganizationUsers.AddRange(
            NewUser(UserA1Id, OrganizationAId, "alice@org-a.test", "Alice A"),
            NewUser(UserB1Id, OrganizationBId, "bob@org-b.test", "Bob B"));

        context.LoginIdentities.AddRange(
            new LoginIdentity { NormalizedEmail = "ADMIN@WABITEX.TEST", PrincipalType = PrincipalType.PlatformAdministrator, PrincipalId = AdministratorId, CreatedAt = DateTimeOffset.UtcNow },
            new LoginIdentity { NormalizedEmail = "ALICE@ORG-A.TEST", PrincipalType = PrincipalType.OrganizationUser, PrincipalId = UserA1Id, OrganizationId = OrganizationAId, CreatedAt = DateTimeOffset.UtcNow },
            new LoginIdentity { NormalizedEmail = "BOB@ORG-B.TEST", PrincipalType = PrincipalType.OrganizationUser, PrincipalId = UserB1Id, OrganizationId = OrganizationBId, CreatedAt = DateTimeOffset.UtcNow });

        await context.SaveChangesAsync();
    }

    public static Organization NewOrganization(Guid id, string code, string name, string nameAr) => new()
    {
        OrganizationId = id,
        OrganizationCode = code,
        OrganizationName = name,
        OrganizationNameAr = nameAr,
        TimeZone = "Asia/Dubai",
        DefaultLanguage = "en",
        Status = OrganizationStatus.Active,
    };

    public static OrganizationUser NewUser(Guid id, Guid organizationId, string email, string fullName) => new()
    {
        UserId = id,
        OrganizationId = organizationId,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FullName = fullName,
        SecurityStamp = Guid.NewGuid().ToString("N"),
        Status = OrganizationUserStatus.Active,
    };
}
