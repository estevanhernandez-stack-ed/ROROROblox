using System.Xml.Linq;

namespace ROROROblox.Core;

/// <inheritdoc cref="IGlobalBasicSettingsProbe" />
public sealed class GlobalBasicSettingsProbe : IGlobalBasicSettingsProbe
{
    private const string FramerateCapName = "FramerateCap";

    private readonly string _robloxAppDataRoot;

    public GlobalBasicSettingsProbe()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox"))
    {
    }

    public GlobalBasicSettingsProbe(string robloxAppDataRoot)
        => _robloxAppDataRoot = robloxAppDataRoot;

    public int? ReadFramerateCap()
    {
        try
        {
            // Resolve() itself hits the filesystem (Directory.Exists + EnumerateFiles) and must
            // stay inside this try: it can throw UnauthorizedAccessException / IOException /
            // DirectoryNotFoundException on its own -- e.g. a Roblox installer recreating
            // %LOCALAPPDATA%\Roblox mid-Squad-Launch (TOCTOU between the Exists check and the
            // lazy enumeration). Both the interface doc and this method promise null for
            // "missing, locked, or malformed"; a Resolve() call outside the try broke that
            // promise and let the exception escape into the launch path.
            var file = GlobalBasicSettingsFile.Resolve(_robloxAppDataRoot);
            if (file is null)
            {
                return null;
            }

            // Read the bytes ourselves rather than XDocument.Load(path): a client may hold the file
            // open mid-write, and we want a locked file to read as "unknown" (null), not as an
            // exception escaping into a launch path.
            var text = File.ReadAllText(file.FullName);
            // Target roblox/Item/Properties/int specifically -- matching GlobalBasicSettingsWriter's
            // shape exactly, not Descendants("int") at any depth. GlobalBasicSettingsFile was
            // extracted precisely so the writer and reader can't independently disagree about which
            // FILE is active; a probe still using its own looser XPath than the writer keeps that
            // same disagreement alive one level down, inside the document.
            var value = XDocument.Parse(text)
                .Element("roblox")?
                .Element("Item")?
                .Element("Properties")?
                .Elements("int")
                .FirstOrDefault(e => (string?)e.Attribute("name") == FramerateCapName)
                ?.Value;

            return int.TryParse(value, out var cap) ? cap : null;
        }
        catch (Exception)
        {
            // Missing, locked, or malformed -> unknown. Callers must not confuse this with a value.
            return null;
        }
    }

    public DateTimeOffset? GetLastWriteTimeUtc()
    {
        try
        {
            // See the comment in ReadFramerateCap(): Resolve() must stay inside the try too.
            var file = GlobalBasicSettingsFile.Resolve(_robloxAppDataRoot);
            if (file is null)
            {
                return null;
            }

            return new DateTimeOffset(File.GetLastWriteTimeUtc(file.FullName), TimeSpan.Zero);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
