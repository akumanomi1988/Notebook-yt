using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotebookYt.Api.Migrations
{
    /// <inheritdoc />
    public partial class CompleteEditorialConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Productions_Niches_ContentNicheId",
                table: "Productions");

            migrationBuilder.DropIndex(
                name: "IX_Productions_ContentNicheId",
                table: "Productions");

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAtUtc",
                table: "Productions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "Singleton",
                table: "Channels",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Niches_Id_ChannelId",
                table: "Niches",
                columns: new[] { "Id", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_Productions_ContentNicheId_ChannelId",
                table: "Productions",
                columns: new[] { "ContentNicheId", "ChannelId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Production_Status",
                table: "Productions",
                sql: "\"Status\" IN ('Draft','BriefReady','AwaitingNotebook','Blocked','Cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_Singleton",
                table: "Channels",
                column: "Singleton",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Channel_Singleton",
                table: "Channels",
                sql: "\"Singleton\" = true");

            migrationBuilder.AddForeignKey(
                name: "FK_Productions_Niches_ContentNicheId_ChannelId",
                table: "Productions",
                columns: new[] { "ContentNicheId", "ChannelId" },
                principalTable: "Niches",
                principalColumns: new[] { "Id", "ChannelId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("UPDATE \"Productions\" SET \"ModifiedAtUtc\" = \"CreatedAtUtc\"");
            migrationBuilder.Sql("""
                CREATE FUNCTION enforce_complete_week() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Channels" c WHERE
                        (SELECT count(*) FROM "Niches" n WHERE n."ChannelId" = c."Id") <> 7) THEN
                        RAISE EXCEPTION 'A configured channel requires exactly seven weekday assignments' USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER complete_week_channel AFTER INSERT OR UPDATE ON "Channels"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_complete_week();
                CREATE CONSTRAINT TRIGGER complete_week_niche AFTER INSERT OR UPDATE OR DELETE ON "Niches"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_complete_week();
                CREATE FUNCTION protect_production_history() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."Status" = 'Cancelled' OR NEW."NicheNameSnapshot" <> OLD."NicheNameSnapshot"
                        OR NEW."ContentNicheId" <> OLD."ContentNicheId" OR NEW."ChannelId" <> OLD."ChannelId"
                        OR NEW."CreatedAtUtc" <> OLD."CreatedAtUtc" THEN
                        RAISE EXCEPTION 'Production history is immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER production_history_guard BEFORE UPDATE ON "Productions"
                    FOR EACH ROW EXECUTE FUNCTION protect_production_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER production_history_guard ON "Productions";
                DROP FUNCTION protect_production_history();
                DROP TRIGGER complete_week_channel ON "Channels";
                DROP TRIGGER complete_week_niche ON "Niches";
                DROP FUNCTION enforce_complete_week();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Productions_Niches_ContentNicheId_ChannelId",
                table: "Productions");

            migrationBuilder.DropIndex(
                name: "IX_Productions_ContentNicheId_ChannelId",
                table: "Productions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Production_Status",
                table: "Productions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Niches_Id_ChannelId",
                table: "Niches");

            migrationBuilder.DropIndex(
                name: "IX_Channels_Singleton",
                table: "Channels");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Channel_Singleton",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "ModifiedAtUtc",
                table: "Productions");

            migrationBuilder.DropColumn(
                name: "Singleton",
                table: "Channels");

            migrationBuilder.CreateIndex(
                name: "IX_Productions_ContentNicheId",
                table: "Productions",
                column: "ContentNicheId");

            migrationBuilder.AddForeignKey(
                name: "FK_Productions_Niches_ContentNicheId",
                table: "Productions",
                column: "ContentNicheId",
                principalTable: "Niches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
