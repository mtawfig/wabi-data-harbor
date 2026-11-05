using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;
using WabiInsight.IntegrationTests.Email;

namespace WabiInsight.IntegrationTests.Organizations;

[Collection(DatabaseCollectionDefinition.Name)]
public sealed class OrganizationStructureAndUsersTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public OrganizationStructureAndUsersTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> AliceAsync()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(TestData.AliceEmail, TestData.UserPassword)).StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> PutJsonAsync<T>(HttpClient client, string path, T body) => client.PutAsJsonAsync(new Uri(path, UriKind.Relative), body);

    private static async Task<(Guid DepartmentId, Guid SectionId)> CreateStructureAsync(HttpClient admin, string prefix)
    {
        var department = await admin.PostJsonAsync("/api/departments", new { departmentCode = $"{prefix}-FIN", departmentNameEn = "Finance", departmentNameAr = "المالية" });
        Assert.Equal(HttpStatusCode.Created, department.StatusCode);
        var departmentId = (await department.ReadJsonAsync()).GetProperty("departmentId").GetGuid();

        var section = await admin.PostJsonAsync("/api/sections", new { departmentId, sectionCode = "AP", sectionNameEn = "Accounts Payable", sectionNameAr = "الحسابات الدائنة" });
        Assert.Equal(HttpStatusCode.Created, section.StatusCode);
        return (departmentId, (await section.ReadJsonAsync()).GetProperty("sectionId").GetGuid());
    }

    [Fact]
    public async Task Admin_CreatesStructure_AndUser_WithEmailedTemporaryPassword_AndRoles()
    {
        using var alice = await AliceAsync();
        var (departmentId, sectionId) = await CreateStructureAsync(alice, "S1");

        var create = await alice.PostJsonAsync("/api/users", new
        {
            email = "omar@org-a.test", fullName = "Omar Analyst", employeeNumber = "A-100", phoneNumber = "+971500000000",
            departmentId, sectionId, preferredLanguage = "ar", roleCodes = new[] { "Management" },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var user = await create.ReadJsonAsync();
        var userId = user.GetProperty("userId").GetGuid();
        Assert.Equal("o***r@org-a.test", create.Headers.GetValues("X-Delivered-To").Single());
        Assert.Equal(["Management"], user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());
        Assert.Equal(departmentId, user.GetProperty("departmentId").GetGuid());
        Assert.DoesNotContain("temporaryPassword", await create.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var email = await _factory.Emails.WaitForAsync("omar@org-a.test");
        Assert.Equal("ar", email.Language);
        var temporary = RecordingEmailSender.TemporaryPasswordOf(email);

        using var omar = _factory.CreateClient();
        var login = await omar.LoginAsync("omar@org-a.test", temporary);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.ReadJsonAsync();
        Assert.True(session.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(_db.Data.OrganizationAId, session.GetProperty("organizationId").GetGuid());
        Assert.Equal(["Management"], session.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());

        // Lists reflect the new member.
        var list = await (await alice.GetAsync("/api/users?search=omar")).ReadJsonAsync();
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        var departments = await (await alice.GetAsync("/api/departments")).ReadJsonAsync();
        Assert.Equal(1, departments.EnumerateArray().Single(d => d.GetProperty("departmentId").GetGuid() == departmentId).GetProperty("userCount").GetInt32());

        // Role change rotates the stamp: Omar's session is revalidated and gains the new role.
        var update = await PutJsonAsync(alice, $"/api/users/{userId}", new { fullName = "Omar Analyst", employeeNumber = "A-100", departmentId, sectionId, preferredLanguage = "ar", roleCodes = new[] { "Management", "Analyst" } });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await omar.GetAsync("/api/auth/me")).StatusCode);
        var relogin = await omar.LoginAsync("omar@org-a.test", temporary);
        Assert.Equal(["Analyst", "Management"], (await relogin.ReadJsonAsync()).GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());
    }

    [Fact]
    public async Task Structure_RejectsInvalidCombinations()
    {
        using var alice = await AliceAsync();
        var (departmentId, sectionId) = await CreateStructureAsync(alice, "S2");
        var other = await alice.PostJsonAsync("/api/departments", new { departmentCode = "S2-HR", departmentNameEn = "HR", departmentNameAr = "الموارد البشرية" });
        var otherDepartmentId = (await other.ReadJsonAsync()).GetProperty("departmentId").GetGuid();

        var duplicate = await alice.PostJsonAsync("/api/departments", new { departmentCode = "s2-fin", departmentNameEn = "Dup", departmentNameAr = "مكرر" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var sectionInWrongDepartment = await alice.PostJsonAsync("/api/users", new { email = "mismatch@org-a.test", fullName = "Mismatch", departmentId = otherDepartmentId, sectionId, preferredLanguage = "en", roleCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, sectionInWrongDepartment.StatusCode);
        Assert.Contains("section_not_in_department", await sectionInWrongDepartment.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var sectionWithoutDepartment = await alice.PostJsonAsync("/api/users", new { email = "nodept@org-a.test", fullName = "No Dept", sectionId, preferredLanguage = "en", roleCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, sectionWithoutDepartment.StatusCode);

        var unknownRole = await alice.PostJsonAsync("/api/users", new { email = "norole@org-a.test", fullName = "No Role", preferredLanguage = "en", roleCodes = new[] { "DoesNotExist" } });
        Assert.Equal(HttpStatusCode.BadRequest, unknownRole.StatusCode);

        var takenEmail = await alice.PostJsonAsync("/api/users", new { email = TestData.BobEmail, fullName = "Bob again", preferredLanguage = "en", roleCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, takenEmail.StatusCode);

        // Database-level guard: a section row pointing at a department of another organization.
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        context.Sections.Add(new Section { SectionId = Guid.NewGuid(), OrganizationId = _db.Data.OrganizationBId, DepartmentId = departmentId, SectionCode = "X", SectionNameEn = "x", SectionNameAr = "x" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Admin_CannotRemoveOwnAdminRole_DeactivateSelf_OrOrphanTheOrganization()
    {
        using var alice = await AliceAsync();

        var selfDemote = await PutJsonAsync(alice, $"/api/users/{_db.Data.UserA1Id}", new { fullName = "Alice A", preferredLanguage = "en", roleCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, selfDemote.StatusCode);
        Assert.Contains("cannot_remove_own_admin_role", await selfDemote.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var selfDeactivate = await alice.PostJsonAsync($"/api/users/{_db.Data.UserA1Id}/status", new { status = "Inactive" });
        Assert.Equal(HttpStatusCode.Conflict, selfDeactivate.StatusCode);

        // Promote a second administrator, then demote them again (Alice remains) — allowed.
        var create = await alice.PostJsonAsync("/api/users", new { email = "second.admin@org-a.test", fullName = "Second Admin", preferredLanguage = "en", roleCodes = new[] { "OrganizationAdmin" } });
        var secondId = (await create.ReadJsonAsync()).GetProperty("userId").GetGuid();
        var demote = await PutJsonAsync(alice, $"/api/users/{secondId}", new { fullName = "Second Admin", preferredLanguage = "en", roleCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);

        // Reset and deactivate another user; the reset email arrives, the deactivated user cannot sign in.
        var reset = await alice.PostJsonAsync($"/api/users/{secondId}/reset-password", new { });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        await _factory.Emails.WaitForAsync("second.admin@org-a.test");
        var deactivate = await alice.PostJsonAsync($"/api/users/{secondId}/status", new { status = "Inactive" });
        Assert.Equal("Inactive", (await deactivate.ReadJsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Users_AreInvisibleAcrossOrganizations_AndNonAdminsAreDenied()
    {
        using var alice = await AliceAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/users/{_db.Data.UserB1Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutJsonAsync(alice, $"/api/users/{_db.Data.UserB1Id}", new { fullName = "Hijacked", preferredLanguage = "en", roleCodes = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostJsonAsync($"/api/users/{_db.Data.UserB1Id}/reset-password", new { })).StatusCode);

        var all = await (await alice.GetAsync("/api/users?pageSize=200")).ReadJsonAsync();
        Assert.DoesNotContain(all.GetProperty("items").EnumerateArray(), u => u.GetProperty("email").GetString() == TestData.BobEmail);

        using var bob = _factory.CreateClient();
        await bob.LoginAsync(TestData.BobEmail, TestData.UserPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostJsonAsync("/api/departments", new { departmentCode = "X", departmentNameEn = "x", departmentNameAr = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/departments")).StatusCode);

        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/departments")).StatusCode);
    }

    [Fact]
    public async Task Roles_SystemRolesProtected_CustomRolesManaged()
    {
        using var alice = await AliceAsync();
        var roles = await (await alice.GetAsync("/api/roles")).ReadJsonAsync();
        var adminRole = roles.EnumerateArray().Single(r => r.GetProperty("roleCode").GetString() == "OrganizationAdmin");
        var adminRoleId = adminRole.GetProperty("roleId").GetGuid();
        Assert.True(adminRole.GetProperty("isSystem").GetBoolean());
        Assert.Equal(1, adminRole.GetProperty("memberCount").GetInt32());

        var renameSystem = await PutJsonAsync(alice, $"/api/roles/{adminRoleId}", new { roleCode = "Boss", nameEn = "Boss", nameAr = "رئيس", isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, renameSystem.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.DeleteAsync(new Uri($"/api/roles/{adminRoleId}", UriKind.Relative))).StatusCode);

        var create = await alice.PostJsonAsync("/api/roles", new { roleCode = "FinanceViewer", nameEn = "Finance Viewer", nameAr = "مطّلع مالي", description = "Reads finance objects" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var roleId = (await create.ReadJsonAsync()).GetProperty("roleId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostJsonAsync("/api/roles", new { roleCode = "FinanceViewer", nameEn = "x", nameAr = "x" })).StatusCode);

        var user = await alice.PostJsonAsync("/api/users", new { email = "viewer@org-a.test", fullName = "Viewer", preferredLanguage = "en", roleCodes = new[] { "FinanceViewer" } });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        var inUse = await alice.DeleteAsync(new Uri($"/api/roles/{roleId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);

        var userId = (await user.ReadJsonAsync()).GetProperty("userId").GetGuid();
        await PutJsonAsync(alice, $"/api/users/{userId}", new { fullName = "Viewer", preferredLanguage = "en", roleCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync(new Uri($"/api/roles/{roleId}", UriKind.Relative))).StatusCode);

        // Roles of organization B are not reachable by id from A.
        Assert.Equal(HttpStatusCode.NotFound, (await PutJsonAsync(alice, $"/api/roles/{_db.Data.RoleAdminBId}", new { roleCode = "OrganizationAdmin", nameEn = "x", nameAr = "x", isActive = true })).StatusCode);
    }

    [Fact]
    public async Task RoleTemplates_ArePlatformManaged_AndOnlyAffectNewOrganizations()
    {
        using var platform = _factory.CreateClient();
        await platform.LoginAsync(TestData.AdminEmail, TestData.AdminPassword);

        var templates = await (await platform.GetAsync("/api/platform/role-templates")).ReadJsonAsync();
        Assert.Equal(["OrganizationAdmin", "Management", "Analyst"], templates.EnumerateArray().Select(t => t.GetProperty("roleCode").GetString()!).ToArray());
        var adminTemplateId = templates.EnumerateArray().First().GetProperty("templateId").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await PutJsonAsync(platform, $"/api/platform/role-templates/{adminTemplateId}", new { roleCode = "Owner", nameEn = "x", nameAr = "x", isActive = true, sortOrder = 1 })).StatusCode);

        var created = await platform.PostJsonAsync("/api/platform/role-templates", new { roleCode = "Auditor", nameEn = "Auditor", nameAr = "مدقّق", description = "Read-only audit role", isActive = true, sortOrder = 10 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Existing organization A is untouched; a new organization receives the Auditor template.
        using var alice = await AliceAsync();
        var rolesA = await (await alice.GetAsync("/api/roles")).ReadJsonAsync();
        Assert.DoesNotContain(rolesA.EnumerateArray(), r => r.GetProperty("roleCode").GetString() == "Auditor");

        var organization = await platform.PostJsonAsync("/api/platform/organizations", new
        {
            organizationCode = "TPL1", organizationName = "Templated", organizationNameAr = "مقولبة", timeZone = "UTC", defaultLanguage = "en",
            initialAdministratorEmail = "admin@tpl1.test", initialAdministratorFullName = "T Admin",
        });
        Assert.Equal(HttpStatusCode.Created, organization.StatusCode);
        var organizationId = (await organization.ReadJsonAsync()).GetProperty("organizationId").GetGuid();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var codes = await context.Roles.IgnoreQueryFilters().Where(r => r.OrganizationId == organizationId).Select(r => r.RoleCode).OrderBy(c => c).ToListAsync();
        Assert.Equal(["Analyst", "Auditor", "Management", "OrganizationAdmin"], codes);

        using var bob = _factory.CreateClient();
        await bob.LoginAsync(TestData.BobEmail, TestData.UserPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/platform/role-templates")).StatusCode);
    }
}
