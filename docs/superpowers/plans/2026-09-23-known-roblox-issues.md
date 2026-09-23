# Known Roblox Issues Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A signed, remotely updated list of known Roblox-side issues, shown on a new Tools page and, for serious ones, as a one-time notice in the main window, each entry pointing at the RoRoRo feature that helps.

**Architecture:** `known-issues.json` is authored at the repo root, validated and signed in CI by `tools/CompatSigner` (the same tool and key that sign `roblox-compat.json`), and attached to every GitHub release. In the app, a stateless typed-HttpClient `KnownIssuesFeed` (Core) downloads, verifies, validates and caches it; a singleton `KnownIssuesState` (Core) holds the current snapshot and logs every outcome; a singleton `KnownIssuesNoticeModel` (App) turns that snapshot into localized notice text, a menu count and page items. Pure rules — parsing, version matching, notice selection — live in Core and return data, never prose.

**Tech Stack:** .NET 10 WPF, xUnit, System.Text.Json, ECDSA P-256 (`RobloxCompatSignature`), `Microsoft.Extensions.Http` typed clients, `Microsoft.Extensions.TimeProvider.Testing`, GitHub Actions (pwsh on windows-latest), the repo's Python translation pipeline.

**Spec:** `docs/superpowers/specs/2026-09-23-known-roblox-issues-design.md` (approved 2026-09-23, commit `912db03`; corrected while planning in `3e5af21`). Read it before starting; this plan argues from it.

## Global Constraints

- **Solution:** always name `ROROROblox.slnx`. Build `dotnet build ROROROblox.slnx -c Release`; test `dotnet test ROROROblox.slnx -c Release --no-build`. Never bare `dotnet build` (MSB1011). Build Release: a running dev RoRoRo locks `bin\Debug`.
- **Branch:** all work on `feat/known-roblox-issues`, cut from `main` at or after `3e5af21`. Never commit to `main`.
- **Typed HttpClient:** exactly one applicable public constructor, logger typed `ILogger<T>`; guarded by `TypedHttpClientRegistrationTests`.
- **User-Agent:** `RORORO/<version>`. No browser spoofing.
- **Core string boundary:** no Core type declares a `string Message` member (`CoreStringBoundaryFenceTests`). Core returns a Kind enum plus data; the App localizes.
- **Themes:** reference themed brushes and type-ladder sizes only with `DynamicResource` (`BodyFontSize`, `MetaFontSize`, `HeadingFontSize`, `DisplayFontSize`). No colour literals (`ThemedStatusColourTests` ceiling), no raw font sizes (`TypeLadderFenceTests`).
- **Buttons:** use existing styles (`CtaButtonStyle`, `SecondaryButtonStyle`, `GhostButtonStyle`, …). No self-painting, no Opacity hover. Every new interactive control is named (`AccessibleNamingFenceTests`, ceiling asserted as equality).
- **Threads:** off the UI thread marshal through `IUiDispatcher`; never `Application.Current?.Dispatcher` directly in new code.
- **Startup order:** theme → resolve mutex name → `TryAcquire` → gate stays untouched. The feed starts after the gate, beside `StartPluginAutostart()`.
- **Localization:** every new UI string exists in `Strings.resx` and all six satellites (fr, de, ru, pt-BR, pl, es) via `docs/store/translations/ui-<culture>.json` and `python scripts/translate-cycle.py`. Product nouns `RoRoRo` and `Roblox` stay English. Registers: German `du`, Spanish `tú`, Russian `вы`, Polish capitalised `Twój/Cię`.
- **MainViewModel tests:** build through `MainViewModelTests.Build(...)`, which disposes the window decorator and calls `StopPeriodicRefresh()`.
- **Feed limits (verbatim from spec):** document refused over 256 KB before its signature is checked; links must be `https://`; versions are two to four dot-separated numbers with a three-digit second number; download at startup (after the gate) and every four hours.
- **Signing:** `ROBLOXCOMPAT_SIGNING_KEY` signs both feeds; the app verifies against `RobloxCompatSigningKey.PublicKeySpki`.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. The pre-commit hooks (secret scan, local-path guard) must pass; never `--no-verify`.

## Review Focus

The five conditions most likely to bite someone that the spec implies but does not spell out. Each has a test in the task named.

1. **A newer feed on an older app.** An entry uses a `rororoHelps.feature` key this build does not know. Expected: the whole file still loads, and that entry shows its text with no button. The validator must *not* refuse unknown feature keys. → Task 1 `AnUnknownFeatureKeyIsAccepted`, Task 8 `AnUnknownFeatureShowsItsTextWithNoButton`.
2. **Roblox not installed, or Bloxstrap owning the launch handler.** The handler version read returns null. Expected: fall back to the installed version, and if that is null too, treat the version as unknown, so the notice still shows. → Task 2 `RunningRobloxVersionTests`.
3. **A torn cache file after a crash mid-write.** Expected: rejected at load by its signature, no exception, and the page empty until the next download. → Task 3 `ATruncatedSavedCopyIsIgnoredWithoutThrowing`.
4. **Two refreshes overlapping** (the startup download still running when a tick fires). Expected: they run one after the other, never together. → Task 4 `OverlappingRefreshesRunOneAtATime`.
5. **A corrupt or deleted dismissal file.** Expected: treated as nothing dismissed, the notice returns, and nothing throws. → Task 5 `ACorruptFileMeansNothingIsDismissed`.

---

## File map

**Core** (`src/ROROROblox.Core/KnownIssues/`, namespace `ROROROblox.Core.KnownIssues`)
- `KnownIssue.cs` — the record types of a valid document.
- `KnownIssueFeatures.cs` — the feature-key constants.
- `RobloxVersion.cs` — parses feed-authored versions and Roblox's comma `FileVersion`.
- `KnownIssuesParser.cs` — JSON → document, or a list of problems. The one validator, used by the app and by CI.
- `KnownIssueApplicability.cs` — version status of one issue against the running version.
- `KnownIssueNotice.cs` — notice selection, the applicable count, page order.
- `RunningRobloxVersion.cs` — handler version, falling back to the installed version.
- `KnownIssuesFeed.cs` — typed HttpClient: download, verify, validate, cache. Stateless.
- `KnownIssuesState.cs` — singleton snapshot holder, serialised refresh, summary logging.
- `KnownIssuesDismissals.cs` — the dismissed-id file.

**App** (`src/ROROROblox.App/KnownIssues/`, namespace `ROROROblox.App.KnownIssues`)
- `KnownIssueFeatureRoutes.cs` — feature key → where a button goes.
- `KnownIssuesNoticeModel.cs` — localized notice text, menu header, page data; dismiss.
- `KnownIssueView.cs` — one page entry, fully localized.
- `KnownRobloxIssuesPage.xaml` / `.xaml.cs` — the Tools page.

**Modified:** `Shell/ShellPage.cs`, `Shell/ShellWindow.xaml(.cs)`, `Preferences/SettingsPage.xaml.cs`, `ViewModels/MainViewModel.cs`, `MainWindow.xaml`, `App.xaml.cs`, `Properties/Strings.resx` and satellites, `docs/store/translations/ui-*.json`, `tools/CompatSigner/Program.cs`, `.github/workflows/compat.yml`, `.github/workflows/release.yml`, `src/ROROROblox.Core/RobloxCompatSigningKey.cs`, `docs/features.md`, `docs/decisions.md`.

**New at repo root:** `known-issues.json`.

**Tests** (`src/ROROROblox.Tests/KnownIssues/`, namespace `ROROROblox.Tests.KnownIssues`), plus one line in `TypedHttpClientRegistrationTests.cs` and a render test in `Rendering/`.

---

### Task 1: Core document model, version parsing and the validator

**Files:**
- Create: `src/ROROROblox.Core/KnownIssues/KnownIssue.cs`
- Create: `src/ROROROblox.Core/KnownIssues/KnownIssueFeatures.cs`
- Create: `src/ROROROblox.Core/KnownIssues/RobloxVersion.cs`
- Create: `src/ROROROblox.Core/KnownIssues/KnownIssuesParser.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/RobloxVersionTests.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/KnownIssuesParserTests.cs`

**Interfaces:**
- Produces: `KnownIssue`, `KnownIssueLink`, `KnownIssueHelp`, `KnownIssueVersions`, `KnownIssuesDocument`; `KnownIssueFeatures.MemoryWatchdog | FpsCaps | Recycle`; `RobloxVersion.TryParseFeed(string?, out Version?)`, `RobloxVersion.TryParseInstalled(string?, out Version?)`; `KnownIssuesParser.Parse(ReadOnlySpan<byte>) → KnownIssuesParseResult`, `KnownIssuesParser.SupportedSchemaVersion` (= 1); `KnownIssuesProblem(Kind, IssueId, Field)`, `KnownIssuesProblemKind`.

- [ ] **Step 1: Cut the branch**

From the repository root:

```bash
git checkout main && git pull --ff-only
git checkout -b feat/known-roblox-issues
```

- [ ] **Step 2: Write the failing version tests**

`src/ROROROblox.Tests/KnownIssues/RobloxVersionTests.cs`:

```csharp
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// Two spellings of a Roblox version reach this feature. The feed author writes <c>0.740</c>; the
/// binary reports <c>0, 740, 0, 7400927</c> — read off every version folder on the owner's PC on
/// 2026-09-23 — which <see cref="Version.TryParse(string?, out Version?)"/> rejects outright.
/// </summary>
public class RobloxVersionTests
{
    [Theory]
    [InlineData("0.740", "0.740")]
    [InlineData("0.740.0", "0.740.0")]
    [InlineData("0.740.0.7400927", "0.740.0.7400927")]
    [InlineData(" 0.739 ", "0.739")]
    public void AFeedVersionWrittenTheWayRobloxWritesItParses(string text, string expected)
    {
        Assert.True(RobloxVersion.TryParseFeed(text, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("0.74")]        // reads as minor 74, a different number from 0.740
    [InlineData("0.7400")]
    [InlineData("1.2.3.4.5")]
    [InlineData("0.740.x")]
    [InlineData("")]
    [InlineData(null)]
    public void AFeedVersionInAnyOtherShapeIsRefused(string? text)
    {
        Assert.False(RobloxVersion.TryParseFeed(text, out _));
    }

    [Theory]
    [InlineData("0, 740, 0, 7400927", "0.740.0.7400927")]
    [InlineData("0,739,0,7390687", "0.739.0.7390687")]
    [InlineData("0.740.0.7400927", "0.740.0.7400927")]
    public void AnInstalledFileVersionParsesInEitherSpelling(string text, string expected)
    {
        Assert.True(RobloxVersion.TryParseInstalled(text, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not a version")]
    public void AnUnreadableInstalledVersionIsRefused(string? text)
    {
        Assert.False(RobloxVersion.TryParseInstalled(text, out _));
    }

    [Fact]
    public void RobloxsFourPartFormComparesCorrectlyAgainstATwoPartBound()
    {
        Assert.True(RobloxVersion.TryParseInstalled("0, 740, 0, 7400927", out var installed));
        Assert.True(RobloxVersion.TryParseFeed("0.740", out var fixedIn));
        Assert.True(installed >= fixedIn);

        Assert.True(RobloxVersion.TryParseInstalled("0, 739, 0, 7390687", out var older));
        Assert.True(older < fixedIn);
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet build ROROROblox.slnx -c Release`
Expected: FAIL — `The type or namespace name 'KnownIssues' does not exist in the namespace 'ROROROblox.Core'`.

- [ ] **Step 4: Write the model, feature keys and version parser**

`src/ROROROblox.Core/KnownIssues/KnownIssue.cs`:

```csharp
namespace ROROROblox.Core.KnownIssues;

/// <summary>Where an issue is documented. <see cref="Url"/> is always absolute https — the parser refuses anything else.</summary>
public sealed record KnownIssueLink(string Label, string Url);

/// <summary>
/// What RoRoRo does about an issue: a feature key (see <see cref="KnownIssueFeatures"/>) and the
/// sentence shown beside it. An unknown key is valid — a newer feed must still load on an older app;
/// the App shows the sentence with no button.
/// </summary>
public sealed record KnownIssueHelp(string Feature, string Text);

/// <summary>Roblox version bounds. At least one is set. <see cref="From"/> is inclusive, <see cref="FixedIn"/> exclusive.</summary>
public sealed record KnownIssueVersions(Version? From, Version? FixedIn);

/// <summary>One known Roblox-side issue, exactly as a valid feed describes it. Text is English.</summary>
public sealed record KnownIssue(
    string Id,
    DateOnly PostedAt,
    bool Notify,
    string Title,
    string Symptom,
    string Workaround,
    KnownIssueHelp? RororoHelps,
    IReadOnlyList<KnownIssueLink> Links,
    KnownIssueVersions? RobloxVersions);

/// <summary>A document that passed <see cref="KnownIssuesParser"/>. An empty <see cref="Issues"/> list is valid: it retires every issue.</summary>
public sealed record KnownIssuesDocument(int SchemaVersion, IReadOnlyList<KnownIssue> Issues);
```

`src/ROROROblox.Core/KnownIssues/KnownIssueFeatures.cs`:

```csharp
namespace ROROROblox.Core.KnownIssues;

/// <summary>
/// The feature keys an entry's <c>rororoHelps.feature</c> may name, version 1. Data, not routing:
/// where each one leads is the App's business (<c>KnownIssueFeatureRoutes</c>).
/// </summary>
public static class KnownIssueFeatures
{
    /// <summary>The Memory section of Settings — "Watch memory while accounts are running".</summary>
    public const string MemoryWatchdog = "memory-watchdog";

    /// <summary>The per-account frame-rate cap on each main-window account row.</summary>
    public const string FpsCaps = "fps-caps";

    /// <summary>The per-account Recycle button on each main-window account row.</summary>
    public const string Recycle = "recycle";
}
```

`src/ROROROblox.Core/KnownIssues/RobloxVersion.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ROROROblox.Core.KnownIssues;

/// <summary>
/// The two spellings of a Roblox version this feature meets.
/// <para>
/// <b>Feed-authored</b> (<c>0.740</c>, <c>0.740.0.7400927</c>): two to four dot-separated numbers with
/// a three-digit second number, which is how Roblox numbers its builds. <c>0.74</c> is refused because
/// <see cref="Version"/> reads it as minor 74 — a different number from 0.740, and an entry written that
/// way would silently match the wrong builds.
/// </para>
/// <para>
/// <b>Installed</b>: <c>RobloxPlayerBeta.exe</c> reports its <c>FileVersion</c> as
/// <c>0, 740, 0, 7400927</c>. <see cref="Version.TryParse(string?, out Version?)"/> rejects that form, so
/// the spaces go and the commas become dots first. (The same raw string reaches
/// <c>RobloxCompatChecker.CheckAsync</c>, which is why its drift banner cannot fire — recorded in the spec,
/// deliberately not fixed here.)
/// </para>
/// </summary>
public static class RobloxVersion
{
    private static readonly Regex FeedForm = new(@"^\d+\.\d{3}(\.\d+){0,2}$", RegexOptions.CultureInvariant);

    public static bool TryParseFeed(string? text, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        var trimmed = text?.Trim();
        return trimmed is not null && FeedForm.IsMatch(trimmed) && Version.TryParse(trimmed, out version);
    }

    public static bool TryParseInstalled(string? text, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalised = text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        return Version.TryParse(normalised, out version);
    }
}
```

- [ ] **Step 5: Run the version tests to verify they pass**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownIssues.RobloxVersionTests"`
Expected: PASS, 20 tests.

- [ ] **Step 6: Write the failing parser tests**

`src/ROROROblox.Tests/KnownIssues/KnownIssuesParserTests.cs`:

```csharp
using System.Text;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// The one validator. <c>tools/CompatSigner</c> runs it before signing and the app runs it after
/// verifying, so "CI never signs a file the app would reject" is true by construction. Every refusal
/// in the spec's §1 has a case here.
/// </summary>
public class KnownIssuesParserTests
{
    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    private const string ValidDocument = """
        {
          "schemaVersion": 1,
          "issues": [
            {
              "id": "window-freeze",
              "postedAt": "2026-09-23",
              "notify": true,
              "title": "The Roblox window freezes when you drag it",
              "symptom": "Dragging stops responding.",
              "workaround": "Press the Windows key, then Escape.",
              "rororoHelps": { "feature": "fps-caps", "text": "Cap below your refresh rate." },
              "links": [ { "label": "DevForum", "url": "https://devforum.roblox.com/t/4032374" } ],
              "robloxVersions": { "from": "0.739", "fixedIn": "0.740" }
            }
          ]
        }
        """;

    /// <summary>A one-issue document whose issue object is exactly <paramref name="issueBody"/>.</summary>
    private static string OneIssue(string issueBody) =>
        "{ \"schemaVersion\": 1, \"issues\": [ { " + issueBody + " } ] }";

    private const string Required =
        "\"id\": \"a\", \"postedAt\": \"2026-09-23\", \"notify\": false, " +
        "\"title\": \"t\", \"symptom\": \"s\", \"workaround\": \"w\"";

    [Fact]
    public void ParsesEveryField()
    {
        var result = KnownIssuesParser.Parse(Bytes(ValidDocument));

        Assert.True(result.IsValid, string.Join(", ", result.Problems));
        var issue = Assert.Single(result.Document!.Issues);
        Assert.Equal("window-freeze", issue.Id);
        Assert.Equal(new DateOnly(2026, 9, 23), issue.PostedAt);
        Assert.True(issue.Notify);
        Assert.Equal("The Roblox window freezes when you drag it", issue.Title);
        Assert.Equal("Dragging stops responding.", issue.Symptom);
        Assert.Equal("Press the Windows key, then Escape.", issue.Workaround);
        Assert.Equal(new KnownIssueHelp("fps-caps", "Cap below your refresh rate."), issue.RororoHelps);
        Assert.Equal(new KnownIssueLink("DevForum", "https://devforum.roblox.com/t/4032374"), Assert.Single(issue.Links));
        Assert.Equal(new Version(0, 739), issue.RobloxVersions!.From);
        Assert.Equal(new Version(0, 740), issue.RobloxVersions.FixedIn);
    }

    [Fact]
    public void AnEmptyIssueListIsValid()
    {
        var result = KnownIssuesParser.Parse(Bytes("{ \"schemaVersion\": 1, \"issues\": [] }"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Document!.Issues);
    }

    [Fact]
    public void OptionalFieldsMayBeLeftOut()
    {
        var result = KnownIssuesParser.Parse(Bytes(OneIssue(Required)));

        Assert.True(result.IsValid, string.Join(", ", result.Problems));
        var issue = Assert.Single(result.Document!.Issues);
        Assert.Null(issue.RororoHelps);
        Assert.Empty(issue.Links);
        Assert.Null(issue.RobloxVersions);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        var json = "{ \"schemaVersion\": 1, \"futureThing\": 3, \"issues\": [ { " + Required + ", \"severity\": \"high\" } ] }";

        Assert.True(KnownIssuesParser.Parse(Bytes(json)).IsValid);
    }

    /// <summary>Review Focus 1: a newer feed names a feature this build does not know; it must still load.</summary>
    [Fact]
    public void AnUnknownFeatureKeyIsAccepted()
    {
        var json = OneIssue(Required + ", \"rororoHelps\": { \"feature\": \"teleport-helper\", \"text\": \"Soon.\" }");

        var result = KnownIssuesParser.Parse(Bytes(json));

        Assert.True(result.IsValid, string.Join(", ", result.Problems));
        Assert.Equal("teleport-helper", result.Document!.Issues[0].RororoHelps!.Feature);
    }

    [Fact]
    public void TrailingCommasAndCommentsAreAcceptedForHandEditing()
    {
        var json = "{ // hand edited\n \"schemaVersion\": 1, \"issues\": [ { " + Required + ", }, ], }";

        Assert.True(KnownIssuesParser.Parse(Bytes(json)).IsValid);
    }

    [Theory]
    [InlineData("not json at all", KnownIssuesProblemKind.NotJson, null)]
    [InlineData("[]", KnownIssuesProblemKind.NotJson, null)]
    [InlineData("null", KnownIssuesProblemKind.NotJson, null)]
    [InlineData("{ \"schemaVersion\": 2, \"issues\": [] }", KnownIssuesProblemKind.UnsupportedSchemaVersion, "schemaVersion")]
    [InlineData("{ \"issues\": [] }", KnownIssuesProblemKind.MissingField, "schemaVersion")]
    [InlineData("{ \"schemaVersion\": 1 }", KnownIssuesProblemKind.MissingField, "issues")]
    public void ADocumentLevelProblemIsRefused(string json, KnownIssuesProblemKind kind, string? field)
    {
        var result = KnownIssuesParser.Parse(Bytes(json));

        Assert.False(result.IsValid);
        Assert.Null(result.Document);
        Assert.Contains(result.Problems, p => p.Kind == kind && p.Field == field);
    }

    [Theory]
    [InlineData("\"id\": \"a\", \"postedAt\": \"2026-09-23\", \"notify\": true, \"symptom\": \"s\", \"workaround\": \"w\"", KnownIssuesProblemKind.MissingField, "title")]
    [InlineData("\"id\": \"a\", \"postedAt\": \"2026-09-23\", \"title\": \"t\", \"symptom\": \"s\", \"workaround\": \"w\"", KnownIssuesProblemKind.MissingField, "notify")]
    [InlineData("\"id\": \"a\", \"postedAt\": \"23/09/2026\", \"notify\": true, \"title\": \"t\", \"symptom\": \"s\", \"workaround\": \"w\"", KnownIssuesProblemKind.BadDate, "postedAt")]
    public void AnIssueMissingOrMisspellingARequiredFieldIsRefused(string issueBody, KnownIssuesProblemKind kind, string field)
    {
        var result = KnownIssuesParser.Parse(Bytes(OneIssue(issueBody)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == kind && p.IssueId == "a" && p.Field == field);
    }

    [Fact]
    public void AnIssueWithNoIdIsRefused()
    {
        var body = "\"postedAt\": \"2026-09-23\", \"notify\": true, \"title\": \"t\", \"symptom\": \"s\", \"workaround\": \"w\"";

        var result = KnownIssuesParser.Parse(Bytes(OneIssue(body)));

        Assert.Contains(result.Problems, p => p.Kind == KnownIssuesProblemKind.MissingField && p.Field == "id");
    }

    [Fact]
    public void ADuplicateIdIsRefused()
    {
        var json = "{ \"schemaVersion\": 1, \"issues\": [ { " + Required + " }, { " + Required + " } ] }";

        var result = KnownIssuesParser.Parse(Bytes(json));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == KnownIssuesProblemKind.DuplicateId && p.IssueId == "a");
    }

    [Theory]
    [InlineData("http://devforum.roblox.com/t/1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("devforum.roblox.com/t/1")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    public void ALinkThatIsNotHttpsIsRefused(string url)
    {
        var body = Required + ", \"links\": [ { \"label\": \"L\", \"url\": \"" + url + "\" } ]";

        var result = KnownIssuesParser.Parse(Bytes(OneIssue(body)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == KnownIssuesProblemKind.NonHttpsLink && p.Field == "links.url");
    }

    [Theory]
    [InlineData("{ \"fixedIn\": \"0.74\" }", KnownIssuesProblemKind.BadVersionFormat, "robloxVersions.fixedIn")]
    [InlineData("{ \"from\": \"zero\" }", KnownIssuesProblemKind.BadVersionFormat, "robloxVersions.from")]
    [InlineData("{ }", KnownIssuesProblemKind.EmptyVersionRange, "robloxVersions")]
    public void ABadVersionRangeIsRefused(string range, KnownIssuesProblemKind kind, string field)
    {
        var result = KnownIssuesParser.Parse(Bytes(OneIssue(Required + ", \"robloxVersions\": " + range)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == kind && p.Field == field);
    }

    [Fact]
    public void AHelpBlockWithoutItsSentenceIsRefused()
    {
        var body = Required + ", \"rororoHelps\": { \"feature\": \"fps-caps\" }";

        var result = KnownIssuesParser.Parse(Bytes(OneIssue(body)));

        Assert.Contains(result.Problems, p => p.Kind == KnownIssuesProblemKind.MissingField && p.Field == "rororoHelps.text");
    }
}
```

