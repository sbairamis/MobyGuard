using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MobyGuard.Migrations
{
    /// <inheritdoc />
    public partial class DropUserFirstAndLastName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "Users" DROP COLUMN "FirstName";""");
            migrationBuilder.Sql("""ALTER TABLE "Users" DROP COLUMN "LastName";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
