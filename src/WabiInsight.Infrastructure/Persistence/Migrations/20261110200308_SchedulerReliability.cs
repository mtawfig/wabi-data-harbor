using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SchedulerReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledJobs_OrganizationId_JobType_TargetId",
                table: "ScheduledJobs");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSuccessAt",
                table: "ScheduledJobs",
                type: "datetimeoffset",
                nullable: true);

            // Jobs that last ran without an error did so at their last run: the digest counts from there.
            migrationBuilder.Sql("""
                UPDATE ScheduledJobs SET LastSuccessAt = LastRunAt WHERE LastOutcome = 'Succeeded' AND LastSuccessAt IS NULL;
                """);

            // Before the index becomes unique: if two instances ever created the same job twice, keep the one that ran
            // last (then the oldest) and remove the others.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT JobId, ROW_NUMBER() OVER (
                        PARTITION BY OrganizationId, JobType, TargetId
                        ORDER BY CASE WHEN LastRunAt IS NULL THEN 1 ELSE 0 END, LastRunAt DESC, CreatedAt, JobId) AS position
                    FROM ScheduledJobs)
                DELETE FROM ScheduledJobs WHERE JobId IN (SELECT JobId FROM ranked WHERE position > 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_OrganizationId_JobType_TargetId",
                table: "ScheduledJobs",
                columns: new[] { "OrganizationId", "JobType", "TargetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledJobs_OrganizationId_JobType_TargetId",
                table: "ScheduledJobs");

            migrationBuilder.DropColumn(
                name: "LastSuccessAt",
                table: "ScheduledJobs");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledJobs_OrganizationId_JobType_TargetId",
                table: "ScheduledJobs",
                columns: new[] { "OrganizationId", "JobType", "TargetId" });
        }
    }
}
