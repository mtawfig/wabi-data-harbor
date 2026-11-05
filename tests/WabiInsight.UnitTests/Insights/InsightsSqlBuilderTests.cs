using WabiInsight.Core.Entities.Insights;
using WabiInsight.Core.Insights;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Platform;

namespace WabiInsight.UnitTests.Insights;

public sealed class InsightsSqlBuilderTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly InsightsViewShape Events = new("UsageEvents", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["EventType"] = "nvarchar", ["ActorId"] = "uniqueidentifier", ["DepartmentId"] = "uniqueidentifier", ["Quantity"] = "bigint", ["Succeeded"] = "bit", ["CreatedAt"] = "datetimeoffset",
    });

    private static readonly MetricDefinition Logins = new("logins.total", MetricAggregate.Count, null, "CreatedAt", [new MetricFilter("EventType", "eq", ["Auth.Login"])]);

    [Fact]
    public void Build_AlwaysStartsWithTheTenantPredicate_AndParameterizesValues()
    {
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var built = InsightsSqlBuilder.Build(Logins, Events, new MetricQueryRequest(from, to, MetricBucket.Day, "DepartmentId", null), Tenant);

        Assert.Equal("SELECT CAST([CreatedAt] AS date) AS [Bucket], [DepartmentId] AS [Group], COUNT_BIG(*) AS [Value] FROM [insights].[UsageEvents] WHERE [OrganizationId] = @tenant AND [CreatedAt] >= @from AND [CreatedAt] < @to AND [EventType] = @f0 GROUP BY CAST([CreatedAt] AS date), [DepartmentId] ORDER BY [Bucket], [Group]", built.Sql);
        Assert.Equal(Tenant, built.Parameters["@tenant"]);
        Assert.Equal("Auth.Login", built.Parameters["@f0"]);
        Assert.DoesNotContain("Auth.Login", built.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RefusesTheOrganizationColumn_UnknownColumns_AndBadOperators()
    {
        Assert.Equal("insights_group_by_not_allowed", Assert.Throws<DomainException>(() => InsightsSqlBuilder.Build(Logins, Events, new MetricQueryRequest(null, null, null, "OrganizationId", null), Tenant)).Message);
        Assert.Equal("insights_column_not_allowed", Assert.Throws<DomainException>(() => InsightsSqlBuilder.Build(Logins, Events, new MetricQueryRequest(null, null, null, null, [new MetricFilter("OrganizationId", "eq", [Guid.NewGuid().ToString()])]), Tenant)).Message);
        Assert.Equal("insights_group_by_not_allowed", Assert.Throws<DomainException>(() => InsightsSqlBuilder.Build(Logins, Events, new MetricQueryRequest(null, null, null, "MetadataJson", null), Tenant)).Message);
        Assert.Equal("insights_group_by_not_allowed", Assert.Throws<DomainException>(() => InsightsSqlBuilder.Build(Logins, Events, new MetricQueryRequest(null, null, null, "EventType; DROP TABLE x", null), Tenant)).Message);
        Assert.Equal("insights_filter_invalid", Assert.Throws<DomainException>(() => InsightsSqlBuilder.Build(Logins, Events, new MetricQueryRequest(null, null, null, null, [new MetricFilter("EventType", "like", ["x"])]), Tenant)).Message);
        Assert.Equal("insights_filter_invalid", Assert.Throws<DomainException>(() => InsightsSqlBuilder.Build(Logins, Events, new MetricQueryRequest(null, null, null, null, [new MetricFilter("Succeeded", "eq", ["maybe", "no"])]), Tenant)).Message);
        Assert.Equal("insights_view_invalid", Assert.Throws<DomainException>(() => InsightsSqlBuilder.Build(Logins, new InsightsViewShape("Users]; --", Events.ApprovedColumns), new MetricQueryRequest(null, null, null, null, null), Tenant)).Message);
    }

    [Fact]
    public void Build_TypedParameters_InLists_AndAggregates()
    {
        var distinct = new MetricDefinition("users.loggedIn", MetricAggregate.CountDistinct, "ActorId", "CreatedAt", []);
        var built = InsightsSqlBuilder.Build(distinct, Events, new MetricQueryRequest(null, null, null, null, [new MetricFilter("Succeeded", "eq", ["true"]), new MetricFilter("EventType", "in", ["A", "B"]), new MetricFilter("Quantity", "gte", ["5"])]), Tenant);
        Assert.Equal("SELECT COUNT_BIG(DISTINCT [ActorId]) AS [Value] FROM [insights].[UsageEvents] WHERE [OrganizationId] = @tenant AND [Succeeded] = @f0 AND [EventType] IN (@f1, @f2) AND [Quantity] >= @f3", built.Sql);
        Assert.Equal(true, built.Parameters["@f0"]);
        Assert.Equal(5L, built.Parameters["@f3"]);

        var sum = new MetricDefinition("usage.quantity", MetricAggregate.Sum, "Quantity", "CreatedAt", []);
        Assert.Contains("SUM(CAST([Quantity] AS bigint))", InsightsSqlBuilder.Build(sum, Events, new MetricQueryRequest(null, null, MetricBucket.Month, null, null), Tenant).Sql, StringComparison.Ordinal);
        Assert.Equal("insights_metric_column_required", Assert.Throws<DomainException>(() => InsightsSqlBuilder.Build(sum with { Column = null }, Events, new MetricQueryRequest(null, null, null, null, null), Tenant)).Message);
    }

    [Fact]
    public void Build_PointInTimeMetric_RejectsPeriodsAndBuckets()
    {
        var active = new MetricDefinition("users.active", MetricAggregate.Count, null, null, [new MetricFilter("EventType", "eq", ["x"])]);
        Assert.Equal("insights_period_requires_date_column", Assert.Throws<DomainException>(() => InsightsSqlBuilder.Build(active, Events, new MetricQueryRequest(DateTimeOffset.UtcNow, null, null, null, null), Tenant)).Message);
        Assert.Equal("insights_bucket_requires_date_column", Assert.Throws<DomainException>(() => InsightsSqlBuilder.Build(active, Events, new MetricQueryRequest(null, null, MetricBucket.Week, null, null), Tenant)).Message);
        Assert.Equal("SELECT COUNT_BIG(*) AS [Value] FROM [insights].[UsageEvents] WHERE [OrganizationId] = @tenant AND [EventType] = @f0", InsightsSqlBuilder.Build(active, Events, new MetricQueryRequest(null, null, null, null, null), Tenant).Sql);
    }
}
