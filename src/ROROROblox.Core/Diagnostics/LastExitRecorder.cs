using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core.Diagnostics;

/// <summary>How the previous RoRoRo session ended. <see cref="At"/> is null for none, and for an unreadable record.</summary>
public sealed record LastExit(string Reason, string Detail, DateTimeOffset? At)
{
    public static LastExit NoPrevious { get; } = new(LastExitRecorder.None, string.Empty, null);
}

/// <summary>Read side of <see cref="LastExitRecorder"/>: what the previous session left behind. Surfaced through HostInfo (contract 0.14.0).</summary>
public interface ILastExitSource
{
    /// <summary>The previous session's exit. <see cref="LastExitRecorder.None"/> until <c>Begin()</c> has run.</summary>
    LastExit Previous { get; }
}

/// <summary>
/// Keeps a tiny <c>last-exit.json</c> so the next session can say how the last one ended (#246).
/// <para>
/// <see cref="Begin"/> reads the previous record, then overwrites it with <c>running</c>. A clean exit,
/// a Windows session end, or a crash then replaces <c>running</c>. A file still saying <c>running</c> at
/// the next start means nobody got to write: killed, power loss, or servicing that gave no notice.
/// </para>
/// <para>
/// Write order: crash beats session-ending beats clean. <see cref="RecordClean"/> never overwrites a
/// crash or a session-ending (Windows ended the session, and WPF then runs OnExit normally);
/// <see cref="RecordSessionEnding"/> never overwrites a crash. Every write is synchronous (the crash
/// one runs inside the AppDomain unhandled handler) and none of them throw. Before <see cref="Begin"/>
/// every Record call is a no-op, so a second instance that exits early never touches the record.
/// </para>
/// <para>
/// A crash detail is the exception type's full name, the HResult in hex and the message. Nothing else:
/// no stack, no inner exception, nothing that could carry a cookie.
/// </para>
/// </summary>
public sealed class LastExitRecorder : ILastExitSource
{
    public const string None = "none";
    public const string Clean = "clean";
    public const string SessionEnding = "session-ending";
    public const string Crash = "crash";
    public const string Unknown = "unknown";

    /// <summary>The on-disk marker for a session that has started and not yet said how it ended.</summary>
    public const string Running = "running";

    public const string FileName = "last-exit.json";

    private const int MaxDetailLength = 500;

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private LastExit _previous = LastExit.NoPrevious;
    private bool _begun;
    private string _state = string.Empty;

    public LastExitRecorder(string dataDirectory, TimeProvider? time = null, ILogger? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _path = Path.Combine(dataDirectory, FileName);
        _time = time ?? TimeProvider.System;
        _log = log ?? NullLogger.Instance;
    }

    public LastExit Previous
    {
        get { lock (_gate) { return _previous; } }
    }

    /// <summary>Read the previous record, then mark this session running. Call once per process; later calls do nothing.</summary>
    public void Begin()
    {
        lock (_gate)
        {
            if (_begun)
            {
                return;
            }
            _begun = true;
            _previous = ReadPrevious();
            _state = Running;
            Write(Running, string.Empty);
        }
    }

    public void RecordClean()
    {
        try
        {
            lock (_gate)
            {
                if (!_begun || _state is Crash or SessionEnding)
                {
                    return;
                }
                _state = Clean;
                Write(Clean, string.Empty);
            }
        }
        catch
        {
            // Never throw out of the exit path.
        }
    }

    public void RecordSessionEnding(string reason)
    {
        try
        {
            lock (_gate)
            {
                if (!_begun || _state is Crash)
                {
                    return;
                }
                _state = SessionEnding;
                Write(SessionEnding, Trim(reason ?? string.Empty));
            }
        }
        catch
        {
            // Never throw out of the session-ending event.
        }
    }

    public void RecordCrash(Exception ex)
    {
        try
        {
            var detail = ex is null
                ? string.Empty
                : $"{ex.GetType().FullName} 0x{ex.HResult:X8} {ex.Message}";
            lock (_gate)
            {
                if (!_begun)
                {
                    return;
                }
                _state = Crash;
                Write(Crash, Trim(detail));
            }
        }
        catch
        {
            // Inside the unhandled-exception handler: nothing here may throw.
        }
    }

    private LastExit ReadPrevious()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return LastExit.NoPrevious;
            }
            var rec = JsonSerializer.Deserialize<Record>(File.ReadAllText(_path));
            var at = rec?.AtUnixMs is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null;
            var detail = rec?.Detail ?? string.Empty;
            return rec?.State switch
            {
                Clean => new LastExit(Clean, string.Empty, at),
                SessionEnding => new LastExit(SessionEnding, detail, at),
                Crash => new LastExit(Crash, detail, at),
                _ => new LastExit(Unknown, string.Empty, at),
            };
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "last-exit.json unreadable; treating the previous exit as unknown.");
            return new LastExit(Unknown, string.Empty, null);
        }
    }

    private void Write(string state, string detail)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(new Record(state, detail, _time.GetUtcNow().ToUnixTimeMilliseconds()));
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Could not write last-exit.json ({State}).", state);
        }
    }

    private static string Trim(string s) => s.Length <= MaxDetailLength ? s : s[..MaxDetailLength];

    private sealed record Record(
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("atUnixMs")] long? AtUnixMs);
}