- [ ] **Step 7: Run them to verify they fail**

Run: `dotnet build ROROROblox.slnx -c Release`
Expected: FAIL — `The name 'KnownIssuesParser' does not exist in the current context`.

- [ ] **Step 8: Write the parser**

`src/ROROROblox.Core/KnownIssues/KnownIssuesParser.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace ROROROblox.Core.KnownIssues;

public enum KnownIssuesProblemKind
{
    NotJson,
    UnsupportedSchemaVersion,
    MissingField,
    DuplicateId,
    BadDate,
    NonHttpsLink,
    BadVersionFormat,
    EmptyVersionRange,
}

/// <summary>One reason a document was refused: a kind plus data, never prose (Core string boundary).</summary>
public sealed record KnownIssuesProblem(KnownIssuesProblemKind Kind, string? IssueId, string? Field);

public sealed record KnownIssuesParseResult(KnownIssuesDocument? Document, IReadOnlyList<KnownIssuesProblem> Problems)
{
    public bool IsValid => Document is not null && Problems.Count == 0;
}

/// <summary>
/// The single validator for <c>known-issues.json</c>. <c>tools/CompatSigner --validate-known-issues</c>
/// runs it before CI signs the file, and <c>KnownIssuesFeed</c> runs it after the signature verifies,
/// so the workflow can never sign a file every client would refuse. A document is all or nothing: any
/// problem refuses the whole file, and the app keeps what it had.
/// <para>
/// Deliberately NOT refused: an unknown <c>rororoHelps.feature</c> key (a newer feed must load on an
/// older app) and unknown fields (a later format may add some).
/// </para>
/// </summary>
public static class KnownIssuesParser
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // The file is edited by hand; a trailing comma or a comment is not worth a failed publish.
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static KnownIssuesParseResult Parse(ReadOnlySpan<byte> utf8Json)
    {
        DocumentDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DocumentDto>(utf8Json, Options);
        }
        catch (JsonException)
        {
            return Refused([new KnownIssuesProblem(KnownIssuesProblemKind.NotJson, null, null)]);
        }

        if (dto is null)
        {
            return Refused([new KnownIssuesProblem(KnownIssuesProblemKind.NotJson, null, null)]);
        }

        var problems = new List<KnownIssuesProblem>();
        if (dto.SchemaVersion is null)
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, null, "schemaVersion"));
        }
        else if (dto.SchemaVersion != SupportedSchemaVersion)
        {
            problems.Add(new(KnownIssuesProblemKind.UnsupportedSchemaVersion, null, "schemaVersion"));
        }

        if (dto.Issues is null)
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, null, "issues"));
            return Refused(problems);
        }

        var issues = new List<KnownIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in dto.Issues)
        {
            if (ReadIssue(raw, problems, seen) is { } issue)
            {
                issues.Add(issue);
            }
        }

        return problems.Count == 0
            ? new KnownIssuesParseResult(new KnownIssuesDocument(SupportedSchemaVersion, issues), [])
            : Refused(problems);
    }

    private static KnownIssuesParseResult Refused(IReadOnlyList<KnownIssuesProblem> problems) => new(null, problems);

    private static KnownIssue? ReadIssue(IssueDto? raw, List<KnownIssuesProblem> problems, HashSet<string> seen)
    {
        if (raw is null)
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, null, "issues[]"));
            return null;
        }

        var id = raw.Id?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, null, "id"));
            return null;
        }

        var before = problems.Count;
        if (!seen.Add(id))
        {
            problems.Add(new(KnownIssuesProblemKind.DuplicateId, id, "id"));
        }

        DateOnly postedAt = default;
        if (string.IsNullOrWhiteSpace(raw.PostedAt))
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, id, "postedAt"));
        }
        else if (!DateOnly.TryParseExact(raw.PostedAt.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out postedAt))
        {
            problems.Add(new(KnownIssuesProblemKind.BadDate, id, "postedAt"));
        }

        if (raw.Notify is null)
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, id, "notify"));
        }

        RequireText(raw.Title, id, "title", problems);
        RequireText(raw.Symptom, id, "symptom", problems);
        RequireText(raw.Workaround, id, "workaround", problems);

        KnownIssueHelp? help = null;
        if (raw.RororoHelps is { } h)
        {
            RequireText(h.Feature, id, "rororoHelps.feature", problems);
            RequireText(h.Text, id, "rororoHelps.text", problems);
            if (!string.IsNullOrWhiteSpace(h.Feature) && !string.IsNullOrWhiteSpace(h.Text))
            {
                help = new KnownIssueHelp(h.Feature.Trim(), h.Text.Trim());
            }
        }

        var links = new List<KnownIssueLink>();
        foreach (var link in raw.Links ?? [])
        {
            if (link is null || string.IsNullOrWhiteSpace(link.Label))
            {
                problems.Add(new(KnownIssuesProblemKind.MissingField, id, "links.label"));
                continue;
            }

            var url = link.Url?.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                problems.Add(new(KnownIssuesProblemKind.NonHttpsLink, id, "links.url"));
                continue;
            }

            links.Add(new KnownIssueLink(link.Label.Trim(), url!));
        }

        KnownIssueVersions? versions = null;
        if (raw.RobloxVersions is { } v)
        {
            if (v.From is null && v.FixedIn is null)
            {
                problems.Add(new(KnownIssuesProblemKind.EmptyVersionRange, id, "robloxVersions"));
            }
            else
            {
                Version? from = null;
                Version? fixedIn = null;
                if (v.From is not null && !RobloxVersion.TryParseFeed(v.From, out from))
                {
                    problems.Add(new(KnownIssuesProblemKind.BadVersionFormat, id, "robloxVersions.from"));
                }

                if (v.FixedIn is not null && !RobloxVersion.TryParseFeed(v.FixedIn, out fixedIn))
                {
                    problems.Add(new(KnownIssuesProblemKind.BadVersionFormat, id, "robloxVersions.fixedIn"));
                }

                versions = new KnownIssueVersions(from, fixedIn);
            }
        }

        if (problems.Count != before)
        {
            return null;
        }

        return new KnownIssue(
            id,
            postedAt,
            raw.Notify!.Value,
            raw.Title!.Trim(),
            raw.Symptom!.Trim(),
            raw.Workaround!.Trim(),
            help,
            links,
            versions);
    }

    private static void RequireText(string? value, string id, string field, List<KnownIssuesProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, id, field));
        }
    }

    internal sealed class DocumentDto
    {
        public int? SchemaVersion { get; set; }
        public List<IssueDto?>? Issues { get; set; }
    }

    internal sealed class IssueDto
    {
        public string? Id { get; set; }
        public string? PostedAt { get; set; }
        public bool? Notify { get; set; }
        public string? Title { get; set; }
        public string? Symptom { get; set; }
        public string? Workaround { get; set; }
        public HelpDto? RororoHelps { get; set; }
        public List<LinkDto?>? Links { get; set; }
        public VersionsDto? RobloxVersions { get; set; }
    }

    internal sealed class HelpDto
    {
        public string? Feature { get; set; }
        public string? Text { get; set; }
    }

    internal sealed class LinkDto
    {
        public string? Label { get; set; }
        public string? Url { get; set; }
    }

    internal sealed class VersionsDto
    {
        public string? From { get; set; }
        public string? FixedIn { get; set; }
    }
}
```

- [ ] **Step 9: Run the parser tests to verify they pass**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownIssues"`
Expected: PASS. Also run `--filter "FullyQualifiedName~CoreStringBoundaryFenceTests"` and confirm it still passes: `KnownIssuesProblem` carries no `string Message`.

- [ ] **Step 10: Commit**

```bash
git add src/ROROROblox.Core/KnownIssues src/ROROROblox.Tests/KnownIssues
git commit -m "feat(known-issues): the document model, Roblox's two version spellings, and the one validator

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Version matching, notice selection, and the running Roblox version

**Files:**
- Create: `src/ROROROblox.Core/KnownIssues/KnownIssueApplicability.cs`
- Create: `src/ROROROblox.Core/KnownIssues/KnownIssueNotice.cs`
- Create: `src/ROROROblox.Core/KnownIssues/RunningRobloxVersion.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/KnownIssueApplicabilityTests.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/KnownIssueNoticeTests.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/RunningRobloxVersionTests.cs`

**Interfaces:**
- Consumes: Task 1's `KnownIssue`, `KnownIssueVersions`, `RobloxVersion.TryParseInstalled`; `RobloxCompatChecker.GetHandlerRobloxVersion()` and `GetInstalledRobloxVersion()` (both `internal static` in Core).
- Produces: `enum KnownIssueVersionStatus { NoVersions, Affected, NotYetAffected, Fixed, UnknownInstalled }`; `KnownIssueApplicability.StatusFor(KnownIssue, Version?)`, `KnownIssueApplicability.Applies(KnownIssue, Version?)`; `KnownIssueNoticeSelection` (`Issues`, `Ids`, `IsEmpty`, static `None`); `KnownIssueNotice.Select(IReadOnlyList<KnownIssue>, Version?, IReadOnlySet<string>)`, `KnownIssueNotice.CountApplicable(IReadOnlyList<KnownIssue>, Version?)`, `KnownIssueNotice.PageOrder(IReadOnlyList<KnownIssue>)`; `RunningRobloxVersion.Read()` and `RunningRobloxVersion.Read(Func<string?>, Func<string?>)`.

- [ ] **Step 1: Write a shared test builder and the failing tests**

`src/ROROROblox.Tests/KnownIssues/KnownIssueBuilder.cs`:

```csharp
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

internal static class KnownIssueBuilder
{
    public static KnownIssue Issue(
        string id,
        bool notify = true,
        string posted = "2026-09-23",
        Version? from = null,
        Version? fixedIn = null,
        KnownIssueHelp? helps = null,
        params KnownIssueLink[] links) => new(
            id,
            DateOnly.Parse(posted, System.Globalization.CultureInfo.InvariantCulture),
            notify,
            $"Title of {id}",
            $"Symptom of {id}",
            $"Workaround for {id}",
            helps,
            links,
            from is null && fixedIn is null ? null : new KnownIssueVersions(from, fixedIn));
}
```

`src/ROROROblox.Tests/KnownIssues/KnownIssueApplicabilityTests.cs`:

```csharp
using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

public class KnownIssueApplicabilityTests
{
    private static readonly Version V739 = Version.Parse("0.739.0.7390687");
    private static readonly Version V740 = Version.Parse("0.740.0.7400927");

    [Fact]
    public void AnIssueWithNoVersionsAlwaysApplies()
    {
        Assert.Equal(KnownIssueVersionStatus.NoVersions, KnownIssueApplicability.StatusFor(Issue("a"), V740));
        Assert.True(KnownIssueApplicability.Applies(Issue("a"), null));
    }

    [Fact]
    public void BelowFixedInIsAffected_AtOrAboveIsFixed()
    {
        var issue = Issue("a", fixedIn: new Version(0, 740));

        Assert.Equal(KnownIssueVersionStatus.Affected, KnownIssueApplicability.StatusFor(issue, V739));
        Assert.Equal(KnownIssueVersionStatus.Fixed, KnownIssueApplicability.StatusFor(issue, V740));
        Assert.False(KnownIssueApplicability.Applies(issue, V740));
    }

    [Fact]
    public void BelowFromIsNotYetAffected_AtFromIsAffected()
    {
        var issue = Issue("a", from: new Version(0, 740));

        Assert.Equal(KnownIssueVersionStatus.NotYetAffected, KnownIssueApplicability.StatusFor(issue, V739));
        Assert.Equal(KnownIssueVersionStatus.Affected, KnownIssueApplicability.StatusFor(issue, V740));
    }

    [Fact]
    public void BothBoundsMakeAWindow()
    {
        var issue = Issue("a", from: new Version(0, 739), fixedIn: new Version(0, 740));

        Assert.True(KnownIssueApplicability.Applies(issue, V739));
        Assert.False(KnownIssueApplicability.Applies(issue, V740));
        Assert.False(KnownIssueApplicability.Applies(issue, Version.Parse("0.738.0.1")));
    }

    /// <summary>When unsure, tell: an unreadable version counts as affected, and says so on the page.</summary>
    [Fact]
    public void AnUnreadableVersionCountsAsAffected()
    {
        var issue = Issue("a", fixedIn: new Version(0, 740));

        Assert.Equal(KnownIssueVersionStatus.UnknownInstalled, KnownIssueApplicability.StatusFor(issue, null));
        Assert.True(KnownIssueApplicability.Applies(issue, null));
    }
}
```

`src/ROROROblox.Tests/KnownIssues/KnownIssueNoticeTests.cs`:

```csharp
using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

public class KnownIssueNoticeTests
{
    private static readonly Version V740 = Version.Parse("0.740.0.7400927");
    private static readonly IReadOnlySet<string> NoneDismissed = new HashSet<string>();

    [Fact]
    public void OnlyApplicableUndismissedNotifyEntriesAreSelected_NewestFirst()
    {
        var issues = new[]
        {
            Issue("old", posted: "2026-09-01"),
            Issue("quiet", notify: false),
            Issue("fixed", fixedIn: new Version(0, 740)),
            Issue("new", posted: "2026-09-22"),
            Issue("closed"),
        };

        // "quiet" is not serious, "fixed" is fixed in 0.740, "closed" was dismissed.
        var selection = KnownIssueNotice.Select(issues, V740, new HashSet<string> { "closed" });

        Assert.Equal(["new", "old"], selection.Ids);
    }

    [Fact]
    public void NothingApplicableIsAnEmptySelection()
    {
        var selection = KnownIssueNotice.Select([Issue("quiet", notify: false)], V740, NoneDismissed);

        Assert.True(selection.IsEmpty);
        Assert.Empty(selection.Ids);
    }

    [Fact]
    public void TheCountIncludesQuietEntriesButNotFixedOnes()
    {
        var issues = new[] { Issue("loud"), Issue("quiet", notify: false), Issue("fixed", fixedIn: new Version(0, 740)) };

        Assert.Equal(2, KnownIssueNotice.CountApplicable(issues, V740));
    }

    [Fact]
    public void ThePageListsEveryEntry_NotifyFirst_ThenNewest_ThenById()
    {
        var issues = new[]
        {
            Issue("b-quiet", notify: false, posted: "2026-09-30"),
            Issue("loud-old", posted: "2026-09-01"),
            Issue("a-quiet", notify: false, posted: "2026-09-30"),
            Issue("loud-new", posted: "2026-09-20"),
        };

        var order = KnownIssueNotice.PageOrder(issues).Select(i => i.Id).ToArray();

        Assert.Equal(["loud-new", "loud-old", "a-quiet", "b-quiet"], order);
    }
}
```

`src/ROROROblox.Tests/KnownIssues/RunningRobloxVersionTests.cs`:

```csharp
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// Review Focus 2. The handler read answers "what will a launch run"; the installed read orders
/// folders by write time, which F-104 measured as a coin flip during a launch batch. Handler first.
/// </summary>
public class RunningRobloxVersionTests
{
    [Fact]
    public void TheHandlerVersionWins()
    {
        var version = RunningRobloxVersion.Read(() => "0, 740, 0, 7400927", () => "0, 739, 0, 7390687");

        Assert.Equal(Version.Parse("0.740.0.7400927"), version);
    }

    [Fact]
    public void AStrapOwnedOrMissingHandlerFallsBackToTheInstalledVersion()
    {
        var version = RunningRobloxVersion.Read(() => null, () => "0, 739, 0, 7390687");

        Assert.Equal(Version.Parse("0.739.0.7390687"), version);
    }

    [Fact]
    public void AnUnreadableHandlerFallsBackToo()
    {
        var version = RunningRobloxVersion.Read(() => "garbage", () => "0, 739, 0, 7390687");

        Assert.Equal(Version.Parse("0.739.0.7390687"), version);
    }

    [Fact]
    public void AThrowingReadIsTreatedAsUnknown()
    {
        var version = RunningRobloxVersion.Read(() => throw new IOException("locked"), () => null);

        Assert.Null(version);
    }

    [Fact]
    public void NoRobloxAtAllIsUnknown()
    {
        Assert.Null(RunningRobloxVersion.Read(() => null, () => null));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build ROROROblox.slnx -c Release`
Expected: FAIL — `The name 'KnownIssueApplicability' does not exist in the current context`.

- [ ] **Step 3: Write the implementations**

`src/ROROROblox.Core/KnownIssues/KnownIssueApplicability.cs`:

```csharp
namespace ROROROblox.Core.KnownIssues;

public enum KnownIssueVersionStatus
{
    /// <summary>The entry names no versions; it stands until the owner removes it.</summary>
    NoVersions,
    Affected,
    /// <summary>The running version is older than <see cref="KnownIssueVersions.From"/>.</summary>
    NotYetAffected,
    Fixed,
    /// <summary>The entry names versions but the running version could not be read.</summary>
    UnknownInstalled,
}

/// <summary>
/// Does an issue apply to this PC? Decides the notice and the count only — the page always lists
/// every entry, because the client actually running can be older than the one installed (the frozen
/// window on 2026-09-22 was 0.739, opened from Chrome, with 0.740 installed).
/// </summary>
public static class KnownIssueApplicability
{
    public static KnownIssueVersionStatus StatusFor(KnownIssue issue, Version? running)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var range = issue.RobloxVersions;
        if (range is null)
        {
            return KnownIssueVersionStatus.NoVersions;
        }

        if (running is null)
        {
            return KnownIssueVersionStatus.UnknownInstalled;
        }

        if (range.From is { } from && running < from)
        {
            return KnownIssueVersionStatus.NotYetAffected;
        }

        if (range.FixedIn is { } fixedIn && running >= fixedIn)
        {
            return KnownIssueVersionStatus.Fixed;
        }

        return KnownIssueVersionStatus.Affected;
    }

    /// <summary>When unsure, tell: an unreadable version applies.</summary>
    public static bool Applies(KnownIssue issue, Version? running) => StatusFor(issue, running) is
        KnownIssueVersionStatus.NoVersions or KnownIssueVersionStatus.Affected or KnownIssueVersionStatus.UnknownInstalled;
}
```

`src/ROROROblox.Core/KnownIssues/KnownIssueNotice.cs`:

```csharp
namespace ROROROblox.Core.KnownIssues;

/// <summary>The entries the main-window notice is about, newest first. Data only; the App words it.</summary>
public sealed record KnownIssueNoticeSelection(IReadOnlyList<KnownIssue> Issues)
{
    public static readonly KnownIssueNoticeSelection None = new([]);

