using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.WhatsApp.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppSmsFallback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "handed_to_sms",
                schema: "whatsapp",
                table: "messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "handed_to_sms",
                schema: "whatsapp",
                table: "messages");
        }
    }
}
