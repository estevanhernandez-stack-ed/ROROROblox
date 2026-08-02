using System;
using System.IO;
using Microsoft.Extensions.Logging;
using ROROROblox.App.Logging;
using Xunit;

namespace ROROROblox.Tests;

public class AppLoggingVersionTests
{
    // Asserts against RENDERED text, not enrichment. The existing "App" property is enriched but
    // missing from outputTemplate and never reaches the file — proof that asserting on enrichment
    // would pass while the log stayed unattributable.
    [Fact]
    public void Configure_WritesVersionIntoEveryRenderedLine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rororo-logtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var factory = AppLogging.Configure("9.9.9", dir);
            factory.CreateLogger("T").LogInformation("marker-line");
            AppLogging.Shutdown();

            var text = string.Join("\n", Directory.GetFiles(dir, "*.log").Select(File.ReadAllText));
            Assert.Contains("marker-line", text);
            Assert.Contains("9.9.9", text);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