    public bool IsEmpty => Issues.Count == 0;

    public IReadOnlyList<string> Ids => Issues.Select(i => i.Id).ToList();
}

public static class KnownIssueNotice
{
    /// <summary>Serious (<c>notify</c>), applicable to this PC, and not dismissed — newest first, id as the tiebreak.</summary>
    public static KnownIssueNoticeSelection Select(
        IReadOnlyList<KnownIssue> issues, Version? running, IReadOnlySet<string> dismissed) =>
        new(issues
            .Where(i => i.Notify && KnownIssueApplicability.Applies(i, running) && !dismissed.Contains(i.Id))
            .OrderByDescending(i => i.PostedAt)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList());

    /// <summary>The menu count: every entry that applies to this PC, serious or not.</summary>
    public static int CountApplicable(IReadOnlyList<KnownIssue> issues, Version? running) =>
        issues.Count(i => KnownIssueApplicability.Applies(i, running));

    /// <summary>Page order: serious first, then newest, then id so the order never shuffles between refreshes.</summary>
    public static IReadOnlyList<KnownIssue> PageOrder(IReadOnlyList<KnownIssue> issues) =>
        issues
            .OrderByDescending(i => i.Notify)
            .ThenByDescending(i => i.PostedAt)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
}
```

`src/ROROROblox.Core/KnownIssues/RunningRobloxVersion.cs`:

```csharp
namespace ROROROblox.Core.KnownIssues;

/// <summary>
/// The Roblox version a launch will actually run: the <c>roblox-player</c> handler's binary, falling
/// back to the newest installed folder only when the handler cannot be read (absent, strap-owned, or
/// unreadable). Reuses <see cref="RobloxCompatChecker"/>'s two readers rather than adding a third copy.
/// </summary>
public static class RunningRobloxVersion
{
    public static Version? Read() =>
        Read(RobloxCompatChecker.GetHandlerRobloxVersion, RobloxCompatChecker.GetInstalledRobloxVersion);

    public static Version? Read(Func<string?> handlerVersion, Func<string?> installedVersion)
    {
        ArgumentNullException.ThrowIfNull(handlerVersion);
        ArgumentNullException.ThrowIfNull(installedVersion);

        if (RobloxVersion.TryParseInstalled(SafeRead(handlerVersion), out var handler))
        {
            return handler;
        }

        return RobloxVersion.TryParseInstalled(SafeRead(installedVersion), out var installed) ? installed : null;
    }

