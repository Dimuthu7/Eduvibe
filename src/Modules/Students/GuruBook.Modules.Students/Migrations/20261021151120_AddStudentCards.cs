using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Students.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "student_cards",
                schema: "students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_student_cards_students_student_id",
                        column: x => x.student_id,
                        principalSchema: "students",
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_student_cards_code",
                schema: "students",
                table: "student_cards",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_cards_student_id",
                schema: "students",
                table: "student_cards",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_cards_tenant_id",
                schema: "students",
                table: "student_cards",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_cards_tenant_id_student_id",
                schema: "students",
                table: "student_cards",
                columns: new[] { "tenant_id", "student_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "student_cards",
                schema: "students");
        }
    }
}
