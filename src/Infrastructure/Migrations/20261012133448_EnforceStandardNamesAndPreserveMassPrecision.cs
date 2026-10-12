using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceStandardNamesAndPreserveMassPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "standard_name",
                table: "pdm_object",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MassKg",
                table: "object_version",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pdm_object_standard_name",
                table: "pdm_object",
                column: "standard_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pdm_object_standard_name",
                table: "pdm_object");

            migrationBuilder.DropColumn(
                name: "standard_name",
                table: "pdm_object");

            migrationBuilder.AlterColumn<decimal>(
                name: "MassKg",
                table: "object_version",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);
        }
    }
}
