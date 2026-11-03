using System.Net.Http.Headers;
using ROROROblox.Core;

namespace ROROROblox.EndpointProbe;

/// <summary>
/// The tool's command line, behind a <see cref="TextWriter"/> pair so a test (and the self-test) can
/// capture exactly what a user would see on stdout and assert a cookie is not in it.
/// <para>
/// WHAT THIS NEVER DOES. It never prints a cookie, under any flag; there is no flag that would. It
/// never writes <c>accounts.dat</c> or calls a setter on the vault it was pointed at: <c>--list</c> and
/// the probe modes only read. It makes no network request unless <c>--live</c> is given, and
/// <c>--offline</c> and <c>--selftest</c> structurally cannot, because they hand the prober a stub
/// handler instead of a socket.
/// </para>
/// </summary>
public static class ProbeCli
{
    public const string FakeCookie = "FAKE-COOKIE-FOR-PROBE-TEST-ONLY";
    public const long FakeUserId = 1234567890L;

    public const string Usage = """
        EndpointProbe: record what Roblox says to the three requests the Library cycle depends on.

          --list                        List saved accounts by local name and id. Reads the vault, prints no cookie.
          --account <name|id>           The account to probe (its local name, or its id from --list).
          --live                        REQUIRED to touch the network. Without it, nothing is sent.
          --offline                     Swap in a stub handler with canned responses. No network, ever.
          --selftest                    Build a temp fake vault, run --offline against it, check the output
                                        for the fake cookie, and delete the vault. Writes offline-selftest-*.md.
          --data-dir <path>             Folder holding accounts.dat. Default: %LOCALAPPDATA%\ROROROblox.
          --out <dir>                   Report folder. Default: docs/cycles/2026-11-01-account-library/probes
                                        under the repo this tool is run from.
          --help                        This text.

        The live run records, for the chosen account: the auth-ticket POST (with its CSRF dance), the
        presence POST and validate-membership: status, every rblx-* header, and the body, into a redacted
        markdown report. Live measurements are the builder's, at checkpoint C1. A live run mints a real
        auth ticket on a successful ticket POST (nothing launches); run it deliberately.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct = default)
    {
        string? account = null, dataDir = null, outDir = null;
        bool list = false, live = false, offline = false, selftest = false, help = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--list": list = true; break;
                case "--live": live = true; break;
                case "--offline": offline = true; break;
                case "--selftest": selftest = true; break;
                case "--help" or "-h" or "/?": help = true; break;
                case "--account" when i + 1 < args.Length: account = args[++i]; break;
                case "--data-dir" when i + 1 < args.Length: dataDir = args[++i]; break;
                case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
                default:
                    await stderr.WriteLineAsync($"Unknown or incomplete argument: {args[i]}").ConfigureAwait(false);
                    await stderr.WriteLineAsync(Usage).ConfigureAwait(false);
                    return 2;
            }
        }

        if (help || args.Length == 0)
        {
            await stdout.WriteLineAsync(Usage).ConfigureAwait(false);
            return args.Length == 0 ? 2 : 0;
        }
        if (live && (offline || selftest))
        {
            await stderr.WriteLineAsync("--live cannot be combined with --offline or --selftest.").ConfigureAwait(false);
            return 2;
        }

        if (selftest) return await SelfTestAsync(outDir, stdout, stderr, ct).ConfigureAwait(false);

        var path = Path.Combine(dataDir ?? DefaultDataDir(), "accounts.dat");
        if (list)
        {
            return await ListAsync(path, stdout, stderr).ConfigureAwait(false);
        }

        if (account is null)
        {
            await stderr.WriteLineAsync("Pick an account with --account <name|id> (see --list).").ConfigureAwait(false);
            return 2;
        }
        if (!live && !offline)
        {
            await stderr.WriteLineAsync("Nothing sent. Add --live to make the real requests (builder's checkpoint), or --offline for the stub.").ConfigureAwait(false);
            return 2;
        }

        return await ProbeAsync(path, account, offline, outDir, null, stdout, stderr, ct).ConfigureAwait(false);
    }

    private static string DefaultDataDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ROROROblox");

    // ---- --list ------------------------------------------------------------------------------

    private static async Task<int> ListAsync(string accountsPath, TextWriter stdout, TextWriter stderr)
    {
        if (!File.Exists(accountsPath))
        {
            await stderr.WriteLineAsync($"No vault at {Path.GetDirectoryName(accountsPath)} (no accounts.dat).").ConfigureAwait(false);
            return 1;
        }

        // Read-only by construction: ListAsync loads and decrypts in memory and writes nothing. The
        // projection below keeps the cookie out of reach of the printing code: there is no cookie on
        // an Account, and this method never calls RetrieveCookieAsync.
        var accounts = await new AccountStore(accountsPath).ListAsync().ConfigureAwait(false);
        await stdout.WriteLineAsync($"{accounts.Count} saved account(s):").ConfigureAwait(false);
        foreach (var a in accounts)
        {
            await stdout.WriteLineAsync($"  {LocalNameOf(a),-28} {a.Id}{(a.IsMain ? "  (main)" : string.Empty)}").ConfigureAwait(false);
        }
        return 0;
    }

    private static string LocalNameOf(Account a) => string.IsNullOrWhiteSpace(a.LocalName) ? a.DisplayName : a.LocalName!;

    // ---- the probe ---------------------------------------------------------------------------

    private static async Task<int> ProbeAsync(
        string accountsPath, string selector, bool offline, string? outDir, string? fileNameOverride,
        TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (!File.Exists(accountsPath))
        {
            await stderr.WriteLineAsync($"No vault at {Path.GetDirectoryName(accountsPath)} (no accounts.dat).").ConfigureAwait(false);
            return 1;
        }

        var store = new AccountStore(accountsPath);
        var accounts = await store.ListAsync().ConfigureAwait(false);
        var matches = accounts.Where(a =>
            string.Equals(a.Id.ToString(), selector, StringComparison.OrdinalIgnoreCase)
            || string.Equals(LocalNameOf(a), selector, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
        {
            await stderr.WriteLineAsync(matches.Count == 0
                ? $"No account matches '{selector}'. Run --list."
                : $"'{selector}' matches {matches.Count} accounts; use the id from --list.").ConfigureAwait(false);
            return 1;
        }
        var chosen = matches[0];
        var name = LocalNameOf(chosen);
        // Reports land in a public repo, and a set of them would tie every alt's Roblox name to the
        // main's. The report and its file carry the local id's prefix; the name stays on this console.
        var label = $"account {chosen.Id.ToString()[..8]}";

        var reportDir = outDir ?? DefaultReportDir();
        if (reportDir is null)
        {
            await stderr.WriteLineAsync("Could not find the repo (ROROROblox.slnx) above here; pass --out <dir>.").ConfigureAwait(false);
            return 2;
        }

        // The cookie leaves the vault here and goes straight into the prober, which attaches it to
        // request headers and registers it with the redactor. It is not assigned to anything printed.
        var cookie = await store.RetrieveCookieAsync(chosen.Id).ConfigureAwait(false);

        var redactor = new Redactor(chosen.RobloxUserId);
        HttpMessageHandler handler = offline
            ? new OfflineStub(chosen.RobloxUserId)
            : new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false };
        using var http = new HttpClient(handler, disposeHandler: true);
        var version = typeof(RobloxApi).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));

        await stdout.WriteLineAsync($"Probing '{name}' ({(offline ? "offline stub, no network" : "LIVE")}).").ConfigureAwait(false);
        var prober = new Prober(http, cookie, chosen.RobloxUserId, redactor,
            pace: offline ? TimeSpan.Zero : TimeSpan.FromMilliseconds(2500),
            log: line => stdout.WriteLine(line));
        var steps = await prober.RunAsync(ct).ConfigureAwait(false);

        var at = DateTimeOffset.UtcNow;
        var report = ReportWriter.Render(label, offline ? "offline (stub handler, no network request was made)" : "live (real requests to roblox.com)", at, steps, redactor);

        Directory.CreateDirectory(reportDir);
        var file = fileNameOverride ?? $"{(offline ? "offline-" : string.Empty)}{at:yyyyMMdd-HHmmss}-{Redactor.Slug(label)}.md";
        var reportPath = Path.Combine(reportDir, file);
        await File.WriteAllTextAsync(reportPath, report, ct).ConfigureAwait(false);
        await stdout.WriteLineAsync($"Report: {reportPath}").ConfigureAwait(false);
        return 0;
    }

    private static string? DefaultReportDir()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ROROROblox.slnx")))
                {
                    return Path.Combine(dir.FullName, "docs", "cycles", "2026-11-01-account-library", "probes");
                }
            }
        }
        return null;
    }

    // ---- --selftest --------------------------------------------------------------------------

    private static async Task<int> SelfTestAsync(string? outDir, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var vaultDir = Path.Combine(Path.GetTempPath(), "endpointprobe-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(vaultDir);
        var captured = new StringWriter();
        try
        {
            // The vault is OURS (a temp dir), so writing it is fine; the guarantee that matters is that
            // the user's real vault is never written, and this path never opens it.
            var store = new AccountStore(Path.Combine(vaultDir, "accounts.dat"));
            var added = await store.AddAsync("Selftest Alt", string.Empty, FakeCookie).ConfigureAwait(false);
            await store.UpdateRobloxUserIdAsync(added.Id, FakeUserId).ConfigureAwait(false);

            var reportDir = outDir ?? DefaultReportDir();
            if (reportDir is null)
            {
                await stderr.WriteLineAsync("Could not find the repo (ROROROblox.slnx) above here; pass --out <dir>.").ConfigureAwait(false);
                return 2;
            }

            var fileName = $"offline-selftest-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.md";
            var code = await ProbeAsync(Path.Combine(vaultDir, "accounts.dat"), added.Id.ToString(), offline: true,
                reportDir, fileName, captured, stderr, ct).ConfigureAwait(false);
            if (code != 0) return code;

            var report = await File.ReadAllTextAsync(Path.Combine(reportDir, fileName), ct).ConfigureAwait(false);
            var stdoutText = captured.ToString();

            string[] forbidden =
            [
                FakeCookie, OfflineStub.StubCsrfToken, OfflineStub.StubSetCookie,
                FakeUserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ];
            var leaks = forbidden
                .SelectMany(f => new[] { (Where: "stdout", f, Hit: stdoutText.Contains(f, StringComparison.Ordinal)),
                                         (Where: "report", f, Hit: report.Contains(f, StringComparison.Ordinal)) })
                .Where(x => x.Hit)
                .ToList();

            await stdout.WriteAsync(stdoutText).ConfigureAwait(false);
            if (leaks.Count > 0)
            {
                foreach (var leak in leaks)
                {
                    await stderr.WriteLineAsync($"SELFTEST FAILED: a forbidden value reached {leak.Where}.").ConfigureAwait(false);
                }
                return 1;
            }
            if (!report.Contains("captcha", StringComparison.Ordinal))
            {
                await stderr.WriteLineAsync("SELFTEST FAILED: the stub's captcha challenge did not reach the report.").ConfigureAwait(false);
                return 1;
            }
            await stdout.WriteLineAsync("Selftest passed: the fake cookie, the stub's CSRF token and Set-Cookie, and the user id appear in neither stdout nor the report.").ConfigureAwait(false);
            return 0;
        }
        finally
        {
            try { Directory.Delete(vaultDir, recursive: true); } catch (IOException) { }
        }
    }
}
