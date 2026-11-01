using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SixJars.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class McpBookkeeping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedVia",
                table: "Transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalRef",
                table: "Transactions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Via",
                table: "AuditEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BookId_ExternalRef",
                table: "Transactions",
                columns: new[] { "BookId", "ExternalRef" },
                unique: true,
                filter: "\"ExternalRef\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_BookId_ExternalRef",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CreatedVia",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ExternalRef",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Via",
                table: "AuditEntries");
        }
    }
}
