using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduVibe.Modules.Classes.Migrations
{
    /// <inheritdoc />
    public partial class AddInstituteDistrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "district",
                schema: "classes",
                table: "institutes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "district",
                schema: "classes",
                table: "institutes");
        }
    }
}
