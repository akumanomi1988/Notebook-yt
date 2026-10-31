using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public sealed class NotebookLmExpertOrchestrator(EditorialDb db, INotebookLmProvider provider)
{
    public async Task Execute(BackgroundJob job, CancellationToken cancellationToken)
    {
        var run = await db.Set<ExpertWorkflowRun>().FindAsync([job.ResourceId], cancellationToken)
            ?? throw new StudioException(404, "Etapa de experto inexistente.");
        if (run.Status is "WaitingForApproval" or "WaitingForExecution" or "AwaitingReview" or "Approved" or "Rejected" or "OutcomeUnknown" or "Blocked") return;
        var reconcilingAmbiguousCopy = run.LastErrorCode == "NOTEBOOK_COPY_OUTCOME_UNKNOWN";
        var expert = await db.Set<NotebookExpert>().FindAsync([run.ExpertId], cancellationToken)
            ?? throw new StudioException(404, "Experto inexistente.");
        if (!expert.Enabled) { run.Status = "Failed"; run.LastErrorCode = "EXPERT_DISABLED"; return; }

        run.Status = "Running"; run.LastErrorCode = ""; await db.SaveChangesAsync(cancellationToken);
        var auth = await provider.CheckAuthenticationAsync(cancellationToken);
        if (!auth.Success || !AuthReady(auth.Json))
        {
            run.Status = "Failed"; run.LastErrorCode = auth.Code == "OK" ? "AUTH_REQUIRED" : auth.Code;
            return;
        }
        var notebook = await provider.GetNotebookAsync(expert.NotebookId, cancellationToken);
        if (!notebook.Success || !ContainsId(notebook.Json, expert.NotebookId))
        {
            run.Status = "Failed"; run.LastErrorCode = notebook.Success ? "NOTEBOOK_ID_MISMATCH" : notebook.Code; return;
        }
        var sources = JsonSerializer.Deserialize<string[]>(run.SourceIdsJson) ?? [];
        if (sources.Length == 0) { run.Status = "Failed"; run.LastErrorCode = "SOURCE_SELECTION_REQUIRED"; return; }
        var sourceResult = await provider.ListSourcesAsync(expert.NotebookId, cancellationToken);
        if (!sourceResult.Success)
        {
            run.Status = "Failed"; run.LastErrorCode = sourceResult.Code; return;
        }
        var available = ReadIds(sourceResult.Json);
        if (sources.Any(id => !available.Contains(id, StringComparer.Ordinal)))
        {
            run.Status = "Failed"; run.LastErrorCode = "SOURCE_NOT_IN_NOTEBOOK"; return;
        }

        var executionNotebookId = run.ExecutionNotebookId;
        if (string.IsNullOrWhiteSpace(executionNotebookId) && run.WorkflowId is Guid workflowScope)
        {
            executionNotebookId = await db.Set<ExpertWorkflowRun>().AsNoTracking()
                .Where(x => x.WorkflowId == workflowScope && x.ExpertId == expert.Id && x.Sequence < run.Sequence && x.ExecutionNotebookId != "")
                .OrderByDescending(x => x.Sequence).Select(x => x.ExecutionNotebookId).FirstOrDefaultAsync(cancellationToken) ?? "";
        }
        if (string.IsNullOrWhiteSpace(executionNotebookId))
        {
            var scopeToken = run.WorkflowId?.ToString("N") ?? run.Id.ToString("N");
            var scopeTitle = "NotebookYt isolated " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{run.ProductionId:N}:{scopeToken}:{expert.Id:N}"))).ToLowerInvariant()[..24];
            var notebooks = await provider.ListNotebooksAsync(cancellationToken);
            if (!notebooks.Success)
            {
                run.Status = "Failed"; run.LastErrorCode = notebooks.Code; return;
            }
            var matchingCopies = FindNotebooksByExactTitle(notebooks.Json, scopeTitle);
            if (matchingCopies.Count > 1)
            {
                run.Status = "OutcomeUnknown"; run.LastErrorCode = "DUPLICATE_ISOLATED_NOTEBOOKS"; job.Status = "NeedsAttention";
                return;
            }
            if (matchingCopies.Count == 1) executionNotebookId = matchingCopies[0];
            else
            {
                var existingArtifacts = await provider.ListArtifactsAsync(expert.NotebookId, "all", cancellationToken);
                if (!existingArtifacts.Success)
                {
                    run.Status = "Failed"; run.LastErrorCode = existingArtifacts.Code; return;
                }
                if (HasArtifacts(existingArtifacts.Json))
                {
                    run.Status = "Failed"; run.LastErrorCode = "EXPERT_NOTEBOOK_ARTIFACTS_PREVENT_ISOLATION_COPY"; return;
                }
                if (reconcilingAmbiguousCopy)
                {
                    run.Status = "OutcomeUnknown"; run.LastErrorCode = "NOTEBOOK_COPY_RECONCILIATION_REQUIRED"; job.Status = "NeedsAttention";
                    return;
                }
                var copy = await provider.CopyNotebookAsync(expert.NotebookId, scopeTitle, cancellationToken);
                if (!copy.Success)
                {
                    run.Status = copy.OutcomeUnknown ? "OutcomeUnknown" : "Failed";
                    run.LastErrorCode = copy.OutcomeUnknown ? "NOTEBOOK_COPY_OUTCOME_UNKNOWN" : copy.Code;
                    if (copy.OutcomeUnknown) job.Status = "NeedsAttention";
                    return;
                }
                executionNotebookId = FindNotebookId(copy.Json, scopeTitle) ?? "";
                if (string.IsNullOrWhiteSpace(executionNotebookId))
                {
                    run.Status = "OutcomeUnknown"; run.LastErrorCode = "NOTEBOOK_COPY_RESPONSE_UNRECOGNIZED"; job.Status = "NeedsAttention";
                    return;
                }
            }
        }
        var executionSources = await provider.ListSourcesAsync(executionNotebookId, cancellationToken);
        if (!executionSources.Success)
        {
            run.Status = "Failed"; run.LastErrorCode = executionSources.Code; return;
        }
        var executionSourceIds = ReadIds(executionSources.Json);
        if (sources.Any(id => !executionSourceIds.Contains(id, StringComparer.Ordinal)))
        {
            run.Status = "Failed"; run.LastErrorCode = "COPIED_NOTEBOOK_SOURCE_IDS_CHANGED"; return;
        }
        run.ExecutionNotebookId = executionNotebookId;
        await db.SaveChangesAsync(cancellationToken);

        var approvedQuery = db.Set<ExpertWorkflowRun>().AsNoTracking()
            .Where(x => x.ProductionId == run.ProductionId && x.Sequence < run.Sequence && x.Status == "Approved");
        if (run.WorkflowId is Guid workflowId) approvedQuery = approvedQuery.Where(x => x.WorkflowId == workflowId);
        var priorApproved = await approvedQuery.OrderBy(x => x.Sequence).Select(x => new
        {
            x.ExpertId,
            x.StageKey,
            response = x.ReviewedResponse == "" ? x.Response : x.ReviewedResponse,
            x.ReferencesJson,
            x.ContextVersion,
            x.ConversationId
        }).ToListAsync(cancellationToken);
        using (var currentContext = JsonDocument.Parse(run.InputContextJson))
        {
            var context = currentContext.RootElement.Clone();
            run.InputContextJson = JsonSerializer.Serialize(new { context, priorApprovedStages = priorApproved });
        }
        var prompt = ComposePrompt(expert, run);
        if (prompt.Length > 20_000)
        {
            run.Status = "Failed"; run.LastErrorCode = "PROMPT_TOO_LARGE"; job.Status = "Succeeded";
            return;
        }
        var conversationToResume = priorApproved.LastOrDefault(x => x.ExpertId == expert.Id)?.ConversationId;
        var response = await provider.AskAsync(executionNotebookId, prompt, sources,
            string.IsNullOrWhiteSpace(conversationToResume) ? null : conversationToResume, cancellationToken);
        if (!response.Success)
        {
            run.LastErrorCode = response.Code;
            run.Status = response.OutcomeUnknown ? "OutcomeUnknown" : "Failed";
            if (response.OutcomeUnknown) job.Status = "NeedsAttention";
            return;
        }
        if (!TryExtractAnswer(response.Json, out var answer, out var references, out var conversationId))
        {
            // The provider operation completed, but its response contract is unknown. Do not ask again.
            run.Status = "OutcomeUnknown"; run.LastErrorCode = "RESPONSE_SHAPE_UNRECOGNIZED"; job.Status = "NeedsAttention"; return;
        }
        run.Response = answer; run.ReferencesJson = references; run.ConversationId = conversationId;
        if (run.WorkflowId is not null && run.WorkflowMode == "Automatic")
        {
            var selectedSources = JsonSerializer.Deserialize<string[]>(run.SourceIdsJson) ?? [];
            if (!ValidateAutomaticEvidence(answer, references, selectedSources, out var validationCode))
            {
                run.Status = "Failed"; run.LastErrorCode = validationCode; run.CompletedAtUtc = DateTime.UtcNow;
                StudioRules.Event(db, run.ProductionId, "NotebookLmExpertAutomaticValidationFailed", $"{run.Id}; {validationCode}");
                job.Status = "Succeeded";
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            run.Status = "Approved";
            run.ReviewedResponse = answer;
            run.ApprovalEvidence = "Automatic structural validation passed: non-empty NotebookLM answer, citations present, and every cited source belongs to the selected source set.";
            run.ApprovedAtUtc = DateTime.UtcNow;
            if (run.WorkflowId is Guid automaticWorkflowId)
            {
                var next = await db.Set<ExpertWorkflowRun>().Where(x => x.WorkflowId == automaticWorkflowId && x.Sequence > run.Sequence && x.Status == "WaitingForExecution")
                    .OrderBy(x => x.Sequence).FirstOrDefaultAsync(cancellationToken);
                if (next is not null)
                {
                    next.Status = "Pending"; next.LastErrorCode = "";
                    await StudioRules.Enqueue(db, "RunNotebookLmExpertStage", next.ProductionId, next.Id, $"expert-workflow:{automaticWorkflowId}:{next.Sequence}");
                }
            }
            StudioRules.Event(db, run.ProductionId, "NotebookLmExpertAutomaticStageValidated", $"{run.Id}; {run.StageKey}; {run.ApprovalEvidence}");
        }
        else
        {
            run.Status = "AwaitingReview";
            StudioRules.Event(db, run.ProductionId, "NotebookLmExpertStageAwaitingReview", $"{run.Id}; {run.StageKey}");
        }
        run.CompletedAtUtc = DateTime.UtcNow;
        expert.LastSyncedAtUtc = DateTime.UtcNow; expert.LastErrorCode = ""; expert.ModifiedAtUtc = DateTime.UtcNow;
        StudioRules.Event(db, run.ProductionId, "NotebookLmExpertResponseReceived", $"{expert.Id}; {run.StageKey}; {run.Id}");
        await db.SaveChangesAsync(cancellationToken);
    }

    private static List<string> FindNotebooksByExactTitle(string? json, string title)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            var results = new List<string>();
            Visit(doc.RootElement, results);
            return results;
            void Visit(JsonElement element, List<string> found)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    var hasTitle = element.TryGetProperty("title", out var titleElement) || element.TryGetProperty("name", out titleElement);
                    if (hasTitle && titleElement.ValueKind == JsonValueKind.String && titleElement.GetString() == title &&
                        element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is string notebookId)
                        found.Add(notebookId);
                    foreach (var property in element.EnumerateObject())
                        if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) Visit(property.Value, found);
                }
                else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Visit(child, found);
            }
        }
        catch (JsonException) { return []; }
    }

    private static string? FindNotebookId(string? json, string expectedTitle)
    {
        var matches = FindNotebooksByExactTitle(json, expectedTitle);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool HasArtifacts(string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? "{}");
            return HasItems(document.RootElement);
        }
        catch (JsonException) { return true; }

        static bool HasItems(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array) return element.GetArrayLength() > 0;
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name is "artifacts" or "items")
                        return property.Value.ValueKind == JsonValueKind.Array ? property.Value.GetArrayLength() > 0 : HasItems(property.Value);
                    if (property.Name == "data" && property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) return HasItems(property.Value);
                }
                return true;
            }
            return true;
        }
    }

    private static bool ValidateAutomaticEvidence(string answer, string referencesJson, IReadOnlyCollection<string> selectedSources, out string errorCode)
    {
        errorCode = "";
        if (string.IsNullOrWhiteSpace(answer)) { errorCode = "AUTOMATIC_EMPTY_ANSWER"; return false; }
        JsonDocument document;
        try { document = JsonDocument.Parse(referencesJson); }
        catch (JsonException) { errorCode = "AUTOMATIC_INVALID_REFERENCES"; return false; }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            { errorCode = "AUTOMATIC_NO_REFERENCES"; return false; }
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("source_id", out var source) || source.ValueKind != JsonValueKind.String ||
                    !selectedSources.Contains(source.GetString()!, StringComparer.Ordinal))
                { errorCode = "AUTOMATIC_REFERENCE_OUTSIDE_SELECTION"; return false; }
                if (!item.TryGetProperty("citation_number", out var citation) || citation.ValueKind != JsonValueKind.Number)
                { errorCode = "AUTOMATIC_INVALID_CITATION"; return false; }
            }
        }
        return true;
    }

    private static string ComposePrompt(NotebookExpert expert, ExpertWorkflowRun run)
    {
        using var context = JsonDocument.Parse(run.InputContextJson);
        return $"{expert.Instructions}\n\nProducción y contexto previamente aprobado:\n{context.RootElement.GetRawText()}\n\nConsulta de esta etapa:\n{run.Prompt}\n\nTrata esta solicitud como independiente de otras producciones y no reutilices sus conclusiones. Responde usando únicamente las fuentes seleccionadas del notebook y el contexto explícito de esta producción. Separa hechos, inferencias y dudas; conserva citas de NotebookLM y no inventes información. Devuelve la respuesta y las referencias en el formato JSON documentado por NotebookLM CLI.";
    }

    private static bool AuthReady(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}"); var root = doc.RootElement;
            return root.TryGetProperty("status", out var status) && status.GetString() == "ok" &&
                root.TryGetProperty("checks", out var checks) && checks.TryGetProperty("token_fetch", out var token) && token.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    private static bool ContainsId(string? json, string expected)
    {
        try { using var doc = JsonDocument.Parse(json ?? "{}"); return ReadIds(doc.RootElement).Contains(expected, StringComparer.Ordinal); }
        catch (JsonException) { return false; }
    }
    private static string[] ReadIds(string? json)
    {
        try { using var doc = JsonDocument.Parse(json ?? "{}"); return ReadIds(doc.RootElement).Distinct(StringComparer.Ordinal).ToArray(); }
        catch (JsonException) { return []; }
    }
    private static List<string> ReadIds(JsonElement element)
    {
        var found = new List<string>();
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) found.Add(id.GetString()!);
            foreach (var item in element.EnumerateObject()) if (item.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) found.AddRange(ReadIds(item.Value));
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) found.AddRange(ReadIds(item));
        return found;
    }

    private static bool TryExtractAnswer(string? json, out string answer, out string references, out string conversationId)
    {
        answer = ""; references = "[]"; conversationId = "";
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}"); var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!FindString(root, ["answer", "response", "text"], out answer)) return false;
            if (FindElement(root, ["references", "citations", "sources"], out var refs)) references = refs.GetRawText();
            _ = FindString(root, ["conversation_id", "conversationId"], out conversationId);
            return !string.IsNullOrWhiteSpace(answer);
        }
        catch (JsonException) { return false; }
    }
    private static bool FindString(JsonElement element, string[] names, out string value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names) if (element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String) { value = p.GetString()!; return true; }
            foreach (var p in element.EnumerateObject()) if ((p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) && FindString(p.Value, names, out value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var p in element.EnumerateArray()) if (FindString(p, names, out value)) return true;
        value = ""; return false;
    }
    private static bool FindElement(JsonElement element, string[] names, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names) if (element.TryGetProperty(name, out value)) return true;
            foreach (var p in element.EnumerateObject()) if ((p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) && FindElement(p.Value, names, out value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var p in element.EnumerateArray()) if (FindElement(p, names, out value)) return true;
        value = default; return false;
    }
}
