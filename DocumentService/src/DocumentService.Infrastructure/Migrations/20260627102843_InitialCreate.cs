using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DocumentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TypeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IssuedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MinioPath = table.Column<string>(type: "text", nullable: false),
                    OcrDataRaw = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DocTypeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_DocumentTypes_DocTypeId",
                        column: x => x.DocTypeId,
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentProcesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentProcesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentProcesses_Documents_DocId",
                        column: x => x.DocId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "DocumentTypes",
                columns: new[] { "Id", "Description", "TypeName" },
                values: new object[,]
                {
                    { new Guid("aaaaaaaa-0000-0000-0000-000000000001"), "Công văn đến từ bên ngoài", "CongVanDen" },
                    { new Guid("aaaaaaaa-0000-0000-0000-000000000002"), "Công văn đi ra bên ngoài", "CongVanDi" },
                    { new Guid("aaaaaaaa-0000-0000-0000-000000000003"), "Tờ trình nội bộ", "ToTrinh" },
                    { new Guid("aaaaaaaa-0000-0000-0000-000000000004"), "Quyết định", "QuyetDinh" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentProcesses_DocId",
                table: "DocumentProcesses",
                column: "DocId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocNumber",
                table: "Documents",
                column: "DocNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocTypeId",
                table: "Documents",
                column: "DocTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_TypeName",
                table: "DocumentTypes",
                column: "TypeName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentProcesses");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "DocumentTypes");
        }
    }
}
