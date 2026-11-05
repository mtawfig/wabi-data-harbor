using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Security;

namespace WabiInsight.IntegrationTests.Database;

/// <summary>
/// Two active organizations (A, B) plus a suspended one (C), several users with known passwords, and two
/// platform administrators. Every tenant-isolation test uses at least two organizations (specification §30).
/// </summary>
public sealed class TestData
{
    public const string AdminPassword = "Admin#Correct#Horse#2026";
    public const string UserPassword = "User#Battery#Staple#2026";

    public static readonly TenantPrincipal SystemPrincipal =
        TenantPrincipal.ForPlatformAdministrator(Guid.Parse("00000000-0000-0000-0000-000000000001"), "system");

    private static readonly IdentityPasswordHashingService Hasher = new();

    public Guid OrganizationAId { get; } = Ids.New();

    public Guid OrganizationBId { get; } = Ids.New();

    public Guid OrganizationCSuspendedId { get; } = Ids.New();

    public Guid UserA1Id { get; } = Ids.New();

    public Guid UserB1Id { get; } = Ids.New();

    public Guid UserAInactiveId { get; } = Ids.New();

    public Guid UserALockoutId { get; } = Ids.New();

    public Guid UserAChangerId { get; } = Ids.New();

    public Guid UserCSuspendedId { get; } = Ids.New();

    public Guid RoleAdminAId { get; } = Ids.New();

    public Guid RoleAdminBId { get; } = Ids.New();

    public Guid AdministratorId { get; } = Ids.New();

    public Guid SecondAdministratorId { get; } = Ids.New();

    public const string AdminEmail = "admin@wabitex.test";
    public const string SecondAdminEmail = "second@wabitex.test";
    public const string AliceEmail = "alice@org-a.test";
    public const string BobEmail = "bob@org-b.test";
    public const string InactiveEmail = "inactive@org-a.test";
    public const string LockoutEmail = "lock@org-a.test";
    public const string ChangerEmail = "changer@org-a.test";
    public const string SuspendedOrgUserEmail = "carol@org-c.test";

    public TenantPrincipal OrganizationAUser => TenantPrincipal.ForOrganizationUser(OrganizationAId, UserA1Id, "Alice A");

    public TenantPrincipal OrganizationBUser => TenantPrincipal.ForOrganizationUser(OrganizationBId, UserB1Id, "Bob B");

    public TenantPrincipal PlatformAdministrator => TenantPrincipal.ForPlatformAdministrator(AdministratorId, "Platform Admin");

    public async Task SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext(PlatformAdministrator);
        var now = DateTimeOffset.UtcNow;

        context.Organizations.AddRange(
            NewOrganization(OrganizationAId, "ORG-A", "Organization A", "المؤسسة أ"),
            NewOrganization(OrganizationBId, "ORG-B", "Organization B", "المؤسسة ب"),
            NewOrganization(OrganizationCSuspendedId, "ORG-C", "Organization C", "المؤسسة ج", OrganizationStatus.Suspended));

        context.Administrators.AddRange(
            NewAdministrator(AdministratorId, AdminEmail, "Platform Admin", isBootstrap: true),
            NewAdministrator(SecondAdministratorId, SecondAdminEmail, "Second Admin", isBootstrap: false));

        context.OrganizationUsers.AddRange(
            NewUser(UserA1Id, OrganizationAId, AliceEmail, "Alice A"),
            NewUser(UserB1Id, OrganizationBId, BobEmail, "Bob B"),
            NewUser(UserAInactiveId, OrganizationAId, InactiveEmail, "Inactive A", OrganizationUserStatus.Inactive),
            NewUser(UserALockoutId, OrganizationAId, LockoutEmail, "Lock A"),
            NewUser(UserAChangerId, OrganizationAId, ChangerEmail, "Changer A"),
            NewUser(UserCSuspendedId, OrganizationCSuspendedId, SuspendedOrgUserEmail, "Carol C"));

