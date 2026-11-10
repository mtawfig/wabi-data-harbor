using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertEvaluatedAs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EvaluatedAsUserId",
                table: "Alerts",
                type: "uniqueidentifier",
                nullable: true);

            // Alerts raised before this column: the best knowledge is the rule's owner today, which is what decided
            // their visibility until now. From here on the owner is recorded at every evaluation.
            migrationBuilder.Sql("""
                UPDATE a SET a.EvaluatedAsUserId = r.OwnerUserId
                FROM Alerts a
                JOIN MonitoringRules r ON r.OrganizationId = a.OrganizationId AND r.RuleId = a.RuleId
                WHERE a.EvaluatedAsUserId IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvaluatedAsUserId",
                table: "Alerts");
        }
    }
}
