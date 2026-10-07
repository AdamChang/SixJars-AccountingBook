using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SixJars.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringPlannedExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "PlannedExpenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringPlannedExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartMonth = table.Column<int>(type: "integer", nullable: false),
                    EndMonth = table.Column<int>(type: "integer", nullable: true),
                    Months = table.Column<int[]>(type: "integer[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringPlannedExpenses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlannedExpenses_SourceId_BudgetMonth",
                table: "PlannedExpenses",
                columns: new[] { "SourceId", "BudgetMonth" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringPlannedExpenses_BookId",
                table: "RecurringPlannedExpenses",
                column: "BookId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlannedExpenses_RecurringPlannedExpenses_SourceId",
                table: "PlannedExpenses",
                column: "SourceId",
                principalTable: "RecurringPlannedExpenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlannedExpenses_RecurringPlannedExpenses_SourceId",
                table: "PlannedExpenses");

            migrationBuilder.DropTable(
                name: "RecurringPlannedExpenses");

            migrationBuilder.DropIndex(
                name: "IX_PlannedExpenses_SourceId_BudgetMonth",
                table: "PlannedExpenses");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "PlannedExpenses");
        }
    }
}
