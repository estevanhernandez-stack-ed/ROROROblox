using System.ComponentModel;
using ROROROblox.App.Localization;

namespace ROROROblox.App.Notices;

/// <summary>
/// One notice as the window's template reads it: its words, its tone and four booleans that pick which
/// declared buttons show. Buttons are declared in the template with fixed ranks, so the notice only says
/// which of them belong to it.
/// </summary>
public sealed class RobloxNoticeViewModel
{
    internal RobloxNoticeViewModel(RobloxNotice notice)
    {
        Text = notice.Text;
        Tone = notice.Tone;
        ShowContestedActions = notice.Kind == RobloxNoticeKind.Contested;
        ShowKnownIssueActions = notice.Kind == RobloxNoticeKind.KnownIssue;
        ShowStatusLink = notice.Kind == RobloxNoticeKind.Outage && notice.WithLink;
        ShowWhatChanged = notice.Kind == RobloxNoticeKind.Update && notice.WithLink;
    }

    public string Text { get; }

    public RobloxNoticeTone Tone { get; }

    public bool ShowContestedActions { get; }

    public bool ShowKnownIssueActions { get; }

    public bool ShowStatusLink { get; }

    public bool ShowWhatChanged { get; }
}

/// <summary>
/// The Roblox line: the top notice, the rest behind "+N more", and whether the rest is open. UI thread only.
/// </summary>
public sealed class RobloxLineViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public RobloxNoticeViewModel? Top { get; private set; }

    public IReadOnlyList<RobloxNoticeViewModel> More { get; private set; } = [];

    public bool HasNotice => Top is not null;

    public int MoreCount => More.Count;

    /// <summary>"+2 more". Empty when there is nothing behind the top notice.</summary>
    public string MoreLabel => MoreCount == 0 ? string.Empty : Loc.Format("RobloxLine_More", MoreCount);

    /// <summary>What a screen reader says for the toggle: "2 more Roblox notices".</summary>
    public string MoreSpokenName => MoreCount == 0 ? string.Empty : Loc.Plural("RobloxLine_More_Name", MoreCount);

    /// <summary>The inline list is open. Falls back to closed when nothing is behind the top notice.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            Raise(nameof(IsExpanded));
        }
    }

    internal void Apply(RobloxLineState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Top = state.Top is null ? null : new RobloxNoticeViewModel(state.Top);
        More = state.More.Select(n => new RobloxNoticeViewModel(n)).ToList();
        if (More.Count == 0)
        {
            IsExpanded = false;
        }

        Raise(string.Empty);
    }

    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
