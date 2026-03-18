using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeuerSoftware.MailAgent.Migrations
{
    /// <inheritdoc />
    public partial class RemoveO365ClientId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "O365ClientId",
                table: "MailAgentSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "O365ClientId",
                table: "MailAgentSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
