using Microsoft.EntityFrameworkCore;
using NotebookYt.Api;
using NotebookYt.Api.Studio;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
builder.WebHost.UseUrls(builder.Configuration["Hosting:Urls"] ?? "http://127.0.0.1:5080");
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(
    new System.Text.Json.Serialization.JsonStringEnumConverter<ProductionStatus>(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.AddOperatorSecurity();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024L * 1024 * 1024);
builder.Services.AddSingleton<MediaStorage>();
builder.Services.AddScoped<GoogleAuthorization>();
builder.Services.AddScoped<NotebookIntegration>();
builder.Services.AddScoped<YouTubePublishing>();
builder.Services.AddHttpClient("Google", client => client.Timeout = TimeSpan.FromMinutes(2))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();
builder.Services.AddHostedService<StudioWorker>();
builder.Services.AddDbContext<EditorialDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Editorial")
        ?? throw new InvalidOperationException("ConnectionStrings:Editorial is required.")));
var app = builder.Build();
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    await migrationScope.ServiceProvider.GetRequiredService<EditorialDb>().Database.MigrateAsync();
    return;
}
app.UseExceptionHandler();
app.UseStatusCodePages();
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (StudioException ex) when (!context.Response.HasStarted)
    { context.Response.Clear(); await Results.Problem(statusCode: ex.Status, detail: ex.Message).ExecuteAsync(context); }
});
app.UseOperatorSecurity();
// Local single-operator API: reject cross-origin browser writes, including simple requests.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("Origin", out var origin) &&
        origin != "http://127.0.0.1:5173" && origin != "http://localhost:5173")
    {
        await Results.Problem(statusCode: 403, detail: "Origin is not allowed.").ExecuteAsync(context);
        return;
    }
    if (HttpMethods.IsGet(context.Request.Method)) { await next(context); return; }
    var db = context.RequestServices.GetRequiredService<EditorialDb>();
    await using var transaction = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74819321)");
    var originalBody = context.Response.Body;
    await using var buffer = new MemoryStream();
    context.Response.Body = buffer;
    try
    {
        await next(context);
        if (context.Response.StatusCode < 400) await transaction.CommitAsync();
        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody);
    }
    finally { context.Response.Body = originalBody; }
});
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapStudio();
app.MapMedia();
app.MapSchedule();
app.MapGoogleAuthorization();
NotebookIntegration.MapNotebook(app);
YouTubePublishing.MapYouTube(app);
app.MapGet("/api/channel", async (EditorialDb db) =>
{
    var channel = await db.Channels.AsNoTracking().SingleOrDefaultAsync();
    return channel is null ? Results.Text("null", "application/json") : Results.Ok(channel.ToResponse());
});
app.MapPut("/api/channel", async (ChannelInput input, EditorialDb db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160 ||
        !Validation.IsIanaTimeZone(input.TimeZone))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["channel"] = ["Valid name and IANA timezone are required."] });
    var channel = await db.Channels.SingleOrDefaultAsync();
    if (channel is null)
    {
        channel = new Channel();
        db.Channels.Add(channel);
        for (var day = 1; day <= 7; day++)
            db.Niches.Add(new ContentNiche { ChannelId = channel.Id, Weekday = day, Name = $"Niche {day}" });
    }
    channel.Name = input.Name.Trim();
    channel.TimeZone = input.TimeZone;
    await db.SaveChangesAsync();
    return Results.Ok(channel.ToResponse());
});
app.MapGet("/api/week", async (EditorialDb db) => (await db.Niches.AsNoTracking().OrderBy(n => n.Weekday).ToListAsync()).Select(n => n.ToResponse()));
app.MapPut("/api/week/{weekday:int}", async (int weekday, NicheInput input, EditorialDb db) =>
{
    if (weekday is < 1 or > 7 || string.IsNullOrWhiteSpace(input.Name) ||
        input.Name.Length > 160 || input.Description is null || input.Guidance is null || input.Description.Length > 2000 || input.Guidance.Length > 4000)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["niche"] = ["Invalid weekday or niche fields."] });
    var niche = await db.Niches.SingleOrDefaultAsync(n => n.Weekday == weekday);
    if (niche is null) return Results.NotFound();
    niche.Name = input.Name.Trim();
    niche.Description = input.Description;
    niche.Guidance = input.Guidance;
    niche.Enabled = input.Enabled;
    await db.SaveChangesAsync();
    return Results.Ok(niche.ToResponse());
});
app.MapGet("/api/productions", async (string? status, Guid? nicheId, EditorialDb db) =>
{
    var query = db.Productions.AsNoTracking();
    if (status is not null)
    {
        if (!Enum.TryParse<ProductionStatus>(status, out var parsed) || !Enum.IsDefined(parsed) || status != parsed.ToString())
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Invalid status filter."] });
        query = query.Where(p => p.Status == parsed);
    }
    if (nicheId is not null) query = query.Where(p => p.ContentNicheId == nicheId);
    return Results.Ok((await query.OrderByDescending(p => p.CreatedAtUtc).ToListAsync()).Select(p => p.ToResponse()));
});
app.MapPost("/api/productions", async (ProductionInput input, HttpRequest request, EditorialDb db) =>
{
    if (string.IsNullOrWhiteSpace(input.Topic) || input.Topic.Length > 300 || input.PlannedDate == default)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["production"] = ["Topic and planned date are required."] });
    var key = request.Headers["Idempotency-Key"].ToString();
    if (key.Length > 128) throw new StudioException(400, "La clave de idempotencia es demasiado larga.");
    var fingerprint = StudioRules.Hash(System.Text.Json.JsonSerializer.Serialize(input));
    if (key.Length > 0)
    {
        var receipt = await db.Set<CreationReceipt>().SingleOrDefaultAsync(r => r.Key == key);
        if (receipt is not null)
        {
            if (receipt.RequestHash != fingerprint) throw new StudioException(409, "La clave de idempotencia ya se usó para otra petición.");
            return Results.Ok((await db.Productions.FindAsync(receipt.ProductionId))!.ToResponse());
        }
    }
    var niche = await db.Niches.SingleOrDefaultAsync(n => n.Id == input.ContentNicheId);
    if (niche is null || !niche.Enabled) return Results.ValidationProblem(new Dictionary<string, string[]> { ["niche"] = ["Select an enabled niche."] });
    var production = new Production { ChannelId = niche.ChannelId, ContentNicheId = niche.Id,
        NicheNameSnapshot = niche.Name, Topic = input.Topic.Trim(), PlannedDate = input.PlannedDate };
    db.Productions.Add(production);
    db.StatusHistory.Add(new ProductionStatusHistory { ProductionId = production.Id, To = ProductionStatus.Draft });
    await db.SaveChangesAsync();
    await StudioEndpoints.OpenWorkspace(db, production, null);
    if (key.Length > 0) db.Add(new CreationReceipt { Key = key, RequestHash = fingerprint, ProductionId = production.Id });
    await db.SaveChangesAsync();
    return Results.Created($"/api/productions/{production.Id}", production.ToResponse());
});
app.MapGet("/api/productions/{id:guid}", async (Guid id, EditorialDb db) =>
{
    var production = await db.Productions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
    if (production is null) return Results.NotFound();
    return Results.Ok(new ProductionDetail(production.ToResponse(),
        (await db.StatusHistory.AsNoTracking().Where(h => h.ProductionId == id).OrderBy(h => h.ChangedAtUtc).ToListAsync()).Select(h => h.ToResponse()).ToList(),
        (await db.NotebookReferences.AsNoTracking().SingleOrDefaultAsync(r => r.ProductionId == id))?.ToResponse()));
});
app.MapPut("/api/productions/{id:guid}", async (Guid id, ProductionEdit input, EditorialDb db) =>
{
    var production = await db.Productions.FindAsync(id);
    if (production is null) return Results.NotFound();
    if (production.Status == ProductionStatus.Cancelled) return Results.Problem(statusCode: 409, detail: "Cancelled productions are immutable.");
    if (await db.Set<YouTubeUpload>().AnyAsync(u => u.ProductionId == id)) throw new StudioException(409, "Los datos de una subida están congelados.");
    if (string.IsNullOrWhiteSpace(input.Topic) || input.Topic.Length > 300 || input.PlannedDate == default)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["production"] = ["Invalid topic or date."] });
    production.Topic = input.Topic.Trim();
    production.PlannedDate = input.PlannedDate;
    production.ModifiedAtUtc = DateTime.UtcNow;
    var workspace = await db.Set<ProductionWorkspace>().FindAsync(id);
    if (workspace is not null) { workspace.ApprovedAtUtc = null; if (workspace.Stage == "ReadyForUpload") workspace.Stage = "QualityReview"; }
    await db.SaveChangesAsync();
    return Results.Ok(production.ToResponse());
});
app.MapPost("/api/productions/{id:guid}/status", async (Guid id, StatusInput input, EditorialDb db) =>
{
    var production = await db.Productions.FindAsync(id);
    if (production is null) return Results.NotFound();
    if (input.Status is null || !Enum.IsDefined(input.Status.Value))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["A valid status is required."] });
    var from = production.Status;
    var allowed = ProductionPolicy.CanTransition(from, input.Status.Value);
    if (!allowed) return Results.Problem(statusCode: 409, detail: "Invalid status transition.");
    if (input.Status == ProductionStatus.Cancelled)
    {
        if (await db.Set<BackgroundJob>().AnyAsync(j => j.ProductionId == id && j.Status == "Running") || await db.Set<YouTubeUpload>().AnyAsync(u => u.ProductionId == id))
            throw new StudioException(409, "No se puede cancelar mientras haya una operación externa o subida en curso.");
        foreach (var job in await db.Set<BackgroundJob>().Where(j => j.ProductionId == id && j.Status == "Queued").ToListAsync()) job.Status = "Cancelled";
        var workspace = await db.Set<ProductionWorkspace>().FindAsync(id);
        if (workspace is not null) workspace.Stage = "Cancelled";
        foreach (var slot in await db.Set<ScheduleSlot>().Where(s => s.ProductionId == id).ToListAsync()) slot.Status = "Cancelled";
        StudioRules.Event(db, id, "ProductionCancelled", "Cancelación explícita del operador");
    }
    production.Status = input.Status.Value;
    production.ModifiedAtUtc = DateTime.UtcNow;
    db.StatusHistory.Add(new ProductionStatusHistory { ProductionId = id, From = from, To = input.Status.Value });
    await db.SaveChangesAsync();
    return Results.Ok(production.ToResponse());
});
app.MapPut("/api/productions/{id:guid}/notebook", async (Guid id, NotebookInput input, EditorialDb db) =>
{
    if (!Validation.IsNotebookUrl(input.Url))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["A valid HTTPS NotebookLM notebook URL is required."] });
    var production = await db.Productions.FindAsync(id);
    if (production is null) return Results.NotFound();
    if (production.Status == ProductionStatus.Cancelled)
        return Results.Problem(statusCode: 409, detail: "Cancelled productions are immutable.");
    var uri = new Uri(input.Url);
    var reference = await db.NotebookReferences.SingleOrDefaultAsync(r => r.ProductionId == id);
    if (reference is not null && reference.Url != uri.AbsoluteUri && await db.Set<ManualNotebookTask>().AnyAsync(t => t.ProductionId == id && t.Status == "Completed"))
        throw new StudioException(409, "El notebook con tareas realizadas no se puede sustituir; conserva su procedencia.");
    if (reference is null) { reference = new NotebookReference { ProductionId = id }; db.NotebookReferences.Add(reference); }
    production.ModifiedAtUtc = DateTime.UtcNow;
    reference.Url = uri.AbsoluteUri;
    reference.RegisteredAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(reference.ToResponse());
});
app.Run();


public partial class Program { }
