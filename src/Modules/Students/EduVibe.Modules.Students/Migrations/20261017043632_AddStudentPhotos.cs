using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduVibe.Modules.Students.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "student_photos",
                schema: "students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_student_photos_students_student_id",
                        column: x => x.student_id,
                        principalSchema: "students",
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_student_photos_student_id",
                schema: "students",
                table: "student_photos",
                column: "student_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "student_photos",
                schema: "students");
        }
    }
}
