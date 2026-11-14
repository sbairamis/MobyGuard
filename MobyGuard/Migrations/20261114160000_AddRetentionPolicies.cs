using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobyGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RetentionPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false, collation: "NOCASE"),
                    RepositoryPattern = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    KeepLastCount = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxAgeDays = table.Column<int>(type: "INTEGER", nullable: true),
                    ProtectedTagPatterns = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RunIntervalHours = table.Column<int>(type: "INTEGER", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastDeletedTagsCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionPolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicies_Name",
                table: "RetentionPolicies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false, collation: "NOCASE"),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TargetUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RepositoryPattern = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    NotifyOnPush = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnDelete = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnRetention = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_Name",
                table: "WebhookSubscriptions",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");

            migrationBuilder.DropTable(
                name: "RetentionPolicies");
        }
    }
}
