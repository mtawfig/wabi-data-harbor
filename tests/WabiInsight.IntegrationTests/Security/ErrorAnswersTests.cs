using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Api.Contracts;
using WabiInsight.Api.Security;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Security;

/// <summary>P13.29 (E3): bad input is a 400 and two simultaneous edits a 409, never a server error.</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ErrorAnswersTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly ApiFactory _factory;

    public ErrorAnswersTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new ApiFactory(db.ConnectionString);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("7")]
    [InlineData("")]
    public async Task AnUnknownStatusIsA400_ForUsersAndOrganizations(string status)
    {
        using var alice = await LoginAsync(TestData.AliceEmail);
        var user = await alice.PostJsonAsync($"/api/users/{_db.Data.UserA1Id}/status", new { status });
        Assert.Equal(HttpStatusCode.BadRequest, user.StatusCode);
        Assert.Contains("status_invalid", await user.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var platform = await LoginAsync(TestData.AdminEmail, TestData.AdminPassword);
        var organization = await platform.PostJsonAsync($"/api/platform/organizations/{_db.Data.OrganizationBId}/status", new { status, reason = "check" });
        Assert.Equal(HttpStatusCode.BadRequest, organization.StatusCode);
        Assert.Contains("status_invalid", await organization.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        Assert.Equal(OrganizationStatus.Active, (await context.Organizations.IgnoreQueryFilters().SingleAsync(o => o.OrganizationId == _db.Data.OrganizationBId)).Status);
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("suspended", true)]
    [InlineData("Unknown", false)]
    [InlineData("1", false)]
    [InlineData("Active,Suspended", false)]
    public void EnumInput_AcceptsDefinedNamesOnly(string value, bool accepted)
    {
        Assert.Equal(accepted, EnumInput.TryParse<OrganizationStatus>(value, out _));
    }

    [Fact]
    public async Task ConcurrentSavesAndDuplicateKeys_Are409WithACode()
    {
        var handler = new DomainExceptionHandler();
        async Task<(int Status, string Body)> Handle(Exception exception)
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));
            context.Response.Body.Position = 0;
            return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
        }

        var concurrency = await Handle(new DbUpdateConcurrencyException("row changed"));
        Assert.Equal(StatusCodes.Status409Conflict, concurrency.Status);
        Assert.Contains("concurrency_conflict", concurrency.Body, StringComparison.Ordinal);

        // A real unique-key violation from SQL Server, raised through EF: insert the same login e-mail twice.
        // Two requests at once, each with its own context: the second insert reaches SQL Server and hits the key.
        var email = $"dup.{Guid.NewGuid():N}@org.test";
        var now = DateTimeOffset.UtcNow;
        await using (var first = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            first.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, Core.Common.Ids.New(), _db.Data.OrganizationAId, now));
            await first.SaveChangesAsync();
        }

        DbUpdateException duplicate;
        await using (var second = _db.CreateContext(_db.Data.PlatformAdministrator))
        {
            second.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, Core.Common.Ids.New(), _db.Data.OrganizationAId, now));
            duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        }

        Assert.IsType<SqlException>(duplicate.InnerException);
        var conflict = await Handle(duplicate);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.Status);
        Assert.Contains("duplicate_record", conflict.Body, StringComparison.Ordinal);

        // Any other database failure is not dressed up as a conflict.
        var context2 = new DefaultHttpContext();
        Assert.False(await handler.TryHandleAsync(context2, new DbUpdateException("other"), CancellationToken.None));
    }
}
