using System.Text.Json;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Permissions;
using WabiInsight.Core.Permissions;

namespace WabiInsight.UnitTests.Ai;

public sealed class QueryPlanAuthorizerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid App = Guid.NewGuid();
    private static readonly Guid OtherApp = Guid.NewGuid();
    private static readonly Guid Orders = Guid.NewGuid();
    private static readonly Guid Customers = Guid.NewGuid();

    private static EffectiveColumn Column(string name) => new(Guid.NewGuid(), name, "nvarchar", null, null, DataClassification.Internal);

    private static EffectivePermissions Permissions(IReadOnlyList<RowScope> orderScopes, bool yields = true, RowScopeContext? context = null) => new(Guid.NewGuid(), Guid.NewGuid(), ["Analyst"], context ?? new RowScopeContext("E-1", "FIN", null),
    [
        new EffectiveObjectAccess(App, "ERP", Orders, "dbo", "Orders", DatabaseObjectType.Table, null, null, [Column("OrderId"), Column("CustomerId"), Column("Amount"), Column("Status"), Column("DeptCode"), Column("EmpNo")], orderScopes, [], yields, ["Analyst"]),
        new EffectiveObjectAccess(App, "ERP", Customers, "dbo", "Customers", DatabaseObjectType.Table, null, null, [Column("CustomerId"), Column("Name")], [RowScope.All], [], true, ["Analyst"]),
        new EffectiveObjectAccess(OtherApp, "CRM", Guid.NewGuid(), "dbo", "Leads", DatabaseObjectType.Table, null, null, [Column("LeadId")], [RowScope.All], [], true, ["Analyst"]),
    ]);

    private static QueryPlan Plan(string select = "{\"alias\":\"name\",\"column\":\"c.Name\",\"aggregate\":null,\"dateTrunc\":null},{\"alias\":\"total\",\"column\":\"o.Amount\",\"aggregate\":\"sum\",\"dateTrunc\":null}", string source = "dbo", string sourceObject = "Orders") => JsonSerializer.Deserialize<QueryPlan>($$"""
        {"objective":"x","source":{"schema":"{{source}}","object":"{{sourceObject}}","alias":"o"},
         "joins":[{"schema":"dbo","object":"Customers","alias":"c","type":"inner","on":[{"left":"o.CustomerId","right":"c.CustomerId"}]}],
         "select":[{{select}}],"filters":null,"groupBy":["name"],"orderBy":[],"limit":null,"visualization":null,"assumptions":[]}
        """, Json)!;

    private static readonly Dictionary<Guid, RowMappingColumns> Mappings = new() { [Orders] = new RowMappingColumns("EmpNo", "DeptCode", null) };

    [Fact]
    public void Authorize_AttachesRowPredicatesFromTheUsersContext_AndAppliesTheDefaultLimit()
    {
        var (plan, errors) = QueryPlanAuthorizer.Authorize(Plan(), App, Permissions([RowScope.Department, RowScope.Self]), Mappings);
        Assert.Empty(errors);
        Assert.NotNull(plan);
        var orders = plan!.Objects.Single(o => o.Alias == "o");
        Assert.Equal(2, orders.RowPredicates.Count);
        Assert.Contains(orders.RowPredicates, p => p.Column == "DeptCode" && p.Value == "FIN" && p.Scope == RowScope.Department);
        Assert.Contains(orders.RowPredicates, p => p.Column == "EmpNo" && p.Value == "E-1" && p.Scope == RowScope.Self);
        Assert.Empty(plan.Objects.Single(o => o.Alias == "c").RowPredicates);
        Assert.Equal(100, plan.EffectiveLimit);

        // The answer keeps which scopes limited it, so the interpretation and the page can say so.
        Assert.Equal([new PanelRowScope("Department", "FIN"), new PanelRowScope("Self", "E-1")], PanelAccess.From(plan));
        Assert.Equal("limited to what the asker may see: only rows of the asker's department or branch FIN or only the asker's own records (employee E-1)", PanelAccess.Describe(PanelAccess.From(plan)));
        Assert.Equal("unrestricted: every row of the queried objects", PanelAccess.Describe(null));

        var (unscoped, _) = QueryPlanAuthorizer.Authorize(Plan(), App, Permissions([RowScope.All]), Mappings);
        Assert.Empty(PanelAccess.From(unscoped!));
    }

    [Fact]
    public void Authorize_RejectsObjectsAndColumnsOutsideThePermissions_IncludingOtherApplications()
    {
        var (_, missingColumn) = QueryPlanAuthorizer.Authorize(Plan(select: "{\"alias\":\"s\",\"column\":\"o.Secret\",\"aggregate\":null,\"dateTrunc\":null}"), App, Permissions([RowScope.All]), Mappings);
        Assert.Contains(missingColumn, e => e.Code == "column_not_permitted" && e.Detail == "o.Secret");

        var (_, otherApp) = QueryPlanAuthorizer.Authorize(Plan(sourceObject: "Leads"), App, Permissions([RowScope.All]), Mappings);
        Assert.Contains(otherApp, e => e.Code == "object_not_permitted" && e.Detail == "dbo.Leads");

        var (_, unknown) = QueryPlanAuthorizer.Authorize(Plan(source: "sys", sourceObject: "tables"), App, Permissions([RowScope.All]), Mappings);
        Assert.Contains(unknown, e => e.Code == "object_not_permitted");
    }

    [Fact]
    public void Authorize_DeniesRowsWhenNoScopeCanBind()
    {
        // Section scope without a mapped section column, and Self without an employee number in the context.
        var (plan, errors) = QueryPlanAuthorizer.Authorize(Plan(), App, Permissions([RowScope.Section, RowScope.Self], context: new RowScopeContext(null, "FIN", "S1")), Mappings);
        Assert.Null(plan);
        Assert.Contains(errors, e => e.Code == "row_access_denied" && e.Detail == "dbo.Orders");
    }
}