        context.LoginIdentities.AddRange(
            Identity(AdminEmail, PrincipalType.PlatformAdministrator, AdministratorId, null, now),
            Identity(SecondAdminEmail, PrincipalType.PlatformAdministrator, SecondAdministratorId, null, now),
            Identity(AliceEmail, PrincipalType.OrganizationUser, UserA1Id, OrganizationAId, now),
            Identity(BobEmail, PrincipalType.OrganizationUser, UserB1Id, OrganizationBId, now),
            Identity(InactiveEmail, PrincipalType.OrganizationUser, UserAInactiveId, OrganizationAId, now),
            Identity(LockoutEmail, PrincipalType.OrganizationUser, UserALockoutId, OrganizationAId, now),
            Identity(ChangerEmail, PrincipalType.OrganizationUser, UserAChangerId, OrganizationAId, now),
            Identity(SuspendedOrgUserEmail, PrincipalType.OrganizationUser, UserCSuspendedId, OrganizationCSuspendedId, now));

        context.Roles.AddRange(
            NewRole(RoleAdminAId, OrganizationAId, SystemRoles.OrganizationAdmin, "Organization Administrator", "مدير المؤسسة"),
            NewRole(RoleAdminBId, OrganizationBId, SystemRoles.OrganizationAdmin, "Organization Administrator", "مدير المؤسسة"),
            NewRole(Ids.New(), OrganizationAId, SystemRoles.Management, "Management", "الإدارة"),
            NewRole(Ids.New(), OrganizationBId, SystemRoles.Management, "Management", "الإدارة"),
            NewRole(Ids.New(), OrganizationAId, SystemRoles.Analyst, "Analyst", "محلّل"),
            NewRole(Ids.New(), OrganizationBId, SystemRoles.Analyst, "Analyst", "محلّل"));
        context.UserRoles.Add(new UserRole { OrganizationId = OrganizationAId, UserId = UserA1Id, RoleId = RoleAdminAId, AssignedAt = now, AssignedByAdministratorId = AdministratorId });
        context.Applications.Add(new Application
        {
            ApplicationId = Ids.New(),
            OrganizationId = OrganizationAId,
            ApplicationName = "Secret HR System",
            ApplicationNameAr = "نظام الموارد البشرية",
            ApplicationCode = "HR",
            DatabaseType = DatabaseType.SqlServer,
            IsApproved = true,
        });

        context.SystemInitialization.Add(new SystemInitialization
        {
            Id = SystemInitialization.SingletonId,
            BootstrapCompleted = true,
            BootstrapCompletedAt = now,
            BootstrapAdministratorId = AdministratorId,
            SchemaVersion = "seeded",
        });

        await context.SaveChangesAsync();
    }

    public static Organization NewOrganization(Guid id, string code, string name, string nameAr, OrganizationStatus status = OrganizationStatus.Active) => new()
    {
        OrganizationId = id,
        OrganizationCode = code,
        OrganizationName = name,
        OrganizationNameAr = nameAr,
        TimeZone = "Asia/Dubai",
        DefaultLanguage = "en",
        Status = status,
    };

    public static OrganizationUser NewUser(Guid id, Guid organizationId, string email, string fullName, OrganizationUserStatus status = OrganizationUserStatus.Active) => new()
    {
        UserId = id,
        OrganizationId = organizationId,
        Email = email,
        NormalizedEmail = EmailNormalizer.Normalize(email),
        FullName = fullName,
        PasswordHash = Hasher.Hash(UserPassword),
        SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp(),
        MustChangePassword = false,
        Status = status,
    };

    public static Administrator NewAdministrator(Guid id, string email, string fullName, bool isBootstrap) => new()
    {
        AdministratorId = id,
        Email = email,
        NormalizedEmail = EmailNormalizer.Normalize(email),
        FullName = fullName,
        PasswordHash = Hasher.Hash(AdminPassword),
        SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp(),
        MustChangePassword = false,
        IsBootstrapAdministrator = isBootstrap,
    };

    public static Role NewRole(Guid id, Guid organizationId, string code, string nameEn, string nameAr) => new()
    {
        RoleId = id,
        OrganizationId = organizationId,
        RoleCode = code,
        NameEn = nameEn,
        NameAr = nameAr,
        IsSystem = true,
        TemplateVersion = SystemRoles.TemplateVersion,
    };

    private static LoginIdentity Identity(string email, PrincipalType type, Guid principalId, Guid? organizationId, DateTimeOffset now) => new()
    {
        NormalizedEmail = EmailNormalizer.Normalize(email),
        PrincipalType = type,
        PrincipalId = principalId,
        OrganizationId = organizationId,
        CreatedAt = now,
    };
}
