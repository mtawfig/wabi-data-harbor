using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.IntegrationTests.Database;

[Collection(DatabaseCollectionDefinition.Name)]
public class TenantFilterTests
{
    private readonly SqlServerTestDatabase _db;

    public TenantFilterTests(SqlServerTestDatabase db)
    {
        _db = db;
    }

    [Fact]
    public async Task OrganizationUser_SeesOnlyOwnOrganizationUsers()
    {
        await using var contextA = _db.CreateContext(_db.Data.OrganizationAUser);
        await using var contextB = _db.CreateContext(_db.Data.OrganizationBUser);

        var usersA = await contextA.OrganizationUsers.Select(u => u.OrganizationId).Distinct().ToListAsync();
        var usersB = await contextB.OrganizationUsers.Select(u => u.OrganizationId).Distinct().ToListAsync();

        Assert.Equal([_db.Data.OrganizationAId], usersA);
        Assert.Equal([_db.Data.OrganizationBId], usersB);
    }

    [Fact]
    public async Task OrganizationUser_CannotFindAnotherOrganizationsUserById()
    {
        await using var contextA = _db.CreateContext(_db.Data.OrganizationAUser);

        var other = await contextA.OrganizationUsers.SingleOrDefaultAsync(u => u.UserId == _db.Data.UserB1Id);

        Assert.Null(other);
    }

    [Fact]
    public async Task PlatformAdministrator_SeesNoTenantRows_WithoutPlatformScope()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);

        Assert.Empty(await context.OrganizationUsers.ToListAsync());
    }

    [Fact]
    public async Task PlatformScope_ReturnsAllOrganizations_ForPlatformAdministrator()
    {
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);

        var organizations = await context.OrganizationUsers
            .AsPlatformScope(ScopeOf(context))
            .Select(u => u.OrganizationId)
            .Distinct()
            .ToListAsync();

        Assert.Contains(_db.Data.OrganizationAId, organizations);
        Assert.Contains(_db.Data.OrganizationBId, organizations);
    }

    [Fact]
    public void PlatformScope_IsDeniedToOrganizationUsers()
    {
        using var context = _db.CreateContext(_db.Data.OrganizationAUser);

        Assert.Throws<TenantContextException>(() => context.OrganizationUsers.AsPlatformScope(ScopeOf(context)));
    }

    [Fact]
    public async Task Anonymous_ReadsNothing_AndCannotWrite()
    {
        await using var context = _db.CreateContext(TenantPrincipal.Anonymous);

        Assert.Empty(await context.OrganizationUsers.ToListAsync());

        context.OrganizationUsers.Add(TestData.NewUser(Ids.New(), _db.Data.OrganizationAId, "anon@org-a.test", "Anon"));
        await Assert.ThrowsAsync<TenantContextException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task OrganizationUser_CannotWriteIntoAnotherOrganization()
    {
        await using var contextA = _db.CreateContext(_db.Data.OrganizationAUser);
        contextA.OrganizationUsers.Add(TestData.NewUser(Ids.New(), _db.Data.OrganizationBId, "intruder@org-b.test", "Intruder"));

        await Assert.ThrowsAsync<TenantContextException>(() => contextA.SaveChangesAsync());
    }

    [Fact]
    public async Task OrganizationUser_CanWriteInsideOwnOrganization()
    {
        var id = Ids.New();
        await using (var contextA = _db.CreateContext(_db.Data.OrganizationAUser))
        {
            contextA.OrganizationUsers.Add(TestData.NewUser(id, _db.Data.OrganizationAId, $"{id:N}@org-a.test", "New colleague"));
            await contextA.SaveChangesAsync();
        }

        await using (var contextB = _db.CreateContext(_db.Data.OrganizationBUser))
        {
            Assert.Null(await contextB.OrganizationUsers.SingleOrDefaultAsync(u => u.UserId == id));
        }
    }

    private static ITenantContext ScopeOf(ConfigurationDbContext context)
    {
        return (ITenantContext)typeof(ConfigurationDbContext)
            .GetField("_tenantContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(context)!;
    }
}
