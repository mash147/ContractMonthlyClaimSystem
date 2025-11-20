using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractMonthlyClaimSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddUserToAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditTrails_AspNetUsers_UserId",
                table: "AuditTrails");

            migrationBuilder.RenameColumn(
                name: "AuditID",
                table: "AuditTrails",
                newName: "AuditTrailID");

            migrationBuilder.AlterColumn<string>(
                name: "AdditionalNotes",
                table: "Claims",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AuditTrails",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "ActionBy",
                table: "AuditTrails",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ActionDate",
                table: "AuditTrails",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AuditTrails",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IPAddress",
                table: "AuditTrails",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NewValues",
                table: "AuditTrails",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OldValues",
                table: "AuditTrails",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "PaymentBatches",
                keyColumn: "BatchId",
                keyValue: 1,
                column: "GeneratedDate",
                value: new DateTime(2025, 10, 21, 12, 31, 29, 92, DateTimeKind.Local).AddTicks(5207));

            migrationBuilder.AddForeignKey(
                name: "FK_AuditTrails_AspNetUsers_UserId",
                table: "AuditTrails",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditTrails_AspNetUsers_UserId",
                table: "AuditTrails");

            migrationBuilder.DropColumn(
                name: "ActionBy",
                table: "AuditTrails");

            migrationBuilder.DropColumn(
                name: "ActionDate",
                table: "AuditTrails");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AuditTrails");

            migrationBuilder.DropColumn(
                name: "IPAddress",
                table: "AuditTrails");

            migrationBuilder.DropColumn(
                name: "NewValues",
                table: "AuditTrails");

            migrationBuilder.DropColumn(
                name: "OldValues",
                table: "AuditTrails");

            migrationBuilder.RenameColumn(
                name: "AuditTrailID",
                table: "AuditTrails",
                newName: "AuditID");

            migrationBuilder.AlterColumn<string>(
                name: "AdditionalNotes",
                table: "Claims",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AuditTrails",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "PaymentBatches",
                keyColumn: "BatchId",
                keyValue: 1,
                column: "GeneratedDate",
                value: new DateTime(2025, 10, 21, 11, 39, 19, 130, DateTimeKind.Local).AddTicks(9109));

            migrationBuilder.AddForeignKey(
                name: "FK_AuditTrails_AspNetUsers_UserId",
                table: "AuditTrails",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
