using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SignedPdfSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignedFilePath",
                table: "Signatures",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignedFilePath",
                table: "Signatures");
        }
    }
}
