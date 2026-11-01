using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public static class NotebookLmEndpoints
{
    private static readonly Regex SafeId = new("^[A-Za-z0-9_-]{1,160}$", RegexOptions.Compiled);
    private static readonly string[] Stages = ["research", "strategy", "script", "production", "seo", "thumbnail", "editorial_review"];

    public static void MapNotebookLm(this WebApplication app)
    {
        app.MapGet("/api/studio/notebooklm/diagnostics", async (INotebookLmProvider provider, CancellationToken ct) =>
        {
            var profiles = await provider.ListProfilesAsync(ct);
            var authentication = await provider.CheckAuthenticationAsync(ct);
            var usage = await provider.GetUsageAsync(ct);
            var authReady = IsAuthenticationReady(authentication.Json);
            return Results.Ok(new
            {
                cliVersion = "0.8.6",
                distribution = "Community CLI; unofficial NotebookLM integration",
                configured = profiles.Code != "CLI_NOT_CONFIGURED",
                profiles = SafeJson(profiles),
                authentication = SafeJson(authentication),
                usage = authReady ? SafeJson(usage) : null,
                usageState = authReady ? (usage.Success ? "available" : usage.Code) : "authentication_required",
                providerVerified = authentication.Success && authReady
            });
        });

        app.MapGet("/api/studio/notebooklm/notebooks", async (INotebookLmProvider provider, CancellationToken ct) =>
        {
            var auth = await provider.CheckAuthenticationAsync(ct);
            if (!auth.Success || !IsAuthenticationReady(auth.Json)) throw new StudioException(409, "NotebookLM CLI no tiene una sesión válida; consulta Diagnóstico NotebookLM.");
            var result = await provider.ListNotebooksAsync(ct);
            return ProviderResponse(result);
        });
        app.MapGet("/api/studio/notebooklm/notebooks/{notebookId}/metadata", async (string notebookId, INotebookLmProvider provider, CancellationToken ct) =>
            ProviderResponse(await provider.GetNotebookAsync(ValidateId(notebookId), ct)));
        app.MapGet("/api/studio/notebooklm/notebooks/{notebookId}/sources", async (string notebookId, INotebookLmProvider provider, CancellationToken ct) =>
            ProviderResponse(await provider.ListSourcesAsync(ValidateId(notebookId), ct)));
        app.MapGet("/api/studio/notebooklm/notebooks/{notebookId}/artifacts", async (string notebookId, string? type, INotebookLmProvider provider, CancellationToken ct) =>
            ProviderResponse(await provider.ListArtifactsAsync(ValidateId(notebookId), type ?? "all", ct)));
        app.MapGet("/api/studio/notebooklm/notebooks/{notebookId}/research", async (string notebookId, string? taskId, INotebookLmProvider provider, CancellationToken ct) =>
            ProviderResponse(await provider.ResearchStatusAsync(ValidateId(notebookId), string.IsNullOrWhiteSpace(taskId) ? null : ValidateId(taskId), ct)));

        app.MapGet("/api/studio/notebooklm/expert-templates", () => Results.Ok(ExpertTemplates));
        app.MapGet("/api/studio/notebooklm/experts", async (EditorialDb db, CancellationToken ct) =>
            Results.Ok(await db.Set<NotebookExpert>().AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)));
        app.MapPost("/api/studio/notebooklm/experts", async (ExpertInput input, INotebookLmProvider provider, EditorialDb db, CancellationToken ct) =>
        {
            ValidateExpert(input);
            var notebook = await provider.GetNotebookAsync(input.NotebookId, ct);
            if (!notebook.Success) throw new StudioException(notebook.Code == "AUTH_REQUIRED" ? 409 : 502, "No se pudo verificar el cuaderno de NotebookLM. Consulta el diagnóstico antes de registrar el experto.");
            if (!NotebookMatches(notebook.Json, input.NotebookId)) throw new StudioException(409, "NotebookLM no confirmó el identificador del cuaderno.");
            var existing = await db.Set<NotebookExpert>().SingleOrDefaultAsync(
                x => x.NotebookId == input.NotebookId && x.Specialization == input.Specialization, ct);
            if (existing is not null)
            {
                existing.Name = input.Name.Trim(); existing.Description = input.Description.Trim();
                existing.NotebookUrl = NotebookUrl(input.NotebookId, input.NotebookUrl);
                existing.Instructions = input.Instructions.Trim(); existing.NicheIdsJson = JsonSerializer.Serialize(input.NicheIds);
                existing.StageKeysJson = JsonSerializer.Serialize(input.StageKeys); existing.Enabled = input.Enabled;
                existing.Version++; existing.LastSyncedAtUtc = DateTime.UtcNow; existing.LastErrorCode = "";
                existing.ModifiedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Ok(existing);
            }
            var row = new NotebookExpert
            {
                Name = input.Name.Trim(), Specialization = input.Specialization.Trim(), Description = input.Description.Trim(),
                NotebookId = input.NotebookId, NotebookUrl = NotebookUrl(input.NotebookId, input.NotebookUrl),
                Instructions = input.Instructions.Trim(), NicheIdsJson = JsonSerializer.Serialize(input.NicheIds),
                StageKeysJson = JsonSerializer.Serialize(input.StageKeys), Enabled = input.Enabled, LastSyncedAtUtc = DateTime.UtcNow
            };
            db.Add(row); await db.SaveChangesAsync(ct); return Results.Created($"/api/studio/notebooklm/experts/{row.Id}", row);
        });
        app.MapPut("/api/studio/notebooklm/experts/{id:guid}", async (Guid id, ExpertUpdate input, EditorialDb db, CancellationToken ct) =>
        {
            var row = await db.Set<NotebookExpert>().FindAsync([id], ct) ?? throw new StudioException(404, "Experto inexistente.");
            if (input.Version != row.Version) throw new StudioException(409, "La configuración del experto cambió; vuelve a cargarla.");
            ValidateExpert(input);
            row.Name = input.Name.Trim(); row.Specialization = input.Specialization.Trim(); row.Description = input.Description.Trim();
            row.Instructions = input.Instructions.Trim(); row.NicheIdsJson = JsonSerializer.Serialize(input.NicheIds);
            row.StageKeysJson = JsonSerializer.Serialize(input.StageKeys); row.Enabled = input.Enabled; row.Version++;
            row.ModifiedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); return Results.Ok(row);
        });

        app.MapGet("/api/studio/productions/{productionId:guid}/expert-runs", async (Guid productionId, EditorialDb db, CancellationToken ct) =>
        {
            if (!await db.Productions.AnyAsync(x => x.Id == productionId, ct)) throw new StudioException(404, "Producción inexistente.");
            return Results.Ok(await db.Set<ExpertWorkflowRun>().AsNoTracking().Where(x => x.ProductionId == productionId)
                .OrderBy(x => x.Sequence).ThenBy(x => x.RequestedAtUtc).ToListAsync(ct));
        });
        app.MapPost("/api/studio/productions/{productionId:guid}/expert-runs", async (Guid productionId, ExpertRunInput input,
            HttpRequest request, EditorialDb db, CancellationToken ct) =>
        {
            var production = await db.Productions.FindAsync([productionId], ct) ?? throw new StudioException(404, "Producción inexistente.");
            if (production.Status == ProductionStatus.Cancelled) throw new StudioException(409, "Una producción cancelada no admite operaciones externas.");
            var expert = await db.Set<NotebookExpert>().FindAsync([input.ExpertId], ct) ?? throw new StudioException(404, "Experto inexistente.");
            if (!expert.Enabled || !SafeId.IsMatch(expert.NotebookId)) throw new StudioException(409, "Activa y verifica un experto vinculado a un cuaderno real.");
            var key = request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new StudioException(400, "Se requiere una cabecera Idempotency-Key válida.");
            if (input.Sequence < 1 || input.Sequence > 50 || !Stages.Contains(input.StageKey) || string.IsNullOrWhiteSpace(input.Prompt) || input.Prompt.Length > 12000)
                throw new StudioException(400, "Etapa, orden y consulta del experto no válidos.");
            var expertStages = JsonSerializer.Deserialize<string[]>(expert.StageKeysJson) ?? [];
            if (!expertStages.Contains(input.StageKey, StringComparer.Ordinal)) throw new StudioException(409, "La etapa solicitada no forma parte de la configuración de este experto.");
            var hash = Hash(JsonSerializer.Serialize(new { productionId, input }));
            var existing = await db.Set<ExpertWorkflowRun>().SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                if (existing.RequestHash != hash) throw new StudioException(409, "La clave idempotente ya se utilizó con otra consulta.");
                return Results.Ok(existing);
            }
            if (await db.Set<ExpertWorkflowRun>().AnyAsync(x => x.ProductionId == productionId && x.Sequence == input.Sequence && x.Status != "Rejected", ct))
                throw new StudioException(409, "Ya existe una etapa en esa posición. Revisa o reordena el flujo.");
            var approved = await db.Set<ExpertWorkflowRun>().AsNoTracking().Where(x => x.ProductionId == productionId && x.Sequence < input.Sequence && x.Status == "Approved")
                .OrderBy(x => x.Sequence).Select(x => new { x.StageKey, Response = x.ReviewedResponse == "" ? x.Response : x.ReviewedResponse, x.ReferencesJson, x.ContextVersion }).ToListAsync(ct);
            var sources = input.SourceIds.Distinct(StringComparer.Ordinal).ToArray();
            if (sources.Any(x => !SafeId.IsMatch(x))) throw new StudioException(400, "Las fuentes deben usar identificadores de NotebookLM.");
            var run = new ExpertWorkflowRun
            {
                ProductionId = productionId, ExpertId = expert.Id, StageKey = input.StageKey, Sequence = input.Sequence,
                IdempotencyKey = key, RequestHash = hash, Prompt = input.Prompt.Trim(), SourceIdsJson = JsonSerializer.Serialize(sources),
                InputContextJson = JsonSerializer.Serialize(new { productionId, production.Topic, expert.Name, expert.Specialization, priorApprovedStages = approved }),
                Status = "Pending", RequestedAtUtc = DateTime.UtcNow
            };
            db.Add(run); await StudioRules.Enqueue(db, "RunNotebookLmExpertStage", productionId, run.Id, "expert-stage:" + key);
            StudioRules.Event(db, productionId, "NotebookLmExpertStageQueued", $"{run.Id}; {expert.Specialization}; {run.StageKey}");
            await db.SaveChangesAsync(ct); return Results.Accepted($"/api/studio/productions/{productionId}/expert-runs", run);
        });
        app.MapPost("/api/studio/productions/{productionId:guid}/expert-workflows", async (Guid productionId, ExpertWorkflowInput input,
            HttpRequest request, EditorialDb db, CancellationToken ct) =>
        {
            var production = await db.Productions.FindAsync([productionId], ct) ?? throw new StudioException(404, "Producción inexistente.");
            if (production.Status == ProductionStatus.Cancelled) throw new StudioException(409, "Una producción cancelada no admite operaciones externas.");
            if (input.Steps is null || input.Steps.Count is < 1 or > 12) throw new StudioException(400, "El flujo debe contener entre 1 y 12 etapas.");
            var mode = string.IsNullOrWhiteSpace(input.Mode) ? "Automatic" : input.Mode.Trim();
            if (mode is not ("Automatic" or "Supervised")) throw new StudioException(400, "El modo debe ser Automatic o Supervised.");
            var key = request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new StudioException(400, "Se requiere una cabecera Idempotency-Key válida.");
            var workflowId = WorkflowGuid(productionId, key);
            var hash = Hash(JsonSerializer.Serialize(input));
            var existing = await db.Set<ExpertWorkflowRun>().Where(x => x.WorkflowId == workflowId).OrderBy(x => x.Sequence).ToListAsync(ct);
            if (existing.Count > 0)
            {
                if (existing.Count != input.Steps.Count || existing.Any(x => x.WorkflowRequestHash != hash))
                    throw new StudioException(409, "La clave idempotente de este flujo ya se utilizó con otros datos.");
                return Results.Ok(existing);
            }
            var nextSequence = (await db.Set<ExpertWorkflowRun>().Where(x => x.ProductionId == productionId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1;
            var runs = new List<ExpertWorkflowRun>(input.Steps.Count);
            for (var index = 0; index < input.Steps.Count; index++)
            {
                var step = input.Steps[index];
                if (step.ExpertId == Guid.Empty || !Stages.Contains(step.StageKey) || string.IsNullOrWhiteSpace(step.Prompt) || step.Prompt.Length > 12000 ||
                    step.SourceIds is null || step.SourceIds.Count == 0 || step.SourceIds.Any(x => !SafeId.IsMatch(x)))
                    throw new StudioException(400, $"La etapa {index + 1} tiene campos incompletos o no válidos.");
                var expert = await db.Set<NotebookExpert>().FindAsync([step.ExpertId], ct) ?? throw new StudioException(404, $"No existe el experto de la etapa {index + 1}.");
                var stages = JsonSerializer.Deserialize<string[]>(expert.StageKeysJson) ?? [];
                if (!expert.Enabled || !stages.Contains(step.StageKey, StringComparer.Ordinal))
                    throw new StudioException(409, $"El experto de la etapa {index + 1} está desactivado o no admite esa etapa.");
                runs.Add(new ExpertWorkflowRun
                {
                    ProductionId = productionId, WorkflowId = workflowId, WorkflowRequestHash = hash,
                    ExpertId = expert.Id, StageKey = step.StageKey, Sequence = nextSequence + index,
                    IdempotencyKey = $"{workflowId:N}:{index + 1}", RequestHash = Hash(JsonSerializer.Serialize(step)),
                    Prompt = step.Prompt.Trim(), SourceIdsJson = JsonSerializer.Serialize(step.SourceIds.Distinct(StringComparer.Ordinal).ToArray()),
                    InputContextJson = JsonSerializer.Serialize(new { productionId, production.Topic, workflowId, step = index + 1, totalSteps = input.Steps.Count }),
                    WorkflowMode = mode,
                    Status = index == 0 ? "Pending" : mode == "Automatic" ? "WaitingForExecution" : "WaitingForApproval",
                    RequestedAtUtc = DateTime.UtcNow
                });
            }
            if (await db.Set<ExpertWorkflowRun>().AnyAsync(x => x.ProductionId == productionId && runs.Select(r => r.Sequence).Contains(x.Sequence) && x.Status != "Rejected", ct))
                throw new StudioException(409, "Una etapa ya ocupa una de las posiciones solicitadas.");
            db.AddRange(runs);
            await StudioRules.Enqueue(db, "RunNotebookLmExpertStage", productionId, runs[0].Id, $"expert-workflow:{workflowId}:{runs[0].Sequence}");
            StudioRules.Event(db, productionId, "NotebookLmExpertWorkflowQueued", $"{workflowId}; {runs.Count} etapas");
            await db.SaveChangesAsync(ct);
            return Results.Accepted($"/api/studio/productions/{productionId}/expert-runs", runs);
        });
        app.MapPost("/api/studio/productions/{productionId:guid}/expert-runs/{runId:guid}/review", async (Guid productionId, Guid runId,
            ExpertReviewInput input, EditorialDb db, CancellationToken ct) =>
        {
            var run = await db.Set<ExpertWorkflowRun>().SingleOrDefaultAsync(x => x.Id == runId && x.ProductionId == productionId, ct)
                ?? throw new StudioException(404, "Resultado de experto inexistente.");
            if (run.Status != "AwaitingReview") throw new StudioException(409, "Solo se pueden revisar respuestas recibidas.");
            if (string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000) throw new StudioException(400, "Registra una evidencia breve de revisión.");
            if (input.CorrectedResponse is { Length: > 20000 }) throw new StudioException(400, "La respuesta corregida es demasiado larga.");
            run.ApprovalEvidence = input.Evidence.Trim();
            run.ReviewedResponse = string.IsNullOrWhiteSpace(input.CorrectedResponse) ? run.Response : input.CorrectedResponse.Trim();
            run.Status = input.Approved ? "Approved" : "Rejected"; run.ApprovedAtUtc = input.Approved ? DateTime.UtcNow : null;
            StudioRules.Event(db, productionId, input.Approved ? "NotebookLmExpertResultApproved" : "NotebookLmExpertResultRejected", run.Id.ToString());
            if (run.WorkflowId is Guid workflowId)
            {
                var following = await db.Set<ExpertWorkflowRun>().Where(x => x.WorkflowId == workflowId && x.Sequence > run.Sequence)
                    .OrderBy(x => x.Sequence).ToListAsync(ct);
                if (!input.Approved)
                {
                    foreach (var pending in following.Where(x => x.Status is "WaitingForApproval" or "WaitingForExecution")) { pending.Status = "Blocked"; pending.LastErrorCode = "PRIOR_STAGE_REJECTED"; }
                }
                else if (run.WorkflowMode == "Supervised")
                {
                    var next = following.FirstOrDefault(x => x.Status == "WaitingForApproval");
                    if (next is not null)
                    {
                        next.Status = "Pending"; next.LastErrorCode = "";
                        await StudioRules.Enqueue(db, "RunNotebookLmExpertStage", productionId, next.Id, $"expert-workflow:{workflowId}:{next.Sequence}");
                    }
                }
            }
            await db.SaveChangesAsync(ct); return Results.Ok(run);
        });
    }

    private static IResult ProviderResponse(NotebookLmCliResult result) =>
        result.Success ? Results.Content(result.Json!, "application/json") : throw new StudioException(
            result.Code is "AUTH_REQUIRED" or "CLI_NOT_CONFIGURED" or "CLI_VERSION_MISMATCH" or "COOKIE_READ_FAILED" ? 409 : 502,
            $"NotebookLM CLI: {result.Code}.");

    private static object SafeJson(NotebookLmCliResult result) => new { result.Success, result.Code, result.ExitCode, result.OutcomeUnknown, data = result.Success ? result.Json : null };
    private static bool IsAuthenticationReady(string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? "{}"); var root = document.RootElement;
            return root.TryGetProperty("status", out var status) && status.GetString() == "ok" &&
                root.TryGetProperty("checks", out var checks) && checks.TryGetProperty("token_fetch", out var token) && token.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }
    private static bool NotebookMatches(string? json, string expected)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "{}");
            return FindIds(doc.RootElement).Contains(expected, StringComparer.Ordinal);
        }
        catch (JsonException) { return false; }
        static IEnumerable<string> FindIds(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) yield return id.GetString()!;
                foreach (var item in element.EnumerateObject())
                    if (item.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        foreach (var found in FindIds(item.Value)) yield return found;
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) foreach (var found in FindIds(item)) yield return found;
        }
    }
    private static string ValidateId(string value) => SafeId.IsMatch(value) ? value : throw new StudioException(400, "Identificador de NotebookLM no válido.");
    private static string NotebookUrl(string id, string? supplied)
    {
        if (NotebookLmBindings.MatchesNotebook(supplied, id)) return supplied!;
        throw new StudioException(400, "Proporciona la URL oficial real del cuaderno; no se generarán enlaces a partir del identificador.");
    }
    private static void ValidateExpert(ExpertInput input) => ValidateExpert((ExpertUpdate)input);
    private static void ValidateExpert(ExpertUpdate input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160 || string.IsNullOrWhiteSpace(input.Specialization) || input.Specialization.Length > 120 ||
            input.Description.Length > 2000 || input.Instructions.Length > 10000 || input.NicheIds.Count > 7 || input.StageKeys.Count > 20 ||
            input.StageKeys.Any(x => !Stages.Contains(x))) throw new StudioException(400, "Los datos del experto no cumplen los límites o contienen etapas no admitidas.");
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static Guid WorkflowGuid(Guid productionId, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{productionId:N}:{key}"))[..16]);

    private static readonly object[] ExpertTemplates =
    [
        new { key = "investigator", name = "Investigador", specialization = "research", stages = new[] { "research" }, instructions = "Contrasta las afirmaciones con fuentes del cuaderno. Separa hechos, inferencias, contradicciones y lagunas; cita los identificadores de fuente y no inventes datos." },
        new { key = "youtube-strategist", name = "Estratega YouTube", specialization = "strategy", stages = new[] { "strategy" }, instructions = "Define audiencia, pregunta, propuesta de valor y diferenciación con evidencia del cuaderno. Explica los límites de cualquier conclusión." },
        new { key = "scriptwriter", name = "Guionista", specialization = "script", stages = new[] { "script" }, instructions = "Propón una estructura narrativa clara que responda la pregunta central y vincula cada afirmación relevante con fuentes del cuaderno." },
        new { key = "audiovisual-producer", name = "Productor audiovisual", specialization = "production", stages = new[] { "production" }, instructions = "Prepara instrucciones de Video Overview acordes al formato y estilo disponibles; distingue objetivos editoriales de controles que NotebookLM no expone." },
        new { key = "seo-specialist", name = "Especialista SEO", specialization = "seo", stages = new[] { "seo" }, instructions = "Propón metadatos fieles al contenido y a las fuentes. No prometas CTR ni inventes capítulos sin marcas temporales." },
        new { key = "thumbnail-specialist", name = "Especialista en miniaturas", specialization = "thumbnail", stages = new[] { "thumbnail" }, instructions = "Describe conceptos de miniatura, composición y jerarquía; identifica claramente que son propuestas textuales si no se ha generado una imagen." },
        new { key = "editorial-reviewer", name = "Revisor editorial", specialization = "editorial_review", stages = new[] { "editorial_review" }, instructions = "Busca contradicciones, afirmaciones sin respaldo, duplicación y problemas de claridad. Atribuye conclusiones a las fuentes consultadas." }
    ];
}

public record ExpertInput(string Name, string Specialization, string Description, string NotebookId, string? NotebookUrl,
    string Instructions, List<Guid> NicheIds, List<string> StageKeys, bool Enabled) : ExpertUpdate(Name, Specialization, Description, Instructions, NicheIds, StageKeys, Enabled, 1);
public record ExpertUpdate(string Name, string Specialization, string Description, string Instructions,
    List<Guid> NicheIds, List<string> StageKeys, bool Enabled, int Version);
public sealed record ExpertRunInput(Guid ExpertId, string StageKey, int Sequence, string Prompt, List<string> SourceIds);
public sealed record ExpertWorkflowInput(List<ExpertWorkflowStepInput> Steps, string Mode = "Automatic");
public sealed record ExpertWorkflowStepInput(Guid ExpertId, string StageKey, string Prompt, List<string> SourceIds);
public sealed record ExpertReviewInput(bool Approved, string Evidence, string? CorrectedResponse);
