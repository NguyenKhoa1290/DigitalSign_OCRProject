using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Signatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignatureType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signatures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Signatures_DocId",
                table: "Signatures",
                column: "DocId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Signatures");
        }
    }
}
