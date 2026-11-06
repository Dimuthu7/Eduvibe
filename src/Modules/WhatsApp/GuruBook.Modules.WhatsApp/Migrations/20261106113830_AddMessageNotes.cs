using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.WhatsApp.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_sms",
                schema: "whatsapp",
                table: "messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "event",
                schema: "whatsapp",
                table: "messages",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "whatsapp",
                table: "messages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allow_sms",
                schema: "whatsapp",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "event",
                schema: "whatsapp",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "note",
                schema: "whatsapp",
                table: "messages");
        }
    }
}
