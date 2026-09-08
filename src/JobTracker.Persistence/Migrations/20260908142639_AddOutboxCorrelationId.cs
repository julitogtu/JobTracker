using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                schema: "jobs",
                table: "outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_correlation_id",
                schema: "jobs",
                table: "outbox_messages",
                column: "correlation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_correlation_id",
                schema: "jobs",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "jobs",
                table: "outbox_messages");
        }
    }
}
