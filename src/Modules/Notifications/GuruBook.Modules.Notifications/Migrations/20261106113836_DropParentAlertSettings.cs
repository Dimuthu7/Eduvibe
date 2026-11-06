using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class DropParentAlertSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parent_alert_settings",
                schema: "notifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parent_alert_settings",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attendance_alerts = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parent_alert_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parent_alert_settings_tenant_id",
                schema: "notifications",
                table: "parent_alert_settings",
                column: "tenant_id");
        }
    }
}
