using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeitorMercadoLivre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RiscosDoProduto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "risk_json",
                table: "analyses",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "risk_json",
                table: "analyses");
        }
    }
}
