using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationIssueAuditKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutomationKey",
                table: "AuditLogok",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogok_AutomationKey",
                table: "AuditLogok",
                column: "AutomationKey",
                unique: true,
                filter: "[AutomationKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogok_AutomationKey",
                table: "AuditLogok");

            migrationBuilder.DropColumn(
                name: "AutomationKey",
                table: "AuditLogok");
        }
    }
}
