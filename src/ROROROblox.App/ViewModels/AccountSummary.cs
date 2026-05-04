using System.ComponentModel;
using System.Runtime.CompilerServices;
using ROROROblox.Core;

namespace ROROROblox.App.ViewModels;

/// <summary>
/// Per-account view model. Wraps a Core <see cref="Account"/> with mutable UI state
/// (session-expired badge, in-flight launch flag) so the row can flip without a re-fetch.
/// </summary>
public sealed class AccountSummary : INotifyPropertyChanged
{
    private bool _sessionExpired;
    private bool _isLaunching;
    private string _statusText = string.Empty;

    public AccountSummary(Account account)
    {
        Id = account.Id;
        DisplayName = account.DisplayName;
        AvatarUrl = account.AvatarUrl;
        LastLaunchedAt = account.LastLaunchedAt;
    }

    public Guid Id { get; }
    public string DisplayName { get; }
    public string AvatarUrl { get; }
    public DateTimeOffset? LastLaunchedAt { get; private set; }

    public bool SessionExpired
    {
        get => _sessionExpired;
        set => SetField(ref _sessionExpired, value);
    }

    public bool IsLaunching
    {
        get => _isLaunching;
        set => SetField(ref _isLaunching, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public void StampLaunched(DateTimeOffset at)
    {
        LastLaunchedAt = at;
        OnPropertyChanged(nameof(LastLaunchedAt));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
