using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobyGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogActorTargetDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActorUserId",
                table: "AuditLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "AuditLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Target",
                table: "AuditLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TargetUserId",
                table: "AuditLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId",
                table: "AuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Target",
                table: "AuditLogs",
                column: "Target");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TargetUserId",
                table: "AuditLogs",
                column: "TargetUserId");

            // Historically, Permission_* and User_Created stored the affected user in Username; move it to Target.
            migrationBuilder.Sql("""
                UPDATE "AuditLogs"
                SET "Target" = "Username", "Username" = 'unknown'
                WHERE "Description" IN ('Permission_Created', 'Permission_Updated', 'Permission_Deleted', 'User_Created')
                  AND "Target" = ''
                  AND "Username" <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AuditLogs"
                SET "Username" = "Target"
                WHERE "Description" IN ('Permission_Created', 'Permission_Updated', 'Permission_Deleted', 'User_Created')
                  AND "Username" = 'unknown'
                  AND "Target" <> '';
                """);

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ActorUserId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Target",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TargetUserId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Target",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                table: "AuditLogs");
        }
    }
}
