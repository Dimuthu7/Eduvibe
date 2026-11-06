using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Messaging.Migrations
{
    /// <inheritdoc />
    public partial class AddMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.CreateTable(
                name: "message_settings",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(40)", maxLength: 40, nullable: false),
                    on = table.Column<bool>(type: "boolean", nullable: false),
                    way = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    percent = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message_texts",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(40)", maxLength: 40, nullable: false),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    text = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_texts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_settings_tenant_id",
                schema: "messaging",
                table: "message_settings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_settings_tenant_id_event",
                schema: "messaging",
                table: "message_settings",
                columns: new[] { "tenant_id", "event" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_message_texts_tenant_id",
                schema: "messaging",
                table: "message_texts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_texts_tenant_id_event_language",
                schema: "messaging",
                table: "message_texts",
                columns: new[] { "tenant_id", "event", "language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_settings",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "message_texts",
                schema: "messaging");
        }
    }
}
