using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowSkippedEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EmailMessages_Status",
                table: "EmailMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmailMessages_Status",
                table: "EmailMessages",
                sql: "[Status] IN ('Queued','Sent','Failed','Skipped')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EmailMessages_Status",
                table: "EmailMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmailMessages_Status",
                table: "EmailMessages",
                sql: "[Status] IN ('Queued','Sent','Failed')");
        }
    }
}
