using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "bank_details",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    account_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    account_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_details", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_claims",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_due_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slip_content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    slip = table.Column<byte[]>(type: "bytea", nullable: true),
                    slip_removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    claimed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    by_parent = table.Column<bool>(type: "boolean", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_claims", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "qr_codes",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qr_codes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_details_tenant_id",
                schema: "payments",
                table: "bank_details",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_claims_decided_at",
                schema: "payments",
                table: "payment_claims",
                column: "decided_at");

            migrationBuilder.CreateIndex(
                name: "ix_payment_claims_fee_due_id",
                schema: "payments",
                table: "payment_claims",
                column: "fee_due_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_claims_student_id",
                schema: "payments",
                table: "payment_claims",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_claims_tenant_id",
                schema: "payments",
                table: "payment_claims",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_claims_tenant_id_status",
                schema: "payments",
                table: "payment_claims",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_qr_codes_tenant_id",
                schema: "payments",
                table: "qr_codes",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_details",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_claims",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "qr_codes",
                schema: "payments");
        }
    }
}
