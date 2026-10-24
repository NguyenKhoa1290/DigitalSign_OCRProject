using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IncomingDocumentWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedToName",
                table: "Documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncomingDirectionPath",
                table: "Documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncomingOriginalPath",
                table: "Documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingDepartmentId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingDepartmentName",
                table: "Documents",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedToId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AssignedToName",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IncomingDirectionPath",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IncomingOriginalPath",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ProcessingDepartmentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ProcessingDepartmentName",
                table: "Documents");
        }
    }
}
