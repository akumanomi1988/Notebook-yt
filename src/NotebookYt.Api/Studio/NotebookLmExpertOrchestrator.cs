using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public sealed class NotebookLmExpertOrchestrator(EditorialDb db, INotebookLmProvider provider)
{
    public async Task Execute(BackgroundJob job, CancellationToken cancellationToken)
    {
        var run = await db.Set<ExpertWorkflowRun>().FindAsync([job.ResourceId], cancellationToken)
            ?? throw new StudioException(404, "Etapa de experto inexistente.");
        if (run.Status is "AwaitingReview" or "Approved" or "Rejected" or "OutcomeUnknown") return;
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

        var prompt = ComposePrompt(expert, run);
        var response = await provider.AskAsync(expert.NotebookId, prompt, sources, cancellationToken);
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
        run.Status = "AwaitingReview"; run.CompletedAtUtc = DateTime.UtcNow;
        expert.LastSyncedAtUtc = DateTime.UtcNow; expert.LastErrorCode = ""; expert.ModifiedAtUtc = DateTime.UtcNow;
        StudioRules.Event(db, run.ProductionId, "NotebookLmExpertResponseReceived", $"{expert.Id}; {run.StageKey}; {run.Id}");
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string ComposePrompt(NotebookExpert expert, ExpertWorkflowRun run)
    {
        using var context = JsonDocument.Parse(run.InputContextJson);
        return $"{expert.Instructions}\n\nProducción y contexto previamente aprobado:\n{context.RootElement.GetRawText()}\n\nConsulta de esta etapa:\n{run.Prompt}\n\nResponde usando únicamente las fuentes seleccionadas del notebook. Separa hechos, inferencias y dudas; conserva citas de NotebookLM y no inventes información. Devuelve la respuesta y las referencias en el formato JSON documentado por NotebookLM CLI.";
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
