using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractMonthlyClaimSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddTotalHoursToClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalHours",
                table: "Claims",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "PaymentBatches",
                keyColumn: "BatchId",
                keyValue: 1,
                column: "GeneratedDate",
                value: new DateTime(2025, 10, 21, 11, 39, 19, 130, DateTimeKind.Local).AddTicks(9109));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalHours",
                table: "Claims");

            migrationBuilder.UpdateData(
                table: "PaymentBatches",
                keyColumn: "BatchId",
                keyValue: 1,
                column: "GeneratedDate",
                value: new DateTime(2025, 10, 20, 16, 4, 6, 499, DateTimeKind.Local).AddTicks(4288));
        }
    }
}
