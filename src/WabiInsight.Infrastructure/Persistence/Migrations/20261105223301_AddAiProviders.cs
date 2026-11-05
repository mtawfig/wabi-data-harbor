using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WabiInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiProviderConfigurationId",
                table: "Organizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AIProviderConfigurations",
                columns: table => new
                {
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApiKeyHint = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    InputPricePerMillion = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    OutputPricePerMillion = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    CachedInputPricePerMillion = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "int", nullable: false),
                    Effort = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    LastTestAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "bit", nullable: true),
                    LastTestDetail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByAdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIProviderConfigurations", x => x.ProviderId);
                    table.CheckConstraint("CK_AIProviderConfigurations_ProviderType", "[ProviderType] IN ('Anthropic','OpenAiCompatible','Gemini')");
                    table.CheckConstraint("CK_AIProviderConfigurations_Scope", "([Scope] = 'Platform' AND [OrganizationId] IS NULL) OR ([Scope] = 'Organization' AND [OrganizationId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AIProviderConfigurations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIProviderConfigurations_IsDefault",
                table: "AIProviderConfigurations",
                column: "IsDefault",
                unique: true,
                filter: "[IsDefault] = 1 AND [Scope] = 'Platform'");

            migrationBuilder.CreateIndex(
                name: "IX_AIProviderConfigurations_OrganizationId",
                table: "AIProviderConfigurations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AIProviderConfigurations_Scope_OrganizationId",
                table: "AIProviderConfigurations",
                columns: new[] { "Scope", "OrganizationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "AiProviderConfigurationId",
                table: "Organizations");
        }
    }
}
