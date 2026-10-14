using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduVibe.Modules.Exams.Migrations
{
    /// <inheritdoc />
    public partial class AddExams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "exams");

            migrationBuilder.CreateTable(
                name: "grade_schemes",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grade_schemes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exams",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_scheme_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    held_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exams", x => x.id);
                    table.ForeignKey(
                        name: "fk_exams_grade_schemes_grade_scheme_id",
                        column: x => x.grade_scheme_id,
                        principalSchema: "exams",
                        principalTable: "grade_schemes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grade_bands",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_scheme_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    min_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grade_bands", x => x.id);
                    table.ForeignKey(
                        name: "fk_grade_bands_grade_schemes_grade_scheme_id",
                        column: x => x.grade_scheme_id,
                        principalSchema: "exams",
                        principalTable: "grade_schemes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exam_parts",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    max_mark = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_parts", x => x.id);
                    table.ForeignKey(
                        name: "fk_exam_parts_exams_exam_id",
                        column: x => x.exam_id,
                        principalSchema: "exams",
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exam_results",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_absent = table.Column<bool>(type: "boolean", nullable: false),
                    total = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    rank = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_exam_results_exams_exam_id",
                        column: x => x.exam_id,
                        principalSchema: "exams",
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exam_part_marks",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mark = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_part_marks", x => x.id);
                    table.ForeignKey(
                        name: "fk_exam_part_marks_exam_parts_exam_part_id",
                        column: x => x.exam_part_id,
                        principalSchema: "exams",
                        principalTable: "exam_parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_part_marks_results_exam_result_id",
                        column: x => x.exam_result_id,
                        principalSchema: "exams",
                        principalTable: "exam_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exam_part_marks_exam_part_id",
                schema: "exams",
                table: "exam_part_marks",
                column: "exam_part_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_part_marks_exam_result_id_exam_part_id",
                schema: "exams",
                table: "exam_part_marks",
                columns: new[] { "exam_result_id", "exam_part_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_part_marks_tenant_id",
                schema: "exams",
                table: "exam_part_marks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_parts_exam_id",
                schema: "exams",
                table: "exam_parts",
                column: "exam_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_parts_tenant_id",
                schema: "exams",
                table: "exam_parts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_enrolment_id",
                schema: "exams",
                table: "exam_results",
                column: "enrolment_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_exam_id_enrolment_id",
                schema: "exams",
                table: "exam_results",
                columns: new[] { "exam_id", "enrolment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_results_tenant_id",
                schema: "exams",
                table: "exam_results",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_exams_class_id_held_on",
                schema: "exams",
                table: "exams",
                columns: new[] { "class_id", "held_on" });

            migrationBuilder.CreateIndex(
                name: "ix_exams_grade_scheme_id",
                schema: "exams",
                table: "exams",
                column: "grade_scheme_id");

            migrationBuilder.CreateIndex(
                name: "ix_exams_tenant_id",
                schema: "exams",
                table: "exams",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_grade_bands_grade_scheme_id",
                schema: "exams",
                table: "grade_bands",
                column: "grade_scheme_id");

            migrationBuilder.CreateIndex(
                name: "ix_grade_bands_tenant_id",
                schema: "exams",
                table: "grade_bands",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_grade_schemes_tenant_id",
                schema: "exams",
                table: "grade_schemes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_grade_schemes_tenant_id_name",
                schema: "exams",
                table: "grade_schemes",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exam_part_marks",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "grade_bands",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "exam_parts",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "exam_results",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "exams",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "grade_schemes",
                schema: "exams");
        }
    }
}
