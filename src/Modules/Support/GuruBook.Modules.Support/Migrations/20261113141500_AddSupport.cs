using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Support.Migrations
{
    /// <inheritdoc />
    public partial class AddSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "support");

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    hours = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_replies",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    from_support = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_replies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    screen = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    shot_content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    shot = table.Column<byte[]>(type: "bytea", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tickets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_replies_tenant_id",
                schema: "support",
                table: "ticket_replies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_replies_ticket_id",
                schema: "support",
                table: "ticket_replies",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id",
                schema: "support",
                table: "tickets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_id_status",
                schema: "support",
                table: "tickets",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settings",
                schema: "support");

            migrationBuilder.DropTable(
                name: "ticket_replies",
                schema: "support");

            migrationBuilder.DropTable(
                name: "tickets",
                schema: "support");
        }
    }
}
