using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelpDeskPro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SlaTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstResponseBreachedAt",
                table: "TicketSlas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstResponseWarnedAt",
                table: "TicketSlas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolutionBreachedAt",
                table: "TicketSlas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolutionWarnedAt",
                table: "TicketSlas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorId",
                table: "TicketHistory",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Backfill: classifica violações já registradas (campo único BreachedAt) por tipo.
            migrationBuilder.Sql("""
                UPDATE "TicketSlas" SET "FirstResponseBreachedAt" = "BreachedAt"
                WHERE "BreachedAt" IS NOT NULL AND "FirstRespondedAt" IS NOT NULL AND "FirstRespondedAt" > "FirstResponseDueAt";
                """);
            migrationBuilder.Sql("""
                UPDATE "TicketSlas" s SET "ResolutionBreachedAt" = s."BreachedAt"
                FROM "Tickets" t
                WHERE t."Id" = s."TicketId" AND s."BreachedAt" IS NOT NULL
                  AND t."ResolvedAt" IS NOT NULL AND t."ResolvedAt" > s."ResolutionDueAt";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstResponseBreachedAt",
                table: "TicketSlas");

            migrationBuilder.DropColumn(
                name: "FirstResponseWarnedAt",
                table: "TicketSlas");

            migrationBuilder.DropColumn(
                name: "ResolutionBreachedAt",
                table: "TicketSlas");

            migrationBuilder.DropColumn(
                name: "ResolutionWarnedAt",
                table: "TicketSlas");

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorId",
                table: "TicketHistory",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
