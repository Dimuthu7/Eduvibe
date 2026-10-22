using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Students.Migrations
{
    /// <inheritdoc />
    public partial class AddParentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parent_links",
                schema: "students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parent_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_parent_links_students_student_id",
                        column: x => x.student_id,
                        principalSchema: "students",
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parent_links_parent_user_id",
                schema: "students",
                table: "parent_links",
                column: "parent_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_parent_links_student_id",
                schema: "students",
                table: "parent_links",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_parent_links_tenant_id",
                schema: "students",
                table: "parent_links",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_parent_links_tenant_id_student_id_parent_user_id",
                schema: "students",
                table: "parent_links",
                columns: new[] { "tenant_id", "student_id", "parent_user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parent_links",
                schema: "students");
        }
    }
}
