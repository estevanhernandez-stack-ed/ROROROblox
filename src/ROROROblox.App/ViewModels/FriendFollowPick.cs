using ROROROblox.Core;

namespace ROROROblox.App.ViewModels;

/// <summary>
/// What the friend-follow picker hands back (F-106): the launch target plus the presence snapshot
/// and friend name the follow guard re-checks — <see cref="MainViewModel.EvaluateFollow"/> owns
/// the launch decision, so the picker must surface everything that rule reads.
/// </summary>
internal sealed record FriendFollowPick(
    Core.LaunchTarget Target,
    UserPresence? Presence,
    string? FriendName);
