using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Audit;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.IntegrationTests.Database;

[Collection(DatabaseCollectionDefinition.Name)]
public class SchemaConstraintTests
{
    private readonly SqlServerTestDatabase _db;

    public SchemaConstraintTests(SqlServerTestDatabase db)
    {
        _db = db;
    }

    [Fact]
    public async Task OrganizationUser_WithUnknownOrganization_IsRejectedByForeignKey()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.OrganizationUsers.Add(TestData.NewUser(Ids.New(), Ids.New(), "ghost@nowhere.test", "Ghost"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("FK_OrganizationUsers_Organizations_OrganizationId", ex.InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrganizationUser_WithEmptyOrganization_IsRejectedBeforeReachingDatabase()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.OrganizationUsers.Add(TestData.NewUser(Ids.New(), Guid.Empty, "empty@nowhere.test", "Empty"));

        await Assert.ThrowsAsync<TenantContextException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task OrganizationCode_MustBeUnique()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.Organizations.Add(TestData.NewOrganization(Ids.New(), "ORG-A", "Duplicate", "مكرر"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("IX_Organizations_OrganizationCode", ex.InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginIdentity_IsUniqueAcrossAdministratorsAndOrganizationUsers()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var userId = Ids.New();
        context.OrganizationUsers.Add(TestData.NewUser(userId, _db.Data.OrganizationAId, "admin@wabitex.test", "Impostor"));
        context.LoginIdentities.Add(new LoginIdentity
        {
            NormalizedEmail = "ADMIN@WABITEX.TEST",
            PrincipalType = PrincipalType.OrganizationUser,
            PrincipalId = userId,
            OrganizationId = _db.Data.OrganizationAId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("PK_LoginIdentities", ex.InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrganizationUserEmail_IsUniquePerOrganization_ButRegistryBlocksItGlobally()
    {
        // Table-level: the same email may exist in two organizations.
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            context.OrganizationUsers.Add(TestData.NewUser(Ids.New(), _db.Data.OrganizationBId, "alice@org-a.test", "Alice in B"));
            await context.SaveChangesAsync();
        }

        // Same organization twice: unique index.
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            context.OrganizationUsers.Add(TestData.NewUser(Ids.New(), _db.Data.OrganizationAId, "alice@org-a.test", "Alice again"));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Contains("IX_OrganizationUsers_OrganizationId_NormalizedEmail", ex.InnerException?.Message, StringComparison.Ordinal);
        }

        // Registry-level: the login email is already taken platform-wide.
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            context.LoginIdentities.Add(new LoginIdentity
            {
                NormalizedEmail = "ALICE@ORG-A.TEST",
                PrincipalType = PrincipalType.OrganizationUser,
                PrincipalId = Ids.New(),
                OrganizationId = _db.Data.OrganizationBId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task OnlyOneBootstrapAdministrator_CanExist()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.Administrators.Add(new Administrator
        {
            AdministratorId = Ids.New(),
            Email = "second@wabitex.test",
            NormalizedEmail = "SECOND@WABITEX.TEST",
            FullName = "Second",
            PasswordHash = "x",
            SecurityStamp = "y",
            IsBootstrapAdministrator = true,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("IX_Administrators_IsBootstrapAdministrator", ex.InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SystemInitialization_IsSingleton()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.SystemInitialization.Add(new SystemInitialization { Id = 2 });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("CK_SystemInitialization_Singleton", ex.InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrganizationStatus_RejectsUnknownCodes()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);

        var ex = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => context.Database.ExecuteSqlAsync(
            $"UPDATE Organizations SET Status = 'Archived' WHERE OrganizationId = {_db.Data.OrganizationBId}"));

        Assert.Contains("CK_Organizations_Status", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditLogs_AreAppendOnly_InTheApplication()
    {
        var id = Ids.New();
        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            context.AuditLogs.Add(new AuditLog
            {
                AuditLogId = id,
                ActorType = PrincipalType.PlatformAdministrator,
                ActorId = _db.Data.AdministratorId,
                ActorDisplayName = "Platform Admin",
                EventType = "Test.Created",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var log = await context.AuditLogs.SingleAsync(a => a.AuditLogId == id);
            log.EventType = "Test.Tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            var log = await context.AuditLogs.SingleAsync(a => a.AuditLogId == id);
            context.AuditLogs.Remove(log);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }
}
