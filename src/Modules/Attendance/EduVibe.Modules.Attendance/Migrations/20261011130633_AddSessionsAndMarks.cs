using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduVibe.Modules.Attendance.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionsAndMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "attendance");

            migrationBuilder.CreateTable(
                name: "class_sessions",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_extra = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attendance_marks",
                schema: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_marks", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_marks_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "attendance",
                        principalTable: "class_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_marks_enrolment_id",
                schema: "attendance",
                table: "attendance_marks",
                column: "enrolment_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_marks_session_id_enrolment_id",
                schema: "attendance",
                table: "attendance_marks",
                columns: new[] { "session_id", "enrolment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_marks_tenant_id",
                schema: "attendance",
                table: "attendance_marks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_class_id_date_start",
                schema: "attendance",
                table: "class_sessions",
                columns: new[] { "class_id", "date", "start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_tenant_id",
                schema: "attendance",
                table: "class_sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_tenant_id_date",
                schema: "attendance",
                table: "class_sessions",
                columns: new[] { "tenant_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_marks",
                schema: "attendance");

            migrationBuilder.DropTable(
                name: "class_sessions",
                schema: "attendance");
        }
    }
}
