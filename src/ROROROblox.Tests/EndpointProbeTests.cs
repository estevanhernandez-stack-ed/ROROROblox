using System.Security.Cryptography;
using System.Text;
using ROROROblox.Core;
using ROROROblox.EndpointProbe;

namespace ROROROblox.Tests;

/// <summary>
/// tools/EndpointProbe (v1.36 item 2). The guarantee under test is "never prints a cookie under any
/// flag", so it is asserted on the captured stdout, stderr and the written report, against a fake
/// vault holding an obviously fake cookie. No test here (or anywhere in the probe) makes a network
/// request: the live path is the builder's, at checkpoint C1.
/// </summary>
public class EndpointProbeTests : IDisposable
{
    private const string Cookie = ProbeCli.FakeCookie;
    private const long UserId = 7654321987L;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "endpointprobe-tests-" + Guid.NewGuid().ToString("N"));

    public EndpointProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<(string DataDir, Account Account)> FakeVaultAsync(string displayName = "Probe Alt", string? localName = null)
    {
        var dir = Path.Combine(_root, "vault");
        Directory.CreateDirectory(dir);
        var store = new AccountStore(Path.Combine(dir, "accounts.dat"));
        var added = await store.AddAsync(displayName, string.Empty, Cookie);
        await store.UpdateRobloxUserIdAsync(added.Id, UserId);
        if (localName is not null) await store.UpdateLocalNameAsync(added.Id, localName);
        return (dir, added);
    }

    private async Task<(int Code, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = await ProbeCli.RunAsync(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));

    [Fact]
    public async Task List_PrintsLocalNamesAndIds_AndNoCookie_AndLeavesTheVaultUntouched()
    {
        var (dir, account) = await FakeVaultAsync(localName: "My Local Name");
        var file = Path.Combine(dir, "accounts.dat");
        var before = (Hash(file), File.GetLastWriteTimeUtc(file));

        var (code, stdout, stderr) = await RunAsync("--list", "--data-dir", dir);

        Assert.Equal(0, code);
        Assert.Contains("My Local Name", stdout);
        Assert.Contains(account.Id.ToString(), stdout);
        Assert.DoesNotContain(Cookie, stdout + stderr);
        Assert.DoesNotContain(UserId.ToString(), stdout + stderr);
        Assert.DoesNotContain("WARNING", stdout + stderr);
        Assert.Equal(before, (Hash(file), File.GetLastWriteTimeUtc(file)));
    }

    [Fact]
    public async Task OfflineRun_WritesARedactedReport_AndNoFakeSecretReachesStdoutOrTheReport()
    {
        var (dir, account) = await FakeVaultAsync(localName: "Captcha Alt");
        var file = Path.Combine(dir, "accounts.dat");
        var before = Hash(file);
        var outDir = Path.Combine(_root, "probes");

        var (code, stdout, stderr) = await RunAsync("--offline", "--account", "Captcha Alt", "--data-dir", dir, "--out", outDir);

        Assert.Equal(0, code);
        var report = File.ReadAllText(Assert.Single(Directory.GetFiles(outDir, "*.md")));
        var everything = stdout + stderr + report;

        foreach (var forbidden in new[] { Cookie, OfflineStub.StubCsrfToken, OfflineStub.StubSetCookie, UserId.ToString() })
        {
            Assert.DoesNotContain(forbidden, everything);
        }
        Assert.DoesNotContain("path=/", report); // the stub's Set-Cookie header, whole

        // What the report is FOR: the challenge type, the id and the decoded metadata are all there.
        Assert.Contains("`rblx-challenge-type`: `captcha`", report);
        Assert.Contains("rblx-challenge-id", report);
        Assert.Contains("\"unifiedCaptchaId\":\"offline-stub\"", report);
        Assert.Contains("validate-membership GET", report);
        Assert.Contains("<uid>", report);

        // Reports land in a public repo: no account name in the report or its file name (it would tie
        // every alt to the main), and no server id (with the place id beside it, enough to join).
        var reportFile = Assert.Single(Directory.GetFiles(outDir, "*.md"));
        Assert.Contains($"# Endpoint probe: account {account.Id.ToString()[..8]}", report);
        Assert.DoesNotContain("Captcha Alt", report);
        Assert.DoesNotContain("captcha-alt", Path.GetFileName(reportFile));
        Assert.Contains(account.Id.ToString()[..8], Path.GetFileName(reportFile));
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", report);
        Assert.Contains("\"gameId\":\"<gameId>\"", report);

        // The vault was only read.
        Assert.Equal(before, Hash(file));
        Assert.Contains(account.Id.ToString(), (await RunAsync("--list", "--data-dir", dir)).Stdout);
    }

    [Fact]
    public async Task OfflineRun_AcceptsTheIdInPlaceOfTheName()
    {
        var (dir, account) = await FakeVaultAsync();
        var outDir = Path.Combine(_root, "probes-by-id");

        var (code, _, _) = await RunAsync("--offline", "--account", account.Id.ToString(), "--data-dir", dir, "--out", outDir);

        Assert.Equal(0, code);
        Assert.Single(Directory.GetFiles(outDir, "*.md"));
    }

    [Fact]
    public async Task WithoutLiveOrOffline_NothingIsSent_AndItSaysSo()
    {
        var (dir, _) = await FakeVaultAsync();

        var (code, stdout, stderr) = await RunAsync("--account", "Probe Alt", "--data-dir", dir, "--out", Path.Combine(_root, "never"));

        Assert.Equal(2, code);
        Assert.Contains("Nothing sent", stderr);
        Assert.False(Directory.Exists(Path.Combine(_root, "never")));
        Assert.DoesNotContain(Cookie, stdout + stderr);
    }

    [Fact]
    public async Task LiveCannotBeCombinedWithOffline()
    {
        var (code, _, stderr) = await RunAsync("--live", "--offline", "--account", "x");

        Assert.Equal(2, code);
        Assert.Contains("cannot be combined", stderr);
    }

    [Fact]
    public async Task UnknownAccount_FailsWithoutWritingAReport()
    {
        var (dir, _) = await FakeVaultAsync();
        var outDir = Path.Combine(_root, "probes-none");

        var (code, _, stderr) = await RunAsync("--offline", "--account", "nobody", "--data-dir", dir, "--out", outDir);

        Assert.Equal(1, code);
        Assert.Contains("No account matches", stderr);
        Assert.False(Directory.Exists(outDir));
    }

    [Fact]
    public async Task Selftest_PassesAndWritesAnOfflineSelftestReportOnly()
    {
        var outDir = Path.Combine(_root, "probes-selftest");

        var (code, stdout, stderr) = await RunAsync("--selftest", "--out", outDir);

        Assert.True(code == 0, stdout + stderr);
        var report = Assert.Single(Directory.GetFiles(outDir, "*.md"));
        Assert.StartsWith("offline-selftest-", Path.GetFileName(report));
        Assert.DoesNotContain(Cookie, stdout + stderr + File.ReadAllText(report));
    }

    // ---- Redactor ----------------------------------------------------------------------------

    [Fact]
    public void Redactor_DecodesChallengeMetadata_AndRemovesNumericIds()
    {
        var redactor = new Redactor(UserId);
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{{\"userId\":\"{UserId}\",\"browserTrackerId\":123456789012,\"challengeId\":\"abc\",\"n\":12}}"));

        var decoded = redactor.DecodeChallengeMetadata(raw);

        Assert.DoesNotContain(UserId.ToString(), decoded);
        Assert.DoesNotContain("123456789012", decoded);
        Assert.Contains("\"challengeId\":\"abc\"", decoded);
        Assert.Contains("\"n\":12", decoded); // a short number is not an id
        Assert.DoesNotContain(raw, decoded);
    }

    [Fact]
    public void Redactor_WithholdsMetadataThatIsNotBase64()
    {
        var decoded = new Redactor(null).DecodeChallengeMetadata("%%% not base64 %%%");

        Assert.Contains("withheld", decoded);
    }

    [Fact]
    public void Redactor_ScrubsRegisteredSecrets_UserIdFields_AndTheSessionCookieShape()
    {
        var redactor = new Redactor(UserId);
        redactor.AddSecret("TOKEN-VALUE-1234");
        var cookieShaped = "_|" + "WARNING" + ":-FAKE-SHAPE-ONLY-NOT-A-REAL-SESSION";

        var scrubbed = redactor.Scrub(
            $"tok TOKEN-VALUE-1234 and {{\"userId\": {UserId}}} and /users/{UserId}/x and {cookieShaped} end");

        Assert.DoesNotContain("TOKEN-VALUE-1234", scrubbed);
        Assert.DoesNotContain(UserId.ToString(), scrubbed);
        Assert.DoesNotContain("FAKE-SHAPE-ONLY", scrubbed);
        Assert.Contains("<uid>", scrubbed);
        Assert.Contains("<withheld>", scrubbed);
    }

    [Fact]
    public void Redactor_ScrubsServerIds_WhatTheLivePresenceBodyCarries()
    {
        // The shape the first live run returned (2026-11-02), with a made-up server id.
        var body = "{\"userPresences\":[{\"userPresenceType\":2,\"placeId\":85920622393590,"
                 + "\"gameId\":\"a1b2c3d4-0000-4000-8000-123456789abc\",\"universeId\":3317771874,\"userId\":1}]}";

        var scrubbed = new Redactor(null).Scrub(body + " {\"jobId\": \"another-server\"}");

        Assert.DoesNotContain("a1b2c3d4-0000-4000-8000-123456789abc", scrubbed);
        Assert.DoesNotContain("another-server", scrubbed);
        Assert.Contains("\"gameId\":\"<gameId>\"", scrubbed);
        Assert.Contains("\"jobId\": \"<gameId>\"", scrubbed);
        Assert.Contains("85920622393590", scrubbed); // the place id stays: it names the game, not the server
    }

    [Fact]
    public void ProbeSource_NeverWritesRequestHeadersOrSetCookie()
    {
        // The report is built from ProbeStep, which has no request-header field and records response
        // headers by the rblx- allow-list. Pin the allow-list: a change that records "all headers"
        // would put Set-Cookie into the report path.
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var prober = File.ReadAllText(Path.Combine(root!, "tools", "EndpointProbe", "Prober.cs"));

        Assert.Contains("StartsWith(\"rblx-\"", prober);
        var stepRecord = prober[prober.IndexOf("public sealed record ProbeStep", StringComparison.Ordinal)..];
        stepRecord = stepRecord[..stepRecord.IndexOf(");", StringComparison.Ordinal)];
        Assert.DoesNotContain("Cookie", stepRecord, StringComparison.OrdinalIgnoreCase);
    }
}
