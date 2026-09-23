using System.IO;
using System.Text.Json;

namespace ROROROblox.Core.KnownIssues;

/// <summary>
/// Which entry ids the user has closed the notice for. A file of its own rather than a
/// <c>SettingsBlob</c> key: it is state no control edits, and a setting would bring an
/// <c>IAppSettings</c> member, four private test fakes and <c>SettingsReachabilityTests</c> for nothing.
/// Every read and write degrades to "nothing dismissed" rather than throwing — the cost of a lost file
/// is one repeated notice.
/// </summary>
public sealed class KnownIssuesDismissals
{
    public const string FileName = "known-issues-dismissed.json";

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _path;

    public KnownIssuesDismissals(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        FileName);

    public IReadOnlySet<string> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllBytes(_path), Options);
            return new HashSet<string>(
                (dto?.Dismissed ?? []).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Adds <paramref name="ids"/>, keeps only ids still in <paramref name="liveIds"/>, writes, and returns the result.</summary>
    public IReadOnlySet<string> Dismiss(IEnumerable<string> ids, IEnumerable<string> liveIds)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(liveIds);

        var live = new HashSet<string>(liveIds, StringComparer.Ordinal);
        var next = new HashSet<string>(Load().Concat(ids).Where(live.Contains), StringComparer.Ordinal);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var dto = new Dto { Dismissed = next.Order(StringComparer.Ordinal).ToList() };
            File.WriteAllBytes(_path, JsonSerializer.SerializeToUtf8Bytes(dto, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still honoured for this session through the returned set.
        }

        return next;
    }

    internal sealed class Dto
    {
        public List<string>? Dismissed { get; set; }
    }
}
