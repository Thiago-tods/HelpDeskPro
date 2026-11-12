using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelpDeskPro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryDefaultTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultTeamId",
                table: "Categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_DefaultTeamId",
                table: "Categories",
                column: "DefaultTeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Teams_DefaultTeamId",
                table: "Categories",
                column: "DefaultTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Teams_DefaultTeamId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_DefaultTeamId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "DefaultTeamId",
                table: "Categories");
        }
    }
}
