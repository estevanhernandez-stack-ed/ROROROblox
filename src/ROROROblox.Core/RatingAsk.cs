using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Core;

/// <summary>How the person answered the rating ask. Either one ends it for good.</summary>
public enum RatingAnswer
{
    Rated,
    NotNow,
}

/// <summary>
/// Decides when RoRoRo asks for a Store rating, and remembers the answer (2026-11-10).
/// </summary>
/// <remarks>
/// <para>
/// The rule is deliberately narrow: a Microsoft Store install only, after a launch has attached on
/// <see cref="DaysNeeded"/> separate local days, asked once, never again after either answer. The
/// caller decides when to look at <see cref="ShouldAsk"/>; the main window looks only at startup,
/// so the ask never appears in the middle of a launch.
/// </para>
/// <para>
/// State lives in <c>rating-ask.json</c> in the data folder: up to three dates and the answer.
/// Nothing is sent anywhere. Every read and write is best-effort, because
/// <see cref="RecordSuccessfulLaunch"/> runs from the launch-attached handler and a rating prompt
/// is never worth a failed launch.
/// </para>
/// </remarks>
public sealed class RatingAsk
{
    public const string FileName = "rating-ask.json";

    /// <summary>Separate local days with an attached launch before the ask can show.</summary>
    public const int DaysNeeded = 3;

    /// <summary>The Store's own review page for RoRoRo. The Store app handles this scheme.</summary>
    public const string StoreReviewUri = "ms-windows-store://review/?ProductId=9NMJCS390KWB";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly bool _storeInstall;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private State _state;

    public RatingAsk(string dataDirectory, InstallChannel channel, TimeProvider? time = null, ILogger? log = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        _path = Path.Combine(dataDirectory, FileName);
        _storeInstall = channel == InstallChannel.Store;
        _time = time ?? TimeProvider.System;
        _log = log ?? NullLogger.Instance;
        _state = _storeInstall ? Load() : new State();
    }

    /// <summary>True when a Store install has had enough good days and has not answered yet.</summary>
    public bool ShouldAsk
    {
        get
        {
            lock (_gate)
            {
                return _storeInstall && _state.Answer is null && _state.Days.Count >= DaysNeeded;
            }
        }
    }

    /// <summary>
    /// A launch attached. Counts today once, stops counting at <see cref="DaysNeeded"/>, and does
    /// nothing at all on a non-Store install or after an answer. Never throws.
    /// </summary>
    public void RecordSuccessfulLaunch()
    {
        if (!_storeInstall)
        {
            return;
        }

        lock (_gate)
        {
            if (_state.Answer is not null || _state.Days.Count >= DaysNeeded)
            {
                return;
            }

            var today = _time.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (_state.Days.Contains(today))
            {
                return;
            }

            _state.Days.Add(today);
            Save();
        }
    }

    /// <summary>The person answered. The ask never shows again. Never throws.</summary>
    public void MarkAnswered(RatingAnswer answer)
    {
        if (!_storeInstall)
        {
            return;
        }

        lock (_gate)
        {
            _state.Answer = answer.ToString();
            Save();
        }
    }

    private State Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new State();
            }

            var loaded = JsonSerializer.Deserialize<State>(File.ReadAllText(_path), Json);
            if (loaded is null)
            {
                return new State();
            }

            loaded.Days ??= [];
            return loaded;
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable: start over. The worst case is one repeated ask.
            _log.LogDebug(ex, "Couldn't read {File}; starting the rating-ask count over.", FileName);
            return new State();
        }
    }

    private void Save()
    {
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_state, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Couldn't write {File}; the rating-ask state stays in memory for this session.", FileName);
        }
    }

    private sealed class State
    {
        public List<string> Days { get; set; } = [];
        public string? Answer { get; set; }
    }
}
