using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ROROROblox.Core;

/// <summary>
/// Writes <c>DFIntTaskSchedulerTargetFps</c> (and the cap-removal flag above 240)
/// into every active Roblox version folder's <c>ClientAppSettings.json</c>.
/// Multi-source: standalone <c>%LOCALAPPDATA%\Roblox\Versions</c> + Microsoft Store /
/// UWP <c>%LOCALAPPDATA%\Packages\ROBLOXCORPORATION.ROBLOX_*\LocalCache\Local\Roblox\Versions</c>.
/// Spec §5.1 + Appendix B.
/// </summary>
public sealed class ClientAppSettingsWriter : IClientAppSettingsWriter
{
    private const string FpsKey = "DFIntTaskSchedulerTargetFps";
    private const string CapRemovalKey = "FFlagTaskSchedulerLimitTargetFpsTo2402";
    private static readonly TimeSpan CoActiveWindow = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _standaloneVersionsRoot;
    private readonly string _packagesRoot;

    public ClientAppSettingsWriter() : this(DefaultStandaloneRoot(), DefaultPackagesRoot()) { }

    // Visible for tests — accept arbitrary roots.
    public ClientAppSettingsWriter(string standaloneVersionsRoot, string packagesRoot)
    {
        _standaloneVersionsRoot = standaloneVersionsRoot;
        _packagesRoot = packagesRoot;
    }

    public async Task WriteFpsAsync(int? fps, CancellationToken ct = default)
    {
        var targets = ResolveCandidateFolders();
        if (targets.Count == 0)
        {
            throw new ClientAppSettingsWriteException(
                "Roblox version folder not found. Standalone and UWP install paths both empty.");
        }

        List<Exception>? failures = null;
        foreach (var folder in targets)
        {
            try
            {
                await WriteOneAsync(folder, fps, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(new ClientAppSettingsWriteException(
                    $"Failed to write FPS flag at {folder}: {ex.Message}", ex));
            }
        }
        if (failures is not null && failures.Count == targets.Count)
        {
            throw new ClientAppSettingsWriteException(
                $"All {targets.Count} candidate write(s) failed: {failures[0].Message}", failures[0]);
        }
    }

    private static string DefaultStandaloneRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox", "Versions");

    private static string DefaultPackagesRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages");

    private List<string> ResolveCandidateFolders()
    {
        var standalone = NewestActiveVersionFolder(_standaloneVersionsRoot);
        var uwp = ResolveUwpVersionFolder(_packagesRoot);

        if (standalone is null && uwp is null) return [];
        if (standalone is null) return [uwp!.Value.FullName];
        if (uwp is null) return [standalone.Value.FullName];

        // Both exist — write to both if both are active in the last 30 days. Otherwise, just the newer.
        var ageStandalone = DateTime.UtcNow - standalone.Value.PlayerBetaWriteUtc;
        var ageUwp = DateTime.UtcNow - uwp.Value.PlayerBetaWriteUtc;
        if (ageStandalone < CoActiveWindow && ageUwp < CoActiveWindow)
        {
            return [standalone.Value.FullName, uwp.Value.FullName];
        }
        return ageStandalone < ageUwp ? [standalone.Value.FullName] : [uwp.Value.FullName];
    }

    private static (string FullName, DateTime PlayerBetaWriteUtc)? NewestActiveVersionFolder(string versionsRoot)
    {
        if (!Directory.Exists(versionsRoot)) return null;
        (string FullName, DateTime PlayerBetaWriteUtc)? best = null;
        foreach (var dir in Directory.EnumerateDirectories(versionsRoot, "version-*"))
        {
            var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
            if (!File.Exists(exe)) continue;
            var lastWrite = File.GetLastWriteTimeUtc(exe);
            if (best is null || lastWrite > best.Value.PlayerBetaWriteUtc)
            {
                best = (dir, lastWrite);
            }
        }
        return best;
    }

    private static (string FullName, DateTime PlayerBetaWriteUtc)? ResolveUwpVersionFolder(string packagesRoot)
    {
        if (!Directory.Exists(packagesRoot)) return null;
        foreach (var pkg in Directory.EnumerateDirectories(packagesRoot, "ROBLOXCORPORATION.ROBLOX_*"))
        {
            var versions = Path.Combine(pkg, "LocalCache", "Local", "Roblox", "Versions");
            var found = NewestActiveVersionFolder(versions);
            if (found is not null) return found;
        }
        return null;
    }

    private static async Task WriteOneAsync(string versionFolder, int? fps, CancellationToken ct)
    {
        var clientSettingsDir = Path.Combine(versionFolder, "ClientSettings");
        Directory.CreateDirectory(clientSettingsDir);
        var jsonPath = Path.Combine(clientSettingsDir, "ClientAppSettings.json");

        JsonObject root;
        if (File.Exists(jsonPath))
        {
            try
            {
                var existing = await File.ReadAllTextAsync(jsonPath, ct).ConfigureAwait(false);
                root = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                // Spec §5.1: malformed file → start fresh, don't surface to user.
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        if (fps is null)
        {
            root.Remove(FpsKey);
            root.Remove(CapRemovalKey);
        }
        else
        {
            root[FpsKey] = fps.Value;
            if (fps.Value > FpsPresets.CapRemovalThreshold)
            {
                root[CapRemovalKey] = false;
            }
            else
            {
                root.Remove(CapRemovalKey);
            }
        }

        var tempPath = jsonPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, root.ToJsonString(WriteOptions), ct).ConfigureAwait(false);
        File.Move(tempPath, jsonPath, overwrite: true);
    }
}
