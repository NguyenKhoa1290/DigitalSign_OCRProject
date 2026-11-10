using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentDepartmentScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentName",
                table: "Documents",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSchoolWide",
                table: "Documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DepartmentId_IsSchoolWide",
                table: "Documents",
                columns: new[] { "DepartmentId", "IsSchoolWide" });

            // Existing rows can only be reconciled from the current creator directory.
            // Missing creators/units stay unscoped and must never become school-wide.
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL") migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF to_regclass('"AppUsers"') IS NOT NULL AND to_regclass('"Departments"') IS NOT NULL THEN
                    UPDATE "Documents" doc SET "DepartmentId"=d."Id", "DepartmentName"=d."DeptName",
                        "IsSchoolWide"=(d."ParentId" IS NULL)
                    FROM "AppUsers" u JOIN "Departments" d ON d."Id"=u."DepartmentId"
                    WHERE doc."CreatedById"=u."Id";
                  END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_DepartmentId_IsSchoolWide",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DepartmentName",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IsSchoolWide",
                table: "Documents");
        }
    }
}
