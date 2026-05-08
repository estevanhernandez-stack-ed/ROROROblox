using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ROROROblox.Core;

/// <summary>
/// Writes <c>FramerateCap</c> in Roblox's user-game-settings XML — the lever that
/// controls in-game Settings → Performance → Frame Rate. Wins over the FFlag system
/// for users who haven't already set their in-game cap to Unlimited.
///
/// File: <c>%LOCALAPPDATA%\Roblox\GlobalBasicSettings_&lt;N&gt;.xml</c> where
/// <c>&lt;N&gt;</c> is a schema version that bumps when Roblox changes the
/// settings format. We pick the highest-numbered non-Studio file in the dir.
/// Roblox itself rewrites this file on session exit, so we write before each launch.
/// Spec banner-correction (2026-05-07).
/// </summary>
public sealed class GlobalBasicSettingsWriter : IGlobalBasicSettingsWriter
{
    private const string FramerateCapName = "FramerateCap";

    // Match GlobalBasicSettings_<digits>.xml — exclude the _Studio variant which is for Roblox Studio.
    private static readonly Regex SettingsFileRegex = new(
        @"^GlobalBasicSettings_(\d+)\.xml$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _robloxAppDataRoot;

    public GlobalBasicSettingsWriter() : this(DefaultRobloxAppDataRoot()) { }

    // Visible for tests — accept arbitrary roots.
    public GlobalBasicSettingsWriter(string robloxAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(robloxAppDataRoot);
        _robloxAppDataRoot = robloxAppDataRoot;
    }

    public async Task WriteFramerateCapAsync(int? fps, CancellationToken ct = default)
    {
        if (fps is null)
        {
            // null = leave the file alone. We don't know what the user's "default"
            // would be; clearing the FramerateCap node would either delete a
            // user-chosen value or be a no-op.
            return;
        }

        var path = ResolveActiveSettingsFile();
        if (path is null)
        {
            throw new GlobalBasicSettingsWriteException(
                $"GlobalBasicSettings_<N>.xml not found under {_robloxAppDataRoot}. " +
                "Roblox may not have been run yet on this machine.");
        }

        XDocument doc;
        try
        {
            await using var read = File.OpenRead(path);
            doc = await XDocument.LoadAsync(read, LoadOptions.PreserveWhitespace, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not GlobalBasicSettingsWriteException)
        {
            throw new GlobalBasicSettingsWriteException(
                $"Could not parse {path}: {ex.Message}", ex);
        }

        var properties = doc
            .Element("roblox")?
            .Element("Item")?
            .Element("Properties");
        if (properties is null)
        {
            throw new GlobalBasicSettingsWriteException(
                $"{path} does not contain a <roblox><Item><Properties> structure.");
        }

        var frameRateNode = properties
            .Elements("int")
            .FirstOrDefault(e => (string?)e.Attribute("name") == FramerateCapName);

        if (frameRateNode is null)
        {
            // Roblox writes FramerateCap on first session, so absence is unusual but not
            // fatal — insert it. Place at end of <Properties> to minimize diff against
            // Roblox's typical output.
            frameRateNode = new XElement("int",
                new XAttribute("name", FramerateCapName),
                fps.Value.ToString());
            properties.Add(frameRateNode);
        }
        else
        {
            frameRateNode.Value = fps.Value.ToString();
        }

        var tempPath = path + ".tmp";
        var moved = false;
        try
        {
            await using (var write = File.Create(tempPath))
            {
                await doc.SaveAsync(write, SaveOptions.DisableFormatting, ct).ConfigureAwait(false);
            }
            File.Move(tempPath, path, overwrite: true);
            moved = true;
        }
        catch (Exception ex)
        {
            throw new GlobalBasicSettingsWriteException(
                $"Failed to write {path}: {ex.Message}", ex);
        }
        finally
        {
            if (!moved && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }
    }

    private static string DefaultRobloxAppDataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox");

    /// <summary>
    /// Resolve the active settings file: highest-numbered <c>GlobalBasicSettings_&lt;N&gt;.xml</c>
    /// (skipping <c>_Studio</c> variants). Returns null if the directory doesn't exist or
    /// contains no matching file.
    /// </summary>
    private string? ResolveActiveSettingsFile()
    {
        if (!Directory.Exists(_robloxAppDataRoot)) return null;

        (string Path, int Version)? best = null;
        foreach (var file in Directory.EnumerateFiles(_robloxAppDataRoot, "GlobalBasicSettings_*.xml"))
        {
            var name = Path.GetFileName(file);
            var match = SettingsFileRegex.Match(name);
            if (!match.Success) continue; // skips _Studio variants — they have non-numeric tail
            if (!int.TryParse(match.Groups[1].Value, out var version)) continue;
            if (best is null || version > best.Value.Version)
            {
                best = (file, version);
            }
        }
        return best?.Path;
    }
}
