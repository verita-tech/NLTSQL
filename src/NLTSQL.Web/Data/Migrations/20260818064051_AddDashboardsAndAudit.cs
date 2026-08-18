using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NLTSQL.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardsAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dashboard",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dashboard", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "query_run",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    QuerySpecJson = table.Column<string>(type: "TEXT", nullable: true),
                    Sql = table.Column<string>(type: "TEXT", nullable: true),
                    RowCount = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationMs = table.Column<double>(type: "REAL", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_query_run", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "saved_query",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModelVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    QuerySpecJson = table.Column<string>(type: "TEXT", nullable: false),
                    ChartSpecJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_query", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_tile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DashboardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SavedQueryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ColumnSpan = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: true),
                    SnapshotTakenAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dashboard_tile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dashboard_tile_dashboard_DashboardId",
                        column: x => x.DashboardId,
                        principalTable: "dashboard",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dashboard_tile_saved_query_SavedQueryId",
                        column: x => x.SavedQueryId,
                        principalTable: "saved_query",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dashboard_Title",
                table: "dashboard",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_dashboard_tile_DashboardId_Position",
                table: "dashboard_tile",
                columns: new[] { "DashboardId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_dashboard_tile_SavedQueryId",
                table: "dashboard_tile",
                column: "SavedQueryId");

            migrationBuilder.CreateIndex(
                name: "IX_query_run_StartedAt",
                table: "query_run",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_query_run_UserId",
                table: "query_run",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_saved_query_CreatedAt",
                table: "saved_query",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dashboard_tile");

            migrationBuilder.DropTable(
                name: "query_run");

            migrationBuilder.DropTable(
                name: "dashboard");

            migrationBuilder.DropTable(
                name: "saved_query");
        }
    }
}
