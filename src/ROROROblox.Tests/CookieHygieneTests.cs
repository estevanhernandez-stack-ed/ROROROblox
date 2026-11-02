using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Transport;
using ROROROblox.EndpointProbe;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 3 follow-up, from the dpapi-cookie-blast-radius audit. A session cookie must never reach
/// <c>Headers.Add</c> with a control character in it (the <see cref="FormatException"/> it throws echoes
/// the whole value), and no record that holds a plaintext cookie may print it from a synthesized
/// <c>ToString</c>. Every cookie here is an obvious fake; the sentinel text is what the assertions hunt.
/// </summary>
public class CookieHygieneTests : IDisposable
{
    private const string Sentinel = "FAKE-COOKIE-CRLF-SENTINEL";
    private static readonly string CrLfCookie = Sentinel + "\r\nX-Injected: 1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cookie-hygiene-" + Guid.NewGuid().ToString("N"));

    public CookieHygieneTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // ---- the store door ----

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\0")]
    [InlineData("\u001f")]
    public async Task AddAsync_RefusesAControlCharacterCookie_WithNoValueInTheMessage(string control)
    {
        using var store = new AccountStore(Path.Combine(_root, "add.dat"));
        var cookie = Sentinel + control + "tail";

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync("Alt", "", cookie));

        Assert.DoesNotContain(Sentinel, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, ex.ToString(), StringComparison.Ordinal);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task AddAsync_StillAcceptsAnOrdinaryCookie_IncludingTabFreeShapes()
    {
        using var store = new AccountStore(Path.Combine(_root, "ok.dat"));

        var account = await store.AddAsync("Alt", "", "FAKE-COOKIE-ordinary_abc-123.DEF|_xyz==");

        Assert.Equal("FAKE-COOKIE-ordinary_abc-123.DEF|_xyz==", await store.RetrieveCookieAsync(account.Id));
    }

    [Fact]
    public async Task UpdateCookieAsync_RefusesAControlCharacterCookie_AndKeepsTheOldOne()
    {
        using var store = new AccountStore(Path.Combine(_root, "update.dat"));
        var account = await store.AddAsync("Alt", "", "FAKE-COOKIE-original");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateCookieAsync(account.Id, CrLfCookie));

        Assert.DoesNotContain(Sentinel, ex.Message, StringComparison.Ordinal);
        Assert.Equal("FAKE-COOKIE-original", await store.RetrieveCookieAsync(account.Id));
        Assert.Equal(0, store.GetCookieGeneration(account.Id)); // a refused recapture is not a reauth
    }

    [Fact]
    public async Task ImportMerge_SkipsAControlCharacterRecord_AndNeverLetsItShadowAValidOneForTheSameUser()
    {
        using var store = new AccountStore(Path.Combine(_root, "import.dat"));
        var records = new List<AccountExportRecord>
        {
            Record(1, CrLfCookie),               // invalid, listed first
            Record(1, "FAKE-COOKIE-valid-one"),  // same user, valid: must still import
            Record(2, CrLfCookie),               // invalid, nothing else for this user
        };

        var result = await store.ImportMergeAsync(records);

        Assert.Equal(1, result.Imported);
        Assert.Equal(2, result.Skipped);
        var account = Assert.Single(await store.ListAsync());
        Assert.Equal(1L, account.RobloxUserId);
        Assert.Equal("FAKE-COOKIE-valid-one", await store.RetrieveCookieAsync(account.Id));
    }

    private static AccountExportRecord Record(long userId, string cookie) => new(
        DisplayName: $"U{userId}", AvatarUrl: "", RobloxUserId: userId, Cookie: cookie,
        Tags: [], FpsCap: null, CaptionColorHex: null, LocalName: null, IsMain: false, SortOrder: 0, IsSelected: true);

    // ---- tools/EndpointProbe ----

    [Fact]
    public void Prober_RefusesAControlCharacterCookie_WithNoValueInTheMessage()
    {
        using var http = new HttpClient(new RespondingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var ex = Assert.Throws<ArgumentException>(() =>
            new Prober(http, CrLfCookie, userId: null, new Redactor(null), TimeSpan.Zero, _ => { }));

        Assert.DoesNotContain(Sentinel, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProbeEntryPoint_PrintsOnlyTheExceptionType_ForAVaultHoldingAControlCharacterCookie()
    {
        // A vault written before the store door existed can hold such a cookie, and the probe reads it
        // without going through AddAsync. Planted as a raw DPAPI blob, the way LocalNameSchemaTests plants
        // a legacy one. The JSON \r\n escapes decode to a real CR LF inside the cookie.
        var dir = Path.Combine(_root, "vault");
        Directory.CreateDirectory(dir);
        var json = """
            {"version":1,"accounts":[{"id":"11111111-1111-1111-1111-111111111111","displayName":"Crlf","avatarUrl":"",
             "cookie":"FAKE-COOKIE-CRLF-SENTINEL\r\nX-Injected: 1","createdAt":"2026-04-01T00:00:00+00:00",
             "lastLaunchedAt":null,"isMain":true,"sortOrder":0,"isSelected":true,"localName":"Crlf Alt","robloxUserId":42}]}
            """;
        await File.WriteAllBytesAsync(Path.Combine(dir, "accounts.dat"), ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json), optionalEntropy: null, DataProtectionScope.CurrentUser));
        var outDir = Path.Combine(_root, "probes");

        var program = typeof(ProbeCli).Assembly.GetType("ROROROblox.EndpointProbe.Program")!;
        var run = program.GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await (Task<int>)run.Invoke(null, [
            new[] { "--offline", "--account", "Crlf Alt", "--data-dir", dir, "--out", outDir },
            stdout, stderr, CancellationToken.None])!;

        Assert.NotEqual(0, code);
        Assert.Contains("ArgumentException", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, stdout + stderr.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir) && Directory.GetFiles(outDir).Length > 0, "no report may be written");
    }

    [Fact]
    public async Task Prober_RegistersTheCsrfTokenBeforeScrubbingTheRblxHeaders()
    {
        // A response that echoes its own CSRF token into an rblx- header. The token is only known to the
        // redactor from this very response, so it must be registered before the headers are scrubbed.
        const string token = "CSRF-SENTINEL-4471";
        var handler = new RespondingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.TryAddWithoutValidation("x-csrf-token", token);
            response.Headers.TryAddWithoutValidation("rblx-challenge-type", "echo-" + token);
            return response;
        });
        using var http = new HttpClient(handler);
        var prober = new Prober(http, "FAKE-COOKIE-ordinary", userId: null, new Redactor(null), TimeSpan.Zero, _ => { });

        var steps = await prober.RunAsync(CancellationToken.None);

        var echoed = steps.SelectMany(s => s.RblxHeaders).Where(h => h.Key == "rblx-challenge-type").ToList();
        Assert.NotEmpty(echoed);
        Assert.All(echoed, h => Assert.DoesNotContain(token, h.Value, StringComparison.Ordinal));
    }

    private sealed class RespondingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    // ---- records that hold a plaintext cookie ----

    [Fact]
    public void EveryRecordHoldingACookie_RedeclaresToString_AndDoesNotPrintIt()
    {
        const string cookie = "FAKE-COOKIE-TOSTRING-SENTINEL";
        var records = CookieRecordTypes().ToList();

        // Vacuity floor: StoredAccount, AccountExportRecord, CookieCaptureResult.Success. If a rename or
        // a move makes the scan find fewer, this fails instead of passing over nothing.
        Assert.True(records.Count >= 3, $"found only {records.Count} record type(s) with a Cookie property: {string.Join(", ", records.Select(r => r.Name))}");

        foreach (var type in records)
        {
            var declared = type.GetMethod("ToString", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, Type.EmptyTypes);
            Assert.True(declared is not null, $"{type.FullName} holds a Cookie and must redeclare ToString().");

            var instance = RuntimeHelpers.GetUninitializedObject(type);
            type.GetProperty("Cookie")!.SetValue(instance, cookie);
            var text = instance.ToString();
            Assert.DoesNotContain(cookie, text, StringComparison.Ordinal);
            Assert.Contains("<redacted>", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheStoredAccountsBlobAndExportRecord_PrintNoCookie_WhenBuiltForReal()
    {
        const string cookie = "FAKE-COOKIE-REAL-SENTINEL";
        using var store = new AccountStore(Path.Combine(_root, "tostring.dat"));
        var account = await store.AddAsync("Alt", "", cookie);
        await store.UpdateRobloxUserIdAsync(account.Id, 9);

        var export = (await store.ExportAccountsAsync([account.Id])).Records.Single();
        var capture = new CookieCaptureResult.Success(cookie, 9, "name");

        Assert.DoesNotContain(cookie, export.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(cookie, capture.ToString(), StringComparison.Ordinal);
    }

    private static IEnumerable<Type> CookieRecordTypes()
    {
        foreach (var assembly in new[] { typeof(Account).Assembly, typeof(MainViewModel).Assembly })
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

            foreach (var type in types)
            {
                var isRecord = type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;
                var cookie = type.GetProperty("Cookie", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (isRecord && cookie is not null && cookie.PropertyType == typeof(string))
                {
                    yield return type;
                }
            }
        }
    }
}
