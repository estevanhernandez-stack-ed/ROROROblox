using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>State, not a setting (spec §3): a small file of ids, pruned to what the feed still carries.</summary>
public sealed class KnownIssuesDismissalsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "rororo-dismissed-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void NoFileMeansNothingIsDismissed()
    {
        Assert.Empty(new KnownIssuesDismissals(_path).Load());
    }

    [Fact]
    public void ADismissalSurvivesARestart()
    {
        new KnownIssuesDismissals(_path).Dismiss(["a"], liveIds: ["a", "b"]);

        Assert.Equal(["a"], new KnownIssuesDismissals(_path).Load().Order().ToArray());
    }

    [Fact]
    public void IdsTheFeedNoLongerCarriesArePrunedOnTheNextWrite()
    {
        var store = new KnownIssuesDismissals(_path);
        store.Dismiss(["old"], liveIds: ["old"]);

        var now = store.Dismiss(["new"], liveIds: ["new"]);

        Assert.Equal(["new"], now.Order().ToArray());
        Assert.Equal(["new"], new KnownIssuesDismissals(_path).Load().Order().ToArray());
    }

    /// <summary>Review Focus 5: a hand-edited or half-written file costs a repeated notice, never a crash.</summary>
    [Fact]
    public void ACorruptFileMeansNothingIsDismissed()
    {
        File.WriteAllText(_path, "{ this is not json");

        var store = new KnownIssuesDismissals(_path);

        Assert.Empty(store.Load());
        Assert.Equal(["a"], store.Dismiss(["a"], liveIds: ["a"]).Order().ToArray());
    }
}
