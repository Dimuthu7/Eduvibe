using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Fees.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "class_reminder_settings",
                schema: "fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    weekly_limit = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_reminder_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fee_reminders",
                schema: "fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_reminders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reminder_skips",
                schema: "fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_skips", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_class_reminder_settings_class_id",
                schema: "fees",
                table: "class_reminder_settings",
                column: "class_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_class_reminder_settings_tenant_id",
                schema: "fees",
                table: "class_reminder_settings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_reminders_due_id_step_number",
                schema: "fees",
                table: "fee_reminders",
                columns: new[] { "due_id", "step", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fee_reminders_enrolment_id",
                schema: "fees",
                table: "fee_reminders",
                column: "enrolment_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_reminders_tenant_id",
                schema: "fees",
                table: "fee_reminders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_skips_enrolment_id",
                schema: "fees",
                table: "reminder_skips",
                column: "enrolment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reminder_skips_tenant_id",
                schema: "fees",
                table: "reminder_skips",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "class_reminder_settings",
                schema: "fees");

            migrationBuilder.DropTable(
                name: "fee_reminders",
                schema: "fees");

            migrationBuilder.DropTable(
                name: "reminder_skips",
                schema: "fees");
        }
    }
}
