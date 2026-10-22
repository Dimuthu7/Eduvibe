using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GuruBook.Modules.Fees.Migrations
{
    /// <inheritdoc />
    public partial class AddFeesLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fees");

            migrationBuilder.CreateTable(
                name: "fee_dues",
                schema: "fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    amount_due = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_dues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    received_by = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                schema: "fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_due_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_allocations_fee_dues_fee_due_id",
                        column: x => x.fee_due_id,
                        principalSchema: "fees",
                        principalTable: "fee_dues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "fees",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fee_dues_class_id_month",
                schema: "fees",
                table: "fee_dues",
                columns: new[] { "class_id", "month" });

            migrationBuilder.CreateIndex(
                name: "ix_fee_dues_enrolment_id_month",
                schema: "fees",
                table: "fee_dues",
                columns: new[] { "enrolment_id", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fee_dues_tenant_id",
                schema: "fees",
                table: "fee_dues",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_fee_due_id",
                schema: "fees",
                table: "payment_allocations",
                column: "fee_due_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_id",
                schema: "fees",
                table: "payment_allocations",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_tenant_id",
                schema: "fees",
                table: "payment_allocations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_enrolment_id",
                schema: "fees",
                table: "payments",
                column: "enrolment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id",
                schema: "fees",
                table: "payments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_receipt_no",
                schema: "fees",
                table: "payments",
                columns: new[] { "tenant_id", "receipt_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_allocations",
                schema: "fees");

            migrationBuilder.DropTable(
                name: "fee_dues",
                schema: "fees");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "fees");
        }
    }
}
