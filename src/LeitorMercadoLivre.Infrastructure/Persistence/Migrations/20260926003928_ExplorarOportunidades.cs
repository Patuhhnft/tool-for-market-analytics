using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeitorMercadoLivre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExplorarOportunidades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Renomear, nao apagar e recriar: o EF gera DropColumn+AddColumn para renomeacao,
            // o que jogaria fora os valores ja gravados.
            migrationBuilder.RenameColumn(
                name: "new_sellers_this_week",
                table: "analyses",
                newName: "new_sellers");

            migrationBuilder.AlterColumn<int>(
                name: "new_sellers",
                table: "analyses",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "category_name",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "price_confidence",
                table: "analyses",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_free_shipping",
                table: "analyses",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_new_condition",
                table: "analyses",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_used_condition",
                table: "analyses",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_new_demand",
                table: "analyses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "last_week_visits",
                table: "analyses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "visit_growth_percent",
                table: "analyses",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category_name",
                table: "products");

            migrationBuilder.DropColumn(
                name: "has_free_shipping",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "has_new_condition",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "has_used_condition",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "is_new_demand",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "last_week_visits",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "visit_growth_percent",
                table: "analyses");

            migrationBuilder.AlterColumn<string>(
                name: "price_confidence",
                table: "analyses",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.Sql("UPDATE analyses SET new_sellers = 0 WHERE new_sellers IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "new_sellers",
                table: "analyses",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "new_sellers",
                table: "analyses",
                newName: "new_sellers_this_week");
        }
    }
}
