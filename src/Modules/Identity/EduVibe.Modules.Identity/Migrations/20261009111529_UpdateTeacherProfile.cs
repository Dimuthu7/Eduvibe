using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduVibe.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTeacherProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "subjects",
                schema: "identity",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "town",
                schema: "identity",
                table: "teachers");

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                schema: "identity",
                table: "users",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                schema: "identity",
                table: "users",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            // Keep existing names: the last word becomes the last name, the rest the first name.
            migrationBuilder.Sql(@"
                UPDATE identity.users SET
                  first_name = CASE WHEN position(' ' in btrim(full_name)) > 0
                                    THEN left(regexp_replace(btrim(full_name), '\s+\S+$', ''), 60)
                                    ELSE left(btrim(full_name), 60) END,
                  last_name  = CASE WHEN position(' ' in btrim(full_name)) > 0
                                    THEN left((regexp_match(btrim(full_name), '(\S+)$'))[1], 60)
                                    ELSE '-' END;");

            migrationBuilder.DropColumn(
                name: "full_name",
                schema: "identity",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "district",
                schema: "identity",
                table: "teachers",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "stream_id",
                schema: "identity",
                table: "teachers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "teacher_subjects",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_subjects", x => x.id);
                    table.ForeignKey(
                        name: "fk_teacher_subjects_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalSchema: "identity",
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_subjects_subject_id",
                schema: "identity",
                table: "teacher_subjects",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_subjects_teacher_id_subject_id",
                schema: "identity",
                table: "teacher_subjects",
                columns: new[] { "teacher_id", "subject_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teacher_subjects",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "first_name",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_name",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "district",
                schema: "identity",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "stream_id",
                schema: "identity",
                table: "teachers");

            migrationBuilder.AddColumn<string>(
                name: "full_name",
                schema: "identity",
                table: "users",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "subjects",
                schema: "identity",
                table: "teachers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "town",
                schema: "identity",
                table: "teachers",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }
    }
}
