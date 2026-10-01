using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeitorMercadoLivre.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Operadores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "created_by_operator_id",
                table: "suppliers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "updated_by_operator_id",
                table: "suppliers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "operators",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operators", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    operator_id = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operators_name_key",
                table: "operators",
                column: "name_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_sessions_operator_id_started_at",
                table: "work_sessions",
                columns: new[] { "operator_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operators");

            migrationBuilder.DropTable(
                name: "work_sessions");

            migrationBuilder.DropColumn(
                name: "created_by_operator_id",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "updated_by_operator_id",
                table: "suppliers");
        }
    }
}
