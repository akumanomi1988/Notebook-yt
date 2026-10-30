using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public static class EditorialScheduler
{
    public static async Task Maintain(EditorialDb db, DateTime now)
    {
        var channel = await db.Channels.SingleOrDefaultAsync(); if (channel is null) return;
        var settings = await db.Set<ChannelSettings>().FindAsync(channel.Id); if (settings?.SchedulingEnabled != true) return;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById(channel.TimeZone)));
        var niches = await db.Niches.Where(n => n.ChannelId == channel.Id).ToListAsync();
        var slots = await db.Set<ScheduleSlot>().Where(s => s.ChannelId == channel.Id).ToListAsync();
        foreach (var past in slots.Where(s => s.PublishAtUtc < now && s.Status is "Unassigned" or "Planned"))
        {
            var upload = past.ProductionId is null ? null : await db.Set<YouTubeUpload>().SingleOrDefaultAsync(u => u.ProductionId == past.ProductionId);
            if (upload?.Status != "Published") past.Status = "Missed";
        }
        for (var offset = settings.LeadDays; offset < settings.HorizonDays; offset++)
        {
            var date = today.AddDays(offset); var day = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            var niche = niches.Single(n => n.Weekday == day); if (!niche.Enabled) continue;
            var slot = slots.SingleOrDefault(s => s.LocalDate == date);
            if (slot is null)
            {
                slot = new ScheduleSlot { ChannelId = channel.Id, ContentNicheId = niche.Id, LocalDate = date, LocalTime = settings.PublishingTime, TimeZone = channel.TimeZone,
                    PublishAtUtc = StudioRules.ResolveLocalTime(date, settings.PublishingTime, channel.TimeZone) };
                db.Add(slot); slots.Add(slot);
            }
            if (slot.ProductionId is Guid existingId)
            {
                var existing = await db.Set<ProductionWorkspace>().FindAsync(existingId);
                // A local planning slot must not turn a private upload into a public publication schedule.
                if (existing is not null && existing.Visibility != "public" && existing.PublishAtUtc == slot.PublishAtUtc && existing.ApprovedAtUtc is null
                    && !await db.Set<YouTubeUpload>().AnyAsync(u => u.ProductionId == existingId))
                {
                    existing.PublishAtUtc = null;
                    StudioRules.Event(db, existingId, "PrivateScheduleAligned", "Conservado el turno editorial; eliminada la fecha pública incompatible con la visibilidad privada u oculta.");
                }
                continue;
            }
            if (slot.PublishAtUtc <= now) continue;
            var topic = await db.Set<EditorialTopic>().Where(t => t.ContentNicheId == niche.Id && t.Status == "Approved" && t.ProductionId == null).OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAtUtc).FirstOrDefaultAsync();
            if (topic is null) continue;
            var production = new Production { ChannelId = channel.Id, ContentNicheId = niche.Id, NicheNameSnapshot = niche.Name, Topic = topic.Title, PlannedDate = date };
            db.Add(production); db.Add(new ProductionStatusHistory { ProductionId = production.Id, To = ProductionStatus.Draft }); topic.ProductionId = production.Id; slot.ProductionId = production.Id; slot.Status = "Planned";
            await db.SaveChangesAsync(); var w = await StudioEndpoints.OpenWorkspace(db, production, topic.Id);
            w.PublishAtUtc = w.Visibility == "public" ? slot.PublishAtUtc : null;
            StudioRules.Event(db, production.Id, "ScheduleAssigned", $"{date} {slot.LocalTime} {channel.TimeZone} = {slot.PublishAtUtc:O}"); await db.SaveChangesAsync();
        }
        await db.SaveChangesAsync();
    }
    public static void MapSchedule(this WebApplication app)
    {
        app.MapPost("/api/studio/schedule/maintain", async (EditorialDb db) => { await Maintain(db, DateTime.UtcNow); return Results.Ok(new { maintained = true }); });
        app.MapPut("/api/studio/slots/{id:guid}", async (Guid id, RescheduleInput input, EditorialDb db) =>
        {
            var slot = await db.Set<ScheduleSlot>().FindAsync(id) ?? throw new StudioException(404, "Turno inexistente.");
            if (input.Date == default) throw new StudioException(400, "Fecha inválida.");
            if (await db.Set<ScheduleSlot>().AnyAsync(s => s.Id != id && s.ChannelId == slot.ChannelId && s.LocalDate == input.Date)) throw new StudioException(409, "Ya existe un turno ese día.");
            var channel = await db.Channels.FindAsync(slot.ChannelId); var dateUtc = StudioRules.ResolveLocalTime(input.Date, input.Time, channel!.TimeZone);
            if (dateUtc <= DateTime.UtcNow) throw new StudioException(400, "Selecciona un horario futuro.");
            if (slot.ProductionId is Guid pId)
            {
                var p = await StudioRules.Editable(db, pId); var w = await StudioRules.Workspace(db, pId); p.PlannedDate = input.Date; p.ModifiedAtUtc = DateTime.UtcNow;
                w.PublishAtUtc = w.Visibility == "public" ? dateUtc : null; w.ApprovedAtUtc = null;
                if (w.Stage == "ReadyForUpload") w.Stage = "QualityReview";
            }
            var day = input.Date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)input.Date.DayOfWeek;
            var assigned = await db.Niches.SingleAsync(n => n.ChannelId == slot.ChannelId && n.Weekday == day);
            if (assigned.Id != slot.ContentNicheId) throw new StudioException(409, "El nuevo día corresponde a otro nicho; conserva la rotación semanal.");
            slot.LocalDate = input.Date; slot.LocalTime = input.Time; slot.TimeZone = channel.TimeZone; slot.PublishAtUtc = dateUtc; slot.Status = slot.ProductionId is null ? "Unassigned" : "Planned";
            StudioRules.Event(db, slot.ProductionId, "Rescheduled", dateUtc.ToString("O")); await db.SaveChangesAsync(); return Results.Ok(slot);
        });
    }
}
public record RescheduleInput(DateOnly Date, TimeOnly Time);
