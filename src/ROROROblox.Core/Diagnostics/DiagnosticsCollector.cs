using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Default <see cref="IDiagnosticsCollector"/>. All probes are best-effort — a missing piece
/// becomes "not detected" rather than throwing. Designed so a clean snapshot is always
/// produceable even on a busted machine where half the surface is broken.
/// </summary>
public sealed class DiagnosticsCollector : IDiagnosticsCollector
{
    // Microsoft Edge WebView2 Runtime evergreen channel — Stable.
    private const string WebView2RegistryPath32 =
        @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    private const string WebView2RegistryPath64 =
        @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    private readonly IAccountStore _accountStore;
    private readonly IRobloxProcessTracker _processTracker;
    private readonly IMutexHolder _mutexHolder;
    private readonly string _logDirectory;
    private readonly string _dataDirectory;

    public DiagnosticsCollector(
        IAccountStore accountStore,
        IRobloxProcessTracker processTracker,
        IMutexHolder mutexHolder,
        string logDirectory,
        string dataDirectory)
    {
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _processTracker = processTracker ?? throw new ArgumentNullException(nameof(processTracker));
        _mutexHolder = mutexHolder ?? throw new ArgumentNullException(nameof(mutexHolder));
        _logDirectory = logDirectory ?? string.Empty;
        _dataDirectory = dataDirectory ?? string.Empty;
    }

    public async Task<DiagnosticsSnapshot> CollectAsync(CancellationToken ct = default)
    {
        var appVersion = typeof(DiagnosticsCollector).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        var dotnet = RuntimeInformation.FrameworkDescription;
        var osVersion = RuntimeInformation.OSDescription;

        var robloxVersion = TryGetRobloxVersion();
        var webView2Version = TryGetWebView2Version();

        int accountCount = 0;
        try
        {
            var accounts = await _accountStore.ListAsync().ConfigureAwait(false);
            accountCount = accounts.Count;
        }
        catch
        {
            // Couldn't open the account store (DPAPI corrupt etc.) — leave count at 0.
        }

        return new DiagnosticsSnapshot(
            AppVersion: appVersion,
            DotNetVersion: dotnet,
            OsVersion: osVersion,
            RobloxInstalledVersion: robloxVersion ?? "not detected",
            RobloxInstalled: robloxVersion is not null,
            WebView2Version: webView2Version ?? "not detected",
            WebView2Installed: webView2Version is not null,
            AccountCount: accountCount,
            LiveProcessCount: _processTracker.Attached.Count,
            MultiInstanceState: _mutexHolder.IsHeld ? "ON" : "OFF",
            LogDirectory: _logDirectory,
            DataDirectory: _dataDirectory,
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string? TryGetRobloxVersion()
    {
        try
        {
            var versionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox",
                "Versions");

            if (!Directory.Exists(versionsDir))
            {
                return null;
            }

            var folders = new DirectoryInfo(versionsDir)
                .GetDirectories("version-*")
                .OrderByDescending(d => d.LastWriteTimeUtc);

            foreach (var folder in folders)
            {
                var exePath = Path.Combine(folder.FullName, "RobloxPlayerBeta.exe");
                if (!File.Exists(exePath))
                {
                    continue;
                }
                var info = FileVersionInfo.GetVersionInfo(exePath);
                return info.FileVersion ?? info.ProductVersion;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetWebView2Version()
    {
        // Try 64-bit then 32-bit registry paths. Evergreen runtime publishes its version under "pv".
        foreach (var path in new[] { WebView2RegistryPath64, WebView2RegistryPath32 })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue("pv") is string version && !string.IsNullOrEmpty(version) && version != "0.0.0.0")
                {
                    return version;
                }
            }
            catch
            {
                // Continue to next path
            }
        }
        return null;
    }
}
