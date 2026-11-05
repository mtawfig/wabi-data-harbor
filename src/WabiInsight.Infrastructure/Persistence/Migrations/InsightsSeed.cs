#nullable enable

using Microsoft.EntityFrameworkCore.Migrations;
using WabiInsight.Infrastructure.Insights;

namespace WabiInsight.Infrastructure.Persistence.Migrations;

/// <summary>
/// Seeds the global Platform Insights catalog from <see cref="InsightsSchema"/>: views and their shipped columns
/// (approved, with EN/AR labels) and the initial metrics. Idempotent: existing rows are left untouched so
/// administrators' edits survive re-runs. Column types come from the live view definitions.
/// </summary>
internal static class InsightsSeed
{
    public static void Apply(MigrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var o in InsightsSchema.Objects)
        {
            builder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM dbo.InsightsObjects WHERE ViewName = {Q(o.ViewName)})
                    INSERT INTO dbo.InsightsObjects (InsightsObjectId, ViewName, DisplayNameEn, DisplayNameAr, Description, IsApproved, IsPresent, SortOrder, UpdatedAt)
                    VALUES (NEWID(), {Q(o.ViewName)}, {Q(o.En)}, {Q(o.Ar)}, {Q(o.Description)}, 1, 1, {o.SortOrder}, SYSDATETIMEOFFSET());
                """);
            foreach (var c in o.Columns)
            {
                builder.Sql($"""
                    IF NOT EXISTS (SELECT 1 FROM dbo.InsightsColumns ic INNER JOIN dbo.InsightsObjects io ON io.InsightsObjectId = ic.InsightsObjectId WHERE io.ViewName = {Q(o.ViewName)} AND ic.ColumnName = {Q(c.Name)})
                        INSERT INTO dbo.InsightsColumns (InsightsColumnId, InsightsObjectId, ColumnName, DataType, IsNullable, OrdinalPosition, DisplayNameEn, DisplayNameAr, Description, IsApproved, IsPresent)
                        SELECT NEWID(), io.InsightsObjectId, col.COLUMN_NAME, col.DATA_TYPE, CASE WHEN col.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END, col.ORDINAL_POSITION, {Q(c.En)}, {Q(c.Ar)}, NULL, 1, 1
                        FROM dbo.InsightsObjects io
                        INNER JOIN INFORMATION_SCHEMA.COLUMNS col ON col.TABLE_SCHEMA = 'insights' AND col.TABLE_NAME = io.ViewName AND col.COLUMN_NAME = {Q(c.Name)}
                        WHERE io.ViewName = {Q(o.ViewName)};
                    """);
            }
        }

        foreach (var m in InsightsSchema.Metrics)
        {
            builder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM dbo.InsightsMetrics WHERE MetricCode = {Q(m.Code)})
                    INSERT INTO dbo.InsightsMetrics (InsightsMetricId, MetricCode, InsightsObjectId, DisplayNameEn, DisplayNameAr, Description, Aggregate, ColumnName, DateColumn, FiltersJson, Unit, IsApproved, SortOrder, UpdatedAt)
                    SELECT NEWID(), {Q(m.Code)}, io.InsightsObjectId, {Q(m.En)}, {Q(m.Ar)}, {Q(m.Description)}, {Q(m.Aggregate.ToString())}, {QN(m.Column)}, {QN(m.DateColumn)}, {Q(m.FiltersJson)}, {Q(m.Unit)}, 1, {m.SortOrder}, SYSDATETIMEOFFSET()
                    FROM dbo.InsightsObjects io WHERE io.ViewName = {Q(m.ViewName)};
                """);
        }
    }

    private static string Q(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string QN(string? value) => value is null ? "NULL" : Q(value);
}
