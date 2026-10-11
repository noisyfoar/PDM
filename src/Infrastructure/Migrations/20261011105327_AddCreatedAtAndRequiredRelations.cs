using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedAtAndRequiredRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bom_link_object_version_ParentVersionId",
                table: "bom_link");

            migrationBuilder.DropForeignKey(
                name: "FK_object_version_pdm_object_ObjectId",
                table: "object_version");

            migrationBuilder.AlterColumn<Guid>(
                name: "ObjectId",
                table: "object_version",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "object_version",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentVersionId",
                table: "bom_link",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_bom_link_object_version_ParentVersionId",
                table: "bom_link",
                column: "ParentVersionId",
                principalTable: "object_version",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_object_version_pdm_object_ObjectId",
                table: "object_version",
                column: "ObjectId",
                principalTable: "pdm_object",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bom_link_object_version_ParentVersionId",
                table: "bom_link");

            migrationBuilder.DropForeignKey(
                name: "FK_object_version_pdm_object_ObjectId",
                table: "object_version");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "object_version");

            migrationBuilder.AlterColumn<Guid>(
                name: "ObjectId",
                table: "object_version",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentVersionId",
                table: "bom_link",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_bom_link_object_version_ParentVersionId",
                table: "bom_link",
                column: "ParentVersionId",
                principalTable: "object_version",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_object_version_pdm_object_ObjectId",
                table: "object_version",
                column: "ObjectId",
                principalTable: "pdm_object",
                principalColumn: "Id");
        }
    }
}
