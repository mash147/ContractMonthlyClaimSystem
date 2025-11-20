using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractMonthlyClaimSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentBatchAndHRModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "Claims",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PaymentBatchId",
                table: "Claims",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDate",
                table: "Claims",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PaymentBatches",
                columns: table => new
                {
                    BatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    GeneratedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalClaims = table.Column<int>(type: "int", nullable: false),
                    GeneratedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentBatches", x => x.BatchId);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledReports",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScheduleTime = table.Column<long>(type: "bigint", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    NextRunDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledReports", x => x.ScheduleId);
                });

            migrationBuilder.InsertData(
                table: "PaymentBatches",
                columns: new[] { "BatchId", "BatchNumber", "GeneratedBy", "GeneratedDate", "TotalAmount", "TotalClaims" },
                values: new object[] { 1, "BATCH-INITIAL", "System", new DateTime(2025, 10, 20, 16, 4, 6, 499, DateTimeKind.Local).AddTicks(4288), 0m, 0 });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_PaymentBatchId",
                table: "Claims",
                column: "PaymentBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Claims_PaymentBatches_PaymentBatchId",
                table: "Claims",
                column: "PaymentBatchId",
                principalTable: "PaymentBatches",
                principalColumn: "BatchId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Claims_PaymentBatches_PaymentBatchId",
                table: "Claims");

            migrationBuilder.DropTable(
                name: "PaymentBatches");

            migrationBuilder.DropTable(
                name: "ScheduledReports");

            migrationBuilder.DropIndex(
                name: "IX_Claims_PaymentBatchId",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "PaymentBatchId",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "PaymentDate",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AspNetUsers");
        }
    }
}
