using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.IntegrationTests.Ai;
using WabiInsight.IntegrationTests.Api;
using WabiInsight.IntegrationTests.Database;

namespace WabiInsight.IntegrationTests.Monitoring;

/// <summary>
/// Phase 14 ledger (specification §35): every analysis, successful or not, leaves one fact per object it touched,
/// in the same unit of work; exports are facts too; facts never cross organizations and can never be edited.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AccessLedgerTests : IDisposable
{
    private readonly SqlServerTestDatabase _db;
    private readonly AiApiFactory _factory;
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "wi-keys-tests", "access-ledger-tests");

    public AccessLedgerTests(SqlServerTestDatabase db)
    {
        _db = db;
        _factory = new AiApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = _keyRing, ["Ai:Anthropic:ApiKey"] = "sk-ant-seeded-platform-key-0002" });
    }

    public void Dispose() => _factory.Dispose();

    private static string Plan(string objectName, string filterColumn, params string[] values) => $$"""
        {"objective":"Ledger test","source":{"schema":"dbo","object":"{{objectName}}","alias":"t"},"joins":[],
         "select":[{"alias":"status","column":"t.Status","aggregate":null,"dateTrunc":null},{"alias":"n","column":null,"aggregate":"count","dateTrunc":null}],
         "filters":{"logic":"and","conditions":[{"column":"t.{{filterColumn}}","operator":"in","values":[{{string.Join(",", values.Select(v => $"\"{v}\""))}}]}],"groups":[]},
         "groupBy":["status"],"orderBy":[],"limit":null,"visualization":{"type":"table","x":null,"y":[]},"assumptions":[]}
        """;

    [Fact]
    public async Task Facts_AreWrittenForSuccess_Unauthorized_AndExport_AndStayInsideTheOrganization()
    {
        FakeAiProvider.Script = null;
        try
        {
            using var alice = await LoginAsync(TestData.AliceEmail);
            var (applicationId, objectId, columns) = await PrepareApplicationAsync(alice, "ledger");
            // Mark the ticket reference as an identifier so filtered access to specific records is counted.
            var referenceColumn = columns.Single(c => c.GetProperty("columnName").GetString() == "ReferenceNumber");
            var update = await alice.PutAsJsonAsync($"/api/metadata/columns/{referenceColumn.GetProperty("columnId").GetGuid()}", new { displayNameEn = "Reference number", displayNameAr = (string?)null, businessDescription = (string?)null, classification = "Confidential", isApproved = true, semanticRole = "Identifier" });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Identifier", updated.GetProperty("semanticRole").GetString());
            Assert.Equal("Identifier", updated.GetProperty("suggestedSemanticRole").GetString());

            var roles = (await (await alice.GetAsync("/api/roles")).ReadJsonAsync()).EnumerateArray().ToList();
            var managementId = roles.Single(r => r.GetProperty("roleCode").GetString() == "Management").GetProperty("roleId").GetGuid();
            var (managerId, managerEmail) = await InsertUserAsync(_db.Data.OrganizationAId, managementId);
            using var manager = await LoginAsync(managerEmail);
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(HttpStatusCode.Created, (await alice.PostJsonAsync("/api/support/tickets", new { ticketType = "Request", impact = "Low", subject = $"Ledger seed {i}", description = "Seed ticket for the ledger test.", requiresSupportAccess = false })).StatusCode);
            }

            // 1. A successful analysis filtered on two specific references.
            FakeAiProvider.Script = request => request.SchemaName == "wabi_interpretation" ? """{"narrative":"ok","highlights":[]}""" : Plan("SupportTickets", "ReferenceNumber", "WI-2026-000001", "WI-2026-000002");
            var ask = await manager.PostJsonAsync("/api/ai/ask", new { applicationId, question = "Tickets by status for two references", language = "en" });
            Assert.True(ask.StatusCode == HttpStatusCode.OK, await ask.Content.ReadAsStringAsync());
            var answer = await ask.Content.ReadFromJsonAsync<JsonElement>();
            var historyId = answer.GetProperty("historyId").GetGuid();

            await using (var context = _db.CreateContext(TestData.SystemPrincipal))
            {
                var facts = await context.AnalysisAccessFacts.IgnoreQueryFilters().Where(f => f.UserId == managerId).ToListAsync();
                var fact = Assert.Single(facts);
                Assert.Equal(_db.Data.OrganizationAId, fact.OrganizationId);
                Assert.Equal(objectId, fact.ObjectId);
                Assert.Equal("SupportTickets", fact.ObjectName);
                Assert.True(fact.IsPrimaryObject);
                Assert.Equal(AccessOutcome.Succeeded, fact.Outcome);
                Assert.Equal(2, fact.ColumnCount);                        // Status, ReferenceNumber
                Assert.Equal(2, fact.FilterValueCount);
                Assert.Equal(2, fact.IdentifierFilterValueCount);        // ReferenceNumber carries the Identifier role
                Assert.Equal(DataClassification.Confidential, fact.HighestClassification);
                Assert.Contains("Management", fact.RoleCodes, StringComparison.Ordinal);
                Assert.NotNull(fact.HistoryVersionId);
                Assert.NotNull(fact.MetricId);
                Assert.False(fact.IsExport);
            }

            // 2. An export of that answer is a fact of its own.
            var export = await manager.PostAsync($"/api/history/{historyId}/versions/1/export/xlsx", null);
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);

            // 3. A plan that reaches for a catalogued object the organization never approved (discovered by the refresh): Unauthorized,
            //    linked to the catalogue entry so rules can tell "tried an unapproved table" from "tried a name that does not exist".
            FakeAiProvider.Script = _ => Plan("Administrators", "Email", "someone@wabitex.test");
            var denied = await manager.PostJsonAsync("/api/ai/ask", new { applicationId, question = "Show platform administrators", language = "en" });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, denied.StatusCode);

            await using (var context = _db.CreateContext(TestData.SystemPrincipal))
            {
                var facts = await context.AnalysisAccessFacts.IgnoreQueryFilters().Where(f => f.UserId == managerId).OrderBy(f => f.AccessedAt).ToListAsync();
                Assert.Equal(3, facts.Count);
                var exported = Assert.Single(facts, f => f.IsExport);
                Assert.Equal(Core.Entities.Analysis.ExportFormat.Xlsx, exported.ExportFormat);
                Assert.Equal(objectId, exported.ObjectId);
                var unauthorized = Assert.Single(facts, f => f.Outcome == AccessOutcome.Unauthorized);
                Assert.NotNull(unauthorized.ObjectId);
                var catalogued = await context.DatabaseObjects.IgnoreQueryFilters().SingleAsync(o => o.ObjectId == unauthorized.ObjectId);
                Assert.False(catalogued.IsApproved);
                Assert.Equal(_db.Data.OrganizationAId, catalogued.OrganizationId);
                Assert.Equal("Administrators", unauthorized.ObjectName);
                Assert.Equal("object_not_permitted", unauthorized.FailureCode);
                Assert.Equal(0, unauthorized.RowCount);
            }

            // 4. Tenant boundary: organization B's context sees none of organization A's facts.
            await using (var contextB = _db.CreateContext(_db.Data.OrganizationBUser))
            {
                // Other tests in this collection may leave organization B facts of its own; none of A's may ever be visible.
                Assert.Equal(0, await contextB.AnalysisAccessFacts.CountAsync(f => f.UserId == managerId));
                Assert.All(await contextB.AnalysisAccessFacts.ToListAsync(), f => Assert.Equal(_db.Data.OrganizationBId, f.OrganizationId));
            }

            // 5. Append-only: an update or a delete through the change tracker is refused.
            await using (var context = _db.CreateContext(TestData.SystemPrincipal))
            {
                var fact = await context.AnalysisAccessFacts.IgnoreQueryFilters().FirstAsync(f => f.UserId == managerId);
                fact.RowCount = 999;
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
                context.ChangeTracker.Clear();
                var again = await context.AnalysisAccessFacts.IgnoreQueryFilters().FirstAsync(f => f.UserId == managerId);
                context.AnalysisAccessFacts.Remove(again);
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            }
        }
        finally
        {
            FakeAiProvider.Script = null;
        }
    }

    private async Task<HttpClient> LoginAsync(string email, string password = TestData.UserPassword)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
        return client;
    }

    private async Task<(Guid ApplicationId, Guid ObjectId, List<JsonElement> Columns)> PrepareApplicationAsync(HttpClient alice, string tag)
    {
        var source = new SqlConnectionStringBuilder(_db.ConnectionString) { WorkstationID = tag }.ConnectionString;
        var created = await (await alice.PostJsonAsync("/api/applications", new { applicationCode = tag, applicationName = tag, applicationNameAr = tag, databaseType = "SqlServer", connectionString = source })).ReadJsonAsync();
        var applicationId = created.GetProperty("applicationId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/test-connection", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/applications/{applicationId}/approval", new { isApproved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync($"/api/metadata/applications/{applicationId}/refresh", new { })).StatusCode);
        var objects = await (await alice.GetAsync($"/api/metadata/applications/{applicationId}/objects?search=SupportTickets&pageSize=10")).ReadJsonAsync();
        var objectId = objects.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("schemaName").GetString() == "dbo" && o.GetProperty("objectName").GetString() == "SupportTickets").GetProperty("objectId").GetGuid();
        var approved = await (await alice.PostJsonAsync($"/api/metadata/objects/{objectId}/approve", new { approve = true })).ReadJsonAsync();
        return (applicationId, objectId, approved.GetProperty("columns").EnumerateArray().ToList());
    }

    private async Task<(Guid UserId, string Email)> InsertUserAsync(Guid organizationId, Guid? roleId)
    {
        var email = $"ledger.{Guid.NewGuid():N}@org.test";
        var userId = Core.Common.Ids.New();
        await using var context = _db.CreateContext(_db.Data.PlatformAdministrator);
        var now = DateTimeOffset.UtcNow;
        context.OrganizationUsers.Add(TestData.NewUser(userId, organizationId, email, "Ledger User"));
        context.LoginIdentities.Add(TestData.Identity(email, Core.Tenancy.PrincipalType.OrganizationUser, userId, organizationId, now));
        if (roleId is { } r)
        {
            context.UserRoles.Add(new UserRole { OrganizationId = organizationId, UserId = userId, RoleId = r, AssignedAt = now, AssignedByAdministratorId = _db.Data.AdministratorId });
        }

        await context.SaveChangesAsync();
        return (userId, email);
    }
}
