using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public sealed class StudioWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<StudioWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Worker:Enabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Tick(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Studio worker cycle failed: {Type}", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
    public async Task Tick(CancellationToken ct)
    {
        Guid? id = null; var owner = Guid.NewGuid();
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EditorialDb>(); await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74819321)", ct);
            await EditorialScheduler.Maintain(db, DateTime.UtcNow);
            var resumedQuotaOperations = await db.Set<NotebookLmOperation>().Where(x => x.Status == "DeferredQuota" && x.ModifiedAtUtc <= DateTime.UtcNow.AddMinutes(-5))
                .Select(x => x.Id).Take(20).ToListAsync(ct);
            foreach (var operationId in resumedQuotaOperations)
            {
                var external = await db.Set<NotebookLmOperation>().FindAsync([operationId], ct);
                if (external is null || external.Status != "DeferredQuota") continue;
                var queued = await db.Set<BackgroundJob>().SingleOrDefaultAsync(x => x.Kind == "RunNotebookLmOperation" && x.ResourceId == operationId && x.Status == "NeedsAttention", ct);
                if (queued is null) continue;
                external.Status = "Pending"; external.ModifiedAtUtc = DateTime.UtcNow;
                queued.Status = "Queued"; queued.AvailableAtUtc = DateTime.UtcNow; queued.LastError = "Se vuelve a comprobar la cuota efectiva de NotebookLM.";
            }
            var job = await db.Set<BackgroundJob>().Where(j => (j.Status == "Queued" && j.AvailableAtUtc <= DateTime.UtcNow && (j.LeaseUntilUtc == null || j.LeaseUntilUtc < DateTime.UtcNow)) || (j.Status == "Running" && j.LeaseUntilUtc < DateTime.UtcNow)).OrderBy(j => j.AvailableAtUtc).FirstOrDefaultAsync(ct);
            if (job is not null)
            {
                var ambiguousSource = job.Kind == "SyncNotebookSources" && await db.Set<NotebookSourceBinding>().AnyAsync(b => b.ExternalSourceId == "" && db.Set<ResearchSource>().Any(s => s.Id == b.ResearchSourceId && s.ProductionId == job.ProductionId), ct);
                if (job.Status == "Running" && (job.Kind == "CreateNotebook" || ambiguousSource))
                {
                    job.Status = "NeedsAttention"; job.LastError = "Operación externa interrumpida: concilia el resultado antes de repetirla.";
                }
                else
                {
                    job.Status = "Running"; job.LeaseOwner = owner; job.LeaseUntilUtc = DateTime.UtcNow.AddMinutes(5); job.Attempts++; id = job.Id;
                }
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        if (id is null) return;
        await using var execution = scopes.CreateAsyncScope(); var services = execution.ServiceProvider; var context = services.GetRequiredService<EditorialDb>();
        var current = await context.Set<BackgroundJob>().FindAsync([id.Value], ct); if (current is null || current.LeaseOwner != owner) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = RenewLease(id.Value, owner, operation);
        try
        {
            if (current.ProductionId is Guid pId) await StudioRules.EditableJob(context, pId);
            switch (current.Kind)
            {
                case "InspectArtifact":
                    var artifact = await context.Set<MediaArtifact>().FindAsync([current.ResourceId], ct) ?? throw new StudioException(404, "Archivo inexistente.");
                    try { await services.GetRequiredService<MediaStorage>().Inspect(artifact, operation.Token); }
                    catch (StudioException ex) { artifact.Status = "Rejected"; artifact.ValidationError = ex.Message; throw; }
                    break;
                case "UploadYouTube": await services.GetRequiredService<YouTubePublishing>().Process(current, operation.Token); break;
                case "VerifyYouTube": await services.GetRequiredService<YouTubePublishing>().Verify(current, operation.Token); break;
                case "PublishYouTube": await services.GetRequiredService<YouTubePublishing>().Publish(current, operation.Token); break;
                case "CreateNotebook": await services.GetRequiredService<NotebookIntegration>().Create(current, operation.Token); break;
                case "SyncNotebookSources": await services.GetRequiredService<NotebookIntegration>().Sync(current, operation.Token); break;
                case "RunNotebookLmExpertStage": await services.GetRequiredService<NotebookLmExpertOrchestrator>().Execute(current, operation.Token); break;
                case "RunNotebookLmOperation": await services.GetRequiredService<NotebookLmOperationProcessor>().Execute(current, operation.Token); break;
                default: throw new StudioException(400, "Tipo de trabajo desconocido.");
            }
            if (current.Status == "Running") current.Status = "Succeeded";
            current.LastError = "";
        }
        catch (Exception ex)
        {
            var operationLedger = current.Kind == "RunNotebookLmOperation" && current.ResourceId is Guid operationId
                ? await context.Set<NotebookLmOperation>().FindAsync([operationId], CancellationToken.None) : null;
            var ambiguous = current.Kind == "CreateNotebook" || (current.Kind == "SyncNotebookSources" && ex is not StudioException { Status: 503 }) ||
                (operationLedger is not null && operationLedger.Status == "Running");
            if (operationLedger is not null && operationLedger.Status == "Running")
            {
                operationLedger.Status = "OutcomeUnknown"; operationLedger.ErrorCode = "OUTCOME_UNKNOWN"; operationLedger.ModifiedAtUtc = DateTime.UtcNow;
            }
            var retryable = ex is HttpRequestException or TimeoutException or OperationCanceledException || ex is StudioException { Status: 429 or 503 };
            current.LastError = ex is StudioException ? ex.Message : "La operación falló; revisa la conexión y las herramientas configuradas. No se han registrado credenciales en el error.";
            current.Status = ambiguous ? "NeedsAttention" : retryable && current.Attempts < 5 ? "Queued" : "Failed";
            current.AvailableAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(900, Math.Pow(2, current.Attempts) * 10));
            if (current.Kind is "UploadYouTube" or "PublishYouTube" or "VerifyYouTube")
            {
                var upload = await context.Set<YouTubeUpload>().FindAsync([current.ResourceId], CancellationToken.None);
                if (upload is not null) { upload.LastError = current.LastError; if (current.Status == "Failed") upload.Status = "NeedsAttention"; }
            }
            StudioRules.Event(context, current.ProductionId, "JobFailed", $"{current.Id}: {current.LastError}");
        }
        operation.Cancel(); await heartbeat;
        current.LeaseUntilUtc = null; current.LeaseOwner = null;
        await using var completed = await context.Database.BeginTransactionAsync(CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74819321)", CancellationToken.None);
        var ownership = await context.Set<BackgroundJob>().AsNoTracking().Where(j => j.Id == current.Id).Select(j => new { j.LeaseOwner, j.Status }).SingleAsync(CancellationToken.None);
        if (ownership.LeaseOwner != owner || ownership.Status is "Cancelled" or "NeedsAttention") return;
        await context.SaveChangesAsync(CancellationToken.None); await completed.CommitAsync(CancellationToken.None);
    }
    private async Task RenewLease(Guid id, Guid owner, CancellationTokenSource operation)
    {
        try
        {
            while (!operation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), operation.Token);
                await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<EditorialDb>();
                var renewed = await db.Set<BackgroundJob>().Where(j => j.Id == id && j.LeaseOwner == owner && (j.Status == "Running" || j.Status == "Queued"))
                    .ExecuteUpdateAsync(set => set.SetProperty(j => j.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(5)), operation.Token);
                if (renewed != 1) { operation.Cancel(); return; }
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception) { operation.Cancel(); }
    }
}
