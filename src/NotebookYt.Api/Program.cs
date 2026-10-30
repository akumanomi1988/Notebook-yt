using Microsoft.EntityFrameworkCore;
using NotebookYt.Api;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(
    new System.Text.Json.Serialization.JsonStringEnumConverter<ProductionStatus>(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<EditorialDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Editorial")
        ?? throw new InvalidOperationException("ConnectionStrings:Editorial is required.")));
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
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
    await next(context);
    if (context.Response.StatusCode < 400) await transaction.CommitAsync();
});
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/channel", async (EditorialDb db) => await db.Channels.AsNoTracking().FirstOrDefaultAsync());
app.MapPut("/api/channel", async (ChannelInput input, EditorialDb db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160 ||
        !Validation.IsIanaTimeZone(input.TimeZone))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["channel"] = ["Valid name and IANA timezone are required."] });
    var channel = await db.Channels.FirstOrDefaultAsync();
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
    return Results.Ok(channel);
});
app.MapGet("/api/week", async (EditorialDb db) => await db.Niches.AsNoTracking().OrderBy(n => n.Weekday).ToListAsync());
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
    return Results.Ok(niche);
});
app.MapGet("/api/productions", async (EditorialDb db) =>
    await db.Productions.AsNoTracking().OrderByDescending(p => p.CreatedAtUtc).ToListAsync());
app.MapPost("/api/productions", async (ProductionInput input, EditorialDb db) =>
{
    if (string.IsNullOrWhiteSpace(input.Topic) || input.Topic.Length > 300 || input.PlannedDate == default)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["production"] = ["Topic and planned date are required."] });
    var niche = await db.Niches.SingleOrDefaultAsync(n => n.Id == input.ContentNicheId);
    if (niche is null || !niche.Enabled) return Results.ValidationProblem(new Dictionary<string, string[]> { ["niche"] = ["Select an enabled niche."] });
    var production = new Production { ChannelId = niche.ChannelId, ContentNicheId = niche.Id,
        NicheNameSnapshot = niche.Name, Topic = input.Topic.Trim(), PlannedDate = input.PlannedDate };
    db.Productions.Add(production);
    db.StatusHistory.Add(new ProductionStatusHistory { ProductionId = production.Id, To = ProductionStatus.Draft });
    await db.SaveChangesAsync();
    return Results.Created($"/api/productions/{production.Id}", production);
});
app.MapGet("/api/productions/{id:guid}", async (Guid id, EditorialDb db) =>
{
    var production = await db.Productions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
    if (production is null) return Results.NotFound();
    return Results.Ok(new { production, history = await db.StatusHistory.AsNoTracking()
        .Where(h => h.ProductionId == id).OrderBy(h => h.ChangedAtUtc).ToListAsync(),
        notebook = await db.NotebookReferences.AsNoTracking().SingleOrDefaultAsync(r => r.ProductionId == id) });
});
app.MapPut("/api/productions/{id:guid}", async (Guid id, ProductionEdit input, EditorialDb db) =>
{
    var production = await db.Productions.FindAsync(id);
    if (production is null) return Results.NotFound();
    if (production.Status == ProductionStatus.Cancelled) return Results.Problem(statusCode: 409, detail: "Cancelled productions are immutable.");
    if (string.IsNullOrWhiteSpace(input.Topic) || input.Topic.Length > 300 || input.PlannedDate == default)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["production"] = ["Invalid topic or date."] });
    production.Topic = input.Topic.Trim();
    production.PlannedDate = input.PlannedDate;
    await db.SaveChangesAsync();
    return Results.Ok(production);
});
app.MapPost("/api/productions/{id:guid}/status", async (Guid id, StatusInput input, EditorialDb db) =>
{
    var production = await db.Productions.FindAsync(id);
    if (production is null) return Results.NotFound();
    if (input.Status is null || !Enum.IsDefined(input.Status.Value))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["A valid status is required."] });
    var from = production.Status;
    var allowed = (from, input.Status) switch
    {
        (ProductionStatus.Draft, ProductionStatus.BriefReady or ProductionStatus.Blocked or ProductionStatus.Cancelled) => true,
        (ProductionStatus.BriefReady, ProductionStatus.AwaitingNotebook or ProductionStatus.Blocked or ProductionStatus.Cancelled) => true,
        (ProductionStatus.AwaitingNotebook, ProductionStatus.Blocked or ProductionStatus.Cancelled) => true,
        (ProductionStatus.Blocked, ProductionStatus.Draft or ProductionStatus.BriefReady or ProductionStatus.AwaitingNotebook or ProductionStatus.Cancelled) => true,
        _ => false
    };
    if (!allowed) return Results.Problem(statusCode: 409, detail: "Invalid status transition.");
    production.Status = input.Status.Value;
    db.StatusHistory.Add(new ProductionStatusHistory { ProductionId = id, From = from, To = input.Status.Value });
    await db.SaveChangesAsync();
    return Results.Ok(production);
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
    if (reference is null) { reference = new NotebookReference { ProductionId = id }; db.NotebookReferences.Add(reference); }
    reference.Url = uri.AbsoluteUri;
    reference.RegisteredAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(reference);
});
app.Run();

public record ChannelInput(string Name, string TimeZone);
public record NicheInput(string Name, string Description, string Guidance, bool Enabled);
public record ProductionInput(Guid ContentNicheId, string Topic, DateOnly PlannedDate);
public record ProductionEdit(string Topic, DateOnly PlannedDate);
public record StatusInput(ProductionStatus? Status);
public record NotebookInput(string Url);
