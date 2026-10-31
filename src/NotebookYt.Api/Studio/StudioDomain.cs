using System.Text.Json.Serialization;

namespace NotebookYt.Api.Studio;

public sealed class ChannelSettings
{
    public Guid Id { get; set; }
    public string Description { get; set; } = "";
    public string Language { get; set; } = "es";
    public string Audience { get; set; } = "";
    public string Tone { get; set; } = "";
    public string Guidelines { get; set; } = "";
    public TimeOnly PublishingTime { get; set; } = new(18, 0);
    public string Visibility { get; set; } = "private";
    public bool SchedulingEnabled { get; set; }
    public int HorizonDays { get; set; } = 14;
    public int LeadDays { get; set; } = 3;
    public int MinimumDurationSeconds { get; set; } = 30;
    public int MaximumDurationSeconds { get; set; } = 3600;
    public int MinimumHeight { get; set; } = 720;
    public int RetentionDays { get; set; } = 90;
    public bool RequireEditorialApproval { get; set; } = true;
    public string NotebookProduct { get; set; } = "Personal";
    public bool EnterpriseLicenseAttested { get; set; }
}

public sealed class NicheSettings
{
    public Guid Id { get; set; }
    public string Audience { get; set; } = "";
    public string InclusionRules { get; set; } = "";
    public string ExclusionRules { get; set; } = "";
    public string VideoApproach { get; set; } = "";
}

public sealed class EditorialTopic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentNicheId { get; set; }
    public string Title { get; set; } = "";
    public string NormalizedTitle { get; set; } = "";
    public string Origin { get; set; } = "Manual";
    public string EvergreenAssessment { get; set; } = "";
    public string Status { get; set; } = "Proposed";
    public int Priority { get; set; }
    public Guid? ProductionId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ProductionWorkspace
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public string Stage { get; set; } = "Research";
    public string EditorialSnapshot { get; set; } = "{}";
    public string Objective { get; set; } = "";
    public string VideoInstructions { get; set; } = "";
    public string Language { get; set; } = "es";
    public string Audience { get; set; } = "";
    public string NotebookCreationMethod { get; set; } = "Unverified";
    public DateTime? NotebookVerifiedAtUtc { get; set; }
    public Guid? CanonicalArtifactId { get; set; }
    public string YouTubeTitle { get; set; } = "";
    public string YouTubeDescription { get; set; } = "";
    public string Tags { get; set; } = "";
    public string CategoryId { get; set; } = "27";
    public bool? MadeForKids { get; set; }
    public bool ContainsSyntheticMedia { get; set; } = true;
    public string Visibility { get; set; } = "private";
    public DateTime? PublishAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string ApprovalEvidence { get; set; } = "";
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ResearchSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "Url";
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Classification { get; set; } = "Primary";
    public string Provenance { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public bool Included { get; set; } = true;
    public Guid? ArtifactId { get; set; }
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SourcePackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public int Version { get; set; }
    public string Markdown { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ManualNotebookTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public string Kind { get; set; } = "CreateNotebook";
    public string Instructions { get; set; } = "";
    public string ExpectedResult { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string ResponsibleOperator { get; set; } = "LocalOperator";
    public string Evidence { get; set; } = "";
    public string EvidenceKind { get; set; } = "Unverified";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class MediaArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public string Kind { get; set; } = "Video";
    public string OriginalName { get; set; } = "";
    [JsonIgnore] public string StorageKey { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public string MimeType { get; set; } = "application/octet-stream";
    public string Status { get; set; } = "PendingInspection";
    public string Origin { get; set; } = "Unverified";
    public string OriginEvidence { get; set; } = "";
    public string Container { get; set; } = "";
    public string VideoCodec { get; set; } = "";
    public bool HasAudio { get; set; }
    public double DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string ValidationError { get; set; } = "";
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class QualityReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public Guid ArtifactId { get; set; }
    public bool Approved { get; set; }
    public string Evidence { get; set; } = "";
    public string Operator { get; set; } = "LocalOperator";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class StudioEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProductionId { get; set; }
    public string Kind { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BackgroundJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProductionId { get; set; }
    public Guid? ResourceId { get; set; }
    public string Kind { get; set; } = "InspectArtifact";
    public string IdempotencyKey { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public int Attempts { get; set; }
    public DateTime AvailableAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseUntilUtc { get; set; }
    public Guid? LeaseOwner { get; set; }
    public string LastError { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ScheduleSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public Guid ContentNicheId { get; set; }
    public DateOnly LocalDate { get; set; }
    public TimeOnly LocalTime { get; set; }
    public string TimeZone { get; set; } = "Etc/UTC";
    public DateTime PublishAtUtc { get; set; }
    public Guid? ProductionId { get; set; }
    public string Status { get; set; } = "Unassigned";
}

public sealed class GoogleConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "YouTube";
    [JsonIgnore] public string ProtectedTokens { get; set; } = "";
    public string ExternalChannelId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "Connected";
    public DateTime? LastVerifiedAtUtc { get; set; }
}

public sealed class OAuthAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StateHash { get; set; } = "";
    public string Kind { get; set; } = "YouTube";
    public string ProtectedVerifier { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public bool Used { get; set; }
}

public sealed class YouTubeUpload
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public Guid ArtifactId { get; set; }
    public string Status { get; set; } = "Queued";
    [JsonIgnore] public string ProtectedSessionUrl { get; set; } = "";
    public string VideoId { get; set; } = "";
    public string MetadataSnapshot { get; set; } = "{}";
    public long BytesConfirmed { get; set; }
    public string LastError { get; set; } = "";
    public DateTime? LastVerifiedAtUtc { get; set; }
    public string LastObservedPrivacyStatus { get; set; } = "";
    public string LastObservedTitle { get; set; } = "";
    public DateTime? LastRemoteMetadataObservedAtUtc { get; set; }
}

public sealed class CreationReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public Guid ProductionId { get; set; }
}
