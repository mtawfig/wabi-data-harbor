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
        Assert.Equal(1000, plan.EffectiveLimit); // no limit asked for: every matching row, up to the maximum (P13.49)

        // The answer keeps which scopes limited it, so the interpretation and the page can say so.
        Assert.Equal([new PanelRowScope("Department", "FIN"), new PanelRowScope("Self", "E-1")], PanelAccess.From(plan));
        Assert.Equal("limited to what the asker may see: only rows of the asker's department FIN or only the asker's own records (employee E-1)", PanelAccess.Describe(PanelAccess.From(plan)));
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

    [Fact]
    public void Authorize_ResolvesAskerPlaceholders_FromTheAskersOwnContext_ForEveryRowScope()
    {
        // "My department" also works for a user whose role reads every row: the placeholder narrows, it never widens.
        var plan = Plan();
        plan.Filters = new PlanFilterGroup { Conditions = [new PlanCondition { Column = "o.DeptCode", Operator = "eq", Values = [AskerValues.Department] }], Groups = [new PlanFilterGroup { Logic = "or", Conditions = [new PlanCondition { Column = "o.EmpNo", Operator = "in", Values = [AskerValues.Employee] }] }] };
        var (authorized, errors) = QueryPlanAuthorizer.Authorize(plan, App, Permissions([RowScope.All]), Mappings);
        Assert.Empty(errors);
        Assert.Equal(new Dictionary<string, string> { [AskerValues.Department] = "FIN", [AskerValues.Employee] = "E-1" }, authorized!.AskerValues);

        // No section code in the profile: the question cannot be answered, and the message says why.
        plan.Filters.Conditions.Add(new PlanCondition { Column = "o.DeptCode", Operator = "eq", Values = [AskerValues.Section] });
        var (missing, missingErrors) = QueryPlanAuthorizer.Authorize(plan, App, Permissions([RowScope.All]), Mappings);
        Assert.Null(missing);
        Assert.Contains(missingErrors, e => e.Code == "asker_value_missing" && e.Detail == AskerValues.Section);

        var (plain, _) = QueryPlanAuthorizer.Authorize(Plan(), App, Permissions([RowScope.All]), Mappings);
        Assert.Null(plain!.AskerValues);
    }

    [Fact]
    public void Authorize_ResolvesRelativeDates_AgainstTheDayOfTheRun_SoAStoredPlanFollowsTheCalendar()
    {
        var plan = Plan();
        plan.Filters = new PlanFilterGroup { Conditions = [new PlanCondition { Column = "o.DeptCode", Operator = "gte", Values = ["$month_start"] }, new PlanCondition { Column = "o.DeptCode", Operator = "lte", Values = ["$today"] }] };

        var (november, errors) = QueryPlanAuthorizer.Authorize(plan, App, Permissions([RowScope.All]), Mappings, today: new DateOnly(2026, 11, 10));
        Assert.Empty(errors);
        Assert.Equal(new Dictionary<string, string> { ["$month_start"] = "2026-11-01", ["$today"] = "2026-11-10" }, november!.AskerValues);

        // The same stored plan, run by a periodic report a month later.
        var (december, _) = QueryPlanAuthorizer.Authorize(plan, App, Permissions([RowScope.All]), Mappings, today: new DateOnly(2026, 12, 15));
        Assert.Equal(new Dictionary<string, string> { ["$month_start"] = "2026-12-01", ["$today"] = "2026-12-15" }, december!.AskerValues);
        Assert.Equal("$month_start", plan.Filters.Conditions[0].Values[0]);

        plan.Filters.Conditions.Add(new PlanCondition { Column = "o.DeptCode", Operator = "gte", Values = ["$today-7x"] });
        var (invalid, invalidErrors) = QueryPlanAuthorizer.Authorize(plan, App, Permissions([RowScope.All]), Mappings, today: new DateOnly(2026, 11, 10));
        Assert.Null(invalid);
        Assert.Contains(invalidErrors, e => e.Code == "plan_relative_date_invalid");
    }

    [Fact]
    public void Authorize_ChecksTheComparedColumnLikeAnyOtherReference()
    {
        var plan = Plan();
        plan.Filters = new PlanFilterGroup { Conditions = [new PlanCondition { Column = "o.Amount", Operator = "gt", CompareTo = "o.Secret" }] };
        var (_, errors) = QueryPlanAuthorizer.Authorize(plan, App, Permissions([RowScope.All]), Mappings);
        Assert.Contains(errors, e => e.Code == "column_not_permitted" && e.Detail == "o.Secret");
    }
}
