using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Fees.Migrations
{
    /// <inheritdoc />
    public partial class AddFeePeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                schema: "fees",
                table: "fee_dues",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "last_month",
                schema: "fees",
                table: "fee_dues",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "period_months",
                schema: "fees",
                table: "fee_dues",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Dues made before fee cycles are all monthly, due on the 10th (the default pay-by day) of their month.
            migrationBuilder.Sql(
                "UPDATE fees.fee_dues SET last_month = month, due_date = make_date(substr(month, 1, 4)::int, substr(month, 6, 2)::int, 10) WHERE last_month = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "due_date",
                schema: "fees",
                table: "fee_dues");

            migrationBuilder.DropColumn(
                name: "last_month",
                schema: "fees",
                table: "fee_dues");

            migrationBuilder.DropColumn(
                name: "period_months",
                schema: "fees",
                table: "fee_dues");
        }
    }
}
