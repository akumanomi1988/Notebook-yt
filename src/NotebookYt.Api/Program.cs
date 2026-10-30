using Microsoft.EntityFrameworkCore;
using NotebookYt.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<EditorialDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Editorial")
        ?? throw new InvalidOperationException("ConnectionStrings:Editorial is required.")));
var app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/channel", async (EditorialDb db) => await db.Channels.AsNoTracking().FirstOrDefaultAsync());
app.MapPut("/api/channel", async (ChannelInput input, EditorialDb db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160 ||
        !TimeZoneInfo.GetSystemTimeZones().Any(t => t.Id == input.TimeZone))
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
        input.Name.Length > 160 || input.Description.Length > 2000 || input.Guidance.Length > 4000)
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
    if (production.Status == ProductionStatus.Cancelled) return Results.Conflict(new { error = "Cancelled productions are immutable." });
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
    var from = production.Status;
    var allowed = (from, input.Status) switch
    {
        (ProductionStatus.Draft, ProductionStatus.BriefReady or ProductionStatus.Blocked or ProductionStatus.Cancelled) => true,
        (ProductionStatus.BriefReady, ProductionStatus.AwaitingNotebook or ProductionStatus.Blocked or ProductionStatus.Cancelled) => true,
        (ProductionStatus.AwaitingNotebook, ProductionStatus.Blocked or ProductionStatus.Cancelled) => true,
        (ProductionStatus.Blocked, ProductionStatus.Draft or ProductionStatus.BriefReady or ProductionStatus.AwaitingNotebook or ProductionStatus.Cancelled) => true,
        _ => false
    };
    if (!allowed) return Results.Conflict(new { error = "Invalid status transition." });
    production.Status = input.Status;
    db.StatusHistory.Add(new ProductionStatusHistory { ProductionId = id, From = from, To = input.Status });
    await db.SaveChangesAsync();
    return Results.Ok(production);
});
app.MapPut("/api/productions/{id:guid}/notebook", async (Guid id, NotebookInput input, EditorialDb db) =>
{
    if (!Uri.TryCreate(input.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
        uri.Host is not ("notebooklm.google.com" or "notebook.google.com" or "notebook.cloud.google.com" or "notebook.cloud.google") ||
        input.Url.Length > 2048 || !uri.AbsolutePath.Contains("notebook", StringComparison.OrdinalIgnoreCase))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["A valid HTTPS NotebookLM notebook URL is required."] });
    if (!await db.Productions.AnyAsync(p => p.Id == id)) return Results.NotFound();
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
public record StatusInput(ProductionStatus Status);
public record NotebookInput(string Url);
