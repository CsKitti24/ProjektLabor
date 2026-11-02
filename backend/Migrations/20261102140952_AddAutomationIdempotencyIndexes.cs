using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationIdempotencyIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MegbizhatosagiEsemenyek_KiadasiKulcsmozgasId",
                table: "MegbizhatosagiEsemenyek");

            migrationBuilder.AddColumn<bool>(
                name: "Automatikus",
                table: "MegbizhatosagiEsemenyek",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AutomationKey",
                table: "Ertesitesek",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MegbizhatosagiEsemenyek_KiadasiKulcsmozgasId",
                table: "MegbizhatosagiEsemenyek",
                column: "KiadasiKulcsmozgasId",
                unique: true,
                filter: "[Automatikus] = 1 AND [KiadasiKulcsmozgasId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Ertesitesek_AutomationKey",
                table: "Ertesitesek",
                column: "AutomationKey",
                unique: true,
                filter: "[AutomationKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MegbizhatosagiEsemenyek_KiadasiKulcsmozgasId",
                table: "MegbizhatosagiEsemenyek");

            migrationBuilder.DropIndex(
                name: "IX_Ertesitesek_AutomationKey",
                table: "Ertesitesek");

            migrationBuilder.DropColumn(
                name: "Automatikus",
                table: "MegbizhatosagiEsemenyek");

            migrationBuilder.DropColumn(
                name: "AutomationKey",
                table: "Ertesitesek");

            migrationBuilder.CreateIndex(
                name: "IX_MegbizhatosagiEsemenyek_KiadasiKulcsmozgasId",
                table: "MegbizhatosagiEsemenyek",
                column: "KiadasiKulcsmozgasId");
        }
    }
}
