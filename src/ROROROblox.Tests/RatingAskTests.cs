using System.IO;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// The one-time rating ask (2026-11-10). 6,501 installs and 20 ratings, two of them 1-star theft
/// allegations, and nothing in the app ever invited a rating. The rule: a Store install only,
/// after a launch has attached on three separate days, asked once, and never again after either
/// answer.
/// </summary>
public sealed class RatingAskTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rororo-rating-ask-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero));

    public RatingAskTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private RatingAsk New(InstallChannel channel = InstallChannel.Store) => new(_dir, channel, _time);

    private string FilePath => Path.Combine(_dir, RatingAsk.FileName);

    private void GoodDays(RatingAsk ask, int days)
    {
        for (var i = 0; i < days; i++)
        {
            ask.RecordSuccessfulLaunch();
            _time.Advance(TimeSpan.FromDays(1));
        }
    }

    [Fact]
    public void AFreshInstall_DoesNotAsk()
    {
        Assert.False(New().ShouldAsk);
    }

    [Fact]
    public void TwoGoodDays_AreNotEnough_AndTheThirdIs()
    {
        var ask = New();

        GoodDays(ask, 2);
        Assert.False(ask.ShouldAsk);

        ask.RecordSuccessfulLaunch();
        Assert.True(ask.ShouldAsk);
    }

    [Fact]
    public void ManyLaunchesOnOneDay_CountAsOneDay()
    {
        var ask = New();

        for (var i = 0; i < 12; i++)
        {
            ask.RecordSuccessfulLaunch();
            _time.Advance(TimeSpan.FromMinutes(20));
        }

        Assert.False(ask.ShouldAsk);
    }

    [Theory]
    [InlineData(InstallChannel.DirectDownload)]
    [InlineData(InstallChannel.Sideload)]
    [InlineData(InstallChannel.Dev)]
    public void OnlyAStoreInstallIsEverAsked_AndTheOthersWriteNothing(InstallChannel channel)
    {
        // The ask opens the Store's review page, which only means something to a Store install.
        var ask = New(channel);

        GoodDays(ask, 5);

        Assert.False(ask.ShouldAsk);
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData(RatingAnswer.Rated)]
    [InlineData(RatingAnswer.NotNow)]
    public void EitherAnswer_EndsTheAskForGood_AcrossRestarts(RatingAnswer answer)
    {
        var first = New();
        GoodDays(first, 3);
        Assert.True(first.ShouldAsk);

        first.MarkAnswered(answer);
        Assert.False(first.ShouldAsk);

        // A restart, and more good days after it: still never again.
        var next = New();
        GoodDays(next, 10);
        Assert.False(next.ShouldAsk);
    }

    [Fact]
    public void GoodDays_SurviveARestart()
    {
        GoodDays(New(), 2);

        var next = New();
        Assert.False(next.ShouldAsk);
        next.RecordSuccessfulLaunch();
        Assert.True(next.ShouldAsk);
    }

    [Fact]
    public void ACorruptFile_StartsOver_AndNeverThrows()
    {
        File.WriteAllText(FilePath, "{ this is not json");

        var ask = New();

        Assert.False(ask.ShouldAsk);
        GoodDays(ask, 3);
        Assert.True(ask.ShouldAsk);
    }

    [Fact]
    public void AFolderThatCannotBeWritten_NeverThrows()
    {
        // The data folder vanishing mid-session must not turn a launch into an exception: this is
        // called from the launch-attached handler.
        var ask = New();
        Directory.Delete(_dir, recursive: true);
        File.WriteAllText(_dir, "a file where the folder was");
        try
        {
            ask.RecordSuccessfulLaunch();
            ask.MarkAnswered(RatingAnswer.NotNow);
        }
        finally
        {
            File.Delete(_dir);
            Directory.CreateDirectory(_dir);
        }
    }

    [Fact]
    public void TheReviewLink_OpensTheStoreReviewPageForThisProduct()
    {
        Assert.Equal("ms-windows-store://review/?ProductId=9NMJCS390KWB", RatingAsk.StoreReviewUri);
    }
}
