using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduVibe.Modules.WhatsApp.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "whatsapp");

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    to_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    params_json = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    dedup_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    error = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "opt_outs",
                schema: "whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opt_outs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_messages_provider_message_id",
                schema: "whatsapp",
                table: "messages",
                column: "provider_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_status",
                schema: "whatsapp",
                table: "messages",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_messages_student_id",
                schema: "whatsapp",
                table: "messages",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id",
                schema: "whatsapp",
                table: "messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_dedup_key",
                schema: "whatsapp",
                table: "messages",
                columns: new[] { "tenant_id", "dedup_key" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_month",
                schema: "whatsapp",
                table: "messages",
                columns: new[] { "tenant_id", "month" });

            migrationBuilder.CreateIndex(
                name: "ix_opt_outs_tenant_id",
                schema: "whatsapp",
                table: "opt_outs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_opt_outs_tenant_id_phone",
                schema: "whatsapp",
                table: "opt_outs",
                columns: new[] { "tenant_id", "phone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages",
                schema: "whatsapp");

            migrationBuilder.DropTable(
                name: "opt_outs",
                schema: "whatsapp");
        }
    }
}
