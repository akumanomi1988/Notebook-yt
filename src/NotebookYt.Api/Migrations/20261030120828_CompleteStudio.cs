using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotebookYt.Api.Migrations
{
    /// <inheritdoc />
    public partial class CompleteStudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackgroundJob",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJob", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackgroundJob_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChannelSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    Audience = table.Column<string>(type: "text", nullable: false),
                    Tone = table.Column<string>(type: "text", nullable: false),
                    Guidelines = table.Column<string>(type: "text", nullable: false),
                    PublishingTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Visibility = table.Column<string>(type: "text", nullable: false),
                    SchedulingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    HorizonDays = table.Column<int>(type: "integer", nullable: false),
                    LeadDays = table.Column<int>(type: "integer", nullable: false),
                    MinimumDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaximumDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    MinimumHeight = table.Column<int>(type: "integer", nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false),
                    RequireEditorialApproval = table.Column<bool>(type: "boolean", nullable: false),
                    NotebookProduct = table.Column<string>(type: "text", nullable: false),
                    EnterpriseLicenseAttested = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelSettings_Channels_Id",
                        column: x => x.Id,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreationReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "text", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreationReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreationReceipt_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EditorialTopic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentNicheId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    NormalizedTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    EvergreenAssessment = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorialTopic", x => x.Id);
                    table.CheckConstraint("CK_Topic_Status", "\"Status\" IN ('Proposed','Approved','Rejected') AND \"Priority\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_EditorialTopic_Niches_ContentNicheId",
                        column: x => x.ContentNicheId,
                        principalTable: "Niches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EditorialTopic_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoogleConnection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ProtectedTokens = table.Column<string>(type: "text", nullable: false),
                    ExternalChannelId = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleConnection", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualNotebookTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Instructions = table.Column<string>(type: "text", nullable: false),
                    ExpectedResult = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ResponsibleOperator = table.Column<string>(type: "text", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    EvidenceKind = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualNotebookTask", x => x.Id);
                    table.CheckConstraint("CK_ManualTask_Status", "\"Status\" IN ('Pending','Completed') AND (\"Status\" <> 'Completed' OR (\"CompletedAtUtc\" IS NOT NULL AND \"EvidenceKind\" IN ('OperatorAttested','ProviderVerified')))");
                    table.ForeignKey(
                        name: "FK_ManualNotebookTask_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaArtifact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OriginalName = table.Column<string>(type: "text", nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    OriginEvidence = table.Column<string>(type: "text", nullable: false),
                    Container = table.Column<string>(type: "text", nullable: false),
                    VideoCodec = table.Column<string>(type: "text", nullable: false),
                    HasAudio = table.Column<bool>(type: "boolean", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    ValidationError = table.Column<string>(type: "text", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaArtifact", x => x.Id);
                    table.CheckConstraint("CK_Artifact_State", "\"SizeBytes\" > 0 AND \"Status\" IN ('PendingInspection','Valid','Rejected','Missing') AND \"Kind\" IN ('Video','Document','Thumbnail')");
                    table.ForeignKey(
                        name: "FK_MediaArtifact_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NicheSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Audience = table.Column<string>(type: "text", nullable: false),
                    InclusionRules = table.Column<string>(type: "text", nullable: false),
                    ExclusionRules = table.Column<string>(type: "text", nullable: false),
                    VideoApproach = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NicheSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NicheSettings_Niches_Id",
                        column: x => x.Id,
                        principalTable: "Niches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OAuthAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StateHash = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ProtectedVerifier = table.Column<string>(type: "text", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Used = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthAttempt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleSlot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentNicheId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    TimeZone = table.Column<string>(type: "text", nullable: false),
                    PublishAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleSlot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleSlot_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleSlot_Niches_ContentNicheId",
                        column: x => x.ContentNicheId,
                        principalTable: "Niches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleSlot_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcePackage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Markdown = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcePackage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourcePackage_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudioEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudioEvent_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionWorkspace",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: true),
                    Stage = table.Column<string>(type: "text", nullable: false),
                    EditorialSnapshot = table.Column<string>(type: "text", nullable: false),
                    Objective = table.Column<string>(type: "text", nullable: false),
                    VideoInstructions = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    Audience = table.Column<string>(type: "text", nullable: false),
                    NotebookCreationMethod = table.Column<string>(type: "text", nullable: false),
                    NotebookVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CanonicalArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    YouTubeTitle = table.Column<string>(type: "text", nullable: false),
                    YouTubeDescription = table.Column<string>(type: "text", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    CategoryId = table.Column<string>(type: "text", nullable: false),
                    MadeForKids = table.Column<bool>(type: "boolean", nullable: true),
                    ContainsSyntheticMedia = table.Column<bool>(type: "boolean", nullable: false),
                    Visibility = table.Column<string>(type: "text", nullable: false),
                    PublishAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovalEvidence = table.Column<string>(type: "text", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionWorkspace", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionWorkspace_EditorialTopic_TopicId",
                        column: x => x.TopicId,
                        principalTable: "EditorialTopic",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionWorkspace_MediaArtifact_CanonicalArtifactId",
                        column: x => x.CanonicalArtifactId,
                        principalTable: "MediaArtifact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionWorkspace_Productions_Id",
                        column: x => x.Id,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QualityReview",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    Operator = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityReview", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityReview_MediaArtifact_ArtifactId",
                        column: x => x.ArtifactId,
                        principalTable: "MediaArtifact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityReview_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResearchSource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    Classification = table.Column<string>(type: "text", nullable: false),
                    Provenance = table.Column<string>(type: "text", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Included = table.Column<bool>(type: "boolean", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegisteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchSource", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchSource_MediaArtifact_ArtifactId",
                        column: x => x.ArtifactId,
                        principalTable: "MediaArtifact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResearchSource_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "YouTubeUpload",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProtectedSessionUrl = table.Column<string>(type: "text", nullable: false),
                    VideoId = table.Column<string>(type: "text", nullable: false),
                    MetadataSnapshot = table.Column<string>(type: "text", nullable: false),
                    BytesConfirmed = table.Column<long>(type: "bigint", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeUpload", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YouTubeUpload_MediaArtifact_ArtifactId",
                        column: x => x.ArtifactId,
                        principalTable: "MediaArtifact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_YouTubeUpload_Productions_ProductionId",
                        column: x => x.ProductionId,
                        principalTable: "Productions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotebookSourceBinding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalSourceId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotebookSourceBinding", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotebookSourceBinding_ResearchSource_ResearchSourceId",
                        column: x => x.ResearchSourceId,
                        principalTable: "ResearchSource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJob_IdempotencyKey",
                table: "BackgroundJob",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJob_ProductionId",
                table: "BackgroundJob",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJob_Status_AvailableAtUtc",
                table: "BackgroundJob",
                columns: new[] { "Status", "AvailableAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CreationReceipt_Key",
                table: "CreationReceipt",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreationReceipt_ProductionId",
                table: "CreationReceipt",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorialTopic_ContentNicheId_NormalizedTitle",
                table: "EditorialTopic",
                columns: new[] { "ContentNicheId", "NormalizedTitle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorialTopic_ProductionId",
                table: "EditorialTopic",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_GoogleConnection_Kind",
                table: "GoogleConnection",
                column: "Kind",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManualNotebookTask_ProductionId_Kind",
                table: "ManualNotebookTask",
                columns: new[] { "ProductionId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaArtifact_ProductionId_Kind_Sha256",
                table: "MediaArtifact",
                columns: new[] { "ProductionId", "Kind", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotebookSourceBinding_ResearchSourceId",
                table: "NotebookSourceBinding",
                column: "ResearchSourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAttempt_StateHash",
                table: "OAuthAttempt",
                column: "StateHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionWorkspace_CanonicalArtifactId",
                table: "ProductionWorkspace",
                column: "CanonicalArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionWorkspace_TopicId",
                table: "ProductionWorkspace",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityReview_ArtifactId",
                table: "QualityReview",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityReview_ProductionId",
                table: "QualityReview",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchSource_ArtifactId",
                table: "ResearchSource",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchSource_ProductionId_Fingerprint",
                table: "ResearchSource",
                columns: new[] { "ProductionId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlot_ChannelId_LocalDate",
                table: "ScheduleSlot",
                columns: new[] { "ChannelId", "LocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlot_ContentNicheId",
                table: "ScheduleSlot",
                column: "ContentNicheId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlot_ProductionId",
                table: "ScheduleSlot",
                column: "ProductionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcePackage_ProductionId_Version",
                table: "SourcePackage",
                columns: new[] { "ProductionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudioEvent_ProductionId",
                table: "StudioEvent",
                column: "ProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeUpload_ArtifactId",
                table: "YouTubeUpload",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeUpload_ProductionId",
                table: "YouTubeUpload",
                column: "ProductionId",
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_studio_snapshot() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."EditorialSnapshot" <> OLD."EditorialSnapshot" THEN
                        RAISE EXCEPTION 'Editorial snapshots are immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER studio_snapshot_guard BEFORE UPDATE ON "ProductionWorkspace"
                    FOR EACH ROW EXECUTE FUNCTION protect_studio_snapshot();
                CREATE FUNCTION prevent_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Audit records and source packages are immutable' USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER source_package_guard BEFORE UPDATE OR DELETE ON "SourcePackage" FOR EACH ROW EXECUTE FUNCTION prevent_history_mutation();
                CREATE TRIGGER studio_event_guard BEFORE UPDATE OR DELETE ON "StudioEvent" FOR EACH ROW EXECUTE FUNCTION prevent_history_mutation();
                CREATE TRIGGER quality_review_guard BEFORE UPDATE OR DELETE ON "QualityReview" FOR EACH ROW EXECUTE FUNCTION prevent_history_mutation();
                CREATE TRIGGER status_history_guard BEFORE UPDATE OR DELETE ON "StatusHistory" FOR EACH ROW EXECUTE FUNCTION prevent_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER studio_snapshot_guard ON "ProductionWorkspace";
                DROP FUNCTION protect_studio_snapshot();
                DROP TRIGGER source_package_guard ON "SourcePackage";
                DROP TRIGGER studio_event_guard ON "StudioEvent";
                DROP TRIGGER quality_review_guard ON "QualityReview";
                DROP TRIGGER status_history_guard ON "StatusHistory";
                DROP FUNCTION prevent_history_mutation();
                """);
            migrationBuilder.DropTable(
                name: "BackgroundJob");

            migrationBuilder.DropTable(
                name: "ChannelSettings");

            migrationBuilder.DropTable(
                name: "CreationReceipt");

            migrationBuilder.DropTable(
                name: "GoogleConnection");

            migrationBuilder.DropTable(
                name: "ManualNotebookTask");

            migrationBuilder.DropTable(
                name: "NicheSettings");

            migrationBuilder.DropTable(
                name: "NotebookSourceBinding");

            migrationBuilder.DropTable(
                name: "OAuthAttempt");

            migrationBuilder.DropTable(
                name: "ProductionWorkspace");

            migrationBuilder.DropTable(
                name: "QualityReview");

            migrationBuilder.DropTable(
                name: "ScheduleSlot");

            migrationBuilder.DropTable(
                name: "SourcePackage");

            migrationBuilder.DropTable(
                name: "StudioEvent");

            migrationBuilder.DropTable(
                name: "YouTubeUpload");

            migrationBuilder.DropTable(
                name: "ResearchSource");

            migrationBuilder.DropTable(
                name: "EditorialTopic");

            migrationBuilder.DropTable(
                name: "MediaArtifact");
        }
    }
}
