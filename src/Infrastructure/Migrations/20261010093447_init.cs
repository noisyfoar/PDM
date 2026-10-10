using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pdm_object",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectType = table.Column<string>(type: "text", nullable: false),
                    Designation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pdm_object", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "object_version",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    VersionType = table.Column<string>(type: "text", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Material = table.Column<string>(type: "text", nullable: true),
                    MassKg = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_object_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_object_version_pdm_object_ObjectId",
                        column: x => x.ObjectId,
                        principalTable: "pdm_object",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "bom_link",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ParentVersionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bom_link", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bom_link_object_version_ParentVersionId",
                        column: x => x.ParentVersionId,
                        principalTable: "object_version",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_bom_link_pdm_object_ChildObjectId",
                        column: x => x.ChildObjectId,
                        principalTable: "pdm_object",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bom_link_ChildObjectId",
                table: "bom_link",
                column: "ChildObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_bom_link_ParentVersionId_ChildObjectId",
                table: "bom_link",
                columns: new[] { "ParentVersionId", "ChildObjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_object_version_ObjectId_VersionNumber",
                table: "object_version",
                columns: new[] { "ObjectId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pdm_object_Designation",
                table: "pdm_object",
                column: "Designation",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bom_link");

            migrationBuilder.DropTable(
                name: "object_version");

            migrationBuilder.DropTable(
                name: "pdm_object");
        }
    }
}
