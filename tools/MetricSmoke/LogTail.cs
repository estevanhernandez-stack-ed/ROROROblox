using System.Text;
using System.Text.RegularExpressions;

namespace ROROROblox.MetricSmoke;

/// <summary>
/// Reads the app's own Serilog file as a smoke-test verdict, from a recorded offset rather than a
/// log-directory override — <c>AppLogging.Configure</c> takes one, but production passes none, and
/// using it here would need a production change this plan does not make (design task 2). Recording
/// the file's length before a scenario and reading only what gets appended after it needs nothing
/// from the app and works against the real, daily-rolling file under
/// <c>%LOCALAPPDATA%\ROROROblox\logs\</c>.
/// <para>
/// The app holds that file open for the life of the process (<c>shared: true</c> in
/// <c>AppLogging.Configure</c>), so every open here is <see cref="FileShare.ReadWrite"/> — an
/// exclusive read would contend with the very writes this class exists to observe.
/// </para>
/// <para>
/// The three recognisers match the EXACT rendered shape of two lines in
/// <c>ROROROblox.App.Discord.AlertDispatcher</c> and one in
/// <c>ROROROblox.App.Plugins.Adapters.MetricReportSinkAdapter</c> — read out of those files, not out
/// of the design doc, because a spec can drift and the code cannot. Matching the rendered shape
/// rather than a keyword is deliberate: a matcher that fires on any line containing the word
/// "alert" would turn ordinary log noise into a green run, which is worse than no harness at all.
/// </para>
/// </summary>
public sealed class LogTail
{
    // AlertDispatcher.DispatchAsync, the delivered-alert line:
    //   log.LogInformation("Alert → {Destination}: {Title} ({Count} account(s)).",
    //       alert.Destination, payload.Title, alert.Triggers.Count);
    // Rendered example (AppLogging.Configure's outputTemplate puts timestamp/level/version/
    // SourceContext before the message; only the message is matched here):
    //   Alert → Mine: BaronBloxwell dropped out (1 account(s)).
    private static readonly Regex DeliveredPattern = new(
        @"Alert → (?<destination>\S+): (?<title>.+) \((?<count>\d+) account\(s\)\)\.\s*$",
        RegexOptions.Compiled);

    // AlertDispatcher.DispatchAsync, the swallowed-alert line — the opposite verdict from the one
    // above, and deliberately not sharing a pattern with it:
    //   log.LogInformation("Alert raised for {Count} account(s) but routed nowhere — check the
    //       destination, the per-account mute, and the {Cooldown}-minute cooldown.", ...);
    private static readonly Regex RoutedNowherePattern = new(
        @"Alert raised for \d+ account\(s\) but routed nowhere",
        RegexOptions.Compiled);

    // MetricReportSinkAdapter.Report, the future-skew drop:
    //   log.LogInformation("Dropped a metric report for {MetricId} stamped {Skew} in the future —
    //       check the reporter's clock; it is sending local time, not UTC.", metricId, ...);
    private static readonly Regex SkewDropPattern = new(
        @"Dropped a metric report for (?<metricId>\S+) stamped .+ in the future",
        RegexOptions.Compiled);

    private readonly string _path;
    private long _offset;

    private LogTail(string path, long offset)
    {
        _path = path;
        _offset = offset;
    }

    /// <summary>
    /// Opens the log at its CURRENT length, so the first <see cref="NewLinesAsync"/> call returns
    /// only what a scenario appends after this point — nothing the app already logged today. A
    /// missing file (nothing has logged yet, or the day has not rolled to it yet) starts at offset
    /// zero rather than throwing.
    /// </summary>
    public static LogTail OpenAt(string logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        var length = File.Exists(logPath) ? new FileInfo(logPath).Length : 0L;
        return new LogTail(logPath, length);
    }

    /// <summary>
    /// Reads whatever has been appended since the last call (or since <see cref="OpenAt"/>, on the
    /// first call), then advances the recorded offset to the new end-of-file — so repeated polling
    /// only ever hands back what is genuinely new.
    /// <para>
    /// Opened with <see cref="FileShare.ReadWrite"/> (plus <see cref="FileShare.Delete"/>, so a
    /// rename or delete happening underneath this handle does not fail): the app's own sink opens
    /// the same file with <c>shared: true</c>, and a reader that demanded exclusive access would
    /// block the app's next write rather than merely observe it.
    /// </para>
    /// <para>
    /// If the file is now SHORTER than the recorded offset — most likely Serilog's daily roll has
    /// started a smaller same-named-pattern file since this offset was captured, or the file was
    /// truncated outright — the old offset no longer describes a position in these bytes. Seeking
    /// to it would either throw (offset past the new end is fine for <c>Seek</c> itself, but reading
    /// from there returns nothing) or, worse, land mid-file at a byte count that happens to still be
    /// in range but belongs to unrelated content. Treating the shrink as a fresh start — read from
    /// byte zero, advance to the new length — avoids both without pretending to reconstruct history
    /// that offset can no longer address.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<string>> NewLinesAsync()
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<string>();
        }

        using var reader = new StreamReader(
            new FileStream(
                _path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, useAsync: true),
            Encoding.UTF8);

        var length = reader.BaseStream.Length;
        if (length < _offset)
        {
            _offset = 0;
        }

        reader.BaseStream.Seek(_offset, SeekOrigin.Begin);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        _offset = length;

        return SplitLines(text);
    }

    /// <summary>
    /// Every "Alert → …" line in <paramref name="lines"/> — a trigger that reached a destination,
    /// parsed into that destination, the alert's title, and how many accounts it covered. Never
    /// matches a routed-nowhere line: the two templates share only the word "Alert", not the shape
    /// after it, so this recogniser cannot count the opposite verdict as a delivery.
    /// </summary>
    public IReadOnlyList<DeliveredAlert> Delivered(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var results = new List<DeliveredAlert>();
        foreach (var line in lines)
        {
            var match = DeliveredPattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            results.Add(new DeliveredAlert(
                match.Groups["destination"].Value,
                match.Groups["title"].Value,
                int.Parse(match.Groups["count"].Value)));
        }
        return results;
    }

    /// <summary>
    /// How many triggers raised but reached no destination — the opposite verdict from
    /// <see cref="Delivered"/>. Counting these as deliveries (or vice versa) would invert a smoke
    /// row's result, which is exactly why the two patterns do not overlap.
    /// </summary>
    public int RoutedNowhereCount(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Count(RoutedNowherePattern.IsMatch);
    }

    /// <summary>The metric id named on every future-dated report <c>MetricReportSinkAdapter</c> dropped.</summary>
    public IReadOnlyList<string> SkewDrops(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var results = new List<string>();
        foreach (var line in lines)
        {
            var match = SkewDropPattern.Match(line);
            if (match.Success)
            {
                results.Add(match.Groups["metricId"].Value);
            }
        }
        return results;
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }

        var raw = text.Split('\n');
        var lines = new List<string>(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            // Serilog's outputTemplate ends every record with {NewLine}, so a chunk ending in "\n"
            // splits into a trailing empty string that is a split artifact, not a blank logged line.
            if (i == raw.Length - 1 && raw[i].Length == 0)
            {
                continue;
            }
            lines.Add(raw[i].TrimEnd('\r'));
        }
        return lines;
    }
}

/// <summary>
/// One alert that reached a destination — <see cref="ROROROblox.App.Discord.AlertDispatcher"/>'s
/// "Alert → {Destination}: {Title} ({Count} account(s))." line, parsed.
/// </summary>
public sealed record DeliveredAlert(string Destination, string Title, int AccountCount);
