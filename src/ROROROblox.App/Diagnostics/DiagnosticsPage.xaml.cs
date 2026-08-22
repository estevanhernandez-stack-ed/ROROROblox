using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ROROROblox.App.Logging;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Diagnostics;

/// <summary>
/// The Diagnostics destination, hosted by the shell (F-013 — formerly <c>DiagnosticsWindow</c>).
/// <c>Loaded</c> refires every time the shell navigates back here, which preserves the old
/// window's per-open semantics: every visit collects a fresh snapshot.
/// </summary>
internal partial class DiagnosticsPage : UserControl
{
    private readonly IDiagnosticsCollector _collector;
    private DiagnosticsSnapshot? _snapshot;

    public DiagnosticsPage(IDiagnosticsCollector collector)
    {
        _collector = collector;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Collecting...";
        try
        {
            _snapshot = await _collector.CollectAsync();
            RenderSnapshot(_snapshot);
            StatusText.Text = $"Captured {_snapshot.CapturedAtUtc.ToLocalTime():HH:mm:ss} local.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't collect diagnostics: {ex.Message}";
        }
    }

    private void RenderSnapshot(DiagnosticsSnapshot s)
    {
        DetailsList.Children.Clear();
        AddSection("Application", new[]
        {
            ("Version", s.AppVersion),
            (".NET runtime", s.DotNetVersion),
            ("OS", s.OsVersion),
            ("Multi-Instance", s.MultiInstanceState),
            ("Saved accounts", s.AccountCount.ToString()),
            ("Live Roblox clients", s.LiveProcessCount.ToString()),
        });
        AddSection("Roblox", new[]
        {
            ("Installed", s.RobloxInstalled ? "yes" : "no"),
            ("Version", s.RobloxInstalledVersion),
        });
        AddSection("WebView2", new[]
        {
            ("Installed", s.WebView2Installed ? "yes" : "no"),
            ("Version", s.WebView2Version),
        });
        AddSection("Memory", new[]
        {
            ("Installed RAM", FormatGb(s.TotalPhysicalMemoryBytes)),
            ("Available RAM", FormatGb(s.AvailablePhysicalMemoryBytes)),
        });
        if (s.AccountMemory.Count > 0)
        {
            // Label is the account's short id only — no display names, no local names, no
            // cookies. This artifact gets pasted into Discord. A ReadOk-false reading carries a
            // stale last-known-good byte figure; tag it "(stale)" so it never reads as a fresh,
            // trustworthy sample (same precedent as MemoryWatchdog's own log payload).
            AddSection("Per-Account Memory", s.AccountMemory.Select(a => (
                Label: a.AccountId.ToString("N")[..8],
                Value: a.ReadOk ? FormatGb(a.PrivateBytes) : $"{FormatGb(a.PrivateBytes)}(stale)")));
        }
        AddSection("Paths", new[]
        {
            ("Logs", s.LogDirectory),
            ("Data", s.DataDirectory),
        });
    }

    private static string FormatGb(long bytes) => $"{bytes / 1024d / 1024d / 1024d:F1} GB";

