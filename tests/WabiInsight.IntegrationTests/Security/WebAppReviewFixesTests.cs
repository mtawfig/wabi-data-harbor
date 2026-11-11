using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Api.Authentication;
using WabiInsight.Api.Security;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Analysis;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Organizations;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Security;

/// <summary>P13.28: the user's own interface language (C10), role names on the profile (F2) and technical fields kept from business users (F5).</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class WebAppReviewFixesTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public WebAppReviewFixesTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>The API's own JSON settings: camelCase, nulls left out.</summary>
    private static readonly JsonSerializerOptions BrowserJson = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, Guid roleId)
    {
        var email = $"p1328.{Guid.NewGuid():N}@org.test";
        var userId = Core.Common.Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var now = DateTimeOffset.UtcNow;
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, "Review User"));
        context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, organizationId, now));
        context.UserRoles.Add(new UserRole { OrganizationId = organizationId, UserId = userId, RoleId = roleId, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
        await context.SaveChangesAsync();
        return (userId, email);
    }

    [Fact]
    public async Task OwnLanguage_IsSavedOnTheAccount_KeptInTheSession_AndAudited()
    {
        var (userId, email) = await InsertUserAsync(_db.Data.OrganizationAId, _db.Data.RoleAdminAId);
        using var user = await LoginAsync(email);
        Assert.Equal("en", (await (await user.GetAsync("/api/auth/me")).ReadJsonAsync()).GetProperty("preferredLanguage").GetString());

        var saved = await user.PutAsJsonAsync(new Uri("/api/auth/language", UriKind.Relative), new { language = "ar" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("ar", (await saved.ReadJsonAsync()).GetProperty("preferredLanguage").GetString());

        // The re-issued session already carries the new language (a reload agrees), and so does a new sign-in.
        Assert.Equal("ar", (await (await user.GetAsync("/api/auth/me")).ReadJsonAsync()).GetProperty("preferredLanguage").GetString());
        using var again = await LoginAsync(email);
        Assert.Equal("ar", (await (await again.GetAsync("/api/auth/me")).ReadJsonAsync()).GetProperty("preferredLanguage").GetString());

        var invalid = await user.PutAsJsonAsync(new Uri("/api/auth/language", UriKind.Relative), new { language = "fr" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("language_invalid", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        Assert.Equal("ar", (await context.OrganizationUsers.IgnoreQueryFilters().SingleAsync(u => u.UserId == userId)).PreferredLanguage);
        Assert.Equal(1, await context.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.EventType == "Auth.LanguageChanged" && a.ActorId == userId));

        // Signed out, the endpoint is closed.
        using var anonymous = _factory.CreateClient();
        await anonymous.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(new Uri("/api/auth/language", UriKind.Relative), new { language = "ar" })).StatusCode);
    }

    [Fact]
    public async Task MyRoles_GivesTheCallersOwnRoleNames_InBothLanguages_AndNothingOfAnotherOrganization()
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var roles = (await (await alice.GetAsync("/api/organization/my-roles")).ReadJsonAsync()).EnumerateArray().ToList();
        var admin = Assert.Single(roles, r => r.GetProperty("roleCode").GetString() == SystemRoles.OrganizationAdmin);
        Assert.Equal("Organization Administrator", admin.GetProperty("nameEn").GetString());
        Assert.Equal("مدير المؤسسة", admin.GetProperty("nameAr").GetString());

        var (_, adminBEmail) = await InsertUserAsync(_db.Data.OrganizationBId, _db.Data.RoleAdminBId);
        using var adminB = await LoginAsync(adminBEmail);
        var rolesB = (await (await adminB.GetAsync("/api/organization/my-roles")).ReadJsonAsync()).EnumerateArray().ToList();
        Assert.Single(rolesB);
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var roleBName = (await context.Roles.IgnoreQueryFilters().SingleAsync(r => r.RoleId == _db.Data.RoleAdminBId)).NameEn;
        Assert.Equal(roleBName, rolesB[0].GetProperty("nameEn").GetString());
    }

    [Fact]
    public void TechnicalFields_ReachOrganizationAdministratorsOnly()
    {
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(WabiClaims.Role, "OrganizationAdmin")], "test"));
        var business = new ClaimsPrincipal(new ClaimsIdentity([new Claim(WabiClaims.Role, "Management")], "test"));
        Assert.True(TechnicalFieldsFilter.SeesTechnicalFields(admin));
        Assert.False(TechnicalFieldsFilter.SeesTechnicalFields(business));

        var usage = new PlanUsage("Anthropic", "model-x", 1500, 400, 0, 0.12m, 900);
        var answer = new AskResponse(Guid.NewGuid(), Guid.NewGuid(), 1, "o", [], [], new Interpretation("n", [], usage), null, null, usage, 0.2m, 1000);
        var reduced = Assert.IsType<AskResponse>(TechnicalFieldsFilter.Reduce(answer));
        Assert.Null(reduced.PlanUsage);
        Assert.Null(reduced.TotalEstimatedCost);
        Assert.Null(reduced.Interpretation!.Usage);
        Assert.Equal("n", reduced.Interpretation.Narrative);

        // What the browser receives: no provider, model, tokens or cost anywhere in the JSON.
        var json = JsonSerializer.Serialize(reduced, BrowserJson);
        Assert.DoesNotContain("model-x", json, StringComparison.Ordinal);
        Assert.DoesNotContain("estimatedCost", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inputTokens", json, StringComparison.Ordinal);

        var version = new HistoryVersionDto(Guid.NewGuid(), 1, AnalysisHistoryStatus.Completed, null, null, "model-x", 1500, 400, 0.2m, 900, 1, 3, true, false, null, DateTimeOffset.UtcNow);
        var summary = new HistorySummaryDto(Guid.NewGuid(), "t", "p", Guid.NewGuid(), "APP", "App", "تطبيق", AnalysisType.Answer, AnalysisHistoryStatus.Completed, DataClassification.Internal, "en", 1, false, false, false, 0.2m, DateTimeOffset.UtcNow, null);
        var detail = Assert.IsType<HistoryDetailDto>(TechnicalFieldsFilter.Reduce(new HistoryDetailDto(summary, "p", "en", [version], [])));
        Assert.Null(detail.Summary.EstimatedCost);
        Assert.Null(detail.Versions[0].ModelName);
        Assert.Null(detail.Versions[0].InputTokens);
        var page = Assert.IsType<PagedResult<HistorySummaryDto>>(TechnicalFieldsFilter.Reduce(new PagedResult<HistorySummaryDto>([summary], 1, 25, 1)));
        Assert.Null(page.Items[0].EstimatedCost);

        var plan = new PlanResponse(new AnalysisPlan(), [], usage);
        Assert.IsType<TechnicalFieldsFilter.PlanView>(TechnicalFieldsFilter.Reduce(plan));
        Assert.Same(summary.Title, ((HistorySummaryDto)TechnicalFieldsFilter.Reduce(summary)).Title);
        Assert.Equal("unchanged", TechnicalFieldsFilter.Reduce("unchanged"));
    }
}
