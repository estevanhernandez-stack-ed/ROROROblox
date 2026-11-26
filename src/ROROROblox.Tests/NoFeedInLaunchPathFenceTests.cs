using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// No launch path waits on a feed (v1.41 spec section 9). The coordinator, the status client and the pulse
/// service are told things from a launch (the gate reports what it saw, a failed launch asks for an early
/// read) and never asked: a launch that awaited one would wait on a third party's page to open a game.
/// A source fence, because the property is "this call is not an await", which no runtime test can prove.
/// <para>
/// It scans every method of the view model whose name says launch, batch, pre-warm, squad, recycle, rejoin
/// or follow, and the whole of <c>RobloxLauncher.cs</c>. The positive controls pin that the scan reached the
/// two bodies where the feeds ARE touched, so a refactor that moves them cannot leave the fence blind.
/// </para>
/// </summary>
public class NoFeedInLaunchPathFenceTests
{
    private static readonly Regex LaunchMethod = new(
        @"(?m)^[ \t]+(?:private|internal|public|protected)(?: static)?(?: async)?[ \t]+[\w<>\[\]?,. ]+?[ \t]+(\w*(?:Launch|Batch|PreWarm|Squad|Recycle|Rejoin|Follow)\w*)\(",
        RegexOptions.Compiled);

    // An await whose operand, anywhere up to the statement's end, names a feed.
    private static readonly Regex AwaitOnAFeed = new(
        @"await\b[^;]*?(?<![A-Za-z0-9])_?(?:robloxfeeds|gamepulse|irobloxstatusapi|igamepulse)\w*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string Code(params string[] relative)
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(new[] { root!, "src" }.Concat(relative).ToArray());
        Assert.True(File.Exists(path), $"{path} is gone; this fence is blind.");
        return string.Join('\n', SettingsReachabilityTests.WithoutComments(path, File.ReadAllLines(path)));
    }

    /// <summary>Every launch-named method of <paramref name="code"/>, name to body (signature included).</summary>
    internal static Dictionary<string, string> LaunchMethods(string code)
    {
        var found = new Dictionary<string, string>();
        foreach (Match m in LaunchMethod.Matches(code))
        {
            var open = code.IndexOf('{', m.Index);
            var arrow = code.IndexOf("=>", m.Index, StringComparison.Ordinal);
            int end;
            if (arrow >= 0 && (open < 0 || arrow < open))
            {
                end = code.IndexOf(';', arrow);
            }
            else
            {
                var depth = 0;
                end = open;
                for (; end < code.Length; end++)
                {
                    if (code[end] == '{') depth++;
                    else if (code[end] == '}' && --depth == 0) break;
                }
            }

            Assert.True(end > m.Index, $"couldn't find the end of {m.Groups[1].Value}.");
            found[m.Groups[1].Value] = code[m.Index..(end + 1)];
        }

        return found;
    }

    [Fact]
    public void NoLaunchMethodOfTheViewModel_AwaitsAFeed()
    {
        var methods = LaunchMethods(Code("ROROROblox.App", "ViewModels", "MainViewModel.cs"));

        // The floor: the methods the spec means are all in the scan, and each body was read to its end.
        Assert.True(methods.Count >= 10, $"only {methods.Count} launch-named methods found; the scan is blind.");
        foreach (var name in new[] { "LaunchAccountAsync", "DispatchBatchAsync", "DecidePreWarmAsync", "WaitForPreWarmAsync", "ReleaseBatchAsync", "SquadLaunchAsync", "RecycleAccountAsync", "LaunchAllCoreAsync" })
        {
            Assert.True(methods.ContainsKey(name), $"{name} is not in the scan.");
        }

        Assert.Contains("OnPropertyChanged(nameof(HasCompactRows))", methods["LaunchAccountAsync"], StringComparison.Ordinal);
        Assert.Contains("return PreWarmDecision.LaunchAllNow;", methods["DecidePreWarmAsync"], StringComparison.Ordinal);

        // The two places a launch touches the feeds, as plain calls.
        Assert.Contains("RequestEarlyStatusRead()", methods["LaunchAccountAsync"], StringComparison.Ordinal);
        Assert.Contains("ReportUpdateState(state)", methods["DecidePreWarmAsync"], StringComparison.Ordinal);

        var offenders = methods
            .Where(kv => AwaitOnAFeed.IsMatch(kv.Value))
            .Select(kv => kv.Key)
            .ToList();
        Assert.True(offenders.Count == 0,
            $"A launch method awaits a feed: {string.Join(", ", offenders)}. A launch never waits on Roblox's status "
            + "page, the update coordinator or the game pulse; call it without await, or move the read off the launch path.");
    }

    [Fact]
    public void RobloxLauncher_ReferencesNoneOfTheThreeFeedTypes()
    {
        var code = Code("ROROROblox.Core", "RobloxLauncher.cs");

        foreach (var type in new[] { "RobloxFeeds", "IRobloxStatusApi", "IGamePulseService" })
        {
            Assert.DoesNotContain(type, code, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("await RobloxFeeds.SomethingAsync();")]
    [InlineData("var x = await _robloxFeeds!.ReadAsync().ConfigureAwait(true);")]
    [InlineData("await _gamePulse\n    .AskAsync();")]
    [InlineData("await Task.WhenAll(a, IGamePulseService.Foo());")]
    public void TheAwaitPattern_CatchesTheShapesItIsFor(string statement)
        => Assert.Matches(AwaitOnAFeed, statement);

    [Theory]
    [InlineData("_robloxFeeds?.RequestEarlyStatusRead();")]
    [InlineData("await _updateProbe.GetUpdateStateAsync().ConfigureAwait(true);")]
    [InlineData("await Task.Delay(100); _robloxFeeds?.ReportUpdateState(state);")]
    public void TheAwaitPattern_LeavesPlainCallsAlone(string statement)
        => Assert.DoesNotMatch(AwaitOnAFeed, statement);
}
