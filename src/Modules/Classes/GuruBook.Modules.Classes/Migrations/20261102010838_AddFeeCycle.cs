using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Classes.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fee_cycle_months",
                schema: "classes",
                table: "classes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "pay_by_day",
                schema: "classes",
                table: "classes",
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fee_cycle_months",
                schema: "classes",
                table: "classes");

            migrationBuilder.DropColumn(
                name: "pay_by_day",
                schema: "classes",
                table: "classes");
        }
    }
}
