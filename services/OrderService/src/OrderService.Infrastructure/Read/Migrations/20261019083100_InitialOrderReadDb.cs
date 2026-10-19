using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OrderService.Infrastructure.Read;

#nullable disable

namespace OrderService.Infrastructure.Read.Migrations;

[DbContext(typeof(OrderReadDbContext))]
[Migration("20261019083100_InitialOrderReadDb")]
public partial class InitialOrderReadDb : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "order_details",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                status_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                cancelled_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                payment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                item_count = table.Column<int>(type: "integer", nullable: false),
                shipping_address = table.Column<string>(type: "jsonb", nullable: false),
                items = table.Column<string>(type: "jsonb", nullable: false),
                history = table.Column<string>(type: "jsonb", nullable: false),
                placed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_details", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "order_summaries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                item_count = table.Column<int>(type: "integer", nullable: false),
                placed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_summaries", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_order_summaries_status_placed_at",
            table: "order_summaries",
            columns: new[] { "status", "placed_at_utc" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "ix_order_summaries_user_id_placed_at",
            table: "order_summaries",
            columns: new[] { "user_id", "placed_at_utc" },
            descending: new[] { false, true });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "order_details");
        migrationBuilder.DropTable(name: "order_summaries");
    }
}
