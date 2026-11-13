using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The <c>insights</c> schema belongs to the owner of <c>dbo</c> (P13.43).
    /// AddPlatformInsights created the schema without naming its owner, so it belonged to whoever ran the migration.
    /// When that is the database owner itself (development, CI) the insights views and the tables they read have one
    /// owner, and the read-only insights login (SELECT on the schema only) can read the views. When the application's
    /// login is a member of db_owner instead (the deployment guides), the schema belonged to that login, the views and
    /// the tables had different owners, and SQL Server refused the insights login on every view:
    /// "The SELECT permission was denied on the object …, schema 'dbo'". Platform and organization insights then
    /// answered "insights query failed" as soon as the dedicated login was configured.
    /// Permissions granted on the schema are kept: they are read before the change and granted again after it.
    /// </summary>
    public partial class AlignInsightsSchemaOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Insights.InsightsSchema.AlignOwnerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the previous owner was an accident of who ran the migration.
        }
    }
}