    private void AddSection(string title, IEnumerable<(string label, string value)> rows)
    {
        // F-064. This was the app's THIRD definition of a section heading — 12px SemiBold on
        // CyanBrush — and it lived in C#, where no markup review would ever have found it. Accent
        // colour was carrying structural work, which fails the same reader that grouping-by-fill
        // fails. It now consumes the one shared style, whose larger top margin supplies the group
        // separation the cyan used to fake.
        var header = new TextBlock
        {
            Text = title,
            Style = (Style)FindResource("SectionHeadingStyle"),
        };
        DetailsList.Children.Add(header);

        foreach (var (label, value) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (Brush)FindResource("MutedTextBrush"),
            };
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)FindResource("WhiteBrush"),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);

            DetailsList.Children.Add(grid);
        }
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        OpenInExplorer(AppLogging.LogDirectory);
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ROROROblox");
        OpenInExplorer(path);
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch
        {
            // Best-effort.
        }
    }

    private void OnSaveBundleClick(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null)
        {
            StatusText.Text = "Snapshot not ready yet — wait a beat and retry.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save support bundle",
            Filter = "Zip archives (*.zip)|*.zip",
            FileName = $"rororoblox-support-{DateTime.Now:yyyyMMdd-HHmm}.zip",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            SaveBundleButton.IsEnabled = false;
            StatusText.Text = "Building bundle...";
            BuildSupportBundle(_snapshot, dialog.FileName);
            StatusText.Text = $"Saved {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't save bundle: {ex.Message}";
        }
        finally
        {
            SaveBundleButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Compose a small zip with: snapshot.txt (the rendered diagnostics) and the latest log
    /// files. Logs are scanned for inline secrets (.ROBLOSECURITY) and redacted before
    /// inclusion as a defense-in-depth — the logger itself never writes them, but a future
    /// caller or third-party library might.
    /// </summary>
    private static void BuildSupportBundle(DiagnosticsSnapshot snapshot, string outputZipPath)
    {
        if (File.Exists(outputZipPath))
        {
            File.Delete(outputZipPath);
        }

        using var zipStream = File.Create(outputZipPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        // 1. Snapshot text.
        var snapshotEntry = archive.CreateEntry("snapshot.txt");
        using (var writer = new StreamWriter(snapshotEntry.Open(), Encoding.UTF8))
        {
            // F-034: the header of the file a clan member attaches to a bug report. Everything
            // else in this bundle is an identifier; this line is read by a person.
            writer.WriteLine($"{Branding.ProductName} support snapshot");
            writer.WriteLine($"Captured (UTC): {snapshot.CapturedAtUtc:O}");
            writer.WriteLine();
            writer.WriteLine($"App version       : {snapshot.AppVersion}");
            writer.WriteLine($".NET runtime      : {snapshot.DotNetVersion}");
            writer.WriteLine($"OS                : {snapshot.OsVersion}");
            writer.WriteLine($"Multi-Instance    : {snapshot.MultiInstanceState}");
            writer.WriteLine($"Saved accounts    : {snapshot.AccountCount}");
            writer.WriteLine($"Live clients      : {snapshot.LiveProcessCount}");
            writer.WriteLine();
            writer.WriteLine($"Roblox installed  : {(snapshot.RobloxInstalled ? "yes" : "no")}");
            writer.WriteLine($"Roblox version    : {snapshot.RobloxInstalledVersion}");
            writer.WriteLine();
            writer.WriteLine($"WebView2 installed: {(snapshot.WebView2Installed ? "yes" : "no")}");
            writer.WriteLine($"WebView2 version  : {snapshot.WebView2Version}");
            writer.WriteLine();
            writer.WriteLine($"Installed RAM     : {FormatGb(snapshot.TotalPhysicalMemoryBytes)}");
            writer.WriteLine($"Available RAM     : {FormatGb(snapshot.AvailablePhysicalMemoryBytes)}");
            if (snapshot.AccountMemory.Count > 0)
            {
                writer.WriteLine("Per-account memory:");
                // Short id only — no display names, no local names, no cookies. This bundle
                // gets pasted into Discord.
                foreach (var a in snapshot.AccountMemory)
                {
                    var shortId = a.AccountId.ToString("N")[..8];
                    var suffix = a.ReadOk ? string.Empty : "(stale)";
                    writer.WriteLine($"  {shortId}: {FormatGb(a.PrivateBytes)}{suffix}");
                }
            }
            writer.WriteLine();
            writer.WriteLine($"Logs path         : {snapshot.LogDirectory}");
            writer.WriteLine($"Data path         : {snapshot.DataDirectory}");
        }

        // 2. Latest log files (up to 3 most recent).
        if (Directory.Exists(snapshot.LogDirectory))
        {
            var logs = new DirectoryInfo(snapshot.LogDirectory)
                .GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(3);

            foreach (var log in logs)
            {
                var entry = archive.CreateEntry($"logs/{log.Name}");
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8);

                // Stream-redact: defense in depth in case a future code path logs a cookie.
                using var reader = new StreamReader(log.FullName);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    writer.WriteLine(RedactSecrets(line));
                }
            }
        }
    }

    private static string RedactSecrets(string line)
    {
        // Defense in depth — the logger never writes ROBLOSECURITY values, but redact anyway.
        const string cookieName = ".ROBLOSECURITY";
        var idx = line.IndexOf(cookieName, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return line[..(idx + cookieName.Length)] + "=[REDACTED]";
        }
        return line;
    }
}
