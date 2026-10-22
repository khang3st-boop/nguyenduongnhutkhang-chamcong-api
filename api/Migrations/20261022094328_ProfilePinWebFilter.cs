using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NguyenDuongNhutKhang_app.Migrations
{
    /// <inheritdoc />
    public partial class ProfilePinWebFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotifyEnter",
                table: "SafeZones",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyExit",
                table: "SafeZones",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PinHash",
                table: "Families",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "AspNetUsers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BlockedSites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FamilyId = table.Column<int>(type: "int", nullable: false),
                    ChildId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Domain = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedSites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedSites_FamilyId_ChildId",
                table: "BlockedSites",
                columns: new[] { "FamilyId", "ChildId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockedSites");

            migrationBuilder.DropColumn(
                name: "NotifyEnter",
                table: "SafeZones");

            migrationBuilder.DropColumn(
                name: "NotifyExit",
                table: "SafeZones");

            migrationBuilder.DropColumn(
                name: "PinHash",
                table: "Families");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "AspNetUsers");
        }
    }
}
