namespace NotebookYt.Api.Studio;

/// <summary>A reusable expert is a real NotebookLM notebook binding, not a prompt-only persona.</summary>
public sealed class NotebookExpert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Specialization { get; set; } = "";
    public string Description { get; set; } = "";
    public string NotebookId { get; set; } = "";
    public string NotebookUrl { get; set; } = "";
    public string Instructions { get; set; } = "";
    public string NicheIdsJson { get; set; } = "[]";
    public string StageKeysJson { get; set; } = "[]";
    public bool Enabled { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTime? LastSyncedAtUtc { get; set; }
    public string LastErrorCode { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Durable context transfer and human approval evidence for one expert stage.</summary>
public sealed class ExpertWorkflowRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionId { get; set; }
    public Guid? WorkflowId { get; set; }
    public string WorkflowRequestHash { get; set; } = "";
    public Guid ExpertId { get; set; }
    public string StageKey { get; set; } = "";
    public int Sequence { get; set; }
    public string Status { get; set; } = "Pending";
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int ContextVersion { get; set; } = 1;
    public string Prompt { get; set; } = "";
    public string SourceIdsJson { get; set; } = "[]";
    public string InputContextJson { get; set; } = "{}";
    public string Response { get; set; } = "";
    public string ReviewedResponse { get; set; } = "";
    public string ReferencesJson { get; set; } = "[]";
    public string ConversationId { get; set; } = "";
    public string ApprovalEvidence { get; set; } = "";
    public DateTime? RequestedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string LastErrorCode { get; set; } = "";
}

/// <summary>External operation ledger; ambiguous provider outcomes are never retried blindly.</summary>
public sealed class NotebookLmOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProductionId { get; set; }
    public string Kind { get; set; } = "";
    public string NotebookId { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string ProviderTaskId { get; set; } = "";
    public string ProviderArtifactId { get; set; } = "";
    public Guid? MediaArtifactId { get; set; }
    public string ResultJson { get; set; } = "{}";
    public string RequestJson { get; set; } = "{}";
    public string ErrorCode { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Last observed compute meter response; null fields remain unknown rather than becoming zero.</summary>
public sealed class NotebookLmUsageSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileName { get; set; } = "operator";
    public string Status { get; set; } = "unknown";
    public bool? IsExhausted { get; set; }
    public string ActiveWindow { get; set; } = "";
    public DateTime? ResetsAtUtc { get; set; }
    public bool? VideoAvailable { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
}
