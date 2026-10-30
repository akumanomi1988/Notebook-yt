using System.Text;
using Microsoft.Extensions.Configuration;
using NotebookYt.Api.Studio;

namespace NotebookYt.Tests;

public class MediaInspectionTests
{
    [Fact]
    public async Task SyntheticTechnicalFixtureIsInspectedWithoutClaimingNotebookOrigin()
    {
        var root = Path.Combine(Path.GetTempPath(), "notebook-media-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Storage:Root"] = root, ["Media:ffmpegPath"] = Environment.GetEnvironmentVariable("TEST_FFMPEG") ?? "ffmpeg", ["Media:ffprobePath"] = Environment.GetEnvironmentVariable("TEST_FFPROBE") ?? "ffprobe"
        }).Build();
        var storage = new MediaStorage(configuration); var fixture = Path.Combine(root, "synthetic-technical-fixture.mp4");
        try
        {
            // This synthetic file tests media plumbing only. It is never a genuine NotebookLM artifact.
            await storage.Run("ffmpeg", ["-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=1280x720:r=5", "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo", "-t", "2", "-c:v", "libx264", "-c:a", "aac", "-pix_fmt", "yuv420p", fixture], CancellationToken.None);
            await using var file = File.OpenRead(fixture); var saved = await storage.Save(file, 10 * 1024 * 1024, CancellationToken.None);
            var artifact = new MediaArtifact { Kind = "Video", StorageKey = saved.Key, Sha256 = saved.Hash, SizeBytes = saved.Length, Origin = "Unverified", OriginEvidence = "Synthetic technical test fixture, not NotebookLM content" };
            await storage.Inspect(artifact, CancellationToken.None); Assert.Equal("Valid", artifact.Status); Assert.True(artifact.HasAudio); Assert.Equal(1280, artifact.Width); Assert.Equal("Unverified", artifact.Origin);
            await using var duplicate = File.OpenRead(fixture); Assert.Equal(saved.Key, (await storage.Save(duplicate, 10 * 1024 * 1024, CancellationToken.None)).Key);
            var corrupt = await storage.Save(new MemoryStream(Encoding.UTF8.GetBytes("Not a video")), 100, CancellationToken.None);
            var invalid = new MediaArtifact { Kind = "Video", StorageKey = corrupt.Key, Sha256 = corrupt.Hash, SizeBytes = corrupt.Length };
            await Assert.ThrowsAsync<StudioException>(() => storage.Inspect(invalid, CancellationToken.None));
            await Assert.ThrowsAsync<StudioException>(() => storage.Save(new MemoryStream(new byte[20]), 10, CancellationToken.None));
            Assert.Throws<StudioException>(() => storage.Resolve("../private-file"));
        }
        finally
        {
            // Only this randomly generated test directory is eligible for cleanup.
            foreach (var path in Directory.EnumerateFiles(root)) File.Delete(path);
            Directory.Delete(root);
        }
    }
}
