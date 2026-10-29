using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SixJars.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorizationCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousRefreshTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpGrants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpGrants_AuthorizationCodeId",
                table: "McpGrants",
                column: "AuthorizationCodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpGrants_PreviousRefreshTokenHash",
                table: "McpGrants",
                column: "PreviousRefreshTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_McpGrants_RefreshTokenHash",
                table: "McpGrants",
                column: "RefreshTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_McpGrants_Subject",
                table: "McpGrants",
                column: "Subject");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpGrants");
        }
    }
}
