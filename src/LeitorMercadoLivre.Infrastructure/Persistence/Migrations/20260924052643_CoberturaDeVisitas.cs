using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeitorMercadoLivre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoberturaDeVisitas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "total_visits",
                table: "listings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "visit_coverage",
                columns: table => new
                {
                    item_id = table.Column<string>(type: "text", nullable: false),
                    covered_from = table.Column<DateOnly>(type: "date", nullable: false),
                    covered_to = table.Column<DateOnly>(type: "date", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visit_coverage", x => x.item_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visit_coverage");

            migrationBuilder.DropColumn(
                name: "total_visits",
                table: "listings");
        }
    }
}
