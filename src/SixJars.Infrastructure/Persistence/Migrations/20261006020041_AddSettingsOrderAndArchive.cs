using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SixJars.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsOrderAndArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "PlanningFunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "PlanningFunds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "Categories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Categories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // SortOrder 回填（P4 J plan D1）：Id 是 UUIDv7，依 Id 排序可以大致還原建立順序；
            // 同一毫秒內建立的項目順序不保證，部署後由使用者在設定頁調整。
            // 分類的 PARTITION BY 含 "ParentId"：PostgreSQL 把 NULL 視為同一組，剛好是「同一種類的主分類」。
            migrationBuilder.Sql("""
                UPDATE "Accounts" AS t SET "SortOrder" = o.rn - 1
                FROM (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "BookId" ORDER BY "Id") AS rn FROM "Accounts") AS o
                WHERE t."Id" = o."Id";
                UPDATE "PlanningFunds" AS t SET "SortOrder" = o.rn - 1
                FROM (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "BookId" ORDER BY "Id") AS rn FROM "PlanningFunds") AS o
                WHERE t."Id" = o."Id";
                UPDATE "Categories" AS t SET "SortOrder" = o.rn - 1
                FROM (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "BookId", "Kind", "ParentId" ORDER BY "Id") AS rn FROM "Categories") AS o
                WHERE t."Id" = o."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "PlanningFunds");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "PlanningFunds");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Accounts");
        }
    }
}
