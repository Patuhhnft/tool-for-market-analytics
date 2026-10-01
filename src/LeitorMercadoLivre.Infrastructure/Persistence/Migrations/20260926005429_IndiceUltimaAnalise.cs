using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeitorMercadoLivre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndiceUltimaAnalise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_analyses_product_id_id",
                table: "analyses",
                columns: new[] { "product_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_analyses_product_id_id",
                table: "analyses");
        }
    }
}
