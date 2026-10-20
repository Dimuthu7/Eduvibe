using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduVibe.Modules.Materials.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "drive_parent_id",
                schema: "materials",
                table: "materials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "folder_id",
                schema: "materials",
                table: "materials",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "material_folders",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_kind_root = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    drive_folder_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_material_folders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_materials_folder_id",
                schema: "materials",
                table: "materials",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_material_folders_class_id_kind",
                schema: "materials",
                table: "material_folders",
                columns: new[] { "class_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_material_folders_tenant_id",
                schema: "materials",
                table: "material_folders",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material_folders",
                schema: "materials");

            migrationBuilder.DropIndex(
                name: "ix_materials_folder_id",
                schema: "materials",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "drive_parent_id",
                schema: "materials",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "folder_id",
                schema: "materials",
                table: "materials");
        }
    }
}
