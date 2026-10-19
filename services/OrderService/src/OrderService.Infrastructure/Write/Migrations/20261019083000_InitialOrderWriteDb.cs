using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OrderService.Infrastructure.Write;

#nullable disable

namespace OrderService.Infrastructure.Write.Migrations;

[DbContext(typeof(OrderDbContext))]
[Migration("20261019083000_InitialOrderWriteDb")]
public partial class InitialOrderWriteDb : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "idempotency_keys",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                response_status_code = table.Column<int>(type: "integer", nullable: true),
                response_body = table.Column<string>(type: "jsonb", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_idempotency_keys", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            columns: table => new
            {
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                consumer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                processed_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox_messages", x => new { x.message_id, x.consumer });
            });

        migrationBuilder.CreateTable(
            name: "orders",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                recipient_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                status_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                cancelled_by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                payment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                placed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_orders", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                occurred_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                processed_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                next_retry_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "order_lines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                quantity = table.Column<int>(type: "integer", nullable: false),
                line_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                line_total_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_lines", x => x.id);
                table.ForeignKey(
                    name: "FK_order_lines_orders_order_id",
                    column: x => x.order_id,
                    principalTable: "orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ux_idempotency_keys_user_id_key",
            table: "idempotency_keys",
            columns: new[] { "user_id", "key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_order_lines_order_id",
            table: "order_lines",
            column: "order_id");

        migrationBuilder.CreateIndex(
            name: "ix_orders_status",
            table: "orders",
            column: "status");

        migrationBuilder.CreateIndex(
            name: "ix_orders_user_id",
            table: "orders",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_unprocessed",
            table: "outbox_messages",
            columns: new[] { "next_retry_at_utc", "occurred_on_utc" },
            filter: "processed_on_utc IS NULL AND attempts < 10");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "idempotency_keys");
        migrationBuilder.DropTable(name: "inbox_messages");
        migrationBuilder.DropTable(name: "order_lines");
        migrationBuilder.DropTable(name: "outbox_messages");
        migrationBuilder.DropTable(name: "orders");
    }
}
