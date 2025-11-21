using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractMonthlyClaimSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalDateToClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovalDate",
                table: "Claims",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalDate",
                table: "Claims");
        }
    }
}
