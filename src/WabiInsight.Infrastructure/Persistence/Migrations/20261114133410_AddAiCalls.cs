using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiCalls",
                columns: table => new
                {
                    CallId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CachedInputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheWriteTokens = table.Column<long>(type: "bigint", nullable: false),
                    InputCost = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: true),
                    OutputCost = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: true),
                    TotalCost = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: false),
                    InputPricePerMillion = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: true),
                    OutputPricePerMillion = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: true),
                    CachedInputPricePerMillion = table.Column<decimal>(type: "decimal(14,6)", precision: 14, scale: 6, nullable: true),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StopReason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DurationMs = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCalls", x => x.CallId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCalls_OrganizationId_CreatedAt",
                table: "AiCalls",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCalls_RunId",
                table: "AiCalls",
                column: "RunId");

            // Questions asked before the ledger existed: one row per question that reached a model, so the month's
            // spending does not drop when the totals start to be read from here. Only the total cost was kept then,
            // so the input and output cost of these rows are unknown (NULL); the total is exact.
            migrationBuilder.Sql("""
                INSERT INTO [AiCalls] ([CallId], [OrganizationId], [UserId], [Purpose], [RunId], [ProviderId], [ProviderType], [Model],
                    [InputTokens], [OutputTokens], [CachedInputTokens], [CacheWriteTokens], [InputCost], [OutputCost], [TotalCost],
                    [InputPricePerMillion], [OutputPricePerMillion], [CachedInputPricePerMillion], [Succeeded], [FailureCode], [StopReason], [DurationMs], [CreatedAt])
                SELECT m.[MetricId], m.[OrganizationId], m.[UserId], N'Analysis', m.[MetricId], m.[ProviderId], N'', ISNULL(m.[Model], N''),
                    m.[PlanInputTokens] + m.[InterpretInputTokens] + m.[ReportInputTokens],
                    m.[PlanOutputTokens] + m.[InterpretOutputTokens] + m.[ReportOutputTokens],
                    0, 0, NULL, NULL, m.[EstimatedCost], NULL, NULL, NULL, CAST(1 AS bit), NULL, NULL, m.[PlanMs] + m.[InterpretMs], m.[CreatedAt]
                FROM [AnalysisExecutionMetrics] m
                WHERE m.[EstimatedCost] > 0
                   OR m.[PlanInputTokens] + m.[PlanOutputTokens] + m.[InterpretInputTokens] + m.[InterpretOutputTokens] + m.[ReportInputTokens] + m.[ReportOutputTokens] > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCalls");
        }
    }
}
