using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentPurgeRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentPurgeRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OriginalVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PurgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObjectNamesJson = table.Column<string>(type: "jsonb", nullable: false),
                    FilesCleanedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CleanupAttempts = table.Column<int>(type: "integer", nullable: false),
                    CleanupError = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPurgeRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPurgeRecords_DocId",
                table: "DocumentPurgeRecords",
                column: "DocId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentPurgeRecords");
        }
    }
}
