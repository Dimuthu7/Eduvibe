using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduVibe.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    params_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    link = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    once_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    group_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_id_group_key",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "recipient_id", "group_key" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_id_once_key",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "recipient_id", "once_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_id_updated_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "recipient_id", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");
        }
    }
}
