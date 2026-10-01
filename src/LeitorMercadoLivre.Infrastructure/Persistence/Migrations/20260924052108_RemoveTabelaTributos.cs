using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeitorMercadoLivre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTabelaTributos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_rules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tax_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cofins = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    icms = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    import_duty = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    import_duty_deduction_foreign = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    ipi = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    pis = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    regime = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tax_rules_regime_valid_from",
                table: "tax_rules",
                columns: new[] { "regime", "valid_from" },
                unique: true);
        }
    }
}