    private static string? SafeRead(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            // "We don't know" is the fallback signal, not an error: the notice still shows.
            return null;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownIssues"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/KnownIssues src/ROROROblox.Tests/KnownIssues
git commit -m "feat(known-issues): match entries against the version a launch will run, and pick the notice

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: The feed — download, verify, validate, cache

**Files:**
- Create: `src/ROROROblox.Core/KnownIssues/KnownIssuesFeed.cs`
- Modify: `src/ROROROblox.Tests/TypedHttpClientRegistrationTests.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/KnownIssuesFeedTests.cs`

**Interfaces:**
- Consumes: `KnownIssuesParser`, `RobloxCompatSignature.Verify`, `RobloxCompatSigningKey.PublicKeySpki`.
- Produces: `interface IKnownIssuesFeed { KnownIssuesDocument? LoadCache(); Task<KnownIssuesFetch> FetchAsync(CancellationToken = default); }`; `KnownIssuesFeed(HttpClient, ILogger<KnownIssuesFeed>? = null, string? cacheDirectory = null, byte[]? pinnedPublicKey = null)`; `enum KnownIssuesRefreshKind { Updated, NetworkFailed, SignatureRejected, Invalid, TooLarge }`; `record KnownIssuesFetch(KnownIssuesRefreshKind Kind, KnownIssuesDocument? Document, IReadOnlyList<KnownIssuesProblem> Problems)`; constants `KnownIssuesFeed.FeedUrl`, `SignatureUrl`, `MaxDocumentBytes` (262144), `CacheFileName` (`"known-issues.cache.json"`, internal).

- [ ] **Step 1: Write the failing tests**

`src/ROROROblox.Tests/KnownIssues/KnownIssuesFeedTests.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ROROROblox.Core;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// The feed is signed exactly like <c>roblox-compat.json</c>: the signature covers the raw downloaded
/// bytes and nothing is parsed before it verifies. Every row of the spec's §2 failure table has a test
/// here, including the log level it writes. Ephemeral test keypairs only — the production private key
/// lives in CI.
/// </summary>
public sealed class KnownIssuesFeedTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "rororo-known-issues-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly StubHttpHandler _stub = new();
    private readonly CapturingLogger<KnownIssuesFeed> _log = new();

    private static readonly byte[] ValidBody = Encoding.UTF8.GetBytes("""
        { "schemaVersion": 1, "issues": [ { "id": "a", "postedAt": "2026-09-23", "notify": true,
          "title": "t", "symptom": "s", "workaround": "w" } ] }
        """);

    public void Dispose()
    {
        _signer.Dispose();
        try { if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true); } catch (IOException) { }
    }

    private KnownIssuesFeed Feed() =>
        new(new HttpClient(_stub), _log, _cacheDir, _signer.ExportSubjectPublicKeyInfo());

    private byte[] Sign(byte[] data) => _signer.SignData(data, HashAlgorithmName.SHA256, RobloxCompatSignature.Format);

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private void EnqueueSigned(byte[] body)
    {
        _stub.EnqueueResponse(Ok(body));
        _stub.EnqueueResponse(Ok(Sign(body)));
    }

    private string CachePath => Path.Combine(_cacheDir, KnownIssuesFeed.CacheFileName);

    [Fact]
    public async Task AVerifiedValidFileIsReturnedAndSavedForNextTime()
    {
        EnqueueSigned(ValidBody);

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.Updated, fetch.Kind);
        Assert.Equal("a", Assert.Single(fetch.Document!.Issues).Id);
        Assert.Equal("a", Assert.Single(Feed().LoadCache()!.Issues).Id);
    }

    [Fact]
    public async Task ItAsksForTheFileAndItsSignatureFromTheLatestRelease()
    {
        EnqueueSigned(ValidBody);

        await Feed().FetchAsync();

        Assert.Equal(KnownIssuesFeed.FeedUrl, _stub.Requests[0].RequestUri!.ToString());
        Assert.Equal(KnownIssuesFeed.SignatureUrl, _stub.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task ASignatureThatDoesNotVerifyIsRejected_NeverCached_AndLoggedAsAWarning()
    {
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _stub.EnqueueResponse(Ok(ValidBody));
        _stub.EnqueueResponse(Ok(stranger.SignData(ValidBody, HashAlgorithmName.SHA256, RobloxCompatSignature.Format)));

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.SignatureRejected, fetch.Kind);
        Assert.Null(fetch.Document);
        Assert.False(File.Exists(CachePath));
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Warning]") && l.Contains("signature did not verify"));
    }

    [Fact]
    public async Task ASignedFileInANewerFormatIsInvalid_NotCached_AndLoggedAsAWarning()
    {
        EnqueueSigned(Encoding.UTF8.GetBytes("{ \"schemaVersion\": 2, \"issues\": [] }"));

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.Invalid, fetch.Kind);
        Assert.Contains(fetch.Problems, p => p.Kind == KnownIssuesProblemKind.UnsupportedSchemaVersion);
        Assert.False(File.Exists(CachePath));
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Warning]") && l.Contains("not valid"));
    }

    [Fact]
    public async Task NoNetworkIsANetworkFailure_LoggedAtDebug()
    {
        _stub.EnqueueResponse(_ => throw new HttpRequestException("offline"));

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.NetworkFailed, fetch.Kind);
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Debug]") && l.Contains("could not reach"));
    }

    [Fact]
    public async Task AMissingSignatureIsANetworkFailure()
    {
        _stub.EnqueueResponse(Ok(ValidBody));
        _stub.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Equal(KnownIssuesRefreshKind.NetworkFailed, (await Feed().FetchAsync()).Kind);
    }

    /// <summary>Refused before the signature is fetched, let alone checked: one request, not two.</summary>
    [Fact]
    public async Task AnOversizedFileIsRefusedBeforeItsSignatureIsChecked()
    {
        _stub.EnqueueResponse(Ok(new byte[KnownIssuesFeed.MaxDocumentBytes + 1]));

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.TooLarge, fetch.Kind);
        Assert.Single(_stub.Requests);
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Warning]") && l.Contains("larger than"));
    }

    [Fact]
    public void WithNoSavedCopyLoadCacheReturnsNothing_AtDebug()
    {
        Assert.Null(Feed().LoadCache());
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Debug]") && l.Contains("no saved copy"));
    }

    /// <summary>
    /// The guarantee the cache rests on: a copy on disk is trusted only if it still verifies. Proven by
    /// breaking the check on purpose (Task 3, Step 5) and watching this fail.
    /// </summary>
    [Fact]
    public async Task ChangingOneByteOfTheSavedCopyGetsItIgnored()
    {
        EnqueueSigned(ValidBody);
        await Feed().FetchAsync();

        var bytes = File.ReadAllBytes(CachePath);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(CachePath, bytes);

        Assert.Null(Feed().LoadCache());
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Warning]") && l.Contains("failed its signature check"));
    }

    /// <summary>Review Focus 3: a crash mid-write leaves half a file. Ignored, never thrown.</summary>
    [Fact]
    public async Task ATruncatedSavedCopyIsIgnoredWithoutThrowing()
    {
        EnqueueSigned(ValidBody);
        await Feed().FetchAsync();

        var bytes = File.ReadAllBytes(CachePath);
        File.WriteAllBytes(CachePath, bytes[..(bytes.Length / 2)]);

        Assert.Null(Feed().LoadCache());
    }

    [Fact]
    public async Task AValidEmptyListIsAnUpdate_ItIsHowEveryIssueIsRetired()
    {
        EnqueueSigned(Encoding.UTF8.GetBytes("{ \"schemaVersion\": 1, \"issues\": [] }"));

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.Updated, fetch.Kind);
        Assert.Empty(fetch.Document!.Issues);
    }
}
```

In `src/ROROROblox.Tests/TypedHttpClientRegistrationTests.cs`, add to the `[Theory]` a line `[InlineData(typeof(ROROROblox.Core.KnownIssues.IKnownIssuesFeed))]`, and inside the method, after `services.AddHttpClient<ROROROblox.App.Notify.NtfySender>();`, add:

```csharp
        services.AddHttpClient<ROROROblox.Core.KnownIssues.IKnownIssuesFeed, ROROROblox.Core.KnownIssues.KnownIssuesFeed>();
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build ROROROblox.slnx -c Release`
Expected: FAIL — `The type or namespace name 'KnownIssuesFeed' could not be found`.

- [ ] **Step 3: Write the feed**

`src/ROROROblox.Core/KnownIssues/KnownIssuesFeed.cs`:

```csharp
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core.KnownIssues;

public enum KnownIssuesRefreshKind
{
    Updated,
    NetworkFailed,
    SignatureRejected,
    Invalid,
    TooLarge,
}

public sealed record KnownIssuesFetch(
    KnownIssuesRefreshKind Kind,
    KnownIssuesDocument? Document,
    IReadOnlyList<KnownIssuesProblem> Problems);

public interface IKnownIssuesFeed
{
    /// <summary>The saved copy, if it still verifies and validates; otherwise null. Never throws.</summary>
    KnownIssuesDocument? LoadCache();

    /// <summary>Downloads, verifies and validates the published file; caches it only on <see cref="KnownIssuesRefreshKind.Updated"/>.</summary>
    Task<KnownIssuesFetch> FetchAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Known Roblox issues, signed like <c>roblox-compat.json</c> (see <see cref="RobloxCompatChecker"/>):
/// the raw bytes are verified against <see cref="RobloxCompatSigningKey"/> before anything is parsed,
/// and nothing unverified is parsed, cached or trusted. Stateless and transient — the snapshot lives in
/// <see cref="KnownIssuesState"/>.
/// <para>
/// ONE public constructor: the typed-HttpClient activator requires exactly one applicable constructor
/// (<c>TypedHttpClientRegistrationTests</c>). DI supplies the <see cref="HttpClient"/> and the logger;
/// tests pass a scratch cache folder and an ephemeral key.
/// </para>
/// </summary>
public sealed class KnownIssuesFeed : IKnownIssuesFeed
{
    public const string FeedUrl =
        "https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/known-issues.json";

    public const string SignatureUrl = FeedUrl + ".sig";

    /// <summary>256 KB — far above any real list. Checked before the signature is fetched.</summary>
    public const int MaxDocumentBytes = 256 * 1024;

    /// <summary>A P-256 P1363 signature is 64 bytes; anything past 1 KB is not a signature.</summary>
    private const int MaxSignatureBytes = 1024;

    internal const string CacheFileName = "known-issues.cache.json";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly ILogger<KnownIssuesFeed> _log;
    private readonly string _cacheDirectory;
    private readonly byte[] _pinnedPublicKey;

    public KnownIssuesFeed(
        HttpClient httpClient,
        ILogger<KnownIssuesFeed>? log = null,
        string? cacheDirectory = null,
        byte[]? pinnedPublicKey = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _log = log ?? NullLogger<KnownIssuesFeed>.Instance;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ROROROblox");
        _pinnedPublicKey = pinnedPublicKey ?? RobloxCompatSigningKey.PublicKeySpki;
    }

    private string CachePath => Path.Combine(_cacheDirectory, CacheFileName);

    private string CacheSignaturePath => CachePath + ".sig";

    public KnownIssuesDocument? LoadCache()
    {
        byte[] body;
        byte[] signature;
        try
        {
            if (!File.Exists(CachePath) || !File.Exists(CacheSignaturePath))
            {
                _log.LogDebug("Known issues: no saved copy yet.");
                return null;
            }

            body = File.ReadAllBytes(CachePath);
            signature = File.ReadAllBytes(CacheSignaturePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Known issues: the saved copy could not be read; ignoring it.");
            return null;
        }

        if (!RobloxCompatSignature.Verify(_pinnedPublicKey, body, signature))
        {
            _log.LogWarning("Known issues: the saved copy failed its signature check; ignoring it.");
            return null;
        }

        var parsed = KnownIssuesParser.Parse(body);
        if (!parsed.IsValid)
        {
            _log.LogWarning(
                "Known issues: the saved copy is signed but not valid ({Problems}); ignoring it.",
                Describe(parsed.Problems));
            return null;
        }

        return parsed.Document;
    }

    public async Task<KnownIssuesFetch> FetchAsync(CancellationToken cancellationToken = default)
    {
        byte[]? body;
        byte[]? signature;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);

            body = await GetLimitedAsync(FeedUrl, MaxDocumentBytes, cts.Token).ConfigureAwait(false);
            if (body is null)
            {
                _log.LogWarning(
                    "Known issues: the published file is larger than {Limit} bytes; refused before checking its signature.",
                    MaxDocumentBytes);
                return new(KnownIssuesRefreshKind.TooLarge, null, []);
            }

            signature = await GetLimitedAsync(SignatureUrl, MaxSignatureBytes, cts.Token).ConfigureAwait(false);
            if (signature is null)
            {
                _log.LogWarning("Known issues: the published signature is larger than {Limit} bytes; refused.", MaxSignatureBytes);
                return new(KnownIssuesRefreshKind.TooLarge, null, []);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _log.LogDebug(ex, "Known issues: could not reach the release; keeping what we have.");
            return new(KnownIssuesRefreshKind.NetworkFailed, null, []);
        }

        if (!RobloxCompatSignature.Verify(_pinnedPublicKey, body, signature))
        {
            _log.LogWarning("Known issues: the published file's signature did not verify; keeping what we have.");
            return new(KnownIssuesRefreshKind.SignatureRejected, null, []);
        }

        var parsed = KnownIssuesParser.Parse(body);
        if (!parsed.IsValid)
        {
            _log.LogWarning(
                "Known issues: the published file is signed but not valid ({Problems}); keeping what we have.",
                Describe(parsed.Problems));
            return new(KnownIssuesRefreshKind.Invalid, null, parsed.Problems);
        }

        TryWriteCache(body, signature);
        return new(KnownIssuesRefreshKind.Updated, parsed.Document, []);
    }

    /// <summary>The body, or null when it is larger than <paramref name="limit"/>. Streams, so an oversized body is never buffered whole.</summary>
    private async Task<byte[]?> GetLimitedAsync(string url, int limit, CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long declared && declared > limit)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private void TryWriteCache(byte[] body, byte[] signature)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var tempBody = CachePath + ".tmp";
            var tempSignature = CacheSignaturePath + ".tmp";
            File.WriteAllBytes(tempBody, body);
            File.WriteAllBytes(tempSignature, signature);
            File.Move(tempBody, CachePath, overwrite: true);
            File.Move(tempSignature, CacheSignaturePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Known issues: could not save a copy for next time; the list still shows this session.");
        }
    }

    private static string Describe(IReadOnlyList<KnownIssuesProblem> problems) =>
        string.Join(", ", problems.Select(p =>
            p.Kind + (p.IssueId is null ? string.Empty : $" '{p.IssueId}'") + (p.Field is null ? string.Empty : $" {p.Field}")));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownIssues|FullyQualifiedName~TypedHttpClientRegistrationTests"`
Expected: PASS, including `TypedHttpClient_Resolves_WithExactlyOneApplicableCtor(IKnownIssuesFeed)`.

- [ ] **Step 5: Break the signature check on purpose and watch the tamper test fail**

In `KnownIssuesFeed.LoadCache`, temporarily change `if (!RobloxCompatSignature.Verify(_pinnedPublicKey, body, signature))` to `if (false)`.

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~ChangingOneByteOfTheSavedCopyGetsItIgnored"`
Expected: FAIL. If it passes, the test is not guarding the signature and must be fixed before going on.

Restore the line exactly, rebuild, and re-run the filter from Step 4: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/KnownIssues/KnownIssuesFeed.cs src/ROROROblox.Tests/KnownIssues/KnownIssuesFeedTests.cs src/ROROROblox.Tests/TypedHttpClientRegistrationTests.cs
git commit -m "feat(known-issues): a signed feed that verifies before it parses and re-verifies its saved copy

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: The snapshot holder — serialised refresh and the summary log line

**Files:**
- Create: `src/ROROROblox.Core/KnownIssues/KnownIssuesState.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/KnownIssuesStateTests.cs`

**Interfaces:**
- Consumes: `IKnownIssuesFeed`, `KnownIssuesFetch`, `KnownIssuesRefreshKind`.
- Produces: `enum KnownIssuesSource { None, SavedCopy, Release }`; `record KnownIssuesSnapshot(IReadOnlyList<KnownIssue> Issues, KnownIssuesSource Source, DateTimeOffset? CheckedAt)` with static `Empty`; `KnownIssuesState(TimeProvider, ILogger<KnownIssuesState>)` with `Current`, `event EventHandler<KnownIssuesSnapshot>? Changed`, `void LoadSavedCopy(IKnownIssuesFeed)`, `Task<KnownIssuesRefreshKind> RefreshAsync(IKnownIssuesFeed, CancellationToken = default)`.

- [ ] **Step 1: Write the failing tests**

`src/ROROROblox.Tests/KnownIssues/KnownIssuesStateTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

public class KnownIssuesStateTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly CapturingLogger<KnownIssuesState> _log = new();

    private KnownIssuesState State() => new(_clock, _log);

    private static KnownIssuesDocument Doc(params KnownIssue[] issues) => new(1, issues);

    private sealed class ScriptedFeed : IKnownIssuesFeed
    {
        private int _inFlight;

        public KnownIssuesDocument? Saved { get; init; }

        public Queue<Func<Task<KnownIssuesFetch>>> Fetches { get; } = new();

        public int MostAtOnce { get; private set; }

        public KnownIssuesDocument? LoadCache() => Saved;

        public async Task<KnownIssuesFetch> FetchAsync(CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _inFlight);
            MostAtOnce = Math.Max(MostAtOnce, now);
            try
            {
                return await Fetches.Dequeue()();
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public void Returns(KnownIssuesRefreshKind kind, KnownIssuesDocument? doc = null) =>
            Fetches.Enqueue(() => Task.FromResult(new KnownIssuesFetch(kind, doc, [])));
    }

    [Fact]
    public void TheSavedCopyIsShownFirst_NotYetChecked()
    {
        var state = State();
        KnownIssuesSnapshot? raised = null;
        state.Changed += (_, s) => raised = s;

        state.LoadSavedCopy(new ScriptedFeed { Saved = Doc(Issue("a"), Issue("b", notify: false)) });

        Assert.Equal(KnownIssuesSource.SavedCopy, state.Current.Source);
        Assert.Null(state.Current.CheckedAt);
        Assert.Equal(2, state.Current.Issues.Count);
        Assert.Same(state.Current, raised);
        Assert.Contains(_log.Snapshot(), l => l == "[Information] Known issues: 2 entries, 1 with a notice, from the saved copy.");
    }

    [Fact]
    public void WithNoSavedCopyNothingChanges()
    {
        var state = State();
        var raised = false;
        state.Changed += (_, _) => raised = true;

        state.LoadSavedCopy(new ScriptedFeed());

        Assert.Same(KnownIssuesSnapshot.Empty, state.Current);
        Assert.False(raised);
    }

    [Fact]
    public async Task AnUpdateReplacesTheListAndStampsTheCheck()
    {
        var state = State();
        var feed = new ScriptedFeed();
        feed.Returns(KnownIssuesRefreshKind.Updated, Doc(Issue("a")));

        var kind = await state.RefreshAsync(feed);

        Assert.Equal(KnownIssuesRefreshKind.Updated, kind);
        Assert.Equal(KnownIssuesSource.Release, state.Current.Source);
        Assert.Equal(Noon, state.Current.CheckedAt);
        Assert.Contains(_log.Snapshot(), l => l == "[Information] Known issues: 1 entries, 1 with a notice, from the release.");
    }

    [Fact]
    public async Task AFailureKeepsTheListButStillStampsTheCheck_AndSaysWhy()
    {
        var state = State();
        var feed = new ScriptedFeed { Saved = Doc(Issue("a")) };
        state.LoadSavedCopy(feed);
        feed.Returns(KnownIssuesRefreshKind.NetworkFailed);

        await state.RefreshAsync(feed);

        Assert.Equal("a", Assert.Single(state.Current.Issues).Id);
        Assert.Equal(KnownIssuesSource.SavedCopy, state.Current.Source);
        Assert.Equal(Noon, state.Current.CheckedAt);
        Assert.Contains(_log.Snapshot(), l => l == "[Information] Known issues: kept 1 entries from SavedCopy: NetworkFailed.");
    }

    [Fact]
    public async Task AnEmptyPublishedListRetiresEverything()
    {
        var state = State();
        var feed = new ScriptedFeed { Saved = Doc(Issue("a")) };
        state.LoadSavedCopy(feed);
        feed.Returns(KnownIssuesRefreshKind.Updated, Doc());

        await state.RefreshAsync(feed);

        Assert.Empty(state.Current.Issues);
    }

    /// <summary>Review Focus 4: the startup download is still running when a tick fires.</summary>
    [Fact]
    public async Task OverlappingRefreshesRunOneAtATime()
    {
        var state = State();
        var feed = new ScriptedFeed();
        var first = new TaskCompletionSource<KnownIssuesFetch>();
        var second = new TaskCompletionSource<KnownIssuesFetch>();
        feed.Fetches.Enqueue(() => first.Task);
        feed.Fetches.Enqueue(() => second.Task);

        var a = state.RefreshAsync(feed);
        var b = state.RefreshAsync(feed);
        first.SetResult(new KnownIssuesFetch(KnownIssuesRefreshKind.NetworkFailed, null, []));
        second.SetResult(new KnownIssuesFetch(KnownIssuesRefreshKind.Updated, Doc(Issue("a")), []));
        await Task.WhenAll(a, b);

        Assert.Equal(1, feed.MostAtOnce);
        Assert.Equal("a", Assert.Single(state.Current.Issues).Id);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build ROROROblox.slnx -c Release`
Expected: FAIL — `The type or namespace name 'KnownIssuesState' could not be found`.

- [ ] **Step 3: Write the state holder**

`src/ROROROblox.Core/KnownIssues/KnownIssuesState.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace ROROROblox.Core.KnownIssues;

public enum KnownIssuesSource
{
    None,
    SavedCopy,
    Release,
}

/// <param name="CheckedAt">When the last download attempt finished, whatever its outcome; null until the first one does.</param>
public sealed record KnownIssuesSnapshot(IReadOnlyList<KnownIssue> Issues, KnownIssuesSource Source, DateTimeOffset? CheckedAt)
{
    public static readonly KnownIssuesSnapshot Empty = new([], KnownIssuesSource.None, null);
}

/// <summary>
/// The one live copy of the list. Singleton; the feed it drives is transient and stateless.
/// <para>
/// Every attempt ends with one Information line saying what happened. The metric path showed on
/// 2026-09-21 what a path that writes nothing costs: no incident on it could be reconstructed.
/// Failures keep the list and still stamp <see cref="KnownIssuesSnapshot.CheckedAt"/>, so the page's
/// "Last checked" line tells the truth about the attempt, not about the last success.
/// </para>
/// </summary>
public sealed class KnownIssuesState
{
    private readonly TimeProvider _time;
    private readonly ILogger<KnownIssuesState> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private KnownIssuesSnapshot _current = KnownIssuesSnapshot.Empty;

    public KnownIssuesState(TimeProvider time, ILogger<KnownIssuesState> log)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public KnownIssuesSnapshot Current => Volatile.Read(ref _current);

    /// <summary>Raised on the calling thread — for a refresh, a thread-pool thread. Listeners marshal.</summary>
    public event EventHandler<KnownIssuesSnapshot>? Changed;

    public void LoadSavedCopy(IKnownIssuesFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var document = feed.LoadCache();
        if (document is null)
        {
            return;
        }

        _log.LogInformation(
            "Known issues: {Count} entries, {NotifyCount} with a notice, from the saved copy.",
            document.Issues.Count,
            document.Issues.Count(i => i.Notify));
        Publish(new KnownIssuesSnapshot(document.Issues, KnownIssuesSource.SavedCopy, null));
    }

    public async Task<KnownIssuesRefreshKind> RefreshAsync(IKnownIssuesFeed feed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fetch = await feed.FetchAsync(cancellationToken).ConfigureAwait(false);
            var now = _time.GetUtcNow();

            KnownIssuesSnapshot next;
            if (fetch.Kind == KnownIssuesRefreshKind.Updated && fetch.Document is { } document)
            {
                next = new KnownIssuesSnapshot(document.Issues, KnownIssuesSource.Release, now);
                _log.LogInformation(
                    "Known issues: {Count} entries, {NotifyCount} with a notice, from the release.",
                    next.Issues.Count,
                    next.Issues.Count(i => i.Notify));
            }
            else
            {
                next = Current with { CheckedAt = now };
                _log.LogInformation(
                    "Known issues: kept {Count} entries from {Source}: {Outcome}.",
                    next.Issues.Count,
                    next.Source,
                    fetch.Kind);
            }

            Publish(next);
            return fetch.Kind;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Publish(KnownIssuesSnapshot snapshot)
    {
        Volatile.Write(ref _current, snapshot);
        Changed?.Invoke(this, snapshot);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownIssues"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/KnownIssues/KnownIssuesState.cs src/ROROROblox.Tests/KnownIssues/KnownIssuesStateTests.cs
git commit -m "feat(known-issues): one live snapshot, refreshes one at a time, and a log line for every attempt

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Dismissals

**Files:**
- Create: `src/ROROROblox.Core/KnownIssues/KnownIssuesDismissals.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/KnownIssuesDismissalsTests.cs`

**Interfaces:**
- Produces: `KnownIssuesDismissals(string path)`, `static string DefaultPath`, `const string FileName` (`"known-issues-dismissed.json"`), `IReadOnlySet<string> Load()`, `IReadOnlySet<string> Dismiss(IEnumerable<string> ids, IEnumerable<string> liveIds)`.

- [ ] **Step 1: Write the failing tests**

`src/ROROROblox.Tests/KnownIssues/KnownIssuesDismissalsTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build ROROROblox.slnx -c Release`
Expected: FAIL — `The type or namespace name 'KnownIssuesDismissals' could not be found`.

- [ ] **Step 3: Write the store**

`src/ROROROblox.Core/KnownIssues/KnownIssuesDismissals.cs`:

```csharp
using System.IO;
using System.Text.Json;

namespace ROROROblox.Core.KnownIssues;

/// <summary>
/// Which entry ids the user has closed the notice for. A file of its own rather than a
/// <c>SettingsBlob</c> key: it is state no control edits, and a setting would bring an
/// <c>IAppSettings</c> member, four private test fakes and <c>SettingsReachabilityTests</c> for nothing.
/// Every read and write degrades to "nothing dismissed" rather than throwing — the cost of a lost file
/// is one repeated notice.
/// </summary>
public sealed class KnownIssuesDismissals
{
    public const string FileName = "known-issues-dismissed.json";

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _path;

    public KnownIssuesDismissals(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        FileName);

    public IReadOnlySet<string> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllBytes(_path), Options);
            return new HashSet<string>(
                (dto?.Dismissed ?? []).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Adds <paramref name="ids"/>, keeps only ids still in <paramref name="liveIds"/>, writes, and returns the result.</summary>
    public IReadOnlySet<string> Dismiss(IEnumerable<string> ids, IEnumerable<string> liveIds)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(liveIds);

        var live = new HashSet<string>(liveIds, StringComparer.Ordinal);
        var next = new HashSet<string>(Load().Concat(ids).Where(live.Contains), StringComparer.Ordinal);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var dto = new Dto { Dismissed = next.Order(StringComparer.Ordinal).ToList() };
            File.WriteAllBytes(_path, JsonSerializer.SerializeToUtf8Bytes(dto, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still honoured for this session through the returned set.
        }

        return next;
    }

    internal sealed class Dto
    {
        public List<string>? Dismissed { get; set; }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownIssues"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/KnownIssues/KnownIssuesDismissals.cs src/ROROROblox.Tests/KnownIssues/KnownIssuesDismissalsTests.cs
git commit -m "feat(known-issues): remember closed notices in a file of their own, pruned to the live feed

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Publishing — validate, sign, attach, and guard it

**Files:**
- Create: `known-issues.json` (repo root)
- Modify: `tools/CompatSigner/Program.cs`
- Modify: `.github/workflows/compat.yml`
- Modify: `.github/workflows/release.yml`
- Modify: `src/ROROROblox.Core/RobloxCompatSigningKey.cs` (doc comment only)
- Test: `src/ROROROblox.Tests/KnownIssues/KnownIssuesPublishingFenceTests.cs`

**Interfaces:**
- Consumes: `KnownIssuesParser.Parse` (public, from Task 1).
- Produces: `dotnet run --project tools/CompatSigner -- --validate-known-issues <path>` → exit 0 valid, 1 invalid or missing.

- [ ] **Step 1: Write the failing guard tests**

`src/ROROROblox.Tests/KnownIssues/KnownIssuesPublishingFenceTests.cs`:

```csharp
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// The app reads only from the release marked Latest, so a release without the feed silences the
/// page the day it ships. Nothing like this existed for plugins-catalog.json, which is how it sat at
/// Ur Score 0.3.5 for months.
/// </summary>
public class KnownIssuesPublishingFenceTests
{
    private static string Root()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx above the test assembly.");
        return root!;
    }

    [Theory]
    [InlineData("release.yml")]
    [InlineData("compat.yml")]
    public void TheWorkflowValidatesTheFeed_ThenAttachesItWithItsSignature(string workflow)
    {
        var text = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", workflow));

        Assert.Contains("--validate-known-issues", text);
        Assert.Contains("known-issues.json known-issues.json.sig", text);
    }

    /// <summary>A bad edit fails CI here, before anyone runs a workflow against it.</summary>
    [Fact]
    public void TheCommittedFeedIsValid()
    {
        var result = KnownIssuesParser.Parse(File.ReadAllBytes(Path.Combine(Root(), "known-issues.json")));

        Assert.True(result.IsValid, string.Join(", ", result.Problems.Select(p => $"{p.Kind} {p.IssueId} {p.Field}")));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownIssuesPublishingFenceTests"`
Expected: FAIL — three failures: `Assert.Contains()` for both workflows and `FileNotFoundException` for `known-issues.json`.

- [ ] **Step 3: Add the feed file**

`known-issues.json` at the repo root. It starts empty: the first real entry (the window freeze) waits on checking its frame-rate advice (spec, Risks).

```json
{
  "schemaVersion": 1,
  "issues": []
}
```

- [ ] **Step 4: Add the validate mode to CompatSigner**

In `tools/CompatSigner/Program.cs`, insert directly above the existing `if (args.Length != 1)` block:

```csharp
// --validate-known-issues <path>: run the app's own validator (KnownIssuesParser, in Core) so the
// workflow can never sign a known-issues.json that every client would refuse. Exit 0 valid, 1 not.
if (args is ["--validate-known-issues", var knownIssuesPath])
{
    Environment.Exit(ValidateKnownIssues(knownIssuesPath));
    return;
}

```

Change the existing usage line inside that block to:

```csharp
    Console.Error.WriteLine("Usage: CompatSigner <path-to-file-to-sign> | CompatSigner --validate-known-issues <path>");
```

And add this local function at the bottom of the file, after `NormalizeKey`:

```csharp
static int ValidateKnownIssues(string path)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"CompatSigner: file not found: {path}");
        return 1;
    }

    var result = ROROROblox.Core.KnownIssues.KnownIssuesParser.Parse(File.ReadAllBytes(path));
    if (result.IsValid)
    {
        var issues = result.Document!.Issues;
        Console.WriteLine($"known-issues.json is valid: {issues.Count} issue(s), {issues.Count(i => i.Notify)} with a notice.");
        return 0;
    }

    foreach (var problem in result.Problems)
    {
        Console.Error.WriteLine(
            $"known-issues.json: {problem.Kind}"
            + (problem.IssueId is null ? string.Empty : $" in '{problem.IssueId}'")
            + (problem.Field is null ? string.Empty : $" at {problem.Field}"));
    }

    return 1;
}
```

Update the header comment's first sentence to read: `CompatSigner: standalone CLI that detached-signs an arbitrary already-written file (roblox-compat.json and known-issues.json; plugins-catalog.json is a natural follow-up, out of scope for now)`, and add after the `Usage:` line: `//        dotnet run --project tools/CompatSigner -- --validate-known-issues <path-to-known-issues.json>`.

- [ ] **Step 5: Check the validate mode by hand**

```bash
dotnet run --project tools/CompatSigner --configuration Release -- --validate-known-issues known-issues.json
echo "exit=$?"
printf '{ "schemaVersion": 2, "issues": [] }' > "$TEMP/bad-known-issues.json"
dotnet run --project tools/CompatSigner --configuration Release -- --validate-known-issues "$TEMP/bad-known-issues.json"
echo "exit=$?"
```

Expected: the first prints `known-issues.json is valid: 0 issue(s), 0 with a notice.` and `exit=0`; the second prints `known-issues.json: UnsupportedSchemaVersion at schemaVersion` and `exit=1`.

- [ ] **Step 6: Extend compat.yml**

In `.github/workflows/compat.yml`:

(a) Change the job's `name:` from `sign + upload roblox-compat.json` to `sign + upload roblox-compat.json and known-issues.json`, and add this paragraph to the header comment after the "How to use" paragraph:

```yaml
# known-issues.json rides the same run: edit it on main, merge, run this workflow. It is validated
# by the app's own parser (tools/CompatSigner --validate-known-issues), signed with the same key,
# and attached beside roblox-compat.json. Clients pick it up within four hours, or on next start.
```

(b) Add this step immediately after the `Validate roblox-compat.json shape` step:

```yaml
      - name: Validate known-issues.json
        shell: pwsh
        run: |
          # The SAME validator the app runs after downloading (KnownIssuesParser, in Core), so this
          # workflow cannot sign a file every client would refuse.
          $issuesPath = (Resolve-Path 'known-issues.json').Path
          dotnet run --project tools/CompatSigner --configuration Release -- --validate-known-issues $issuesPath
          if ($LASTEXITCODE -ne 0) { throw "known-issues.json failed validation (exit $LASTEXITCODE)" }
```

(c) Rename the `Sign compat config` step to `Sign compat config and known issues`, and append to its `run:` block:

```yaml
          $issuesPath = (Resolve-Path 'known-issues.json').Path
          dotnet run --project tools/CompatSigner --configuration Release -- $issuesPath
          if ($LASTEXITCODE -ne 0) { throw "CompatSigner failed on known-issues.json (exit $LASTEXITCODE)" }
          if (-not (Test-Path 'known-issues.json.sig') -or (Get-Item 'known-issues.json.sig').Length -eq 0) { throw "No known-issues signature written." }
```

(d) In the `Upload to the release` step, directly after the existing `gh release upload … roblox-compat.json roblox-compat.json.sig …` line and its `$LASTEXITCODE` check, add:

```yaml
          gh release upload '${{ steps.rel.outputs.tag }}' known-issues.json known-issues.json.sig --clobber --repo $env:GITHUB_REPOSITORY
          if ($LASTEXITCODE -ne 0) { throw "gh release upload of known-issues.json failed (exit $LASTEXITCODE)" }
```

(e) At the end of that step's `run:` block, after the existing summary `"@ | Out-File $env:GITHUB_STEP_SUMMARY -Append`, add a second summary block at the same indentation:

```yaml
          @"

          ## known-issues.json pushed to ${{ steps.rel.outputs.tag }}

          ``````json
          $(Get-Content known-issues.json -Raw)
          ``````
          "@ | Out-File $env:GITHUB_STEP_SUMMARY -Append
```

- [ ] **Step 7: Extend release.yml**

In `.github/workflows/release.yml`:

(a) In the `Sign compat config` step, append to its `run:` block:

```yaml
          # known-issues.json is validated by the app's own parser, then signed with the same key, so
          # KnownIssuesFeed can verify it exactly as RobloxCompatChecker verifies roblox-compat.json.
          $issuesPath = (Resolve-Path 'known-issues.json').Path
          dotnet run --project tools/CompatSigner --configuration Release -- --validate-known-issues $issuesPath
          if ($LASTEXITCODE -ne 0) { throw "known-issues.json failed validation (exit $LASTEXITCODE)" }
          dotnet run --project tools/CompatSigner --configuration Release -- $issuesPath
          if ($LASTEXITCODE -ne 0) { throw "CompatSigner failed on known-issues.json (exit $LASTEXITCODE)" }
```

(b) In the `Upload remote compat config` step, after the `gh release upload … docs/store/plugins-catalog.json --clobber` line, add:

```yaml
          # known-issues.json rides every release for the same reason as the two above: the app reads
          # releases/latest/download/known-issues.json (+ .sig), so a release that becomes latest
          # without it would empty the Known Roblox issues page for everyone.
          gh release upload '${{ steps.ver.outputs.tag }}' known-issues.json known-issues.json.sig --clobber
```

- [ ] **Step 8: Update the signing key's comment**

In `src/ROROROblox.Core/RobloxCompatSigningKey.cs`, add this paragraph to the class `<summary>` directly after the sentence ending `…unrelated blast radii).`:

```csharp
/// Since 2026-09-23 the same key also signs <c>known-issues.json</c> (<see cref="KnownIssues.KnownIssuesFeed"/>).
/// "One key per trust surface" separates PRODUCTS; two feeds of this one product, published by the
/// same person from the same repo, are one trust surface.
```

- [ ] **Step 9: Run the guard tests to verify they pass**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownIssuesPublishingFenceTests|FullyQualifiedName~RobloxCompatSigningKey"`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add known-issues.json tools/CompatSigner/Program.cs .github/workflows/compat.yml .github/workflows/release.yml src/ROROROblox.Core/RobloxCompatSigningKey.cs src/ROROROblox.Tests/KnownIssues/KnownIssuesPublishingFenceTests.cs
git commit -m "feat(known-issues): CI validates with the app's parser, signs with the compat key, and attaches on every release

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Strings, in all seven languages

**Files:**
- Modify: `src/ROROROblox.App/Properties/Strings.resx`
- Modify: `docs/store/translations/ui-fr.json`, `ui-de.json`, `ui-ru.json`, `ui-pt-BR.json`, `ui-pl.json`, `ui-es.json`
- Regenerated: `src/ROROROblox.App/Properties/Strings.<culture>.resx` ×6, `docs/store/ui-translations.json`

**Interfaces:**
- Produces: the resx keys every later task uses (table below). Two existing keys change value: `MainWindow_HistoryDiagnosticsPluginsLogFolder` (the Tools tooltip lists the new page) and `AboutPage_InsideTheToolsWindowCtrl` (Ctrl+6 becomes Ctrl+7).

All strings, verbatim:

| Key | en | fr | de | ru | pt-BR | pl | es |
|---|---|---|---|---|---|---|---|
| `ShellWindow_KnownRobloxIssues` | Known Roblox issues | Problèmes Roblox connus | Bekannte Roblox-Probleme | Известные проблемы Roblox | Problemas conhecidos do Roblox | Znane problemy Roblox | Problemas conocidos de Roblox |
| `Shell_Title_KnownRobloxIssues` | Known Roblox issues | Problèmes Roblox connus | Bekannte Roblox-Probleme | Известные проблемы Roblox | Problemas conhecidos do Roblox | Znane problemy Roblox | Problemas conocidos de Roblox |
| `KnownIssuesPage_Heading` | Known Roblox issues | Problèmes Roblox connus | Bekannte Roblox-Probleme | Известные проблемы Roblox | Problemas conhecidos do Roblox | Znane problemy Roblox | Problemas conocidos de Roblox |
| `KnownIssuesPage_Intro` | Problems in Roblox itself, not in RoRoRo, and what to do about them. | Des problèmes de Roblox lui-même, pas de RoRoRo, et quoi faire. | Probleme in Roblox selbst, nicht in RoRoRo, und was du dagegen tun kannst. | Проблемы в самом Roblox, а не в RoRoRo, и что с ними делать. | Problemas do próprio Roblox, não do RoRoRo, e o que fazer a respeito. | Problemy w samym Roblox, a nie w RoRoRo, i co z nimi zrobić. | Problemas del propio Roblox, no de RoRoRo, y qué hacer al respecto. |
| `KnownIssuesPage_EnglishOnly` | Issue descriptions are in English. | Les descriptions des problèmes sont en anglais. | Die Problembeschreibungen sind auf Englisch. | Описания проблем приводятся на английском. | As descrições dos problemas estão em inglês. | Opisy problemów są po angielsku. | Las descripciones de los problemas están en inglés. |
| `KnownIssuesPage_Empty` | No known Roblox issues right now. | Aucun problème Roblox connu pour le moment. | Derzeit keine bekannten Roblox-Probleme. | Сейчас известных проблем Roblox нет. | Nenhum problema conhecido do Roblox no momento. | Obecnie brak znanych problemów Roblox. | No hay problemas conocidos de Roblox ahora mismo. |
| `KnownIssuesPage_LastChecked` | Last checked at {0}. | Dernière vérification à {0}. | Zuletzt geprüft um {0}. | Последняя проверка в {0}. | Última verificação às {0}. | Ostatnie sprawdzenie o {0}. | Última comprobación a las {0}. |
| `KnownIssuesPage_Checking` | Checking for known issues… | Recherche des problèmes connus… | Suche nach bekannten Problemen… | Проверяем известные проблемы… | Verificando problemas conhecidos… | Sprawdzanie znanych problemów… | Buscando problemas conocidos… |
| `KnownIssuesPage_RobloxTag` | Roblox | Roblox | Roblox | Roblox | Roblox | Roblox | Roblox |
| `KnownIssuesPage_Posted` | Posted {0} | Publié le {0} | Veröffentlicht am {0} | Опубликовано {0} | Publicado em {0} | Opublikowano {0} | Publicado el {0} |
| `KnownIssuesPage_WhatToDo` | What to do | Que faire | Was du tun kannst | Что делать | O que fazer | Co zrobić | Qué hacer |
| `KnownIssuesPage_RororoCanHelp` | RoRoRo can help | RoRoRo peut aider | RoRoRo kann helfen | RoRoRo может помочь | O RoRoRo pode ajudar | RoRoRo może pomóc | RoRoRo puede ayudar |
| `KnownIssuesPage_VersionAffected` | Your Roblox version ({0}) is affected. | Votre version de Roblox ({0}) est concernée. | Deine Roblox-Version ({0}) ist betroffen. | Ваша версия Roblox ({0}) затронута. | Sua versão do Roblox ({0}) é afetada. | Twoja wersja Roblox ({0}) jest objęta problemem. | Tu versión de Roblox ({0}) está afectada. |
| `KnownIssuesPage_VersionFixed` | Fixed in {0}. You have {1}. | Corrigé dans la version {0}. Vous avez la {1}. | Behoben in {0}. Du hast {1}. | Исправлено в {0}. У вас {1}. | Corrigido na {0}. Você tem a {1}. | Naprawiono w {0}. Masz {1}. | Corregido en {0}. Tienes {1}. |
| `KnownIssuesPage_VersionNotYet` | Affects {0} and later. You have {1}. | Concerne la version {0} et les suivantes. Vous avez la {1}. | Betrifft {0} und neuer. Du hast {1}. | Затрагивает {0} и новее. У вас {1}. | Afeta a {0} e posteriores. Você tem a {1}. | Dotyczy wersji {0} i nowszych. Masz {1}. | Afecta a {0} y posteriores. Tienes {1}. |
| `KnownIssuesPage_VersionUnknown` | Couldn't read your Roblox version. | Impossible de lire votre version de Roblox. | Deine Roblox-Version konnte nicht gelesen werden. | Не удалось определить вашу версию Roblox. | Não foi possível ler sua versão do Roblox. | Nie udało się odczytać Twojej wersji Roblox. | No se pudo leer tu versión de Roblox. |
| `KnownIssuesPage_OpenMemorySettings` | Open memory settings | Ouvrir les paramètres de mémoire | Speichereinstellungen öffnen | Открыть настройки памяти | Abrir configurações de memória | Otwórz ustawienia pamięci | Abrir la configuración de memoria |
| `KnownIssuesPage_ShowAccounts` | Show accounts | Afficher les comptes | Konten anzeigen | Показать аккаунты | Mostrar contas | Pokaż konta | Mostrar cuentas |
| `KnownIssuesPage_OpenLink` | Open {0} in your browser | Ouvrir {0} dans votre navigateur | {0} im Browser öffnen | Открыть {0} в браузере | Abrir {0} no navegador | Otwórz {0} w przeglądarce | Abrir {0} en tu navegador |
| `MainWindow_KnownIssueNoticeOne` | Known Roblox issue: {0} | Problème Roblox connu : {0} | Bekanntes Roblox-Problem: {0} | Известная проблема Roblox: {0} | Problema conhecido do Roblox: {0} | Znany problem Roblox: {0} | Problema conocido de Roblox: {0} |
| `MainWindow_KnownIssueNoticeMany` | Known Roblox issues that affect you: {0} | Problèmes Roblox connus qui vous concernent : {0} | Bekannte Roblox-Probleme, die dich betreffen: {0} | Известные проблемы Roblox, которые вас затрагивают: {0} | Problemas conhecidos do Roblox que afetam você: {0} | Znane problemy Roblox, które Cię dotyczą: {0} | Problemas conocidos de Roblox que te afectan: {0} |
| `MainWindow_SeeKnownIssues` | See known issues | Voir les problèmes connus | Bekannte Probleme ansehen | Посмотреть известные проблемы | Ver problemas conhecidos | Zobacz znane problemy | Ver problemas conocidos |
| `MainWindow_DismissKnownIssueNotice` | Dismiss the known Roblox issue notice | Ignorer l'avis de problème Roblox connu | Hinweis zum bekannten Roblox-Problem ausblenden | Скрыть уведомление об известной проблеме Roblox | Dispensar o aviso de problema conhecido do Roblox | Odrzuć powiadomienie o znanym problemie Roblox | Descartar el aviso de problema conocido de Roblox |
| `MainWindow_KnownRobloxIssues` | Known Roblox issues | Problèmes Roblox connus | Bekannte Roblox-Probleme | Известные проблемы Roblox | Problemas conhecidos do Roblox | Znane problemy Roblox | Problemas conocidos de Roblox |
| `MainWindow_KnownRobloxIssuesCount` | Known Roblox issues ({0}) | Problèmes Roblox connus ({0}) | Bekannte Roblox-Probleme ({0}) | Известные проблемы Roblox ({0}) | Problemas conhecidos do Roblox ({0}) | Znane problemy Roblox ({0}) | Problemas conocidos de Roblox ({0}) |
| `MainWindow_KnownRobloxIssuesTooltip` | Problems in Roblox itself, and what to do about them | Des problèmes de Roblox lui-même, et quoi faire | Probleme in Roblox selbst und was du dagegen tun kannst | Проблемы в самом Roblox и что с ними делать | Problemas do próprio Roblox e o que fazer a respeito | Problemy w samym Roblox i co z nimi zrobić | Problemas del propio Roblox y qué hacer al respecto |
| `MainWindow_HistoryDiagnosticsPluginsLogFolder` *(existing, new value)* | History, Diagnostics, Plugins, Known Roblox issues, log folder, Stop all, tour, About. | Historique, Diagnostics, Plugins, Problèmes Roblox connus, dossier des journaux, Tout arrêter, visite, À propos. | Verlauf, Diagnose, Plugins, Bekannte Roblox-Probleme, Log-Ordner, Alle stoppen, Tour, Über. | История, диагностика, плагины, известные проблемы Roblox, папка журналов, «Остановить все», тур, «О программе». | Histórico, Diagnóstico, Plugins, Problemas conhecidos do Roblox, pasta de logs, Parar tudo, tour, Sobre. | Historia, Diagnostyka, Wtyczki, Znane problemy Roblox, folder dzienników, Zatrzymaj wszystko, przewodnik, O programie. | Historial, Diagnóstico, Complementos, Problemas conocidos de Roblox, carpeta de registros, Detener todo, recorrido, Acerca de. |
| `AboutPage_InsideTheToolsWindowCtrl` *(existing, new value)* | Inside the tools window, Ctrl+1 through Ctrl+7 also jump straight to a page. | *(current value with `Ctrl+6` → `Ctrl+7`)* | *(same)* | *(same)* | *(same)* | *(same)* | *(same)* |

"Many" is worded with the number last ("…that affect you: 2") so no language has to agree a noun with the count; Russian and Polish plural forms change between 2–4 and 5+.

- [ ] **Step 1: Write the one-off update script**

Save as `%TEMP%\known-issues-strings.py` (outside the repo; it is thrown away after this task). It carries the table above as data:

```python
"""One-off: add the Known Roblox issues strings to the neutral resx and the six translation sets."""
import io
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip())
RESX = ROOT / "src" / "ROROROblox.App" / "Properties" / "Strings.resx"
TRANS = ROOT / "docs" / "store" / "translations"
CULTURES = ["fr", "de", "ru", "pt-BR", "pl", "es"]

# key: [en, fr, de, ru, pt-BR, pl, es]
NEW = {
    "ShellWindow_KnownRobloxIssues": ["Known Roblox issues", "Problèmes Roblox connus", "Bekannte Roblox-Probleme", "Известные проблемы Roblox", "Problemas conhecidos do Roblox", "Znane problemy Roblox", "Problemas conocidos de Roblox"],
    "Shell_Title_KnownRobloxIssues": ["Known Roblox issues", "Problèmes Roblox connus", "Bekannte Roblox-Probleme", "Известные проблемы Roblox", "Problemas conhecidos do Roblox", "Znane problemy Roblox", "Problemas conocidos de Roblox"],
    "KnownIssuesPage_Heading": ["Known Roblox issues", "Problèmes Roblox connus", "Bekannte Roblox-Probleme", "Известные проблемы Roblox", "Problemas conhecidos do Roblox", "Znane problemy Roblox", "Problemas conocidos de Roblox"],
    "KnownIssuesPage_Intro": ["Problems in Roblox itself, not in RoRoRo, and what to do about them.", "Des problèmes de Roblox lui-même, pas de RoRoRo, et quoi faire.", "Probleme in Roblox selbst, nicht in RoRoRo, und was du dagegen tun kannst.", "Проблемы в самом Roblox, а не в RoRoRo, и что с ними делать.", "Problemas do próprio Roblox, não do RoRoRo, e o que fazer a respeito.", "Problemy w samym Roblox, a nie w RoRoRo, i co z nimi zrobić.", "Problemas del propio Roblox, no de RoRoRo, y qué hacer al respecto."],
    "KnownIssuesPage_EnglishOnly": ["Issue descriptions are in English.", "Les descriptions des problèmes sont en anglais.", "Die Problembeschreibungen sind auf Englisch.", "Описания проблем приводятся на английском.", "As descrições dos problemas estão em inglês.", "Opisy problemów są po angielsku.", "Las descripciones de los problemas están en inglés."],
    "KnownIssuesPage_Empty": ["No known Roblox issues right now.", "Aucun problème Roblox connu pour le moment.", "Derzeit keine bekannten Roblox-Probleme.", "Сейчас известных проблем Roblox нет.", "Nenhum problema conhecido do Roblox no momento.", "Obecnie brak znanych problemów Roblox.", "No hay problemas conocidos de Roblox ahora mismo."],
    "KnownIssuesPage_LastChecked": ["Last checked at {0}.", "Dernière vérification à {0}.", "Zuletzt geprüft um {0}.", "Последняя проверка в {0}.", "Última verificação às {0}.", "Ostatnie sprawdzenie o {0}.", "Última comprobación a las {0}."],
    "KnownIssuesPage_Checking": ["Checking for known issues…", "Recherche des problèmes connus…", "Suche nach bekannten Problemen…", "Проверяем известные проблемы…", "Verificando problemas conhecidos…", "Sprawdzanie znanych problemów…", "Buscando problemas conocidos…"],
    "KnownIssuesPage_RobloxTag": ["Roblox"] * 7,
    "KnownIssuesPage_Posted": ["Posted {0}", "Publié le {0}", "Veröffentlicht am {0}", "Опубликовано {0}", "Publicado em {0}", "Opublikowano {0}", "Publicado el {0}"],
    "KnownIssuesPage_WhatToDo": ["What to do", "Que faire", "Was du tun kannst", "Что делать", "O que fazer", "Co zrobić", "Qué hacer"],
    "KnownIssuesPage_RororoCanHelp": ["RoRoRo can help", "RoRoRo peut aider", "RoRoRo kann helfen", "RoRoRo может помочь", "O RoRoRo pode ajudar", "RoRoRo może pomóc", "RoRoRo puede ayudar"],
    "KnownIssuesPage_VersionAffected": ["Your Roblox version ({0}) is affected.", "Votre version de Roblox ({0}) est concernée.", "Deine Roblox-Version ({0}) ist betroffen.", "Ваша версия Roblox ({0}) затронута.", "Sua versão do Roblox ({0}) é afetada.", "Twoja wersja Roblox ({0}) jest objęta problemem.", "Tu versión de Roblox ({0}) está afectada."],
    "KnownIssuesPage_VersionFixed": ["Fixed in {0}. You have {1}.", "Corrigé dans la version {0}. Vous avez la {1}.", "Behoben in {0}. Du hast {1}.", "Исправлено в {0}. У вас {1}.", "Corrigido na {0}. Você tem a {1}.", "Naprawiono w {0}. Masz {1}.", "Corregido en {0}. Tienes {1}."],
    "KnownIssuesPage_VersionNotYet": ["Affects {0} and later. You have {1}.", "Concerne la version {0} et les suivantes. Vous avez la {1}.", "Betrifft {0} und neuer. Du hast {1}.", "Затрагивает {0} и новее. У вас {1}.", "Afeta a {0} e posteriores. Você tem a {1}.", "Dotyczy wersji {0} i nowszych. Masz {1}.", "Afecta a {0} y posteriores. Tienes {1}."],
    "KnownIssuesPage_VersionUnknown": ["Couldn't read your Roblox version.", "Impossible de lire votre version de Roblox.", "Deine Roblox-Version konnte nicht gelesen werden.", "Не удалось определить вашу версию Roblox.", "Não foi possível ler sua versão do Roblox.", "Nie udało się odczytać Twojej wersji Roblox.", "No se pudo leer tu versión de Roblox."],
    "KnownIssuesPage_OpenMemorySettings": ["Open memory settings", "Ouvrir les paramètres de mémoire", "Speichereinstellungen öffnen", "Открыть настройки памяти", "Abrir configurações de memória", "Otwórz ustawienia pamięci", "Abrir la configuración de memoria"],
    "KnownIssuesPage_ShowAccounts": ["Show accounts", "Afficher les comptes", "Konten anzeigen", "Показать аккаунты", "Mostrar contas", "Pokaż konta", "Mostrar cuentas"],
    "KnownIssuesPage_OpenLink": ["Open {0} in your browser", "Ouvrir {0} dans votre navigateur", "{0} im Browser öffnen", "Открыть {0} в браузере", "Abrir {0} no navegador", "Otwórz {0} w przeglądarce", "Abrir {0} en tu navegador"],
    "MainWindow_KnownIssueNoticeOne": ["Known Roblox issue: {0}", "Problème Roblox connu : {0}", "Bekanntes Roblox-Problem: {0}", "Известная проблема Roblox: {0}", "Problema conhecido do Roblox: {0}", "Znany problem Roblox: {0}", "Problema conocido de Roblox: {0}"],
    "MainWindow_KnownIssueNoticeMany": ["Known Roblox issues that affect you: {0}", "Problèmes Roblox connus qui vous concernent : {0}", "Bekannte Roblox-Probleme, die dich betreffen: {0}", "Известные проблемы Roblox, которые вас затрагивают: {0}", "Problemas conhecidos do Roblox que afetam você: {0}", "Znane problemy Roblox, które Cię dotyczą: {0}", "Problemas conocidos de Roblox que te afectan: {0}"],
    "MainWindow_SeeKnownIssues": ["See known issues", "Voir les problèmes connus", "Bekannte Probleme ansehen", "Посмотреть известные проблемы", "Ver problemas conhecidos", "Zobacz znane problemy", "Ver problemas conocidos"],
    "MainWindow_DismissKnownIssueNotice": ["Dismiss the known Roblox issue notice", "Ignorer l'avis de problème Roblox connu", "Hinweis zum bekannten Roblox-Problem ausblenden", "Скрыть уведомление об известной проблеме Roblox", "Dispensar o aviso de problema conhecido do Roblox", "Odrzuć powiadomienie o znanym problemie Roblox", "Descartar el aviso de problema conocido de Roblox"],
    "MainWindow_KnownRobloxIssues": ["Known Roblox issues", "Problèmes Roblox connus", "Bekannte Roblox-Probleme", "Известные проблемы Roblox", "Problemas conhecidos do Roblox", "Znane problemy Roblox", "Problemas conocidos de Roblox"],
    "MainWindow_KnownRobloxIssuesCount": ["Known Roblox issues ({0})", "Problèmes Roblox connus ({0})", "Bekannte Roblox-Probleme ({0})", "Известные проблемы Roblox ({0})", "Problemas conhecidos do Roblox ({0})", "Znane problemy Roblox ({0})", "Problemas conocidos de Roblox ({0})"],
    "MainWindow_KnownRobloxIssuesTooltip": ["Problems in Roblox itself, and what to do about them", "Des problèmes de Roblox lui-même, et quoi faire", "Probleme in Roblox selbst und was du dagegen tun kannst", "Проблемы в самом Roblox и что с ними делать", "Problemas do próprio Roblox e o que fazer a respeito", "Problemy w samym Roblox i co z nimi zrobić", "Problemas del propio Roblox y qué hacer al respecto"],
}

CHANGED = {
    "MainWindow_HistoryDiagnosticsPluginsLogFolder": ["History, Diagnostics, Plugins, Known Roblox issues, log folder, Stop all, tour, About.", "Historique, Diagnostics, Plugins, Problèmes Roblox connus, dossier des journaux, Tout arrêter, visite, À propos.", "Verlauf, Diagnose, Plugins, Bekannte Roblox-Probleme, Log-Ordner, Alle stoppen, Tour, Über.", "История, диагностика, плагины, известные проблемы Roblox, папка журналов, «Остановить все», тур, «О программе».", "Histórico, Diagnóstico, Plugins, Problemas conhecidos do Roblox, pasta de logs, Parar tudo, tour, Sobre.", "Historia, Diagnostyka, Wtyczki, Znane problemy Roblox, folder dzienników, Zatrzymaj wszystko, przewodnik, O programie.", "Historial, Diagnóstico, Complementos, Problemas conocidos de Roblox, carpeta de registros, Detener todo, recorrido, Acerca de."],
}

SHORTCUT_KEY = "AboutPage_InsideTheToolsWindowCtrl"


def esc(text):
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def set_resx_value(resx, key, value):
    pattern = re.compile(r'(<data name="' + re.escape(key) + r'" xml:space="preserve">\s*<value>)(.*?)(</value>)', re.S)
    resx, count = pattern.subn(lambda m: m.group(1) + esc(value) + m.group(3), resx, count=1)
    assert count == 1, key
    return resx


resx = io.open(RESX, encoding="utf-8").read()
for key in NEW:
    assert f'name="{key}"' not in resx, f"{key} already exists"
for key, values in CHANGED.items():
    resx = set_resx_value(resx, key, values[0])
old_shortcut = re.search(r'name="' + SHORTCUT_KEY + r'" xml:space="preserve">\s*<value>(.*?)</value>', resx, re.S).group(1)
assert "Ctrl+6" in old_shortcut, old_shortcut
resx = set_resx_value(resx, SHORTCUT_KEY, old_shortcut.replace("Ctrl+6", "Ctrl+7"))
nodes = "".join(
    f'  <data name="{key}" xml:space="preserve">\n    <value>{esc(values[0])}</value>\n  </data>\n'
    for key, values in NEW.items())
resx = resx.replace("</root>", nodes + "</root>", 1)
io.open(RESX, "w", encoding="utf-8", newline="").write(resx)

for index, culture in enumerate(CULTURES, start=1):
    path = TRANS / f"ui-{culture}.json"
    data = json.load(io.open(path, encoding="utf-8"))
    for key, values in {**NEW, **CHANGED}.items():
        data[key] = values[index]
    assert "Ctrl+6" in data[SHORTCUT_KEY], (culture, data[SHORTCUT_KEY])
    data[SHORTCUT_KEY] = data[SHORTCUT_KEY].replace("Ctrl+6", "Ctrl+7")
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"{culture}: {len(NEW)} added, {len(CHANGED) + 1} changed")
print("neutral resx updated")
```

- [ ] **Step 2: Run the script, then the translation cycle**

```bash
PYTHONIOENCODING=utf-8 python "$TEMP/known-issues-strings.py"   # run from the repository root
PYTHONIOENCODING=utf-8 python scripts/translate-cycle.py
```

Expected: the script prints six culture lines (`26 added, 2 changed`) and `neutral resx updated`; the cycle's lint stage reports no violations, and generate rewrites all six `Strings.<culture>.resx`. If lint flags a product noun or a format token, fix that value in the script and re-run both commands; do not hand-edit the generated resx.

- [ ] **Step 3: Check the diff is only what was intended**

Run: `git diff --stat`
Expected: `Strings.resx`, six `Strings.<culture>.resx`, six `ui-<culture>.json`, and `docs/store/ui-translations.json`. In `git diff docs/store/translations/ui-fr.json`, only the 26 new keys at the end and the two changed values appear; if the whole file shows as changed, the indentation or line endings drifted, so restore it and fix the script before continuing.

- [ ] **Step 4: Run the localization guards**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~LocKeyParityFenceTests|FullyQualifiedName~UiCultureTests|FullyQualifiedName~NoVendorNameFenceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Properties docs/store/translations docs/store/ui-translations.json
git commit -m "feat(known-issues): the page, notice and menu strings in all seven languages; Tools lists seven pages

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: The App's notice model, feature routes and page entries

**Files:**
- Create: `src/ROROROblox.App/KnownIssues/KnownIssueFeatureRoutes.cs`
- Create: `src/ROROROblox.App/KnownIssues/KnownIssuesNoticeModel.cs`
- Create: `src/ROROROblox.App/KnownIssues/KnownIssueView.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/KnownIssueFeatureRoutesTests.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/KnownIssuesNoticeModelTests.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/KnownIssueViewTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5 (`KnownIssue`, `KnownIssueNotice`, `KnownIssueApplicability`, `KnownIssuesSnapshot`, `KnownIssuesDismissals`, `KnownIssueFeatures`); `IUiDispatcher` (Core); `Loc` (`ROROROblox.App.Localization`); Task 7's keys.
- Produces: `enum KnownIssueFeatureRoute { None, MemorySettings, MainWindow }`; `KnownIssueFeatureRoutes.For(string?)`, `KnownIssueFeatureRoutes.ButtonLabelKey(KnownIssueFeatureRoute)`; `KnownIssuesNoticeModel(KnownIssuesDismissals, Func<Version?>, IUiDispatcher)` with `NoticeText`, `HasNotice`, `MenuHeader`, `ApplicableCount`, `PageIssues`, `RunningVersion`, `CheckedAt`, `Apply(KnownIssuesSnapshot)`, `SetSuppressed(bool)`, `Dismiss()`, events `PropertyChanged` and `Changed`; `KnownIssueView.From(KnownIssue, Version?, CultureInfo)` and `KnownIssueLinkView(Label, Url, AccessibleName)`.

- [ ] **Step 1: Write the failing tests**

`src/ROROROblox.Tests/KnownIssues/KnownIssueFeatureRoutesTests.cs`:

```csharp
using ROROROblox.App.KnownIssues;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

public class KnownIssueFeatureRoutesTests
{
    [Theory]
    [InlineData(KnownIssueFeatures.MemoryWatchdog, KnownIssueFeatureRoute.MemorySettings)]
    [InlineData(KnownIssueFeatures.FpsCaps, KnownIssueFeatureRoute.MainWindow)]
    [InlineData(KnownIssueFeatures.Recycle, KnownIssueFeatureRoute.MainWindow)]
    [InlineData("teleport-helper", KnownIssueFeatureRoute.None)]
    [InlineData("MEMORY-WATCHDOG", KnownIssueFeatureRoute.None)]
    [InlineData(null, KnownIssueFeatureRoute.None)]
    public void EachKeyGoesWhereTheSpecSays(string? key, KnownIssueFeatureRoute expected)
    {
        Assert.Equal(expected, KnownIssueFeatureRoutes.For(key));
    }

    [Fact]
    public void OnlyARealRouteHasAButtonLabel()
    {
        Assert.Equal("KnownIssuesPage_OpenMemorySettings", KnownIssueFeatureRoutes.ButtonLabelKey(KnownIssueFeatureRoute.MemorySettings));
        Assert.Equal("KnownIssuesPage_ShowAccounts", KnownIssueFeatureRoutes.ButtonLabelKey(KnownIssueFeatureRoute.MainWindow));
        Assert.Null(KnownIssueFeatureRoutes.ButtonLabelKey(KnownIssueFeatureRoute.None));
    }
}
```

`src/ROROROblox.Tests/KnownIssues/KnownIssuesNoticeModelTests.cs`:

```csharp
using ROROROblox.App.KnownIssues;
using ROROROblox.App.Localization;
using ROROROblox.Core;
using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

public sealed class KnownIssuesNoticeModelTests : IDisposable
{
    private readonly string _dismissedPath = Path.Combine(Path.GetTempPath(), "rororo-notice-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_dismissedPath)) File.Delete(_dismissedPath);
    }

    internal sealed class InlineDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
    }

    private KnownIssuesNoticeModel Model(Version? running = null) =>
        new(new KnownIssuesDismissals(_dismissedPath), () => running, new InlineDispatcher());

    private static KnownIssuesSnapshot Snapshot(params KnownIssue[] issues) =>
        new(issues, KnownIssuesSource.Release, DateTimeOffset.UnixEpoch);

    [Fact]
    public void OneApplicableSeriousEntryIsNamedInTheNotice()
    {
        var model = Model();

        model.Apply(Snapshot(Issue("a")));

        Assert.True(model.HasNotice);
        Assert.Equal(Loc.Format("MainWindow_KnownIssueNoticeOne", "Title of a"), model.NoticeText);
    }

    [Fact]
    public void SeveralAreCounted()
    {
        var model = Model();

        model.Apply(Snapshot(Issue("a"), Issue("b")));

        Assert.Equal(Loc.Format("MainWindow_KnownIssueNoticeMany", 2), model.NoticeText);
    }

    [Fact]
    public void QuietAndFixedEntriesRaiseNoNotice_ButStayOnThePage()
    {
        var model = Model(Version.Parse("0.740.0.7400927"));

        model.Apply(Snapshot(Issue("quiet", notify: false), Issue("fixed", fixedIn: new Version(0, 740))));

        Assert.False(model.HasNotice);
        Assert.Equal(string.Empty, model.NoticeText);
        Assert.Equal(2, model.PageIssues.Count);
        Assert.Equal(1, model.ApplicableCount);
    }

    [Fact]
    public void TheContestedWarningSuppressesTheNotice_AndItComesBack()
    {
        var model = Model();
        model.Apply(Snapshot(Issue("a")));

        model.SetSuppressed(true);
        Assert.Equal(string.Empty, model.NoticeText);

        model.SetSuppressed(false);
        Assert.NotEqual(string.Empty, model.NoticeText);
    }

    [Fact]
    public void ClosingTheNoticeKeepsItClosed_UntilANewEntryArrives()
    {
        var model = Model();
        model.Apply(Snapshot(Issue("a")));

        model.Dismiss();
        Assert.False(model.HasNotice);

        var afterRestart = Model();
        afterRestart.Apply(Snapshot(Issue("a")));
        Assert.False(afterRestart.HasNotice);

        afterRestart.Apply(Snapshot(Issue("a"), Issue("b")));
        Assert.Equal(Loc.Format("MainWindow_KnownIssueNoticeOne", "Title of b"), afterRestart.NoticeText);
    }

    [Fact]
    public void TheMenuShowsACountOnlyWhenSomethingApplies()
    {
        var model = Model();
        Assert.Equal(Loc.Get("MainWindow_KnownRobloxIssues"), model.MenuHeader);

        model.Apply(Snapshot(Issue("a"), Issue("b", notify: false)));
        Assert.Equal(Loc.Format("MainWindow_KnownRobloxIssuesCount", 2), model.MenuHeader);
    }

    [Fact]
    public void EveryApplyRaisesChangedForThePage()
    {
        var model = Model();
        var raised = 0;
        model.Changed += (_, _) => raised++;

        model.Apply(Snapshot(Issue("a")));

        Assert.Equal(1, raised);
    }
}
```

`src/ROROROblox.Tests/KnownIssues/KnownIssueViewTests.cs`:

```csharp
using System.Globalization;
using ROROROblox.App.KnownIssues;
using ROROROblox.App.Localization;
using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

public class KnownIssueViewTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly Version V739 = Version.Parse("0.739.0.7390687");
    private static readonly Version V740 = Version.Parse("0.740.0.7400927");

    [Fact]
    public void TheEntryCarriesItsTextAndPostedDate()
    {
        var view = KnownIssueView.From(Issue("a"), V740, Culture);

        Assert.Equal("Title of a", view.Title);
        Assert.Equal("Symptom of a", view.Symptom);
        Assert.Equal("Workaround for a", view.Workaround);
        Assert.Equal(Loc.Format("KnownIssuesPage_Posted", new DateOnly(2026, 9, 23).ToString("d", Culture)), view.PostedText);
    }

    [Fact]
    public void NoVersionsMeansNoVersionLine()
    {
        Assert.Null(KnownIssueView.From(Issue("a"), V740, Culture).VersionText);
    }

    [Fact]
    public void TheVersionLineSaysAffectedFixedNotYetOrUnknown()
    {
        Assert.Equal(Loc.Format("KnownIssuesPage_VersionAffected", "0.739"),
            KnownIssueView.From(Issue("a", fixedIn: new Version(0, 740)), V739, Culture).VersionText);
        Assert.Equal(Loc.Format("KnownIssuesPage_VersionFixed", "0.740", "0.740"),
            KnownIssueView.From(Issue("a", fixedIn: new Version(0, 740)), V740, Culture).VersionText);
        Assert.Equal(Loc.Format("KnownIssuesPage_VersionNotYet", "0.740", "0.739"),
            KnownIssueView.From(Issue("a", from: new Version(0, 740)), V739, Culture).VersionText);
        Assert.Equal(Loc.Get("KnownIssuesPage_VersionUnknown"),
            KnownIssueView.From(Issue("a", fixedIn: new Version(0, 740)), null, Culture).VersionText);
    }

    [Fact]
    public void AKnownFeatureGetsAButtonThatGoesThere()
    {
        var view = KnownIssueView.From(Issue("a", helps: new KnownIssueHelp(KnownIssueFeatures.MemoryWatchdog, "Watch memory.")), V740, Culture);

        Assert.Equal("Watch memory.", view.HelpText);
        Assert.Equal(Loc.Get("KnownIssuesPage_OpenMemorySettings"), view.HelpButtonText);
        Assert.Equal(KnownIssueFeatureRoute.MemorySettings, view.HelpRoute);
    }

    /// <summary>Review Focus 1: an entry written for a newer app still reads on this one.</summary>
    [Fact]
    public void AnUnknownFeatureShowsItsTextWithNoButton()
    {
        var view = KnownIssueView.From(Issue("a", helps: new KnownIssueHelp("teleport-helper", "Soon.")), V740, Culture);

        Assert.Equal("Soon.", view.HelpText);
        Assert.Null(view.HelpButtonText);
        Assert.Equal(KnownIssueFeatureRoute.None, view.HelpRoute);
    }

    [Fact]
    public void EachLinkIsNamedForAScreenReaderAndShowsItsAddress()
    {
        var link = new KnownIssueLink("DevForum", "https://devforum.roblox.com/t/4032374");

        var view = KnownIssueView.From(Issue("a", links: [link]), V740, Culture);

        var shown = Assert.Single(view.Links);
        Assert.Equal("DevForum", shown.Label);
        Assert.Equal("https://devforum.roblox.com/t/4032374", shown.Url);
        Assert.Equal(Loc.Format("KnownIssuesPage_OpenLink", "DevForum"), shown.AccessibleName);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build ROROROblox.slnx -c Release`
Expected: FAIL — `The type or namespace name 'KnownIssues' does not exist in the namespace 'ROROROblox.App'`.

- [ ] **Step 3: Write the routes, the model and the view**

`src/ROROROblox.App/KnownIssues/KnownIssueFeatureRoutes.cs`:

```csharp
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.App.KnownIssues;

internal enum KnownIssueFeatureRoute
{
    /// <summary>An unknown key: the entry's sentence shows, with no button.</summary>
    None,

    /// <summary>Tools › Settings › Alerts &amp; memory, scrolled to "Watch memory while accounts are running".</summary>
    MemorySettings,

    /// <summary>The main window, brought forward — frame-rate caps and Recycle are per account row.</summary>
    MainWindow,
}

/// <summary>Where a <c>rororoHelps.feature</c> key leads. Exact, ordinal match: keys are data, not prose.</summary>
internal static class KnownIssueFeatureRoutes
{
    public static KnownIssueFeatureRoute For(string? featureKey) => featureKey switch
    {
        KnownIssueFeatures.MemoryWatchdog => KnownIssueFeatureRoute.MemorySettings,
        KnownIssueFeatures.FpsCaps or KnownIssueFeatures.Recycle => KnownIssueFeatureRoute.MainWindow,
        _ => KnownIssueFeatureRoute.None,
    };

    public static string? ButtonLabelKey(KnownIssueFeatureRoute route) => route switch
    {
        KnownIssueFeatureRoute.MemorySettings => "KnownIssuesPage_OpenMemorySettings",
        KnownIssueFeatureRoute.MainWindow => "KnownIssuesPage_ShowAccounts",
        _ => null,
    };
}
```

`src/ROROROblox.App/KnownIssues/KnownIssuesNoticeModel.cs`:

```csharp
using System.ComponentModel;
using ROROROblox.App.Localization;
using ROROROblox.Core;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.App.KnownIssues;

/// <summary>
/// Known Roblox issues, worded for the main window and the page (spec §3). Singleton. Takes a
/// <see cref="KnownIssuesSnapshot"/> from any thread and marshals through <see cref="IUiDispatcher"/>;
/// everything it raises, it raises on the UI thread.
/// <para>
/// Suppressed while the contested-singleton warning shows: that warning is about RoRoRo working at
/// all and must not compete for attention (spec §3, corrected while planning).
/// </para>
/// </summary>
internal sealed class KnownIssuesNoticeModel : INotifyPropertyChanged
{
    private readonly KnownIssuesDismissals _dismissals;
    private readonly Func<Version?> _readRunningVersion;
    private readonly IUiDispatcher _ui;

    private IReadOnlyList<KnownIssue> _issues = [];
    private IReadOnlySet<string> _dismissed;
    private Version? _running;
    private DateTimeOffset? _checkedAt;
    private bool _suppressed;
    private KnownIssueNoticeSelection _selection = KnownIssueNoticeSelection.None;

    public KnownIssuesNoticeModel(KnownIssuesDismissals dismissals, Func<Version?> readRunningVersion, IUiDispatcher ui)
    {
        _dismissals = dismissals ?? throw new ArgumentNullException(nameof(dismissals));
        _readRunningVersion = readRunningVersion ?? throw new ArgumentNullException(nameof(readRunningVersion));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _dismissed = dismissals.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised on the UI thread after anything the page must redraw for.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<KnownIssue> PageIssues => KnownIssueNotice.PageOrder(_issues);

    public Version? RunningVersion => _running;

    public DateTimeOffset? CheckedAt => _checkedAt;

    public int ApplicableCount => KnownIssueNotice.CountApplicable(_issues, _running);

    public bool HasNotice => !_suppressed && !_selection.IsEmpty;

    public string NoticeText => !HasNotice
        ? string.Empty
        : _selection.Issues.Count == 1
            ? Loc.Format("MainWindow_KnownIssueNoticeOne", _selection.Issues[0].Title)
            : Loc.Format("MainWindow_KnownIssueNoticeMany", _selection.Issues.Count);

    public string MenuHeader => ApplicableCount == 0
        ? Loc.Get("MainWindow_KnownRobloxIssues")
        : Loc.Format("MainWindow_KnownRobloxIssuesCount", ApplicableCount);

    /// <summary>Any thread. Re-reads the running Roblox version each time: an update may have landed.</summary>
    public void Apply(KnownIssuesSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ui.Invoke(() =>
        {
            _issues = snapshot.Issues;
            _checkedAt = snapshot.CheckedAt;
            _running = _readRunningVersion();
            Recompute();
        });
    }

    public void SetSuppressed(bool suppressed) => _ui.Invoke(() =>
    {
        if (_suppressed == suppressed)
        {
            return;
        }

        _suppressed = suppressed;
        Recompute();
    });

    /// <summary>Closes every entry the notice was showing. Only an id not seen before brings it back.</summary>
    public void Dismiss() => _ui.Invoke(() =>
    {
        if (_selection.IsEmpty)
        {
            return;
        }

        _dismissed = _dismissals.Dismiss(_selection.Ids, _issues.Select(i => i.Id));
        Recompute();
    });

    private void Recompute()
    {
        _selection = KnownIssueNotice.Select(_issues, _running, _dismissed);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
```

`src/ROROROblox.App/KnownIssues/KnownIssueView.cs`:

```csharp
using System.Globalization;
using ROROROblox.App.Localization;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.App.KnownIssues;

internal sealed record KnownIssueLinkView(string Label, string Url, string AccessibleName);

/// <summary>One entry on the Known Roblox issues page, fully worded. Built fresh on every redraw and culture change.</summary>
internal sealed class KnownIssueView
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string PostedText { get; init; }
    public required string Symptom { get; init; }
    public required string Workaround { get; init; }
    public string? HelpText { get; init; }
    public string? HelpButtonText { get; init; }
    public KnownIssueFeatureRoute HelpRoute { get; init; }
    public string? VersionText { get; init; }
    public required IReadOnlyList<KnownIssueLinkView> Links { get; init; }

    public static KnownIssueView From(KnownIssue issue, Version? running, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(culture);

        var route = KnownIssueFeatureRoutes.For(issue.RororoHelps?.Feature);
        var buttonKey = issue.RororoHelps is null ? null : KnownIssueFeatureRoutes.ButtonLabelKey(route);

        return new KnownIssueView
        {
            Id = issue.Id,
            Title = issue.Title,
            PostedText = Loc.Format("KnownIssuesPage_Posted", issue.PostedAt.ToString("d", culture)),
            Symptom = issue.Symptom,
            Workaround = issue.Workaround,
            HelpText = issue.RororoHelps?.Text,
            HelpButtonText = buttonKey is null ? null : Loc.Get(buttonKey),
            HelpRoute = route,
            VersionText = VersionLine(issue, running),
            Links = issue.Links
                .Select(l => new KnownIssueLinkView(l.Label, l.Url, Loc.Format("KnownIssuesPage_OpenLink", l.Label)))
                .ToList(),
        };
    }

    private static string? VersionLine(KnownIssue issue, Version? running) =>
        KnownIssueApplicability.StatusFor(issue, running) switch
        {
            KnownIssueVersionStatus.NoVersions => null,
            KnownIssueVersionStatus.UnknownInstalled => Loc.Get("KnownIssuesPage_VersionUnknown"),
            KnownIssueVersionStatus.Affected => Loc.Format("KnownIssuesPage_VersionAffected", Short(running!)),
            KnownIssueVersionStatus.Fixed => Loc.Format("KnownIssuesPage_VersionFixed", issue.RobloxVersions!.FixedIn!.ToString(), Short(running!)),
            KnownIssueVersionStatus.NotYetAffected => Loc.Format("KnownIssuesPage_VersionNotYet", issue.RobloxVersions!.From!.ToString(), Short(running!)),
            _ => null,
        };

    /// <summary>"0.740", not "0.740.0.7400927": the build number means nothing to a reader.</summary>
    private static string Short(Version version) => $"{version.Major}.{version.Minor}";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownIssues"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/KnownIssues src/ROROROblox.Tests/KnownIssues
git commit -m "feat(known-issues): word the notice, the menu count and each page entry; route feature keys

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: The Known Roblox issues page

**Files:**
- Create: `src/ROROROblox.App/KnownIssues/KnownRobloxIssuesPage.xaml`
- Create: `src/ROROROblox.App/KnownIssues/KnownRobloxIssuesPage.xaml.cs`
- Modify: `src/ROROROblox.App/Shell/ShellPage.cs`
- Modify: `src/ROROROblox.App/Shell/ShellWindow.xaml`
- Modify: `src/ROROROblox.App/Shell/ShellWindow.xaml.cs`
- Modify: `src/ROROROblox.App/Preferences/SettingsPage.xaml.cs`
- Modify: `src/ROROROblox.App/App.xaml.cs`
- Test: `src/ROROROblox.Tests/Rendering/KnownRobloxIssuesPageRenderTests.cs`

**Interfaces:**
- Consumes: Task 8's `KnownIssuesNoticeModel`, `KnownIssueView`, `KnownIssueLinkView`, `KnownIssueFeatureRoute`; `IShellOpener` (Core); Task 5's `KnownIssuesDismissals`.
- Produces: `ShellPage.KnownRobloxIssues`; `KnownRobloxIssuesPage(KnownIssuesNoticeModel, Action<KnownIssueFeatureRoute>, IShellOpener)` (IDisposable); `ShellWindow.CurrentPage`; `SettingsPage.RevealMemorySettings()`; `App.GoToKnownIssueFeature(KnownIssueFeatureRoute)`; DI singletons `KnownIssuesDismissals` and `KnownIssuesNoticeModel`.

- [ ] **Step 1: Write the failing render test**

`src/ROROROblox.Tests/Rendering/KnownRobloxIssuesPageRenderTests.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.KnownIssues;
using ROROROblox.Core;
using ROROROblox.Core.KnownIssues;
using ROROROblox.Core.Theming;
using Xunit.Abstractions;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The page with realistic entries, rendered off-screen in every built-in theme, saved as PNGs for the
/// owner to look at before this is called done — there is no approved mock, so the render is the review
/// surface. The assertion is structural: every entry and its buttons made it into the tree.
/// </summary>
public class KnownRobloxIssuesPageRenderTests(ITestOutputHelper output)
{
    private static readonly KnownIssue[] Sample =
    [
        new("window-freeze-on-drag-2026-09", new DateOnly(2026, 9, 23), true,
            "The Roblox window freezes when you drag or resize it",
            "Dragging or resizing a Roblox window stops responding until you press the Windows key.",
            "Press the Windows key, then Escape. Updating Roblox helps.",
            new KnownIssueHelp(KnownIssueFeatures.FpsCaps, "Set this account's frame-rate cap below your monitor's refresh rate."),
            [new KnownIssueLink("Roblox DevForum thread", "https://devforum.roblox.com/t/4032374")],
            new KnownIssueVersions(null, new Version(0, 740))),
        new("memory-leak-2026-08", new DateOnly(2026, 8, 30), false,
            "Roblox's memory use climbs the longer it runs",
            "Each client's memory grows over hours until the PC slows down.",
            "Restart long-running clients now and then.",
            new KnownIssueHelp(KnownIssueFeatures.MemoryWatchdog, "RoRoRo can watch memory and warn you before it gets bad."),
            [],
            null),
    ];

    private sealed class Inline : IUiDispatcher
    {
        public void Invoke(Action action) => action();
    }

    private sealed class NoShell : IShellOpener
    {
        public void Open(string path) { }
    }

    [WindowRenderFact]
    public void EveryEntryAndItsButtonsRender_InEveryTheme()
    {
        var themesDir = Path.Combine(Path.GetTempPath(), "rororo-kri-themes-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(themesDir).ListAsync().GetAwaiter().GetResult().Where(t => t.IsBuiltIn).ToList();
        Assert.True(themes.Count >= 4);

        foreach (var theme in themes)
        {
            var dismissed = Path.Combine(Path.GetTempPath(), "rororo-kri-" + Guid.NewGuid().ToString("N") + ".json");
            var (entries, buttons, png) = ThemedWindowRender.Inspect(
                theme,
                $"KnownRobloxIssuesPage [{theme.Id}]",
                () =>
                {
                    var model = new KnownIssuesNoticeModel(new KnownIssuesDismissals(dismissed), () => Version.Parse("0.739.0.7390687"), new Inline());
                    model.Apply(new KnownIssuesSnapshot(Sample, KnownIssuesSource.Release, DateTimeOffset.Now));
                    return ThemedWindowRender.HostPage(new KnownRobloxIssuesPage(model, _ => { }, new NoShell()), 760, 900);
                },
                content =>
                {
                    var list = (ItemsControl)ThemedWindowRender.Find(content, fe => fe is ItemsControl { Name: "IssueList" }, "the issue list");
                    var count = CountButtons(list); // inside the list only, so shared page chrome cannot move the count
                    var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    return (list.Items.Count, count, stream.ToArray());
                },
                raiseLoaded: true);

            var path = Path.Combine(Path.GetTempPath(), $"rororo-known-issues-page-{theme.Id}.png");
            File.WriteAllBytes(path, png);
            output.WriteLine($"{theme.Id}: {path}");

            Assert.Equal(2, entries);
            Assert.Equal(3, buttons); // two "RoRoRo can help" buttons and one link
        }
    }

    private static int CountButtons(DependencyObject root)
    {
        var total = root is Button ? 1 : 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            total += CountButtons(VisualTreeHelper.GetChild(root, i));
        }

        return total;
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build ROROROblox.slnx -c Release`
Expected: FAIL — `The type or namespace name 'KnownRobloxIssuesPage' could not be found`.

- [ ] **Step 3: Add the page to the shell**

In `src/ROROROblox.App/Shell/ShellPage.cs`, append `KnownRobloxIssues,` after `About,`.

In `src/ROROROblox.App/Shell/ShellWindow.xaml.cs`:
- `RailOrder` becomes `[ShellPage.Games, ShellPage.Settings, ShellPage.History, ShellPage.Diagnostics, ShellPage.Plugins, ShellPage.About, ShellPage.KnownRobloxIssues]`. Last, so every Ctrl+N shortcut keeps its number and the new page takes Ctrl+7.
- Add `[ShellPage.KnownRobloxIssues] = "Shell_Title_KnownRobloxIssues",` to `TitleKeys`.
- In the comment `Ctrl+1..6 walk the rail in order`, change `6` to `7`.
- Add this member directly after `NavigateTo`:

```csharp
    /// <summary>The page on screen, for callers that must act on it after navigating (Known Roblox issues → Settings).</summary>
    internal UserControl? CurrentPage => PageHost.Content as UserControl;
```

In `src/ROROROblox.App/Shell/ShellWindow.xaml`, after `<ListBoxItem Content="{loc:Loc ShellWindow_About}" />`, add:

```xml
            <ListBoxItem Content="{loc:Loc ShellWindow_KnownRobloxIssues}" />
```

- [ ] **Step 4: Write the page**

`src/ROROROblox.App/KnownIssues/KnownRobloxIssuesPage.xaml`:

```xml
<UserControl x:Class="ROROROblox.App.KnownIssues.KnownRobloxIssuesPage"
             x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:ROROROblox.App.Localization"
             xmlns:local="clr-namespace:ROROROblox.App"
             xmlns:controls="clr-namespace:ROROROblox.App.Controls">

    <!-- Declared here, not borrowed from App.xaml: off-screen render tests do not load App.xaml. -->
    <UserControl.Resources>
        <local:StringToVisibilityConverter x:Key="StringToVisibilityConverter" />
    </UserControl.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0">
            <controls:PageHeader Heading="{loc:Loc KnownIssuesPage_Heading}" />
            <TextBlock Text="{loc:Loc KnownIssuesPage_Intro}"
                       FontSize="{DynamicResource BodyFontSize}"
                       Foreground="{DynamicResource MutedTextBrush}"
                       Margin="0,4,0,0"
                       TextWrapping="Wrap" />
            <TextBlock x:Name="EnglishOnlyText"
                       Text="{loc:Loc KnownIssuesPage_EnglishOnly}"
                       FontSize="{DynamicResource MetaFontSize}"
                       Foreground="{DynamicResource MutedTextBrush}"
                       Margin="0,2,0,0"
                       TextWrapping="Wrap"
                       Visibility="Collapsed" />
        </StackPanel>

        <ScrollViewer Grid.Row="1" Margin="0,20,0,0" VerticalScrollBarVisibility="Auto">
            <StackPanel>
                <TextBlock x:Name="EmptyText"
                           Text="{loc:Loc KnownIssuesPage_Empty}"
                           FontSize="{DynamicResource BodyFontSize}"
                           Foreground="{DynamicResource WhiteBrush}"
                           TextWrapping="Wrap" />
                <ItemsControl x:Name="IssueList" Focusable="False">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Background="{DynamicResource RowBgBrush}"
                                    BorderBrush="{DynamicResource DividerBrush}"
                                    BorderThickness="1"
                                    CornerRadius="6"
                                    Padding="16,12"
                                    Margin="0,0,0,12">
                                <StackPanel>
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*" />
                                            <ColumnDefinition Width="Auto" />
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Grid.Column="0"
                                                   Text="{Binding Title}"
                                                   FontSize="{DynamicResource BodyFontSize}"
                                                   FontWeight="SemiBold"
                                                   Foreground="{DynamicResource WhiteBrush}"
                                                   TextWrapping="Wrap" />
                                        <Border Grid.Column="1"
                                                Margin="12,0,0,0"
                                                Padding="6,1"
                                                VerticalAlignment="Top"
                                                CornerRadius="3"
                                                BorderThickness="1"
                                                BorderBrush="{DynamicResource CyanBrush}">
                                            <TextBlock Text="{loc:Loc KnownIssuesPage_RobloxTag}"
                                                       FontSize="{DynamicResource MetaFontSize}"
                                                       Foreground="{DynamicResource CyanBrush}" />
                                        </Border>
                                    </Grid>
                                    <TextBlock Text="{Binding PostedText}"
                                               FontSize="{DynamicResource MetaFontSize}"
                                               Foreground="{DynamicResource MutedTextBrush}"
                                               Margin="0,2,0,0" />
                                    <TextBlock Text="{Binding VersionText}"
                                               FontSize="{DynamicResource MetaFontSize}"
                                               Foreground="{DynamicResource MutedTextBrush}"
                                               Margin="0,2,0,0"
                                               TextWrapping="Wrap"
                                               Visibility="{Binding VersionText, Converter={StaticResource StringToVisibilityConverter}}" />
                                    <TextBlock Text="{Binding Symptom}"
                                               FontSize="{DynamicResource BodyFontSize}"
                                               Foreground="{DynamicResource WhiteBrush}"
                                               Margin="0,8,0,0"
                                               TextWrapping="Wrap" />
                                    <TextBlock Text="{loc:Loc KnownIssuesPage_WhatToDo}"
                                               FontSize="{DynamicResource BodyFontSize}"
                                               FontWeight="SemiBold"
                                               Foreground="{DynamicResource WhiteBrush}"
                                               Margin="0,10,0,0" />
                                    <TextBlock Text="{Binding Workaround}"
                                               FontSize="{DynamicResource BodyFontSize}"
                                               Foreground="{DynamicResource WhiteBrush}"
                                               Margin="0,2,0,0"
                                               TextWrapping="Wrap" />
                                    <StackPanel Margin="0,10,0,0"
                                                Visibility="{Binding HelpText, Converter={StaticResource StringToVisibilityConverter}}">
                                        <TextBlock Text="{loc:Loc KnownIssuesPage_RororoCanHelp}"
                                                   FontSize="{DynamicResource BodyFontSize}"
                                                   FontWeight="SemiBold"
                                                   Foreground="{DynamicResource CyanBrush}" />
                                        <TextBlock Text="{Binding HelpText}"
                                                   FontSize="{DynamicResource BodyFontSize}"
                                                   Foreground="{DynamicResource WhiteBrush}"
                                                   Margin="0,2,0,0"
                                                   TextWrapping="Wrap" />
                                        <Button Content="{Binding HelpButtonText}"
                                                AutomationProperties.Name="{Binding HelpButtonText}"
                                                Style="{StaticResource SecondaryButtonStyle}"
                                                HorizontalAlignment="Left"
                                                Padding="10,4"
                                                Margin="0,6,0,0"
                                                FontSize="{DynamicResource BodyFontSize}"
                                                Tag="{Binding}"
                                                Click="OnHelpClick"
                                                Visibility="{Binding HelpButtonText, Converter={StaticResource StringToVisibilityConverter}}" />
                                    </StackPanel>
                                    <ItemsControl ItemsSource="{Binding Links}" Margin="0,8,0,0" Focusable="False">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <StackPanel Margin="0,2,0,0">
                                                    <Button Content="{Binding Label}"
                                                            AutomationProperties.Name="{Binding AccessibleName}"
                                                            ToolTip="{Binding Url}"
                                                            Style="{StaticResource GhostButtonStyle}"
                                                            HorizontalAlignment="Left"
                                                            Padding="6,2"
                                                            FontSize="{DynamicResource BodyFontSize}"
                                                            Tag="{Binding}"
                                                            Click="OnLinkClick" />
                                                    <TextBlock Text="{Binding Url}"
                                                               FontSize="{DynamicResource MetaFontSize}"
                                                               Foreground="{DynamicResource MutedTextBrush}"
                                                               Margin="6,0,0,0"
                                                               TextWrapping="Wrap" />
                                                </StackPanel>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </ScrollViewer>

        <TextBlock Grid.Row="2"
                   x:Name="LastCheckedText"
                   FontSize="{DynamicResource MetaFontSize}"
                   Foreground="{DynamicResource MutedTextBrush}"
                   Margin="0,12,0,0"
                   TextWrapping="Wrap" />
    </Grid>
</UserControl>
```

`src/ROROROblox.App/KnownIssues/KnownRobloxIssuesPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using ROROROblox.App.Localization;
using ROROROblox.Core;

namespace ROROROblox.App.KnownIssues;

/// <summary>
/// Tools › Known Roblox issues (spec §3). Every entry, serious first, whatever the running version —
/// the version decides only the notice and the count. Rebuilt whenever the model changes or the UI
/// language does; disposed by the shell when its window closes.
/// </summary>
internal sealed partial class KnownRobloxIssuesPage : UserControl, IDisposable
{
    private readonly KnownIssuesNoticeModel _model;
    private readonly Action<KnownIssueFeatureRoute> _goToFeature;
    private readonly IShellOpener _shellOpener;

    public KnownRobloxIssuesPage(KnownIssuesNoticeModel model, Action<KnownIssueFeatureRoute> goToFeature, IShellOpener shellOpener)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _goToFeature = goToFeature ?? throw new ArgumentNullException(nameof(goToFeature));
        _shellOpener = shellOpener ?? throw new ArgumentNullException(nameof(shellOpener));
        InitializeComponent();

        _model.Changed += OnModelChanged;
        TranslationSource.Instance.CultureChanged += OnCultureChanged;
        Rebuild();
    }

    public void Dispose()
    {
        _model.Changed -= OnModelChanged;
        TranslationSource.Instance.CultureChanged -= OnCultureChanged;
    }

    private void OnModelChanged(object? sender, EventArgs e) => Rebuild();

    private void OnCultureChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        var culture = TranslationSource.Instance.CurrentCulture;
        var views = _model.PageIssues.Select(i => KnownIssueView.From(i, _model.RunningVersion, culture)).ToList();

        IssueList.ItemsSource = views;
        EmptyText.Visibility = views.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EnglishOnlyText.Visibility = views.Count > 0 && culture.TwoLetterISOLanguageName != "en"
            ? Visibility.Visible
            : Visibility.Collapsed;
        LastCheckedText.Text = _model.CheckedAt is { } checkedAt
            ? Loc.Format("KnownIssuesPage_LastChecked", checkedAt.ToLocalTime().ToString("t", culture))
            : Loc.Get("KnownIssuesPage_Checking");
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: KnownIssueView view } && view.HelpRoute != KnownIssueFeatureRoute.None)
        {
            _goToFeature(view.HelpRoute);
        }
    }

    private void OnLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: KnownIssueLinkView link })
        {
            _shellOpener.Open(link.Url);
        }
    }
}
```

- [ ] **Step 5: Let Settings jump to the memory switch**

In `src/ROROROblox.App/Preferences/SettingsPage.xaml.cs`:

Add a field next to the other private fields:

```csharp
    // Known Roblox issues' "Open memory settings" (spec §4). OnLoaded resets the rail to its first
    // section every time the page is shown — the shell swaps pages in and out — so a reveal asked for
    // before Loaded is queued here and applied straight after that reset.
    private bool _revealMemoryOnLoad;
```

In `OnLoaded`, directly after `SettingsNav.SelectedIndex = 0;`, add:

```csharp
            if (_revealMemoryOnLoad)
            {
                _revealMemoryOnLoad = false;
                ShowMemorySwitch();
            }
```

Add these members next to `OnNavSelectionChanged`:

```csharp
    /// <summary>
    /// Alerts &amp; memory, scrolled to "Watch memory while accounts are running", with focus on it.
    /// Found by the rail item's AutomationId, not its index, so reordering the rail cannot send it to
    /// the wrong section.
    /// </summary>
    internal void RevealMemorySettings()
    {
        if (IsLoaded)
        {
            ShowMemorySwitch();
            return;
        }

        _revealMemoryOnLoad = true;
    }

    private void ShowMemorySwitch()
    {
        SettingsNav.SelectedItem = SettingsNav.Items
            .OfType<System.Windows.Controls.ListBoxItem>()
            .First(item => System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "NavAlerts");

        // After the section becomes visible and lays out; BringIntoView on a collapsed element does nothing.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            MemoryWatchdogEnabledToggle.BringIntoView();
            MemoryWatchdogEnabledToggle.Focus();
        }));
    }
```

- [ ] **Step 6: Register the model and build the page in the composition root**

In `src/ROROROblox.App/App.xaml.cs`, confirm `IShellOpener` is registered: `grep -n "IShellOpener" src/ROROROblox.App/App.xaml.cs` shows an `AddSingleton<IShellOpener, ShellOpener>` (or equivalent). If it does not, add `services.AddSingleton<IShellOpener, ShellOpener>();` beside the `IRobloxCompatChecker` registration.

Directly after the `services.AddHttpClient<IRobloxCompatChecker, RobloxCompatChecker>(…);` registration, add:

```csharp
        // Known Roblox issues (spec 2026-09-23). The dismissals file and the worded model are
        // singletons; the feed and its state are registered with the startup wiring (Task 10).
        services.AddSingleton(_ => new ROROROblox.Core.KnownIssues.KnownIssuesDismissals(
            ROROROblox.Core.KnownIssues.KnownIssuesDismissals.DefaultPath));
        services.AddSingleton(sp => new KnownIssues.KnownIssuesNoticeModel(
            sp.GetRequiredService<ROROROblox.Core.KnownIssues.KnownIssuesDismissals>(),
            ROROROblox.Core.KnownIssues.RunningRobloxVersion.Read,
            new Threading.WpfUiDispatcher()));
```

In `CreateShellPage`, add before the `_ => throw` arm:

```csharp
            Shell.ShellPage.KnownRobloxIssues => new KnownIssues.KnownRobloxIssuesPage(
                _services.GetRequiredService<KnownIssues.KnownIssuesNoticeModel>(),
                GoToKnownIssueFeature,
                _services.GetRequiredService<IShellOpener>()),
```

Add this method directly after `CreateShellPage`:

```csharp
    /// <summary>Where a known issue's "RoRoRo can help" button goes (spec §4).</summary>
    private void GoToKnownIssueFeature(KnownIssues.KnownIssueFeatureRoute route)
    {
        switch (route)
        {
            case KnownIssues.KnownIssueFeatureRoute.MemorySettings:
                OpenShellPage(Shell.ShellPage.Settings);
                (_shell?.CurrentPage as Preferences.SettingsPage)?.RevealMemorySettings();
                break;
            case KnownIssues.KnownIssueFeatureRoute.MainWindow:
                if (Current.MainWindow is { } main)
                {
                    SurfaceMainWindow(main);
                }

                break;
        }
    }
```

- [ ] **Step 7: Run the render test and the shell tests**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownRobloxIssuesPageRenderTests|FullyQualifiedName~WindowContentFitsTests|FullyQualifiedName~AccessibleNamingFenceTests|FullyQualifiedName~ButtonRankFenceTests|FullyQualifiedName~ButtonStateGateTests|FullyQualifiedName~ThemedStatusColourTests|FullyQualifiedName~TypeLadderFenceTests|FullyQualifiedName~XamlStyleIntegrityTests" --logger "console;verbosity=detailed"`
Expected: PASS. The detailed log prints four PNG paths, one per built-in theme. If `WindowContentFitsTests` fails on the shell, the seventh rail item pushed past the window's height; fix the height in `ShellWindow.xaml`, not the test.

- [ ] **Step 8: Look at the renders**

Open each `%TEMP%\rororo-known-issues-page-<theme>.png` and check: the freeze entry is first; the Roblox tag sits top-right of each title; the freeze shows "Your Roblox version (0.739) is affected."; both "RoRoRo can help" blocks have their button ("Show accounts", "Open memory settings"); the DevForum link shows its address under it; nothing is clipped or overlapping in any theme. Fix any defect and re-run Step 7 before committing.

- [ ] **Step 9: Commit**

```bash
git add src/ROROROblox.App/KnownIssues src/ROROROblox.App/Shell src/ROROROblox.App/Preferences/SettingsPage.xaml.cs src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/Rendering/KnownRobloxIssuesPageRenderTests.cs
git commit -m "feat(known-issues): the Known Roblox issues page, seventh in the Tools window, with feature buttons

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: The main window notice, the Tools menu entry, and the startup feed

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`
- Modify: `src/ROROROblox.App/MainWindow.xaml`
- Modify: `src/ROROROblox.App/App.xaml.cs`
- Test: `src/ROROROblox.Tests/KnownIssues/MainViewModelKnownIssuesTests.cs`

**Interfaces:**
- Consumes: Task 8's model; Tasks 3–4's `IKnownIssuesFeed`, `KnownIssuesFeed`, `KnownIssuesState`.
- Produces: `MainViewModel.KnownIssuesNotice` (settable), `KnownIssueNoticeText`, `KnownIssuesMenuHeader`, `OpenKnownIssuesCommand`, `DismissKnownIssueNoticeCommand`; `App.StartKnownIssuesFeed()`.

- [ ] **Step 1: Write the failing tests**

`src/ROROROblox.Tests/KnownIssues/MainViewModelKnownIssuesTests.cs`:

```csharp
using ROROROblox.App.KnownIssues;
using ROROROblox.App.Localization;
using ROROROblox.App.Shell;
using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>Built through MainViewModelTests.Build, which disposes the decorator and stops the 30 s ticker.</summary>
public sealed class MainViewModelKnownIssuesTests : IDisposable
{
    private readonly string _dismissedPath = Path.Combine(Path.GetTempPath(), "rororo-mvm-kri-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly List<string> _accountStores = [];

    public void Dispose()
    {
        if (File.Exists(_dismissedPath)) File.Delete(_dismissedPath);
        foreach (var path in _accountStores.Where(File.Exists)) File.Delete(path);
    }

    private ROROROblox.App.ViewModels.MainViewModel Vm()
    {
        var (vm, _, _, path) = MainViewModelTests.Build(uiDispatcher: new KnownIssuesNoticeModelTests.InlineDispatcher());
        _accountStores.Add(path);
        return vm;
    }

    private KnownIssuesNoticeModel ModelWith(params KnownIssue[] issues)
    {
        var model = new KnownIssuesNoticeModel(
            new KnownIssuesDismissals(_dismissedPath), () => null, new KnownIssuesNoticeModelTests.InlineDispatcher());
        model.Apply(new KnownIssuesSnapshot(issues, KnownIssuesSource.Release, DateTimeOffset.UnixEpoch));
        return model;
    }

    [Fact]
    public void WithoutAModelThereIsNoNoticeAndAPlainMenuEntry()
    {
        var vm = Vm();

        Assert.Equal(string.Empty, vm.KnownIssueNoticeText);
        Assert.Equal(Loc.Get("MainWindow_KnownRobloxIssues"), vm.KnownIssuesMenuHeader);
    }

    [Fact]
    public void TheNoticeFollowsTheModel_AndStepsAsideForTheContestedWarning()
    {
        var vm = Vm();
        vm.KnownIssuesNotice = ModelWith(Issue("a"));
        Assert.Equal(Loc.Format("MainWindow_KnownIssueNoticeOne", "Title of a"), vm.KnownIssueNoticeText);

        vm.SetContested(true);
        Assert.Equal(string.Empty, vm.KnownIssueNoticeText);

        vm.SetContested(false);
        Assert.NotEqual(string.Empty, vm.KnownIssueNoticeText);
    }

    [Fact]
    public void AModelAttachedWhileContestedStartsSuppressed()
    {
        var vm = Vm();
        vm.SetContested(true);

        vm.KnownIssuesNotice = ModelWith(Issue("a"));

        Assert.Equal(string.Empty, vm.KnownIssueNoticeText);
    }

    [Fact]
    public void DismissingClearsTheNotice()
    {
        var vm = Vm();
        vm.KnownIssuesNotice = ModelWith(Issue("a"));

        vm.DismissKnownIssueNoticeCommand.Execute(null);

        Assert.Equal(string.Empty, vm.KnownIssueNoticeText);
    }

    [Fact]
    public void TheCommandOpensTheKnownIssuesPage()
    {
        var vm = Vm();
        ShellPage? opened = null;
        vm.ShellPageOpener = page => opened = page;

        vm.OpenKnownIssuesCommand.Execute(null);

        Assert.Equal(ShellPage.KnownRobloxIssues, opened);
    }

    [Fact]
    public void TheMenuHeaderRaisesChangeWhenTheModelDoes()
    {
        var vm = Vm();
        var model = ModelWith();
        vm.KnownIssuesNotice = model;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.Apply(new KnownIssuesSnapshot([Issue("a")], KnownIssuesSource.Release, DateTimeOffset.UnixEpoch));

        Assert.Contains(nameof(vm.KnownIssuesMenuHeader), changed);
        Assert.Contains(nameof(vm.KnownIssueNoticeText), changed);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build ROROROblox.slnx -c Release`
Expected: FAIL — `'MainViewModel' does not contain a definition for 'KnownIssueNoticeText'`.

- [ ] **Step 3: Add the notice to MainViewModel**

In `src/ROROROblox.App/ViewModels/MainViewModel.cs`:

In the constructor, next to `OpenPluginsCommand = …`, add:

```csharp
        OpenKnownIssuesCommand = new RelayCommand(() => OpenShellPage(Shell.ShellPage.KnownRobloxIssues));
        DismissKnownIssueNoticeCommand = new RelayCommand(() => _knownIssuesNotice?.Dismiss());
```

Next to `public ICommand OpenPluginsCommand { get; }`, add:

```csharp
    public ICommand OpenKnownIssuesCommand { get; }

    public ICommand DismissKnownIssueNoticeCommand { get; }
```

In `SetContested`, after the `ContestedBannerText = …` line, add:

```csharp
        _knownIssuesNotice?.SetSuppressed(contested);
```

Add these members directly after `SetContested`:

```csharp
    private ROROROblox.App.KnownIssues.KnownIssuesNoticeModel? _knownIssuesNotice;

    /// <summary>
    /// Known Roblox issues' notice and menu count (spec 2026-09-23). Set by the composition root, like
    /// <see cref="ShellPageOpener"/>; null in tests that do not exercise it. Attaching one while the
    /// contested warning shows starts it suppressed.
    /// </summary>
    internal ROROROblox.App.KnownIssues.KnownIssuesNoticeModel? KnownIssuesNotice
    {
        get => _knownIssuesNotice;
        set
        {
            if (ReferenceEquals(_knownIssuesNotice, value))
            {
                return;
            }

            if (_knownIssuesNotice is not null)
            {
                _knownIssuesNotice.PropertyChanged -= OnKnownIssuesNoticeChanged;
            }

            _knownIssuesNotice = value;
            if (_knownIssuesNotice is not null)
            {
                _knownIssuesNotice.PropertyChanged += OnKnownIssuesNoticeChanged;
                _knownIssuesNotice.SetSuppressed(_isContested);
            }

            OnKnownIssuesNoticeChanged(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    /// <summary>The notice row's text; empty collapses the row, like the other rows of the strip.</summary>
    public string KnownIssueNoticeText => _knownIssuesNotice?.NoticeText ?? string.Empty;

    public string KnownIssuesMenuHeader => _knownIssuesNotice?.MenuHeader ?? Loc.Get("MainWindow_KnownRobloxIssues");

    private void OnKnownIssuesNoticeChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(KnownIssueNoticeText));
        OnPropertyChanged(nameof(KnownIssuesMenuHeader));
    }
```

If `Loc` does not resolve in this file, add `using ROROROblox.App.Localization;` at the top.

- [ ] **Step 4: Add the notice row and the menu item to MainWindow**

In `src/ROROROblox.App/MainWindow.xaml`:

(a) In the notice strip Border's `MultiDataTrigger.Conditions` (the block with `StatusBanner`, `IdleSummaryText`, `ContestedBannerText`, `FpsCapWarningText`), add:

```xml
                                <Condition Binding="{Binding KnownIssueNoticeText}" Value="" />
```

(b) Directly after the closing `</Grid>` of the frame-rate-cap row (the `Grid` whose `Visibility` binds `FpsCapWarningText`), add:

```xml
                <!-- Known Roblox issues (spec 2026-09-23). Suppressed while the contested warning shows
                     (KnownIssuesNoticeModel.SetSuppressed, fed from SetContested). A Grid for the same
                     reason as the frame-rate row above: declaration order is reading order, so the
                     message is read before its two actions. -->
                <Grid Margin="0,4,0,0"
                      Visibility="{Binding KnownIssueNoticeText, Converter={StaticResource StringToVisibilityConverter}}">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0"
                               Text="{Binding KnownIssueNoticeText}"
                               FontSize="{DynamicResource BodyFontSize}"
                               TextWrapping="Wrap"
                               VerticalAlignment="Center"
                               Foreground="{DynamicResource WhiteBrush}" />
                    <Button Grid.Column="1"
                            Style="{StaticResource SecondaryButtonStyle}"
                            Content="{loc:Loc MainWindow_SeeKnownIssues}"
                            Command="{Binding OpenKnownIssuesCommand}"
                            Padding="10,4"
                            Margin="8,0,0,0"
                            FontSize="{DynamicResource BodyFontSize}" />
                    <Button Grid.Column="2"
                            Style="{StaticResource GhostButtonStyle}"
                            Content="{loc:Loc MainWindow_Dismiss}"
                            AutomationProperties.Name="{loc:Loc MainWindow_DismissKnownIssueNotice}"
                            Command="{Binding DismissKnownIssueNoticeCommand}"
                            Padding="10,4"
                            Margin="4,0,0,0"
                            FontSize="{DynamicResource BodyFontSize}" />
                </Grid>
```

(c) In the Tools `ContextMenu`, directly after the `Plugins` `MenuItem` (`AutomationProperties.AutomationId="ToolsPlugins"`), add:

```xml
                                <MenuItem Header="{Binding KnownIssuesMenuHeader}"
                                AutomationProperties.AutomationId="ToolsKnownRobloxIssues"
                                          Command="{Binding OpenKnownIssuesCommand}"
                                          ToolTip="{loc:Loc MainWindow_KnownRobloxIssuesTooltip}" />
```

- [ ] **Step 5: Register the feed and start it after the gate**

In `src/ROROROblox.App/App.xaml.cs`:

(a) Directly after the two Known-issues singletons added in Task 9, add:

```csharp
        // Its own typed HttpClient with the RORORO UA, like the compat checker. Transient and
        // stateless; KnownIssuesState is the singleton that holds the list.
        services.AddHttpClient<ROROROblox.Core.KnownIssues.IKnownIssuesFeed, ROROROblox.Core.KnownIssues.KnownIssuesFeed>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Clear();
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));
        });
        services.AddSingleton(sp => new ROROROblox.Core.KnownIssues.KnownIssuesState(
            TimeProvider.System,
            sp.GetRequiredService<ILogger<ROROROblox.Core.KnownIssues.KnownIssuesState>>()));
```

(b) Next to `_services.GetRequiredService<MainViewModel>().ShellPageOpener = OpenShellPage;`, add:

```csharp
        _services.GetRequiredService<MainViewModel>().KnownIssuesNotice =
            _services.GetRequiredService<KnownIssues.KnownIssuesNoticeModel>();
```

(c) Directly after the `StartPluginAutostart();` call, add:

```csharp
        StartKnownIssuesFeed();
```

(d) Add these members next to `StartPluginAutostart`:

```csharp
    private ITimer? _knownIssuesTimer;

    private static readonly TimeSpan KnownIssuesRefreshInterval = TimeSpan.FromHours(4);

    /// <summary>
    /// Known Roblox issues (spec 2026-09-23 §2). Shows the saved copy at once, then downloads now and
    /// every four hours on the thread pool. Called after the gate, beside plugin autostart, so it never
    /// touches the theme → mutex → gate order. A failure here costs the page, never startup.
    /// </summary>
    private void StartKnownIssuesFeed()
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var state = _services.GetRequiredService<ROROROblox.Core.KnownIssues.KnownIssuesState>();
            var model = _services.GetRequiredService<KnownIssues.KnownIssuesNoticeModel>();
            state.Changed += (_, snapshot) => model.Apply(snapshot);
            state.LoadSavedCopy(_services.GetRequiredService<ROROROblox.Core.KnownIssues.IKnownIssuesFeed>());

            _knownIssuesTimer = TimeProvider.System.CreateTimer(
                _ => _ = RefreshKnownIssuesAsync(),
                state: null,
                dueTime: TimeSpan.Zero,
                period: KnownIssuesRefreshInterval);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Known issues could not start; the page stays empty this session.");
        }
    }

    private async Task RefreshKnownIssuesAsync()
    {
        try
        {
            var services = _services;
            if (services is null)
            {
                return;
            }

            // A fresh typed client per attempt: IHttpClientFactory pools the handler, so this is the
            // intended use and keeps DNS rotation honest over a days-long session.
            await services.GetRequiredService<ROROROblox.Core.KnownIssues.KnownIssuesState>()
                .RefreshAsync(services.GetRequiredService<ROROROblox.Core.KnownIssues.IKnownIssuesFeed>())
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Known issues refresh threw; trying again on the next tick.");
        }
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet build ROROROblox.slnx -c Release && dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~KnownIssues|FullyQualifiedName~AccessibleNamingFenceTests|FullyQualifiedName~ButtonRankFenceTests|FullyQualifiedName~TriggeredStatusColourGateTests|FullyQualifiedName~ThemedStatusColourTests|FullyQualifiedName~MainViewModelTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.App/MainWindow.xaml src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/KnownIssues/MainViewModelKnownIssuesTests.cs
git commit -m "feat(known-issues): the one-time notice in the main window, the counted Tools entry, and the startup feed

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 11: Whole-suite verification, a live run, and the record

**Files:**
- Modify: `docs/features.md`
- Modify: `docs/decisions.md`

- [ ] **Step 1: Build and run the whole solution**

```bash
dotnet build ROROROblox.slnx -c Release
dotnet test ROROROblox.slnx -c Release --no-build
```

Expected: 0 build errors (the ~43 known warnings are noise). Every unit test and every harness test passes, with the one harness `[Skip]` by design. Record the unit-test count; it should be the pre-branch count plus the tests added in Tasks 1–10. Run the suite twice more; any test that fails once and passes alone is a load flake to investigate and fix, not to note and move past.

- [ ] **Step 2: A live run against the real release**

The release does not carry `known-issues.json` until this branch ships or the compat workflow runs, so the live run exercises the 404 path. That is the right thing to see first.

Quit any running RoRoRo from the tray. Check with `Get-Process ROROROblox.App -ErrorAction SilentlyContinue | Select-Object Path` that nothing is running, then start the Release build: `src\ROROROblox.App\bin\Release\net10.0-windows\ROROROblox.App.exe` (adjust the TFM folder to what `ls src/ROROROblox.App/bin/Release` shows).

Check:
- The day's log (`%LOCALAPPDATA%\ROROROblox\logs\rororoblox-<yyyyMMdd>.log`) has, within a few seconds, `Known issues: kept 0 entries from None: NetworkFailed.` at Information.
- Tools ▾ lists "Known Roblox issues" after Plugins, with no count.
- The page opens from the menu and with Ctrl+7 in the Tools window. It says "No known Roblox issues right now." and "Last checked at <time>."
- Ctrl+1 through Ctrl+6 still open Games, Settings, History, Diagnostics, Plugins, About.
- Switch the UI language to French in Settings › Appearance. The page's heading and empty text change language; the "Issue descriptions are in English" line stays hidden, because there are no entries.
- No notice row appears in the main window.

Quit RoRoRo from the tray when done.

- [ ] **Step 3: Add the feature row**

In `docs/features.md`, under `## Runtime awareness (`Core/Diagnostics/`)`, add this row at the end of that section's table:

```markdown
| Known Roblox issues | A signed list of Roblox-side problems, each with a workaround and the RoRoRo feature that helps. Tools › Known Roblox issues lists every entry; a serious entry that applies to the version a launch will run raises a one-time notice in the main window, suppressed while the contested-singleton warning shows. Downloaded at startup (after the gate) and every four hours, verified with the compat key, cached and re-verified; published by editing `known-issues.json` and running the `compat` workflow. | `Core/KnownIssues/`, `App/KnownIssues/`, `known-issues.json`, `tools/CompatSigner` (`--validate-known-issues`) | always; entries decide the rest | active |
```

- [ ] **Step 4: Add the decision**

Append to `docs/decisions.md`:

```markdown
### 2026-09-23 — Known Roblox issues ship as a signed feed, matched to the version a launch will run

**Context:** Twice a clan member blamed RoRoRo for a Roblox fault: first a memory leak, which is how the memory watchdog and Recycle came to exist, then on 2026-09-22 a window that froze on drag until the Windows key was pressed, traced into Roblox's own render call on 0.739. Both answers existed and had nowhere to live in the app. Spec `docs/superpowers/specs/2026-09-23-known-roblox-issues-design.md`, approved by Este section by section the same day.
**Consequences:** A separate `known-issues.json`, not entries folded into `roblox-compat.json` (a wording fix must never risk the file that carries the mutex name) and not an unsigned Pages file (anyone who could change it could show the whole clan a fake "fix"). It is validated by the app's own parser inside `tools/CompatSigner`, so CI cannot sign a file the app would refuse; signed with the compat key (one product, one trust surface); attached to every release by `release.yml`, with a guard test, because the app reads only the release marked Latest — the gap that left the plugin catalog at Ur Score 0.3.5 for months. Entries are matched against `GetHandlerRobloxVersion` (what a launch will run), falling back to the installed read, which F-104 measured as a coin flip during a launch batch; the version gates only the notice and the count, never the page. Three corrections were made while planning, recorded in the spec: the version source; Roblox's `FileVersion` is `0, 740, 0, 7400927`, which `Version.TryParse` rejects, so this feature normalises it; and the main window's notice area is a stack of rows, so the known-issues row yields to the contested warning rather than to an empty slot. **Found and deliberately left:** `RobloxCompatChecker.CheckAsync` passes that same raw comma string to `Version.TryParse`, so its version-drift banner can never fire. Fixing the parse would show a drift banner to every 0.740 user the day it shipped, because `knownGoodVersionMax` is still 0.729.24 — it needs its own decision.
**Evidence:** plan `docs/superpowers/plans/2026-09-23-known-roblox-issues.md`; `Core/KnownIssues/`, `App/KnownIssues/`; `KnownIssuesPublishingFenceTests`; `KnownIssuesFeedTests.ChangingOneByteOfTheSavedCopyGetsItIgnored`, checked by breaking the verification and watching it fail; the render PNGs in all four built-in themes; the live run's `NetworkFailed` log line.
```

Then log the same decision to the 626 Labs dashboard with `mcp__626labs-cloud__manage_decisions` (`action: log`), passing `filesChanged` and `nextSteps` as real arguments, and confirm it landed with a search, per the standing note that hand-written parameter tags are silently swallowed.

- [ ] **Step 5: Commit**

```bash
git add docs/features.md docs/decisions.md
git commit -m "docs: record Known Roblox issues in the feature map and the decision log

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 6: Hand back to the owner**

Do not merge, push to `main`, tag, or run the `compat` workflow. Report: the branch name and commit list; the three full-suite runs with their counts; the four render PNG paths; the live-run observations from Step 2. Name the two open follow-ups: the first real entry (the window freeze) waits on checking its frame-rate advice, and the drift-banner defect needs its own decision.
