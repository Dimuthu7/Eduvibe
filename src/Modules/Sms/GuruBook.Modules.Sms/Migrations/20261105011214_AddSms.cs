using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Sms.Migrations
{
    /// <inheritdoc />
    public partial class AddSms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sms");

            migrationBuilder.CreateTable(
                name: "channel_settings",
                schema: "sms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "text_opt_outs",
                schema: "sms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    by_owner = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_text_opt_outs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "texts",
                schema: "sms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    to_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    parts = table.Column<int>(type: "integer", nullable: false),
                    dedup_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    fallback = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_texts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_settings_tenant_id",
                schema: "sms",
                table: "channel_settings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_text_opt_outs_phone",
                schema: "sms",
                table: "text_opt_outs",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_texts_provider_message_id",
                schema: "sms",
                table: "texts",
                column: "provider_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_texts_status",
                schema: "sms",
                table: "texts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_texts_student_id",
                schema: "sms",
                table: "texts",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_texts_tenant_id",
                schema: "sms",
                table: "texts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_texts_tenant_id_dedup_key",
                schema: "sms",
                table: "texts",
                columns: new[] { "tenant_id", "dedup_key" });

            migrationBuilder.CreateIndex(
                name: "ix_texts_tenant_id_month",
                schema: "sms",
                table: "texts",
                columns: new[] { "tenant_id", "month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_settings",
                schema: "sms");

            migrationBuilder.DropTable(
                name: "text_opt_outs",
                schema: "sms");

            migrationBuilder.DropTable(
                name: "texts",
                schema: "sms");
        }
    }
}
