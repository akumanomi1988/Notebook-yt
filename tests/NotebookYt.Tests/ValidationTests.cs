using NotebookYt.Api;
namespace NotebookYt.Tests;
public class ValidationTests
{
    [Theory]
    [InlineData("Etc/UTC", true)]
    [InlineData("Europe/Madrid", true)]
    [InlineData("Romance Standard Time", false)]
    [InlineData(null, false)]
    [InlineData("Invalid/Zone", false)]
    public void ValidatesIanaZones(string? value, bool expected) => Assert.Equal(expected, Validation.IsIanaTimeZone(value));
    [Theory]
    [InlineData("https://notebooklm.google.com/notebook/abc-123", true)]
    [InlineData("https://notebooklm.google.com/notebook", false)]
    [InlineData("https://notebooklm.google.com.evil.test/notebook/id", false)]
    [InlineData("https://user@notebooklm.google.com/notebook/id", false)]
    [InlineData("https://notebooklm.google.com:999/notebook/id", false)]
    [InlineData("https://notebook.cloud.google.com/global/notebook/id?project=123", true)]
    [InlineData("https://notebook.cloud.google/eu/notebook/id?project=123", true)]
    [InlineData("https://notebook.cloud.google.com/notebook/id", false)]
    [InlineData(null, false)]
    public void ValidatesNotebookUrls(string? value, bool expected) => Assert.Equal(expected, Validation.IsNotebookUrl(value));
}
