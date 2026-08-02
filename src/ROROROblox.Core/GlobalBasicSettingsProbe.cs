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
        var file = GlobalBasicSettingsFile.Resolve(_robloxAppDataRoot);
        if (file is null)
        {
            return null;
        }

        try
        {
            // Read the bytes ourselves rather than XDocument.Load(path): a client may hold the file
            // open mid-write, and we want a locked file to read as "unknown" (null), not as an
            // exception escaping into a launch path.
            var text = File.ReadAllText(file.FullName);
            var value = XDocument.Parse(text)
                .Descendants("int")
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
        var file = GlobalBasicSettingsFile.Resolve(_robloxAppDataRoot);
        if (file is null)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(file.FullName), TimeSpan.Zero);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
