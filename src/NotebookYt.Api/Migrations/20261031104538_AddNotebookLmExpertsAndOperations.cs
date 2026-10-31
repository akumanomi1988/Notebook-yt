using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotebookYt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotebookLmExpertsAndOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastObservedPrivacyStatus",
                table: "YouTubeUpload",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastObservedTitle",
                table: "YouTubeUpload",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRemoteMetadataObservedAtUtc",
                table: "YouTubeUpload",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotebookExpert",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Specialization = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NotebookId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NotebookUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    NicheIdsJson = table.Column<string>(type: "text", nullable: false),
                    StageKeysJson = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookExpert", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotebookLmOperation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    NotebookId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProviderTaskId = table.Column<string>(type: "text", nullable: false),
                    ProviderArtifactId = table.Column<string>(type: "text", nullable: false),
                    MediaArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    RequestJson = table.Column<string>(type: "text", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookLmOperation", x => x.Id);
                    table.CheckConstraint("CK_NotebookLmOperation_Status", "\"Status\" IN ('Pending','Running','Completed','OutcomeUnknown','Failed','DeferredQuota')");
                    table.ForeignKey(
                        name: "FK_NotebookLmOperation_MediaArtifact_MediaArtifactId",
                        column: x => x.MediaArtifactId,
                        principalTable: "MediaArtifact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotebookLmOperation_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotebookLmUsageSnapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsExhausted = table.Column<bool>(type: "boolean", nullable: true),
                    ActiveWindow = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ResetsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VideoAvailable = table.Column<bool>(type: "boolean", nullable: true),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    CheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookLmUsageSnapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpertWorkflowRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpertId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextVersion = table.Column<int>(type: "integer", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    SourceIdsJson = table.Column<string>(type: "text", nullable: false),
                    InputContextJson = table.Column<string>(type: "text", nullable: false),
                    Response = table.Column<string>(type: "text", nullable: false),
                    ReviewedResponse = table.Column<string>(type: "text", nullable: false),
                    ReferencesJson = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<string>(type: "text", nullable: false),
                    ApprovalEvidence = table.Column<string>(type: "text", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpertWorkflowRun", x => x.Id);
                    table.CheckConstraint("CK_ExpertRun_Status", "\"Status\" IN ('Pending','Running','AwaitingReview','Approved','Rejected','OutcomeUnknown','Failed')");
                    table.ForeignKey(
                        name: "FK_ExpertWorkflowRun_NotebookExpert_ExpertId",
                        column: x => x.ExpertId,
                        principalTable: "NotebookExpert",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpertWorkflowRun_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpertWorkflowRun_ExpertId",
                table: "ExpertWorkflowRun",
                column: "ExpertId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpertWorkflowRun_IdempotencyKey",
                table: "ExpertWorkflowRun",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpertWorkflowRun_ProductionId_Sequence",
                table: "ExpertWorkflowRun",
                columns: new[] { "ProductionId", "Sequence" },
                unique: true,
                filter: "\"Status\" <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookExpert_Enabled_Specialization",
                table: "NotebookExpert",
                columns: new[] { "Enabled", "Specialization" });

            migrationBuilder.CreateIndex(
                name: "IX_NotebookLmOperation_IdempotencyKey",
                table: "NotebookLmOperation",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotebookLmOperation_MediaArtifactId",
                table: "NotebookLmOperation",
                column: "MediaArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookLmOperation_ProductionId",
                table: "NotebookLmOperation",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_NotebookLmOperation_Status_ModifiedAtUtc",
                table: "NotebookLmOperation",
                columns: new[] { "Status", "ModifiedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotebookLmUsageSnapshot_CheckedAtUtc",
                table: "NotebookLmUsageSnapshot",
                column: "CheckedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpertWorkflowRun");

            migrationBuilder.DropTable(
                name: "NotebookLmOperation");

            migrationBuilder.DropTable(
                name: "NotebookLmUsageSnapshot");

            migrationBuilder.DropTable(
                name: "NotebookExpert");

            migrationBuilder.DropColumn(
                name: "LastObservedPrivacyStatus",
                table: "YouTubeUpload");

            migrationBuilder.DropColumn(
                name: "LastObservedTitle",
                table: "YouTubeUpload");

            migrationBuilder.DropColumn(
                name: "LastRemoteMetadataObservedAtUtc",
                table: "YouTubeUpload");
        }
    }
}
