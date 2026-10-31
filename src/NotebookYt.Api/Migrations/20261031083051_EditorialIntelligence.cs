using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotebookYt.Api.Migrations
{
    /// <inheritdoc />
    public partial class EditorialIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EditorialPackageSnapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationVersion = table.Column<long>(type: "bigint", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorialPackageSnapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditorialPackageSnapshot_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EditorialPackageSnapshot_SourcePackage_SourcePackageId",
                        column: x => x.SourcePackageId,
                        principalTable: "SourcePackage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NarrativeDefinition",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NarrativeDefinition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EditorialAssessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorialAssessment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditorialAssessment_EditorialPackageSnapshot_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "EditorialPackageSnapshot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EditorialAssessment_MediaArtifact_ArtifactId",
                        column: x => x.ArtifactId,
                        principalTable: "MediaArtifact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EditorialAssessment_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EditorialConfiguration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentNicheId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    DraftJson = table.Column<string>(type: "text", nullable: false),
                    ActiveSnapshotId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorialConfiguration", x => x.Id);
                    table.CheckConstraint("CK_EditorialConfiguration_Scope", "num_nonnulls(\"ChannelId\", \"ContentNicheId\", \"ProductionId\") = 1 AND \"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_EditorialConfiguration_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EditorialConfiguration_EditorialPackageSnapshot_ActiveSnaps~",
                        column: x => x.ActiveSnapshotId,
                        principalTable: "EditorialPackageSnapshot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EditorialConfiguration_Niches_ContentNicheId",
                        column: x => x.ContentNicheId,
                        principalTable: "Niches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EditorialConfiguration_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EditorialAssessment_ArtifactId",
                table: "EditorialAssessment",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorialAssessment_ProductionId",
                table: "EditorialAssessment",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorialAssessment_SnapshotId",
                table: "EditorialAssessment",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorialConfiguration_ActiveSnapshotId",
                table: "EditorialConfiguration",
                column: "ActiveSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorialConfiguration_ChannelId",
                table: "EditorialConfiguration",
                column: "ChannelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorialConfiguration_ContentNicheId",
                table: "EditorialConfiguration",
                column: "ContentNicheId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorialConfiguration_ProductionId",
                table: "EditorialConfiguration",
                column: "ProductionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorialPackageSnapshot_ProductionId_ConfigurationVersion",
                table: "EditorialPackageSnapshot",
                columns: new[] { "ProductionId", "ConfigurationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorialPackageSnapshot_SourcePackageId",
                table: "EditorialPackageSnapshot",
                column: "SourcePackageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EditorialAssessment");

            migrationBuilder.DropTable(
                name: "EditorialConfiguration");

            migrationBuilder.DropTable(
                name: "NarrativeDefinition");

            migrationBuilder.DropTable(
                name: "EditorialPackageSnapshot");
        }
    }
}
