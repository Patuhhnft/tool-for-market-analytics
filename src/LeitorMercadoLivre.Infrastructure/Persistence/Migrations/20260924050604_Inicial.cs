using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeitorMercadoLivre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analyses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cycle_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<string>(type: "text", nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    price_status = table.Column<string>(type: "text", nullable: false),
                    market_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    price_confidence = table.Column<string>(type: "text", nullable: true),
                    sellers_found = table.Column<int>(type: "integer", nullable: false),
                    new_sellers_this_week = table.Column<int>(type: "integer", nullable: false),
                    demand_score = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    demand_source = table.Column<string>(type: "text", nullable: false),
                    low_demand = table.Column<bool>(type: "boolean", nullable: false),
                    worst_margin = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    opportunity_index = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    quadrant = table.Column<string>(type: "text", nullable: true),
                    explanation = table.Column<string>(type: "text", nullable: false),
                    price_json = table.Column<string>(type: "jsonb", nullable: false),
                    demand_json = table.Column<string>(type: "jsonb", nullable: false),
                    margin_json = table.Column<string>(type: "jsonb", nullable: true),
                    opportunity_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analyses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credentials",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    provider = table.Column<string>(type: "text", nullable: false),
                    access_token = table.Column<string>(type: "text", nullable: false),
                    refresh_token = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credentials", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cycles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    stats_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cycles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    currency = table.Column<string>(type: "text", nullable: false),
                    requested_date = table.Column<DateOnly>(type: "date", nullable: false),
                    quote_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sell = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rates", x => new { x.currency, x.requested_date });
                });

            migrationBuilder.CreateTable(
                name: "highlights",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cycle_id = table.Column<long>(type: "bigint", nullable: false),
                    category_id = table.Column<string>(type: "text", nullable: false),
                    product_id = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_highlights", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "listings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cycle_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<string>(type: "text", nullable: false),
                    item_id = table.Column<string>(type: "text", nullable: false),
                    seller_id = table.Column<string>(type: "text", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    listing_type_id = table.Column<string>(type: "text", nullable: false),
                    condition = table.Column<string>(type: "text", nullable: false),
                    free_shipping = table.Column<bool>(type: "boolean", nullable: false),
                    logistic_type = table.Column<string>(type: "text", nullable: true),
                    official_store_id = table.Column<long>(type: "bigint", nullable: true),
                    min_purchase_unit = table.Column<int>(type: "integer", nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    category_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seen_sellers",
                columns: table => new
                {
                    product_id = table.Column<string>(type: "text", nullable: false),
                    seller_id = table.Column<string>(type: "text", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seen_sellers", x => new { x.product_id, x.seller_id });
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    minimum_order = table.Column<int>(type: "integer", nullable: false),
                    shipment_quantity = table.Column<int>(type: "integer", nullable: false),
                    international_freight_total = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    contact = table.Column<string>(type: "text", nullable: true),
                    whats_app = table.Column<string>(type: "text", nullable: true),
                    url = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    regime = table.Column<string>(type: "text", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    import_duty = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    import_duty_deduction_foreign = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    ipi = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    pis = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    cofins = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    icms = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "visits",
                columns: table => new
                {
                    item_id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    visits = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visits", x => new { x.item_id, x.date });
                });

            migrationBuilder.CreateIndex(
                name: "IX_analyses_cycle_id_product_id",
                table: "analyses",
                columns: new[] { "cycle_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_analyses_product_id_calculated_at",
                table: "analyses",
                columns: new[] { "product_id", "calculated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_credentials_provider",
                table: "credentials",
                column: "provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cycles_window_start",
                table: "cycles",
                column: "window_start",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_highlights_cycle_id_category_id_product_id",
                table: "highlights",
                columns: new[] { "cycle_id", "category_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_highlights_product_id_collected_at",
                table: "highlights",
                columns: new[] { "product_id", "collected_at" });

            migrationBuilder.CreateIndex(
                name: "IX_listings_cycle_id_item_id",
                table: "listings",
                columns: new[] { "cycle_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_listings_product_id_cycle_id",
                table: "listings",
                columns: new[] { "product_id", "cycle_id" });

            migrationBuilder.CreateIndex(
                name: "IX_seen_sellers_product_id_first_seen_at",
                table: "seen_sellers",
                columns: new[] { "product_id", "first_seen_at" });

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_product_id_active",
                table: "suppliers",
                columns: new[] { "product_id", "active" });

            migrationBuilder.CreateIndex(
                name: "IX_tax_rules_regime_valid_from",
                table: "tax_rules",
                columns: new[] { "regime", "valid_from" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analyses");

            migrationBuilder.DropTable(
                name: "credentials");

            migrationBuilder.DropTable(
                name: "cycles");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "highlights");

            migrationBuilder.DropTable(
                name: "listings");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "seen_sellers");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "tax_rules");

            migrationBuilder.DropTable(
                name: "visits");
        }
    }
}
