using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduVibe.Modules.Students.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentsAndEnrolments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "students");

            migrationBuilder.CreateTable(
                name: "invite_links",
                schema: "students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invite_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "students",
                schema: "students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    last_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    school = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    grade = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    guardian_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    guardian_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_students", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "enrolments",
                schema: "students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index_number = table.Column<int>(type: "integer", nullable: false),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                    left_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fee_plan = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrolments", x => x.id);
                    table.ForeignKey(
                        name: "fk_enrolments_students_student_id",
                        column: x => x.student_id,
                        principalSchema: "students",
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_enrolments_class_id_index_number",
                schema: "students",
                table: "enrolments",
                columns: new[] { "class_id", "index_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_enrolments_class_id_student_id",
                schema: "students",
                table: "enrolments",
                columns: new[] { "class_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_enrolments_student_id",
                schema: "students",
                table: "enrolments",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrolments_tenant_id",
                schema: "students",
                table: "enrolments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_invite_links_class_id",
                schema: "students",
                table: "invite_links",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_invite_links_tenant_id",
                schema: "students",
                table: "invite_links",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_invite_links_token",
                schema: "students",
                table: "invite_links",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_students_phone",
                schema: "students",
                table: "students",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_students_user_id",
                schema: "students",
                table: "students",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrolments",
                schema: "students");

            migrationBuilder.DropTable(
                name: "invite_links",
                schema: "students");

            migrationBuilder.DropTable(
                name: "students",
                schema: "students");
        }
    }
}
