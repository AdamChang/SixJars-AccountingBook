using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SixJars.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class McpGrantConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "McpGrants",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "McpGrants");
        }
    }
}
