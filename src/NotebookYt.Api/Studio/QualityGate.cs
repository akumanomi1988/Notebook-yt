using Microsoft.EntityFrameworkCore;

namespace NotebookYt.Api.Studio;

public record QualityResult(bool Ready, List<string> Failures, List<string> Warnings);

public static class QualityGate
{
    public static async Task<QualityResult> Check(EditorialDb db, MediaStorage storage, Guid id)
    {
        var failures = new List<string>(); var warnings = new List<string>();
        var p = await db.Productions.FindAsync(id) ?? throw new StudioException(404, "Producción inexistente.");
        var w = await StudioRules.Workspace(db, id); var settings = await db.Set<ChannelSettings>().FindAsync(p.ChannelId) ?? new ChannelSettings();
        if (p.Status == ProductionStatus.Cancelled) failures.Add("La producción está cancelada.");
        if (!await db.Set<SourcePackage>().AnyAsync(s => s.ProductionId == id)) failures.Add("Falta el paquete de fuentes.");
        if (!await db.NotebookReferences.AnyAsync(n => n.ProductionId == id)) failures.Add("Falta el notebook.");
        if (!await db.Set<ManualNotebookTask>().AnyAsync(t => t.ProductionId == id) || await db.Set<ManualNotebookTask>().AnyAsync(t => t.ProductionId == id && t.Status != "Completed")) failures.Add("Quedan tareas de NotebookLM pendientes.");
        var artifact = await db.Set<MediaArtifact>().SingleOrDefaultAsync(a => a.Id == w.CanonicalArtifactId && a.ProductionId == id);
        if (artifact is null || artifact.Kind != "Video" || artifact.Status != "Valid" || artifact.Origin != "OperatorAttested" || !storage.Exists(artifact.StorageKey)) failures.Add("Falta un vídeo canónico válido de NotebookLM.");
        else
        {
            if (!artifact.HasAudio || artifact.Height < settings.MinimumHeight || artifact.DurationSeconds < settings.MinimumDurationSeconds || artifact.DurationSeconds > settings.MaximumDurationSeconds) failures.Add("El vídeo no cumple la configuración técnica de calidad.");
            warnings.Add("La procedencia NotebookLM está declarada por el operador; no está verificada criptográficamente por Google.");
        }
        var thumbnail = await db.Set<MediaArtifact>().Where(a => a.ProductionId == id && a.Kind == "Thumbnail" && a.Status == "Valid").OrderByDescending(a => a.ImportedAtUtc).FirstOrDefaultAsync();
        if (thumbnail is null || !storage.Exists(thumbnail.StorageKey)) failures.Add("Falta una miniatura válida.");
        if (string.IsNullOrWhiteSpace(w.YouTubeTitle) || string.IsNullOrWhiteSpace(w.YouTubeDescription) || w.MadeForKids is null) failures.Add("Completa título, descripción y público infantil de YouTube.");
        try { StudioRules.ValidatePublicationText(w.YouTubeTitle, w.YouTubeDescription, w.Tags); }
        catch (StudioException ex) { failures.Add(ex.Message); }
        try { StudioRules.ValidatePublicationSchedule(w.Visibility, w.PublishAtUtc); }
        catch (StudioException ex) { failures.Add(ex.Message); }
        if (w.PublishAtUtc is not null && w.PublishAtUtc <= DateTime.UtcNow) failures.Add("La fecha de publicación programada ya pasó.");
        return new(failures.Count == 0, failures, warnings);
    }
}
