using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.App.Games;

/// <summary>
/// What one Library entry shows: a <see cref="LibraryEntry"/> plus the words and the one fact that
/// changes while the page is open (the icon, which arrives after the page does). Data stays on the
/// entry; this class only words it, so the grid tile and the list row read the same strings and a
/// language switch needs nothing from the store.
/// <para>
/// Identity: the view model keeps one <see cref="LibraryItem"/> per saved entry across reloads
/// (<see cref="Key"/>), so typing in the box moves existing items rather than rebuilding the tiles.
/// </para>
/// </summary>
internal sealed class LibraryItem : INotifyPropertyChanged
{
    private LibraryEntry _entry;
    private string? _iconUrl;

    public LibraryItem(LibraryEntry entry)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _iconUrl = entry.IconUrl;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The identity that survives a reload: the kind, the place and (for a server) its id.</summary>
    public static (LibraryEntryKind Kind, long PlaceId, Guid? ServerId) KeyOf(LibraryEntry e) => (e.Kind, e.PlaceId, e.PrivateServerId);

    public (LibraryEntryKind Kind, long PlaceId, Guid? ServerId) Key => KeyOf(_entry);

    public LibraryEntry Entry => _entry;

    /// <summary>Replaces the data after a reload (a rename, a new default, a fresh last-played) and re-raises every word.</summary>
    public void Update(LibraryEntry entry)
    {
        if (entry.Equals(_entry)) return;
        _entry = entry;
        if (!string.IsNullOrWhiteSpace(entry.IconUrl)) _iconUrl = entry.IconUrl;
        OnChanged(string.Empty);
    }

    /// <summary>The icon: the entry's own, then whatever the icon cache resolved while the page was open. Null draws the initials tile.</summary>
    public string? IconUrl
    {
        get => _iconUrl;
        set
        {
            if (_iconUrl == value) return;
            _iconUrl = value;
            OnChanged();
        }
    }

    public long PlaceId => _entry.PlaceId;

    public string DisplayName => _entry.RenderName;

    /// <summary>Up to two letters of the name, drawn on the icon tile until (or unless) the real icon arrives.</summary>
    public string Initials
    {
        get
        {
            var letters = DisplayName.Split([' ', '-', '_', ':', '!', '.'], StringSplitOptions.RemoveEmptyEntries)
                                     .Select(w => w.FirstOrDefault(char.IsLetterOrDigit))
                                     .Where(c => c != default)
                                     .Take(2)
                                     .ToArray();
            return letters.Length == 0 ? string.Empty : new string(letters).ToUpper(CultureInfo.CurrentCulture);
        }
    }

    public bool IsPrivate => _entry.Kind == LibraryEntryKind.PrivateServer;

    public bool IsDefault => _entry.IsDefault;

    /// <summary>Only saved entries (favourites and servers) can be made default, renamed or removed; a Recent place is not saved.</summary>
    public bool IsSaved => _entry.Kind != LibraryEntryKind.Recent;

    /// <summary>The Set-default item: a saved entry that is not the default.</summary>
    public bool CanSetDefault => IsSaved && !_entry.IsDefault;

    public bool CanClearDefault => IsSaved && _entry.IsDefault;

    public bool CanResetName => IsSaved && !string.IsNullOrEmpty(_entry.LocalName);

    /// <summary>"Favourite", "Private server" or "Recent": the list view's kind column.</summary>
    public string KindText => _entry.Kind switch
    {
        LibraryEntryKind.Favourite => Loc.Get("Library_Kind_Favourite"),
        LibraryEntryKind.PrivateServer => Loc.Get("Library_Kind_PrivateServer"),
        _ => Loc.Get("Library_Kind_Recent"),
    };

    /// <summary>"2 h ago", or "Never played". Local history: never masked.</summary>
    public string LastPlayedText => _entry.LastPlayedUtc is { } at
        ? AccountSummary.RelativeAgo(at)
        : Loc.Get("Library_Meta_NeverPlayed");

    /// <summary>The all-time total ("61 hr"), or empty when nothing was recorded.</summary>
    public string TotalText => _entry.TotalPlayed is { } t && t > TimeSpan.Zero ? FormatDuration(t) : string.Empty;

    /// <summary>The tile's meta line: "2 h ago · 61 hr", or just the age, or "Never played".</summary>
    public string MetaText => TotalText.Length > 0 && _entry.LastPlayedUtc is not null
        ? Loc.Format("Library_Meta_Played", LastPlayedText, TotalText)
        : LastPlayedText;

    /// <summary>The full name over the meta line over the place id: for the day the tile trims the name with an ellipsis.</summary>
    public string TooltipText => DisplayName + "\n" + MetaText + "\n" + Loc.Format("Library_PlaceId", _entry.PlaceId.ToString(CultureInfo.InvariantCulture));

    public string PlayName => Loc.Format("Library_Play_Name", DisplayName);

    public string PlayAsName => Loc.Format("Library_PlayAs_Name", DisplayName);

    public string MoreName => Loc.Format("Library_More_Name", DisplayName);

    private static string FormatDuration(TimeSpan d)
        => d < TimeSpan.FromMinutes(1) ? Loc.Get("Shell_Panel_Duration_LessThanMinute")
         : d < TimeSpan.FromHours(1) ? Loc.Format("Shell_Panel_Duration_Minutes", (int)d.TotalMinutes)
         : Loc.Format("Shell_Panel_Duration_Hours", (int)d.TotalHours);

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One "From Roblox" search result, worded: the creator and player lines come from resx keys, not XAML format strings.</summary>
internal sealed class FromRobloxItem
{
    public FromRobloxItem(GameSearchResult result) => Result = result ?? throw new ArgumentNullException(nameof(result));

    public GameSearchResult Result { get; }

    public string Name => Result.Name;

    public string IconUrl => Result.IconUrl;

    public string CreatorText => Loc.Format("Library_By", Result.CreatorName);

    public string PlayersText => Loc.Format("Library_Playing", Result.PlayerCount.ToString("N0", TranslationSource.Instance.CurrentCulture));

    public string AddName => Loc.Format("Library_Add_Name", Result.Name);
}
