using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommunicationModule.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS `openmrsinstances` (
  `Id` char(36) NOT NULL,
  `OrganisationId` char(36) NOT NULL,
  `DisplayName` longtext NOT NULL,
  `BaseUrl` longtext NOT NULL,
  `ApiVersion` longtext NOT NULL,
  `IsActive` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;");

                        migrationBuilder.Sql(@"ALTER TABLE `openmrsinstances` ADD COLUMN IF NOT EXISTS `AccessKeyHash` longtext NOT NULL DEFAULT '';");

            migrationBuilder.AlterColumn<string>(
                name: "BaseUrl",
                table: "openmrsinstances",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "BaseUrl",
                table: "openmrsinstances",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
