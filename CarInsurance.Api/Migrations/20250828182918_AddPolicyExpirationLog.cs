using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarInsurance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyExpirationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicyExpirationLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PolicyId = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyExpirationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyExpirationLogs_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyExpirationLogs_PolicyId",
                table: "PolicyExpirationLogs",
                column: "PolicyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyExpirationLogs");
        }
    }
}
