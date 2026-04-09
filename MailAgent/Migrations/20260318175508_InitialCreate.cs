using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeuerSoftware.MailAgent.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    EMailHost = table.Column<string>(type: "TEXT", nullable: false),
                    EMailPort = table.Column<int>(type: "INTEGER", nullable: false),
                    EMailUsername = table.Column<string>(type: "TEXT", nullable: false),
                    EMailPassword = table.Column<string>(type: "TEXT", nullable: false),
                    EMailSubjectFilter = table.Column<string>(type: "TEXT", nullable: false),
                    EMailSenderFilter = table.Column<string>(type: "TEXT", nullable: false),
                    AuthenticationType = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailAgentSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EMailPollingIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    SecretKeyPassphrase = table.Column<string>(type: "TEXT", nullable: false),
                    OutputPath = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessMode = table.Column<string>(type: "TEXT", nullable: false),
                    EMailMode = table.Column<string>(type: "TEXT", nullable: false),
                    IgnoreCertificateErrors = table.Column<bool>(type: "INTEGER", nullable: false),
                    HeartbeatInterval = table.Column<string>(type: "TEXT", nullable: true),
                    HeartbeatUrl = table.Column<string>(type: "TEXT", nullable: false),
                    O365ClientId = table.Column<string>(type: "TEXT", nullable: false),
                    DisableEmailAgeThreshold = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailAgentSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatternSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartPattern = table.Column<string>(type: "TEXT", nullable: false),
                    NumberPattern = table.Column<string>(type: "TEXT", nullable: false),
                    KeywordPattern = table.Column<string>(type: "TEXT", nullable: false),
                    FactsPattern = table.Column<string>(type: "TEXT", nullable: false),
                    StreetPattern = table.Column<string>(type: "TEXT", nullable: false),
                    HouseNumberPattern = table.Column<string>(type: "TEXT", nullable: false),
                    CityPattern = table.Column<string>(type: "TEXT", nullable: false),
                    DistrictPattern = table.Column<string>(type: "TEXT", nullable: false),
                    ZipCodePattern = table.Column<string>(type: "TEXT", nullable: false),
                    RicPattern = table.Column<string>(type: "TEXT", nullable: false),
                    LongitudePattern = table.Column<string>(type: "TEXT", nullable: false),
                    LatitudePattern = table.Column<string>(type: "TEXT", nullable: false),
                    ReporterNamePattern = table.Column<string>(type: "TEXT", nullable: false),
                    ReporterPhonePattern = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatternSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdditionalPatterns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatternSettingsId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalPatterns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdditionalPatterns_PatternSettings_PatternSettingsId",
                        column: x => x.PatternSettingsId,
                        principalTable: "PatternSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalPatterns_PatternSettingsId",
                table: "AdditionalPatterns",
                column: "PatternSettingsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdditionalPatterns");

            migrationBuilder.DropTable(
                name: "EmailAccounts");

            migrationBuilder.DropTable(
                name: "MailAgentSettings");

            migrationBuilder.DropTable(
                name: "PatternSettings");
        }
    }
}
