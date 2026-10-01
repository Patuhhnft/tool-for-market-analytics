using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeitorMercadoLivre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EixoDoTempo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "demand_phase",
                table: "analyses",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "runway_json",
                table: "analyses",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "runway_weeks_lower",
                table: "analyses",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "worth_importing",
                table: "analyses",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "demand_phase",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "runway_json",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "runway_weeks_lower",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "worth_importing",
                table: "analyses");
        }
    }
}
