using System.Collections.ObjectModel;
using ROROROblox.App.Preferences;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// I6: Settings is a non-modal shell page (F-013), so an account's auto-rejoin can change (the row
/// menu, or a pause) while the page is open. The alerts status line counts AutoRejoinPaused only
/// while some account has auto-rejoin on, so the page has to hear about every change that moves
/// that answer. The page's own refresh needs a live window; this is the part that doesn't.
/// </summary>
public class AutoRejoinUsageWatcherTests
{
    private static AccountSummary Row(string name) => new(new Account(
        Guid.NewGuid(), name, "", DateTimeOffset.UtcNow, null));

    [Fact]
    public void AnAccountsAutoRejoinFlip_RaisesTheCallback()
    {
        var a = Row("A");
        var rows = new ObservableCollection<AccountSummary> { a };
        var calls = 0;
        using var watcher = new AutoRejoinUsageWatcher(rows, () => calls++);

        a.AutoRejoin = true;
        Assert.Equal(1, calls);
        a.AutoRejoin = false;
        Assert.Equal(2, calls);
    }

    [Fact]
    public void OtherPropertyChanges_DoNotRaiseIt()
    {
        var a = Row("A");
        var rows = new ObservableCollection<AccountSummary> { a };
        var calls = 0;
        using var watcher = new AutoRejoinUsageWatcher(rows, () => calls++);

        a.JoinViaFriend = true;
        a.StatusText = "x";
        Assert.Equal(0, calls);
    }

    [Fact]
    public void RowsAddedLater_AreWatched_AndRemovedRowsAreNot()
    {
        var rows = new ObservableCollection<AccountSummary>();
        var calls = 0;
        using var watcher = new AutoRejoinUsageWatcher(rows, () => calls++);

        var b = Row("B");
        rows.Add(b);            // the set of accounts changed: recount
        Assert.Equal(1, calls);
        b.AutoRejoin = true;
        Assert.Equal(2, calls);

        rows.Remove(b);         // an opted-in account removed changes the answer too
        Assert.Equal(3, calls);
        b.AutoRejoin = false;   // no longer a row: ignored
        Assert.Equal(3, calls);
    }

    [Fact]
    public void AfterDispose_NothingIsHeard()
    {
        var a = Row("A");
        var rows = new ObservableCollection<AccountSummary> { a };
        var calls = 0;
        var watcher = new AutoRejoinUsageWatcher(rows, () => calls++);
        watcher.Dispose();

        a.AutoRejoin = true;
        rows.Add(Row("B"));
        Assert.Equal(0, calls);
    }
}
