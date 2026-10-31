using System.Net;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public class StudioRulesTests
{
    [Fact]
    public void YouTubeMetadataEnforcesUtf8ByteLimitsAndTagQuoting()
    {
        StudioRules.ValidatePublicationText("Título válido", "Descripción válida", "historia, ciencia natural");
        Assert.Throws<StudioException>(() => StudioRules.ValidatePublicationText("Valid", new string('\u00e9', 3000), ""));
        Assert.Throws<StudioException>(() => StudioRules.ValidatePublicationText("<invalid>", "Valid", ""));
        Assert.Throws<StudioException>(() => StudioRules.ValidatePublicationText("Valid", "Valid", new string('a', 497) + " b"));
    }
    [Fact]
    public void DstGapMovesForwardAndOverlapChoosesLaterOccurrence()
    {
        Assert.Equal(new DateTime(2027, 3, 28, 1, 0, 0, DateTimeKind.Utc), StudioRules.ResolveLocalTime(new(2027, 3, 28), new(2, 30), "Europe/Madrid"));
        Assert.Equal(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc), StudioRules.ResolveLocalTime(new(2026, 10, 25), new(2, 30), "Europe/Madrid"));
    }
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://example.com")]
    [InlineData("https://localhost")]
    [InlineData("https://127.0.0.1/private")]
    [InlineData("https://user@example.com")]
    public void SourceRegistrationRejectsUnsafeUrls(string url) => Assert.Throws<StudioException>(() => StudioRules.PublicUrl(url));

    [Fact]
    public void ResumableOffsetsComeFromServerEvidence()
    {
        using var response = new HttpResponseMessage((HttpStatusCode)308); response.Headers.Add("Range", "bytes=0-8388607");
        Assert.Equal(8388608, YouTubePublishing.ConfirmedBytes(response));
        response.Headers.Remove("Range"); response.Headers.Add("Range", "bytes=5-10");
        Assert.Throws<StudioException>(() => YouTubePublishing.ConfirmedBytes(response));
        Assert.True(YouTubePublishing.IsUploadSession("https://www.googleapis.com/upload/youtube/v3/videos?upload_id=test"));
        Assert.False(YouTubePublishing.IsUploadSession("https://www.googleapis.com.evil.test/upload/youtube/v3/videos"));
    }

    [Theory]
    [InlineData("private", true, false)]
    [InlineData("unlisted", true, false)]
    [InlineData("public", true, true)]
    [InlineData("private", false, true)]
    [InlineData("unlisted", false, true)]
    [InlineData("public", false, true)]
    public void ScheduledPublicationRequiresExplicitPublicVisibility(string visibility, bool scheduled, bool allowed)
    {
        DateTime? date = scheduled ? DateTime.UtcNow.AddDays(1) : null;
        if (allowed) StudioRules.ValidatePublicationSchedule(visibility, date);
        else Assert.Equal(409, Assert.Throws<StudioException>(() => StudioRules.ValidatePublicationSchedule(visibility, date)).Status);
    }
}
