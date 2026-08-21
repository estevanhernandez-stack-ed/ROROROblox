using System.IO;
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

        var path = GlobalBasicSettingsFile.Resolve(_robloxAppDataRoot)?.FullName;
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

    /// <summary>
    /// Writes the window state RoRoRo's own kill is about to destroy (F-109).
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS IS NEEDED AT ALL. Roblox persists window geometry and fullscreen on CLEAN EXIT and
    /// not before — measured 2026-08-20: nothing changed on disk through a whole session while the
    /// window was resized, then the exact on-screen rect appeared within a second of the user
    /// closing it. A terminated process runs no exit path, so every force-close throws that away
    /// and the next launch comes back at the old size, or fullscreen. That is what "Roblox keeps
    /// opening in full screen" turned out to be.
    /// </para>
    /// <para>
    /// The clean path is preferred and tried first — see <c>ClientStopSequence</c>. This is the
    /// fallback for when the user does not answer Roblox's confirm and we force. It restores only
    /// what is observable from OUTSIDE the process: rect and fullscreen. Anything the player
    /// changed inside Roblox's own settings menu that session — graphics quality, volume,
    /// sensitivity — is not visible from here and is still lost. Stated rather than discovered.
    /// </para>
    /// </remarks>
    public async Task WriteWindowStateAsync(
        bool fullscreen, int x, int y, int width, int height, CancellationToken ct = default)
    {
        // A zero-area rect means we could not read the window. Writing it would replace a good
        // saved size with a broken one — worse than doing nothing.
        if (width <= 0 || height <= 0) return;

        await MutateAsync(properties =>
        {
            SetSimple(properties, "bool", "Fullscreen", fullscreen ? "true" : "false");

            // Roblox stores these as <Vector2 name="…"><X>..</X><Y>..</Y></Vector2>. Patch the
            // children in place rather than replacing the element, for the same reason the
            // FramerateCap path patches a value: everything not named here belongs to the user.
            SetVector2(properties, "StartScreenPosition", x, y);
            SetVector2(properties, "StartScreenSize", width, height);
        }, ct).ConfigureAwait(false);
    }

    private static void SetSimple(XElement properties, string elementName, string name, string value)
    {
        var node = properties.Elements(elementName).FirstOrDefault(e => (string?)e.Attribute("name") == name);
        if (node is null)
        {
            properties.Add(new XElement(elementName, new XAttribute("name", name), value));
            return;
        }
        node.Value = value;
    }

    private static void SetVector2(XElement properties, string name, int x, int y)
    {
        var node = properties.Elements("Vector2").FirstOrDefault(e => (string?)e.Attribute("name") == name);
        if (node is null)
        {
            properties.Add(new XElement("Vector2", new XAttribute("name", name),
                new XElement("X", x), new XElement("Y", y)));
            return;
        }

        void Child(string childName, int value)
        {
            var c = node.Element(childName);
            if (c is null) node.Add(new XElement(childName, value));
            else c.Value = value.ToString();
        }

        Child("X", x);
        Child("Y", y);
    }

    /// <summary>
    /// Load, patch, save atomically. Shared by both writers so the parse rules, the error shape and
    /// the temp-file swap have exactly one implementation — this repo has twice shipped a fix into
    /// one copy of a routine and missed the other.
    /// </summary>
    private async Task MutateAsync(Action<XElement> patch, CancellationToken ct)
    {
        var path = GlobalBasicSettingsFile.Resolve(_robloxAppDataRoot)?.FullName;
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
            throw new GlobalBasicSettingsWriteException($"Could not parse {path}: {ex.Message}", ex);
        }

        var properties = doc.Element("roblox")?.Element("Item")?.Element("Properties");
        if (properties is null)
        {
            throw new GlobalBasicSettingsWriteException(
                $"{path} does not contain a <roblox><Item><Properties> structure.");
        }

        patch(properties);

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
            throw new GlobalBasicSettingsWriteException($"Failed to write {path}: {ex.Message}", ex);
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
}
