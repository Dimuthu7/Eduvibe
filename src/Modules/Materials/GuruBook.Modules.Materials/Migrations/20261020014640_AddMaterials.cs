using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Materials.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "materials");

            migrationBuilder.CreateTable(
                name: "class_folders",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    drive_folder_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_folders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drive_connections",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    refresh_token = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    root_folder_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    needs_reconnect = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drive_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "materials",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    drive_file_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    topic = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_materials", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_class_folders_class_id",
                schema: "materials",
                table: "class_folders",
                column: "class_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_class_folders_tenant_id",
                schema: "materials",
                table: "class_folders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_drive_connections_tenant_id",
                schema: "materials",
                table: "drive_connections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_drive_connections_tenant_id1",
                schema: "materials",
                table: "drive_connections",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_materials_class_id",
                schema: "materials",
                table: "materials",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_materials_tenant_id",
                schema: "materials",
                table: "materials",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "class_folders",
                schema: "materials");

            migrationBuilder.DropTable(
                name: "drive_connections",
                schema: "materials");

            migrationBuilder.DropTable(
                name: "materials",
                schema: "materials");
        }
    }
}
