using CineLibraryEssentials.Services;
using Xunit;

namespace CineLibraryEssentials.Tests;

/// <summary>
/// ConfigService is created in many places (each step, the Settings dialog, the
/// main window). They must all see one set of settings, or a change made in
/// Settings is lost when another screen saves its stale copy.
/// </summary>
[Collection("Config")]
public class ConfigServiceTests
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    [Fact]
    public void ChangeInOneInstanceIsSeenByAnother()
    {
        var settings = new ConfigService();
        var step3 = new ConfigService();

        settings.SetScrapeLanguage("my");

        Assert.Equal("my", step3.GetScrapeLanguage());
    }

    [Fact]
    public void AnotherInstanceSavingDoesNotUndoAChange()
    {
        var step1 = new ConfigService();     // created first, like the steps at startup
        var settings = new ConfigService();

        settings.SetScrapeLanguage("ja");
        step1.AddRecentSourceFolder(@"C:\Movies");   // step 1 saves afterwards

        Assert.Contains("\"ScrapeLanguage\": \"ja\"", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void DiagnosticsShowSettingsAndRecentProblemsButNeverTheKey()
    {
        var config = new ConfigService();
        config.SetApiKey("my-secret-key-123");
        try
        {
            for (int i = 1; i <= 30; i++) DiagnosticsService.Record($"Problem {i}");

            var text = DiagnosticsService.Build(config, "1.3.0");

            Assert.StartsWith("CineLibrary Essentials 1.3.0", text);
            Assert.Contains("TMDb key: your own", text);
            Assert.DoesNotContain("my-secret-key-123", text);
            Assert.Contains("Problem 30", text);
            Assert.Contains("Problem 6", text);
            Assert.DoesNotContain("Problem 5", text);   // only the newest 25 are kept
        }
        finally
        {
            config.SetApiKey("");
        }
    }

    [Fact]
    public void CustomRemoveWordsRoundTrip()
    {
        var a = new ConfigService();
        a.SetCustomRemoveWords(new[] { " ION10 ", "mSD", "", "ion10" });

        // Trimmed, blanks dropped, case-insensitive duplicates removed
        Assert.Equal(new[] { "ION10", "mSD" }, new ConfigService().GetCustomRemoveWords());
    }
}
