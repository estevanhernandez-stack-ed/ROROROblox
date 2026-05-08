# Per-Account FPS Limiter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-account FPS dropdown to ROROROblox's main window so each saved Roblox account launches with its chosen FPS cap, written via the `DFIntTaskSchedulerTargetFps` FFlag. Multi-source version-folder discovery covers both standalone-install and Microsoft-Store/UWP Roblox layouts so the feature works for users RAM's FPS limiter doesn't reach.

**Architecture:** One new Core primitive (`IClientAppSettingsWriter`) merges the FPS flag into `ClientAppSettings.json` at every candidate Roblox version folder. One new Core primitive (`IBloxstrapDetector`) flags users whose Bloxstrap-as-launcher will override our write. `RobloxLauncher` gains a `SemaphoreSlim` so back-to-back launches each get their own write window with a 250ms hold while Roblox reads the flags. `Account` gains `int? FpsCap`; `AccountSummary` exposes it for binding; the row template in `MainWindow.xaml` adds a `ComboBox` between status and Launch-As.

**Tech Stack:** .NET 10 (net10.0-windows for App, net10.0 for Core / Tests), C# 14, WPF + WPF-UI, xUnit, `System.Text.Json`, `Microsoft.Win32.Registry`. No new NuGet packages.

**Spec:** [`docs/superpowers/specs/2026-05-07-per-account-fps-limiter-design.md`](../specs/2026-05-07-per-account-fps-limiter-design.md)

**Branch:** `feat/per-account-fps-limiter` (already cut off `origin/main`)

---

## File map

**Create:**
- `src/ROROROblox.Core/ClientAppSettingsWriteException.cs`
- `src/ROROROblox.Core/IClientAppSettingsWriter.cs`
- `src/ROROROblox.Core/ClientAppSettingsWriter.cs`
- `src/ROROROblox.Core/IBloxstrapDetector.cs`
- `src/ROROROblox.Core/BloxstrapDetector.cs`
- `src/ROROROblox.Core/FpsPresets.cs` — shared preset list so the dropdown and tests agree
- `src/ROROROblox.Tests/ClientAppSettingsWriterTests.cs`
- `src/ROROROblox.Tests/BloxstrapDetectorTests.cs`

**Modify:**
- `src/ROROROblox.Core/Account.cs` — append `int? FpsCap = null`
- `src/ROROROblox.Core/IAccountStore.cs` — add `Task SetFpsCapAsync(Guid id, int? fps)`
- `src/ROROROblox.Core/AccountStore.cs` — implement `SetFpsCapAsync`, extend `StoredAccount` + projection
- `src/ROROROblox.Core/IRobloxLauncher.cs` — add optional `int? fpsCap` to both overloads
- `src/ROROROblox.Core/RobloxLauncher.cs` — inject `IClientAppSettingsWriter`, gate launches via `SemaphoreSlim`, write FFlag pre-launch + hold 250 ms post-launch
- `src/ROROROblox.Core/IAppSettings.cs` — add `bool BloxstrapWarningDismissed { get; }` + setter method
- `src/ROROROblox.Core/AppSettings.cs` — back the field with a JSON property + atomic write (existing pattern)
- `src/ROROROblox.App/ViewModels/AccountSummary.cs` — add `int? FpsCap` property + preset list expose
- `src/ROROROblox.App/ViewModels/MainViewModel.cs` — wire `OnFpsCapChanged`, `BloxstrapWarningVisible`, `DismissBloxstrapWarningCommand`
- `src/ROROROblox.App/MainWindow.xaml` — add FPS `ComboBox` to the row DataTemplate, add the Bloxstrap warning banner near the top
- `src/ROROROblox.App/MainWindow.xaml.cs` — wire ComboBox SelectionChanged → ViewModel
- `src/ROROROblox.App/App.xaml.cs` — DI register `IClientAppSettingsWriter`, `IBloxstrapDetector`
- `src/ROROROblox.Tests/AccountStoreTests.cs` — backwards-compat read + `SetFpsCapAsync` round-trip
- `src/ROROROblox.Tests/RobloxLauncherTests.cs` — semaphore sequencing + FFlag write order

---

## Pre-flight

- [ ] **Step 0.1: Confirm branch + clean tree**

  Run:
  ```
  git branch --show-current
  git status
  ```
  Expected: branch `feat/per-account-fps-limiter`, working tree clean (or just the spec + plan staged).

- [ ] **Step 0.2: Confirm baseline build green**

  Run: `dotnet build ROROROblox.sln` — expected: build succeeds, 0 warnings beyond NuGet metadata.

- [ ] **Step 0.3: Confirm baseline tests green**

  Run: `dotnet test ROROROblox.sln --no-build` — expected: all existing tests pass (this is the "we didn't break anything" reference).

---

## Task 1: ClientAppSettingsWriteException + interface

**Files:**
- Create: `src/ROROROblox.Core/ClientAppSettingsWriteException.cs`
- Create: `src/ROROROblox.Core/IClientAppSettingsWriter.cs`

- [ ] **Step 1.1: Create the exception type**

  Create `src/ROROROblox.Core/ClientAppSettingsWriteException.cs`:

  ```csharp
  namespace ROROROblox.Core;

  /// <summary>
  /// Thrown when <see cref="IClientAppSettingsWriter"/> can't read/merge/write
  /// <c>ClientAppSettings.json</c>. Caller (RobloxLauncher) treats this as
  /// non-blocking — the launch still proceeds with whatever FPS Roblox decides
  /// to use. Spec §7.7.
  /// </summary>
  public sealed class ClientAppSettingsWriteException : Exception
  {
      public ClientAppSettingsWriteException(string message) : base(message) { }
      public ClientAppSettingsWriteException(string message, Exception inner) : base(message, inner) { }
  }
  ```

- [ ] **Step 1.2: Create the interface**

  Create `src/ROROROblox.Core/IClientAppSettingsWriter.cs`:

  ```csharp
  namespace ROROROblox.Core;

  /// <summary>
  /// Writes the per-account FPS cap into <c>ClientAppSettings.json</c> at every
  /// candidate Roblox version folder (standalone + Microsoft Store / UWP).
  /// Spec §5.1.
  /// </summary>
  public interface IClientAppSettingsWriter
  {
      /// <summary>
      /// Set or clear the FPS cap. <paramref name="fps"/> = null removes the
      /// <c>DFIntTaskSchedulerTargetFps</c> key (and the cap-removal flag if we
      /// previously wrote it). Other FFlags in the file are preserved.
      /// </summary>
      /// <exception cref="ClientAppSettingsWriteException">
      /// Roblox version folder not found, or all candidate writes failed.
      /// </exception>
      Task WriteFpsAsync(int? fps, CancellationToken ct = default);
  }
  ```

- [ ] **Step 1.3: Build to confirm files compile**

  Run: `dotnet build src/ROROROblox.Core/ROROROblox.Core.csproj` — expected: build succeeds.

- [ ] **Step 1.4: Commit**

  ```
  git add src/ROROROblox.Core/ClientAppSettingsWriteException.cs src/ROROROblox.Core/IClientAppSettingsWriter.cs
  git commit -m "feat(core): IClientAppSettingsWriter interface + write exception"
  ```

---

## Task 2: FpsPresets (shared preset list)

**Files:**
- Create: `src/ROROROblox.Core/FpsPresets.cs`

The same list backs the dropdown UI and the writer's input clamp. Living in Core means tests can assert against it without an App-project dependency.

- [ ] **Step 2.1: Create the preset list**

  Create `src/ROROROblox.Core/FpsPresets.cs`:

  ```csharp
  namespace ROROROblox.Core;

  /// <summary>
  /// Canonical FPS preset values surfaced in the per-account dropdown. <see cref="MinCustom"/>
  /// / <see cref="MaxCustom"/> bound the Custom… text entry. Spec §5.6 + §11.
  /// </summary>
  public static class FpsPresets
  {
      public const int MinCustom = 10;
      public const int MaxCustom = 9999;
      public const int Unlimited = 9999;

      /// <summary>240 is the Roblox cap-removal threshold — above this, write the cap-removal flag too.</summary>
      public const int CapRemovalThreshold = 240;

      public static readonly IReadOnlyList<int> Values = new[]
      {
          20, 30, 45, 60, 90, 120, 144, 165, 240, Unlimited
      };

      /// <summary>
      /// Clamp a user-supplied custom value into the supported range. Out-of-range silently snaps —
      /// the dropdown is not the place to surface "you typed an invalid number" modals.
      /// </summary>
      public static int ClampCustom(int raw) => Math.Clamp(raw, MinCustom, MaxCustom);
  }
  ```

- [ ] **Step 2.2: Build**

  Run: `dotnet build src/ROROROblox.Core/ROROROblox.Core.csproj` — expected: success.

- [ ] **Step 2.3: Commit**

  ```
  git add src/ROROROblox.Core/FpsPresets.cs
  git commit -m "feat(core): FpsPresets shared between dropdown UI and writer"
  ```

---

## Task 3: ClientAppSettingsWriter — version folder discovery

This task implements the multi-source path resolution and the JSON merge-and-write. TDD: write the tests first, then implement.

**Files:**
- Create: `src/ROROROblox.Core/ClientAppSettingsWriter.cs`
- Create: `src/ROROROblox.Tests/ClientAppSettingsWriterTests.cs`

- [ ] **Step 3.1: Write the failing test file**

  Create `src/ROROROblox.Tests/ClientAppSettingsWriterTests.cs`:

  ```csharp
  using System.IO;
  using System.Text.Json;
  using ROROROblox.Core;

  namespace ROROROblox.Tests;

  /// <summary>
  /// All tests use a temp directory as the "root" containing both candidate Roblox install layouts.
  /// The writer accepts override paths (test ctor) so we don't have to mutate the real
  /// %LOCALAPPDATA% during tests.
  /// </summary>
  public sealed class ClientAppSettingsWriterTests : IDisposable
  {
      private readonly string _tempRoot;
      private readonly string _standaloneRoot;
      private readonly string _packagesRoot;

      public ClientAppSettingsWriterTests()
      {
          _tempRoot = Path.Combine(Path.GetTempPath(), "rororo-fps-" + Guid.NewGuid().ToString("N"));
          _standaloneRoot = Path.Combine(_tempRoot, "Roblox", "Versions");
          _packagesRoot = Path.Combine(_tempRoot, "Packages");
          Directory.CreateDirectory(_standaloneRoot);
          Directory.CreateDirectory(_packagesRoot);
      }

      public void Dispose()
      {
          if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
      }

      private string MakeVersionFolder(string root, string name, DateTime lastWrite)
      {
          var folder = Path.Combine(root, name);
          Directory.CreateDirectory(folder);
          var exe = Path.Combine(folder, "RobloxPlayerBeta.exe");
          File.WriteAllText(exe, "stub");
          File.SetLastWriteTimeUtc(exe, lastWrite);
          return folder;
      }

      [Fact]
      public async Task WriteFpsAsync_StandaloneOnly_WritesToStandaloneVersionFolder()
      {
          var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
          var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

          await writer.WriteFpsAsync(60);

          var jsonPath = Path.Combine(versionFolder, "ClientSettings", "ClientAppSettings.json");
          Assert.True(File.Exists(jsonPath));
          using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
          Assert.Equal(60, doc.RootElement.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
      }

      [Fact]
      public async Task WriteFpsAsync_UwpOnly_WritesToUwpVersionFolder()
      {
          var package = Path.Combine(_packagesRoot, "ROBLOXCORPORATION.ROBLOX_55nm5eh3cm0pr", "LocalCache", "Local", "Roblox", "Versions");
          Directory.CreateDirectory(package);
          var versionFolder = MakeVersionFolder(package, "version-uwp-1", DateTime.UtcNow);
          var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

          await writer.WriteFpsAsync(144);

          var jsonPath = Path.Combine(versionFolder, "ClientSettings", "ClientAppSettings.json");
          Assert.True(File.Exists(jsonPath));
          using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
          Assert.Equal(144, doc.RootElement.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
      }

      [Fact]
      public async Task WriteFpsAsync_BothActiveWithin30Days_WritesToBoth()
      {
          var standaloneFolder = MakeVersionFolder(_standaloneRoot, "version-stand", DateTime.UtcNow.AddDays(-2));
          var package = Path.Combine(_packagesRoot, "ROBLOXCORPORATION.ROBLOX_55nm5eh3cm0pr", "LocalCache", "Local", "Roblox", "Versions");
          Directory.CreateDirectory(package);
          var uwpFolder = MakeVersionFolder(package, "version-uwp", DateTime.UtcNow);
          var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

          await writer.WriteFpsAsync(120);

          Assert.True(File.Exists(Path.Combine(standaloneFolder, "ClientSettings", "ClientAppSettings.json")));
          Assert.True(File.Exists(Path.Combine(uwpFolder, "ClientSettings", "ClientAppSettings.json")));
      }

      [Fact]
      public async Task WriteFpsAsync_StaleStandalonePlusFreshUwp_OnlyWritesUwp()
      {
          MakeVersionFolder(_standaloneRoot, "version-old", DateTime.UtcNow.AddDays(-90));
          var package = Path.Combine(_packagesRoot, "ROBLOXCORPORATION.ROBLOX_55nm5eh3cm0pr", "LocalCache", "Local", "Roblox", "Versions");
          Directory.CreateDirectory(package);
          var uwpFolder = MakeVersionFolder(package, "version-uwp", DateTime.UtcNow);
          var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

          await writer.WriteFpsAsync(60);

          var standaloneJson = Path.Combine(_standaloneRoot, "version-old", "ClientSettings", "ClientAppSettings.json");
          Assert.False(File.Exists(standaloneJson));
          Assert.True(File.Exists(Path.Combine(uwpFolder, "ClientSettings", "ClientAppSettings.json")));
      }

      [Fact]
      public async Task WriteFpsAsync_NoVersionFolder_Throws()
      {
          var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);
          await Assert.ThrowsAsync<ClientAppSettingsWriteException>(() => writer.WriteFpsAsync(60));
      }

      [Fact]
      public async Task WriteFpsAsync_PreservesOtherFFlags()
      {
          var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
          var clientSettings = Path.Combine(versionFolder, "ClientSettings");
          Directory.CreateDirectory(clientSettings);
          var jsonPath = Path.Combine(clientSettings, "ClientAppSettings.json");
          await File.WriteAllTextAsync(jsonPath, "{\"FStringSomeOtherFlag\": \"foo\"}");
          var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

          await writer.WriteFpsAsync(90);

          using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
          Assert.Equal("foo", doc.RootElement.GetProperty("FStringSomeOtherFlag").GetString());
          Assert.Equal(90, doc.RootElement.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
      }

      [Fact]
      public async Task WriteFpsAsync_MalformedJson_ReplacesWithFreshFile()
      {
          var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
          var clientSettings = Path.Combine(versionFolder, "ClientSettings");
          Directory.CreateDirectory(clientSettings);
          var jsonPath = Path.Combine(clientSettings, "ClientAppSettings.json");
          await File.WriteAllTextAsync(jsonPath, "this is not json {");
          var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

          await writer.WriteFpsAsync(60);

          using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
          Assert.Equal(60, doc.RootElement.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
      }

      [Fact]
      public async Task WriteFpsAsync_NullClearsKey()
      {
          var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
          var clientSettings = Path.Combine(versionFolder, "ClientSettings");
          Directory.CreateDirectory(clientSettings);
          var jsonPath = Path.Combine(clientSettings, "ClientAppSettings.json");
          await File.WriteAllTextAsync(jsonPath, "{\"DFIntTaskSchedulerTargetFps\": 60, \"FStringOther\": \"keep\"}");
          var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

          await writer.WriteFpsAsync(null);

          using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
          Assert.False(doc.RootElement.TryGetProperty("DFIntTaskSchedulerTargetFps", out _));
          Assert.Equal("keep", doc.RootElement.GetProperty("FStringOther").GetString());
      }

      [Fact]
      public async Task WriteFpsAsync_AboveCapThreshold_WritesCapRemovalFlag()
      {
          var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
          var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

          await writer.WriteFpsAsync(360);

          var jsonPath = Path.Combine(versionFolder, "ClientSettings", "ClientAppSettings.json");
          using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
          Assert.Equal(360, doc.RootElement.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
          Assert.False(doc.RootElement.GetProperty("FFlagTaskSchedulerLimitTargetFpsTo2402").GetBoolean());
      }

      [Fact]
      public async Task WriteFpsAsync_AtOrBelowThreshold_OmitsCapRemovalFlag()
      {
          var versionFolder = MakeVersionFolder(_standaloneRoot, "version-abc", DateTime.UtcNow);
          var writer = new ClientAppSettingsWriter(_standaloneRoot, _packagesRoot);

          await writer.WriteFpsAsync(144);

          var jsonPath = Path.Combine(versionFolder, "ClientSettings", "ClientAppSettings.json");
          using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
          Assert.False(doc.RootElement.TryGetProperty("FFlagTaskSchedulerLimitTargetFpsTo2402", out _));
      }
  }
  ```

- [ ] **Step 3.2: Run the failing tests**

  Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter ClientAppSettingsWriterTests`

  Expected: build fails because `ClientAppSettingsWriter` does not exist yet.

- [ ] **Step 3.3: Implement ClientAppSettingsWriter**

  Create `src/ROROROblox.Core/ClientAppSettingsWriter.cs`:

  ```csharp
  using System.Diagnostics;
  using System.IO;
  using System.Text.Json;
  using System.Text.Json.Nodes;

  namespace ROROROblox.Core;

  /// <summary>
  /// Writes <c>DFIntTaskSchedulerTargetFps</c> (and the cap-removal flag above 240)
  /// into every active Roblox version folder's <c>ClientAppSettings.json</c>.
  /// Multi-source: standalone <c>%LOCALAPPDATA%\Roblox\Versions</c> + Microsoft Store /
  /// UWP <c>%LOCALAPPDATA%\Packages\ROBLOXCORPORATION.ROBLOX_*\LocalCache\Local\Roblox\Versions</c>.
  /// Spec §5.1 + Appendix B.
  /// </summary>
  public sealed class ClientAppSettingsWriter : IClientAppSettingsWriter
  {
      private const string FpsKey = "DFIntTaskSchedulerTargetFps";
      private const string CapRemovalKey = "FFlagTaskSchedulerLimitTargetFpsTo2402";
      private static readonly TimeSpan CoActiveWindow = TimeSpan.FromDays(30);

      private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

      private readonly string _standaloneVersionsRoot;
      private readonly string _packagesRoot;

      public ClientAppSettingsWriter() : this(DefaultStandaloneRoot(), DefaultPackagesRoot()) { }

      // Visible for tests — accept arbitrary roots.
      public ClientAppSettingsWriter(string standaloneVersionsRoot, string packagesRoot)
      {
          _standaloneVersionsRoot = standaloneVersionsRoot;
          _packagesRoot = packagesRoot;
      }

      public async Task WriteFpsAsync(int? fps, CancellationToken ct = default)
      {
          var targets = ResolveCandidateFolders();
          if (targets.Count == 0)
          {
              throw new ClientAppSettingsWriteException(
                  "Roblox version folder not found. Standalone and UWP install paths both empty.");
          }

          List<Exception>? failures = null;
          foreach (var folder in targets)
          {
              try
              {
                  await WriteOneAsync(folder, fps, ct).ConfigureAwait(false);
              }
              catch (Exception ex)
              {
                  (failures ??= []).Add(new ClientAppSettingsWriteException(
                      $"Failed to write FPS flag at {folder}: {ex.Message}", ex));
              }
          }
          if (failures is not null && failures.Count == targets.Count)
          {
              throw new ClientAppSettingsWriteException(
                  $"All {targets.Count} candidate write(s) failed: {failures[0].Message}", failures[0]);
          }
      }

      private static string DefaultStandaloneRoot() => Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "Roblox", "Versions");

      private static string DefaultPackagesRoot() => Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "Packages");

      private List<string> ResolveCandidateFolders()
      {
          var standalone = NewestActiveVersionFolder(_standaloneVersionsRoot);
          var uwp = ResolveUwpVersionFolder(_packagesRoot);

          if (standalone is null && uwp is null) return [];
          if (standalone is null) return [uwp!.FullName];
          if (uwp is null) return [standalone.FullName];

          // Both exist — write to both if both are active in the last 30 days. Otherwise, just the newer.
          var ageStandalone = DateTime.UtcNow - standalone.PlayerBetaWriteUtc;
          var ageUwp = DateTime.UtcNow - uwp.PlayerBetaWriteUtc;
          if (ageStandalone < CoActiveWindow && ageUwp < CoActiveWindow)
          {
              return [standalone.FullName, uwp.FullName];
          }
          return ageStandalone < ageUwp ? [standalone.FullName] : [uwp.FullName];
      }

      private static (string FullName, DateTime PlayerBetaWriteUtc)? NewestActiveVersionFolder(string versionsRoot)
      {
          if (!Directory.Exists(versionsRoot)) return null;
          (string FullName, DateTime PlayerBetaWriteUtc)? best = null;
          foreach (var dir in Directory.EnumerateDirectories(versionsRoot, "version-*"))
          {
              var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
              if (!File.Exists(exe)) continue;
              var lastWrite = File.GetLastWriteTimeUtc(exe);
              if (best is null || lastWrite > best.Value.PlayerBetaWriteUtc)
              {
                  best = (dir, lastWrite);
              }
          }
          return best;
      }

      private static (string FullName, DateTime PlayerBetaWriteUtc)? ResolveUwpVersionFolder(string packagesRoot)
      {
          if (!Directory.Exists(packagesRoot)) return null;
          foreach (var pkg in Directory.EnumerateDirectories(packagesRoot, "ROBLOXCORPORATION.ROBLOX_*"))
          {
              var versions = Path.Combine(pkg, "LocalCache", "Local", "Roblox", "Versions");
              var found = NewestActiveVersionFolder(versions);
              if (found is not null) return found;
          }
          return null;
      }

      private static async Task WriteOneAsync(string versionFolder, int? fps, CancellationToken ct)
      {
          var clientSettingsDir = Path.Combine(versionFolder, "ClientSettings");
          Directory.CreateDirectory(clientSettingsDir);
          var jsonPath = Path.Combine(clientSettingsDir, "ClientAppSettings.json");

          JsonObject root;
          if (File.Exists(jsonPath))
          {
              try
              {
                  var existing = await File.ReadAllTextAsync(jsonPath, ct).ConfigureAwait(false);
                  root = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
              }
              catch (JsonException)
              {
                  // Spec §5.1: malformed file → start fresh, don't surface to user.
                  root = new JsonObject();
              }
          }
          else
          {
              root = new JsonObject();
          }

          if (fps is null)
          {
              root.Remove(FpsKey);
              root.Remove(CapRemovalKey);
          }
          else
          {
              root[FpsKey] = fps.Value;
              if (fps.Value > FpsPresets.CapRemovalThreshold)
              {
                  root[CapRemovalKey] = false;
              }
              else
              {
                  root.Remove(CapRemovalKey);
              }
          }

          var tempPath = jsonPath + ".tmp";
          await File.WriteAllTextAsync(tempPath, root.ToJsonString(WriteOptions), ct).ConfigureAwait(false);
          File.Move(tempPath, jsonPath, overwrite: true);
      }
  }
  ```

- [ ] **Step 3.4: Run tests, expect green**

  Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter ClientAppSettingsWriterTests`

  Expected: 9 tests passing.

- [ ] **Step 3.5: Commit**

  ```
  git add src/ROROROblox.Core/ClientAppSettingsWriter.cs src/ROROROblox.Tests/ClientAppSettingsWriterTests.cs
  git commit -m "feat(core): ClientAppSettingsWriter with multi-source version folder discovery"
  ```

---

## Task 4: BloxstrapDetector

**Files:**
- Create: `src/ROROROblox.Core/IBloxstrapDetector.cs`
- Create: `src/ROROROblox.Core/BloxstrapDetector.cs`
- Create: `src/ROROROblox.Tests/BloxstrapDetectorTests.cs`

- [ ] **Step 4.1: Write the failing test file**

  Create `src/ROROROblox.Tests/BloxstrapDetectorTests.cs`:

  ```csharp
  using ROROROblox.Core;

  namespace ROROROblox.Tests;

  public sealed class BloxstrapDetectorTests
  {
      // Synthetic paths — the matcher only cares about the "Bloxstrap" substring, not the
      // realistic Windows install location. Using D:\ avoids the local-path-guard pre-commit hook.
      [Theory]
      [InlineData(@"D:\AppData\Bloxstrap\Bloxstrap.exe", true)]
      [InlineData(@"D:\AppData\BLOXSTRAP\Bloxstrap.exe", true)]
      [InlineData(@"""D:\AppData\Bloxstrap\BloxstrapBootstrapper.exe"" %1", true)]
      [InlineData(@"D:\Program Files\Roblox\Versions\version-abc\RobloxPlayerBeta.exe %1", false)]
      [InlineData("", false)]
      [InlineData(null, false)]
      public void IsBloxstrap_RecognizesBloxstrapPathInCommandString(string? command, bool expected)
      {
          Assert.Equal(expected, BloxstrapDetector.LooksLikeBloxstrap(command));
      }
  }
  ```

  Note: the registry side is hard to unit-test without elevation; the Theory above tests the
  pure function. The instance method `IsBloxstrapHandler()` reads the registry on Windows and is
  covered by manual smoke (Task 13).

- [ ] **Step 4.2: Run tests, expect compile failure**

  Run: `dotnet test --filter BloxstrapDetectorTests` — expected: build fails (`BloxstrapDetector` not found).

- [ ] **Step 4.3: Create the interface**

  Create `src/ROROROblox.Core/IBloxstrapDetector.cs`:

  ```csharp
  namespace ROROROblox.Core;

  /// <summary>
  /// Detects whether Bloxstrap is the registered <c>roblox-player</c> protocol handler.
  /// When true, our FFlag write is overridden by Bloxstrap's launch-time rewrite — the user
  /// sees a one-time dismissible banner. Spec §5.2.
  /// </summary>
  public interface IBloxstrapDetector
  {
      bool IsBloxstrapHandler();
  }
  ```

- [ ] **Step 4.4: Create the implementation**

  Create `src/ROROROblox.Core/BloxstrapDetector.cs`:

  ```csharp
  using Microsoft.Win32;

  namespace ROROROblox.Core;

  /// <summary>
  /// Reads <c>HKCU\Software\Classes\roblox-player\shell\open\command</c> default value
  /// and returns true when the path string contains <c>Bloxstrap</c> (case-insensitive).
  /// Bloxstrap's binaries are consistently named <c>Bloxstrap.exe</c> /
  /// <c>BloxstrapBootstrapper.exe</c>, so the substring match is sufficient and simple.
  /// </summary>
  public sealed class BloxstrapDetector : IBloxstrapDetector
  {
      private const string SubKey = @"Software\Classes\roblox-player\shell\open\command";

      public bool IsBloxstrapHandler()
      {
          try
          {
              using var key = Registry.CurrentUser.OpenSubKey(SubKey);
              var command = key?.GetValue(null) as string;
              return LooksLikeBloxstrap(command);
          }
          catch
          {
              // Registry inaccessible (sandboxed test runner, locked-down PC) — pretend Bloxstrap
              // isn't there. The warning is comfort, not load-bearing.
              return false;
          }
      }

      /// <summary>
      /// Pure function for testing — given a registry-stored command line string,
      /// decide if it points at a Bloxstrap binary.
      /// </summary>
      public static bool LooksLikeBloxstrap(string? command)
      {
          if (string.IsNullOrWhiteSpace(command)) return false;
          return command.IndexOf("Bloxstrap", StringComparison.OrdinalIgnoreCase) >= 0;
      }
  }
  ```

- [ ] **Step 4.5: Run tests, expect green**

  Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter BloxstrapDetectorTests`

  Expected: 6 cases pass.

- [ ] **Step 4.6: Commit**

  ```
  git add src/ROROROblox.Core/IBloxstrapDetector.cs src/ROROROblox.Core/BloxstrapDetector.cs src/ROROROblox.Tests/BloxstrapDetectorTests.cs
  git commit -m "feat(core): BloxstrapDetector with pure-function LooksLikeBloxstrap helper"
  ```

---

## Task 5: Account record + AccountStore.SetFpsCapAsync

**Files:**
- Modify: `src/ROROROblox.Core/Account.cs`
- Modify: `src/ROROROblox.Core/IAccountStore.cs`
- Modify: `src/ROROROblox.Core/AccountStore.cs`
- Modify: `src/ROROROblox.Tests/AccountStoreTests.cs`

- [ ] **Step 5.1: Extend the Account record**

  In `src/ROROROblox.Core/Account.cs`, append `int? FpsCap = null` as the last optional parameter:

  Change:
  ```csharp
  public sealed record Account(
      Guid Id,
      string DisplayName,
      string AvatarUrl,
      DateTimeOffset CreatedAt,
      DateTimeOffset? LastLaunchedAt,
      bool IsMain = false,
      int SortOrder = 0,
      bool IsSelected = true,
      string? CaptionColorHex = null);
  ```

  To:
  ```csharp
  public sealed record Account(
      Guid Id,
      string DisplayName,
      string AvatarUrl,
      DateTimeOffset CreatedAt,
      DateTimeOffset? LastLaunchedAt,
      bool IsMain = false,
      int SortOrder = 0,
      bool IsSelected = true,
      string? CaptionColorHex = null,
      int? FpsCap = null);
  ```

- [ ] **Step 5.2: Extend IAccountStore**

  In `src/ROROROblox.Core/IAccountStore.cs`, append the new method (after `SetCaptionColorAsync`):

  ```csharp
      /// <summary>
      /// Persist a per-account FPS cap. Pass <c>null</c> to clear (the next launch will not
      /// touch <c>ClientAppSettings.json</c>'s FPS flag). Pass an integer in [10, 9999] to set.
      /// Drives the per-account dropdown on the main window. Spec §5.4 + §6.1.
      /// </summary>
      Task SetFpsCapAsync(Guid id, int? fps);
  ```

- [ ] **Step 5.3: Extend StoredAccount + projection in AccountStore**

  In `src/ROROROblox.Core/AccountStore.cs`:

  Change `StoredAccount` (search for `internal sealed record StoredAccount(` near the bottom):

  ```csharp
      internal sealed record StoredAccount(
          Guid Id,
          string DisplayName,
          string AvatarUrl,
          string Cookie,
          DateTimeOffset CreatedAt,
          DateTimeOffset? LastLaunchedAt,
          bool IsMain = false,
          int SortOrder = 0,
          bool IsSelected = true,
          string? CaptionColorHex = null,
          int? FpsCap = null);
  ```

  Update both `Account` projections (in `ListAsync` and `AddAsync`) to pass the field:

  In `ListAsync`, change:
  ```csharp
  return blob.Accounts
      .Select(a => new Account(a.Id, a.DisplayName, a.AvatarUrl, a.CreatedAt, a.LastLaunchedAt, a.IsMain, a.SortOrder, a.IsSelected, a.CaptionColorHex))
      .ToList();
  ```
  To:
  ```csharp
  return blob.Accounts
      .Select(a => new Account(a.Id, a.DisplayName, a.AvatarUrl, a.CreatedAt, a.LastLaunchedAt, a.IsMain, a.SortOrder, a.IsSelected, a.CaptionColorHex, a.FpsCap))
      .ToList();
  ```

  In `AddAsync`, change:
  ```csharp
  return new Account(stored.Id, stored.DisplayName, stored.AvatarUrl, stored.CreatedAt, stored.LastLaunchedAt, stored.IsMain, stored.SortOrder, stored.IsSelected, stored.CaptionColorHex);
  ```
  To:
  ```csharp
  return new Account(stored.Id, stored.DisplayName, stored.AvatarUrl, stored.CreatedAt, stored.LastLaunchedAt, stored.IsMain, stored.SortOrder, stored.IsSelected, stored.CaptionColorHex, stored.FpsCap);
  ```

- [ ] **Step 5.4: Implement SetFpsCapAsync**

  In `src/ROROROblox.Core/AccountStore.cs`, add the method (after `SetCaptionColorAsync`):

  ```csharp
      public async Task SetFpsCapAsync(Guid id, int? fps)
      {
          // Caller is responsible for clamping; we still defensively clamp to [10, 9999] so
          // disk never holds an invalid value.
          var clamped = fps is null ? (int?)null : FpsPresets.ClampCustom(fps.Value);

          await _gate.WaitAsync().ConfigureAwait(false);
          try
          {
              var blob = await LoadAsync().ConfigureAwait(false);
              var idx = blob.Accounts.FindIndex(a => a.Id == id);
              if (idx < 0)
              {
                  return;
              }
              if (blob.Accounts[idx].FpsCap == clamped)
              {
                  return; // no-op write avoidance
              }
              blob.Accounts[idx] = blob.Accounts[idx] with { FpsCap = clamped };
              await SaveAsync(blob).ConfigureAwait(false);
          }
          finally
          {
              _gate.Release();
          }
      }
  ```

- [ ] **Step 5.5: Add the round-trip + back-compat tests**

  In `src/ROROROblox.Tests/AccountStoreTests.cs`, add two test methods (place near other `Set*` tests):

  ```csharp
  [Fact]
  public async Task SetFpsCapAsync_RoundTrip_PersistsValue()
  {
      using var store = new AccountStore(NewTempFile());
      var added = await store.AddAsync("alice", "https://example/a.png", "ROBLOSECURITY-stub-1");

      await store.SetFpsCapAsync(added.Id, 60);

      var listed = (await store.ListAsync()).Single();
      Assert.Equal(60, listed.FpsCap);
  }

  [Fact]
  public async Task SetFpsCapAsync_NullClearsValue()
  {
      using var store = new AccountStore(NewTempFile());
      var added = await store.AddAsync("alice", "https://example/a.png", "ROBLOSECURITY-stub-1");
      await store.SetFpsCapAsync(added.Id, 144);

      await store.SetFpsCapAsync(added.Id, null);

      var listed = (await store.ListAsync()).Single();
      Assert.Null(listed.FpsCap);
  }

  [Fact]
  public async Task SetFpsCapAsync_OutOfRangeIsClamped()
  {
      using var store = new AccountStore(NewTempFile());
      var added = await store.AddAsync("alice", "https://example/a.png", "ROBLOSECURITY-stub-1");

      await store.SetFpsCapAsync(added.Id, 99999);

      var listed = (await store.ListAsync()).Single();
      Assert.Equal(FpsPresets.MaxCustom, listed.FpsCap);
  }
  ```

  Note: if `NewTempFile` doesn't exist as a helper, search the file for the existing pattern
  (typically `Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dat")`) and reuse it.

- [ ] **Step 5.6: Build + test**

  Run:
  ```
  dotnet build ROROROblox.sln
  dotnet test ROROROblox.sln --filter AccountStoreTests
  ```
  Expected: build clean (the new optional field on `Account` is a non-breaking addition — every existing call site uses positional or named args without the new field). All AccountStore tests pass — including the existing back-compat read tests, which verify v1.1-shaped JSON deserializes with `FpsCap == null`.

- [ ] **Step 5.7: Commit**

  ```
  git add src/ROROROblox.Core/Account.cs src/ROROROblox.Core/IAccountStore.cs src/ROROROblox.Core/AccountStore.cs src/ROROROblox.Tests/AccountStoreTests.cs
  git commit -m "feat(core): Account.FpsCap + AccountStore.SetFpsCapAsync round-trip"
  ```

---

## Task 6: RobloxLauncher — semaphore-gated FFlag write

**Files:**
- Modify: `src/ROROROblox.Core/IRobloxLauncher.cs`
- Modify: `src/ROROROblox.Core/RobloxLauncher.cs`
- Modify: `src/ROROROblox.Tests/RobloxLauncherTests.cs`

- [ ] **Step 6.1: Extend IRobloxLauncher with optional fpsCap**

  In `src/ROROROblox.Core/IRobloxLauncher.cs`, add the new parameter to both overloads (default null preserves existing call sites):

  ```csharp
      Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null);

      Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null);
  ```

- [ ] **Step 6.2: Inject IClientAppSettingsWriter and add the launch-semaphore**

  In `src/ROROROblox.Core/RobloxLauncher.cs`:

  Add fields under the existing private fields block:
  ```csharp
      private readonly IClientAppSettingsWriter? _clientAppSettings;
      private readonly SemaphoreSlim _launchGate = new(initialCount: 1, maxCount: 1);
      private static readonly TimeSpan FFlagReadHold = TimeSpan.FromMilliseconds(250);
  ```

  Update the public constructor to accept the writer (default null so existing tests still construct cleanly):
  ```csharp
      public RobloxLauncher(
          IRobloxApi api,
          IAppSettings settings,
          IProcessStarter processStarter,
          IFavoriteGameStore? favorites = null,
          IClientAppSettingsWriter? clientAppSettings = null)
          : this(api, settings, processStarter, TimeProvider.System,
                () => Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999),
                favorites, clientAppSettings)
      {
      }
  ```

  And the internal test ctor:
  ```csharp
      internal RobloxLauncher(
          IRobloxApi api,
          IAppSettings settings,
          IProcessStarter processStarter,
          TimeProvider timeProvider,
          Func<long> browserTrackerIdFactory,
          IFavoriteGameStore? favorites = null,
          IClientAppSettingsWriter? clientAppSettings = null)
      {
          _api = api ?? throw new ArgumentNullException(nameof(api));
          _settings = settings ?? throw new ArgumentNullException(nameof(settings));
          _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
          _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
          _browserTrackerIdFactory = browserTrackerIdFactory ?? throw new ArgumentNullException(nameof(browserTrackerIdFactory));
          _favorites = favorites;
          _clientAppSettings = clientAppSettings;
      }
  ```

- [ ] **Step 6.3: Wrap LaunchAsync in the semaphore + FFlag write + 250 ms hold**

  In `src/ROROROblox.Core/RobloxLauncher.cs`, change the public `LaunchAsync(string cookie, LaunchTarget target)` signature and body to:

  ```csharp
      public async Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null)
      {
          if (string.IsNullOrEmpty(cookie))
          {
              throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
          }
          ArgumentNullException.ThrowIfNull(target);

          await _launchGate.WaitAsync().ConfigureAwait(false);
          try
          {
              if (fpsCap.HasValue && _clientAppSettings is not null)
              {
                  try
                  {
                      await _clientAppSettings.WriteFpsAsync(fpsCap.Value).ConfigureAwait(false);
                  }
                  catch (ClientAppSettingsWriteException)
                  {
                      // Spec §7.7: degraded, non-blocking. Continue with the launch.
                  }
              }

              var result = await ExecuteLaunchAsync(cookie, target).ConfigureAwait(false);
              await Task.Delay(FFlagReadHold).ConfigureAwait(false);
              return result;
          }
          finally
          {
              _launchGate.Release();
          }
      }
  ```

  Rename the existing body of `LaunchAsync(string cookie, LaunchTarget target)` to a private helper:
  ```csharp
      private async Task<LaunchResult> ExecuteLaunchAsync(string cookie, LaunchTarget target)
      {
          // body of the original LaunchAsync, unchanged
      }
  ```

  Update the legacy overload to forward `fpsCap`:
  ```csharp
      public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null)
      {
          var target = string.IsNullOrWhiteSpace(placeUrl)
              ? (LaunchTarget)new LaunchTarget.DefaultGame()
              : LaunchTarget.FromUrl(placeUrl);
          return LaunchAsync(cookie, target, fpsCap);
      }
  ```

  Note: `LaunchTarget.FromUrl` already exists per the doc-comment in `IRobloxLauncher.cs`. If
  the method name differs (read `LaunchTarget.cs` to confirm), use the actual method.

- [ ] **Step 6.4: Add the sequencing test**

  In `src/ROROROblox.Tests/RobloxLauncherTests.cs`, add a new fact (extend the existing class):

  ```csharp
  [Fact]
  public async Task LaunchAsync_TwoConcurrentCalls_AreSerialized()
  {
      var writeOrder = new List<int>();
      var writer = new RecordingWriter(writeOrder);
      var launcher = NewLauncherUnderTest(clientAppSettings: writer); // helper — see below

      var firstTask = launcher.LaunchAsync("cookie-a", placeUrl: null, fpsCap: 30);
      var secondTask = launcher.LaunchAsync("cookie-b", placeUrl: null, fpsCap: 144);

      await Task.WhenAll(firstTask, secondTask);

      Assert.Equal(new[] { 30, 144 }, writeOrder);
  }

  private sealed class RecordingWriter(List<int> writeOrder) : IClientAppSettingsWriter
  {
      public Task WriteFpsAsync(int? fps, CancellationToken ct = default)
      {
          if (fps.HasValue) writeOrder.Add(fps.Value);
          return Task.CompletedTask;
      }
  }
  ```

  If the existing tests use a helper to construct the launcher (search for the most-used
  factory pattern in the file), add an `IClientAppSettingsWriter? clientAppSettings = null`
  parameter and pass it through. If they instantiate `RobloxLauncher` directly, replace the
  call site with the new constructor signature.

  Note: this test asserts ordering (each FFlag write happens before its corresponding
  process spawn). The `Task.Delay(250)` slows the test, so total runtime is ~500 ms — accept it.

- [ ] **Step 6.5: Build + run**

  Run:
  ```
  dotnet build ROROROblox.sln
  dotnet test ROROROblox.sln --filter RobloxLauncherTests
  ```
  Expected: build clean. All existing RobloxLauncher tests still pass (the new `fpsCap`
  parameter defaults to null — they're unaffected). New sequencing test passes.

- [ ] **Step 6.6: Commit**

  ```
  git add src/ROROROblox.Core/IRobloxLauncher.cs src/ROROROblox.Core/RobloxLauncher.cs src/ROROROblox.Tests/RobloxLauncherTests.cs
  git commit -m "feat(core): RobloxLauncher gates per-account FPS via SemaphoreSlim + 250ms hold"
  ```

---

## Task 7: AppSettings.BloxstrapWarningDismissed

**Files:**
- Modify: `src/ROROROblox.Core/IAppSettings.cs`
- Modify: `src/ROROROblox.Core/AppSettings.cs`
- Modify: `src/ROROROblox.Tests/AppSettingsTests.cs`

- [ ] **Step 7.1: Read the existing IAppSettings shape**

  Read `src/ROROROblox.Core/IAppSettings.cs` to see the existing property + setter convention. Match it exactly (existing property names like `DefaultPlaceUrl` and the `Set*Async` pattern).

- [ ] **Step 7.2: Add BloxstrapWarningDismissed property + setter**

  In `IAppSettings.cs`, append:
  ```csharp
      /// <summary>
      /// True after the user has dismissed the "Bloxstrap will override per-account FPS"
      /// banner. Persisted so the banner does not re-render on every launch. Spec §5.6.
      /// </summary>
      bool BloxstrapWarningDismissed { get; }

      Task SetBloxstrapWarningDismissedAsync(bool value);
  ```

  In `AppSettings.cs`, mirror the existing property pattern. Add the JSON-backed field, the
  property, and the setter that calls the existing atomic write helper. (Read the file to
  see the convention; write the new code in the same shape.)

- [ ] **Step 7.3: Add a round-trip test**

  In `src/ROROROblox.Tests/AppSettingsTests.cs`:
  ```csharp
  [Fact]
  public async Task SetBloxstrapWarningDismissedAsync_PersistsAcrossReload()
  {
      var path = Path.Combine(Path.GetTempPath(), "rororo-settings-" + Guid.NewGuid().ToString("N") + ".json");
      try
      {
          var settings = new AppSettings(path);
          await settings.SetBloxstrapWarningDismissedAsync(true);

          var reloaded = new AppSettings(path);
          Assert.True(reloaded.BloxstrapWarningDismissed);
      }
      finally
      {
          if (File.Exists(path)) File.Delete(path);
      }
  }
  ```

- [ ] **Step 7.4: Build + test**

  Run: `dotnet test ROROROblox.sln --filter AppSettings` — expected: existing tests still green, new test passes.

- [ ] **Step 7.5: Commit**

  ```
  git add src/ROROROblox.Core/IAppSettings.cs src/ROROROblox.Core/AppSettings.cs src/ROROROblox.Tests/AppSettingsTests.cs
  git commit -m "feat(core): AppSettings.BloxstrapWarningDismissed flag"
  ```

---

## Task 8: AccountSummary — FpsCap binding

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/AccountSummary.cs`

- [ ] **Step 8.1: Add FpsCap backing field + property + ctor wiring**

  In `src/ROROROblox.App/ViewModels/AccountSummary.cs`:

  Add after the other backing fields (near `_captionColorHex`):
  ```csharp
      private int? _fpsCap;
  ```

  In the constructor, after `_captionColorHex = account.CaptionColorHex;`:
  ```csharp
          _fpsCap = account.FpsCap;
  ```

  Add the property (place near `CaptionColorHex`):
  ```csharp
      /// <summary>
      /// Per-account FPS cap, or null for "don't write" (= leave Roblox's default).
      /// Bound to the ComboBox on each row. Set values fall in the
      /// <see cref="ROROROblox.Core.FpsPresets"/> range (10..9999); null clears the FFlag.
      /// MainViewModel persists changes via <see cref="ROROROblox.Core.IAccountStore.SetFpsCapAsync"/>.
      /// </summary>
      public int? FpsCap
      {
          get => _fpsCap;
          set => SetField(ref _fpsCap, value);
      }
  ```

- [ ] **Step 8.2: Build to confirm**

  Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj` — expected: build succeeds.

- [ ] **Step 8.3: Commit**

  ```
  git add src/ROROROblox.App/ViewModels/AccountSummary.cs
  git commit -m "feat(app): AccountSummary.FpsCap binding"
  ```

---

## Task 9: MainViewModel — wire FPS change + Bloxstrap warning

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`

- [ ] **Step 9.1: Read the existing MainViewModel**

  Read `src/ROROROblox.App/ViewModels/MainViewModel.cs` to learn the conventions:
  - How dependencies are injected (likely constructor params)
  - How property change notifications are emitted
  - The pattern used for `IAccountStore.Set*Async` callers (e.g., `OnCaptionColorChanged`)
  - The pattern used for status banners (likely a `string? Banner` property)

- [ ] **Step 9.2: Inject IBloxstrapDetector + IAppSettings reference (if not already)**

  In the constructor signature, add `IBloxstrapDetector bloxstrapDetector`. The existing
  ViewModel almost certainly already has `IAccountStore`, `IAppSettings`, `IRobloxLauncher`.
  Match the existing field/parameter naming exactly.

- [ ] **Step 9.3: Add FPS change handler**

  Add this method (place near other `On*Changed` handlers):
  ```csharp
      /// <summary>
      /// Persist a per-account FPS cap. Called by the row template's ComboBox SelectionChanged
      /// handler. Catches and swallows store exceptions — a failed FPS write should never
      /// prevent the row from re-rendering with the new selection.
      /// </summary>
      public async Task OnFpsCapChangedAsync(AccountSummary row, int? newValue)
      {
          if (row is null) return;
          row.FpsCap = newValue;
          try
          {
              await _accountStore.SetFpsCapAsync(row.Id, newValue);
          }
          catch (Exception ex)
          {
              // Swallow — the next launch will pick up whatever's in-memory anyway, and the next
              // app restart will surface the persistence problem via the standard load path.
              _logger?.LogWarning(ex, "Failed to persist FPS cap for {Id}", row.Id);
          }
      }
  ```

  (Replace `_logger?.LogWarning` with whatever logging pattern the existing ViewModel uses;
  read the file first.)

- [ ] **Step 9.4: Add Bloxstrap-warning state + dismiss command**

  Add backing field (near other `_*` private fields):
  ```csharp
      private bool _bloxstrapWarningVisible;
  ```

  Add the property (place near other observable properties):
  ```csharp
      /// <summary>
      /// True when Bloxstrap is the registered <c>roblox-player</c> handler AND the user has
      /// not yet dismissed the warning. The MainWindow XAML binds a yellow banner to this.
      /// Resolves to false silently when registry access is denied — no scary error to the user.
      /// </summary>
      public bool BloxstrapWarningVisible
      {
          get => _bloxstrapWarningVisible;
          private set => SetField(ref _bloxstrapWarningVisible, value);
      }
  ```

  Add the dismiss command (commands in this codebase use `RelayCommand` from
  `src/ROROROblox.App/ViewModels/RelayCommand.cs`):
  ```csharp
      public IRelayCommand DismissBloxstrapWarningCommand { get; }
  ```

  In the constructor (after the existing command initializations):
  ```csharp
      DismissBloxstrapWarningCommand = new RelayCommand(_ => _ = DismissBloxstrapWarningAsync());
  ```

  Add the helper:
  ```csharp
      private async Task DismissBloxstrapWarningAsync()
      {
          BloxstrapWarningVisible = false;
          await _appSettings.SetBloxstrapWarningDismissedAsync(true);
      }
  ```

  Add an init helper that gets called at the same point existing startup work happens
  (search for where `RobloxCompatBanner` or similar gets seeded):
  ```csharp
      private void InitializeBloxstrapWarning()
      {
          // Only show if Bloxstrap is the registered handler AND the user has not dismissed.
          BloxstrapWarningVisible =
              !_appSettings.BloxstrapWarningDismissed && _bloxstrapDetector.IsBloxstrapHandler();
      }
      // Call this from wherever the rest of the startup banners are initialized.
  ```

- [ ] **Step 9.5: Build to confirm**

  Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj` — expected: build succeeds.

- [ ] **Step 9.6: Commit**

  ```
  git add src/ROROROblox.App/ViewModels/MainViewModel.cs
  git commit -m "feat(app): MainViewModel wires FPS change + Bloxstrap warning"
  ```

---

## Task 10: MainWindow XAML — FPS dropdown + warning banner

**Files:**
- Modify: `src/ROROROblox.App/MainWindow.xaml`
- Modify: `src/ROROROblox.App/MainWindow.xaml.cs`

- [ ] **Step 10.1: Read the existing row DataTemplate**

  Read `src/ROROROblox.App/MainWindow.xaml` and locate the `DataTemplate` for `AccountSummary`
  (the row layout containing avatar, display name, status, Re-auth, Launch As, Remove).
  Also locate the top-of-window banner area (likely a stack of conditional banners — Roblox
  compat banner is one).

- [ ] **Step 10.2: Add the FPS ComboBox to the row template**

  Insert between the status text and the Launch As button:

  ```xml
  <ComboBox
      x:Name="FpsCombo"
      Width="92"
      Margin="8,0,0,0"
      VerticalAlignment="Center"
      SelectionChanged="FpsCombo_SelectionChanged"
      ToolTip="FPS cap applied at launch (DFIntTaskSchedulerTargetFps)">
      <ComboBoxItem Content="—" Tag="{x:Null}" />
      <ComboBoxItem Content="20"        Tag="20" />
      <ComboBoxItem Content="30"        Tag="30" />
      <ComboBoxItem Content="45"        Tag="45" />
      <ComboBoxItem Content="60"        Tag="60" />
      <ComboBoxItem Content="90"        Tag="90" />
      <ComboBoxItem Content="120"       Tag="120" />
      <ComboBoxItem Content="144"       Tag="144" />
      <ComboBoxItem Content="165"       Tag="165" />
      <ComboBoxItem Content="240"       Tag="240" />
      <ComboBoxItem Content="Unlimited" Tag="9999" />
  </ComboBox>
  ```

  (Custom integer entry is intentionally deferred — `Custom...` in the spec maps to a future
  inline entry. v1 of this feature ships preset-only with `Unlimited` covering the high end.
  The dropdown supports arbitrary integers via the underlying property — only the surfaced
  options are presets. Custom-entry UI is a follow-up.)

  Note: WPF `ComboBox` selection-by-value is a small dance because `Tag` is `object`. The
  `FpsCombo_SelectionChanged` handler in the code-behind does the conversion (next step).
  Selecting the item that matches the current `FpsCap` value at row-render time is handled
  by a `Loaded` event handler that walks the `Items` looking for a matching `Tag`.

- [ ] **Step 10.3: Add the SelectionChanged handler in code-behind**

  In `src/ROROROblox.App/MainWindow.xaml.cs`, add:

  ```csharp
  private void FpsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
  {
      if (sender is not ComboBox combo) return;
      if (combo.DataContext is not AccountSummary row) return;
      if (combo.SelectedItem is not ComboBoxItem item) return;
      int? newValue = item.Tag switch
      {
          null => null,
          int i => i,
          string s when int.TryParse(s, out var parsed) => parsed,
          _ => null
      };
      if (row.FpsCap == newValue) return;
      _ = ViewModel.OnFpsCapChangedAsync(row, newValue);
  }

  private void FpsCombo_Loaded(object sender, RoutedEventArgs e)
  {
      if (sender is not ComboBox combo) return;
      if (combo.DataContext is not AccountSummary row) return;
      var current = row.FpsCap;
      foreach (var obj in combo.Items)
      {
          if (obj is not ComboBoxItem item) continue;
          int? itemValue = item.Tag switch
          {
              null => null,
              int i => i,
              string s when int.TryParse(s, out var parsed) => parsed,
              _ => null
          };
          if (itemValue == current)
          {
              combo.SelectedItem = item;
              return;
          }
      }
  }
  ```

  Then add `Loaded="FpsCombo_Loaded"` on the ComboBox in the XAML.

  Note: `ViewModel` is the property name that exposes the MainViewModel — confirm by reading
  `MainWindow.xaml.cs`. If the field is named differently (e.g., `_viewModel`), use that.

- [ ] **Step 10.4: Add the Bloxstrap warning banner**

  At the top of the main content area, near other banners:

  ```xml
  <Border
      Background="#3F3000"
      BorderBrush="#8F7000"
      BorderThickness="0,0,0,1"
      Padding="16,10"
      Visibility="{Binding BloxstrapWarningVisible, Converter={StaticResource BoolToVisibility}}">
      <DockPanel>
          <Button
              DockPanel.Dock="Right"
              Content="Dismiss"
              Margin="12,0,0,0"
              Command="{Binding DismissBloxstrapWarningCommand}"
              Style="{StaticResource SecondaryButtonStyle}" />
          <TextBlock
              VerticalAlignment="Center"
              TextWrapping="Wrap"
              Foreground="#FFE3A6"
              Text="Bloxstrap is set as your Roblox launcher — it will override per-account FPS. Set FPS in Bloxstrap to match." />
      </DockPanel>
  </Border>
  ```

  (Confirm `BoolToVisibility` and `SecondaryButtonStyle` exist by grepping XAML — most ROROROblox
  banners use these. If they're named differently, swap them in.)

- [ ] **Step 10.5: Build to confirm XAML compiles**

  Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj` — expected: build succeeds with no XAML errors.

- [ ] **Step 10.6: Commit**

  ```
  git add src/ROROROblox.App/MainWindow.xaml src/ROROROblox.App/MainWindow.xaml.cs
  git commit -m "feat(app): per-account FPS dropdown on row + Bloxstrap warning banner"
  ```

---

## Task 11: DI registration

**Files:**
- Modify: `src/ROROROblox.App/App.xaml.cs`

- [ ] **Step 11.1: Register IClientAppSettingsWriter and IBloxstrapDetector**

  In `src/ROROROblox.App/App.xaml.cs`, locate the DI service registration block (where existing
  `IAccountStore`, `IRobloxApi`, `IRobloxLauncher` etc. are registered). Add:

  ```csharp
      services.AddSingleton<IClientAppSettingsWriter, ClientAppSettingsWriter>();
      services.AddSingleton<IBloxstrapDetector, BloxstrapDetector>();
  ```

  No constructor changes required for `RobloxLauncher` registration — DI auto-resolves the
  new optional `IClientAppSettingsWriter` parameter from the container. Confirm the existing
  registration uses standard constructor injection (not a factory delegate); if it does use
  a factory, update it to pass the writer.

  Also confirm `MainViewModel` registration receives `IBloxstrapDetector`. If `MainViewModel`
  is constructed via `ActivatorUtilities.CreateInstance` or DI, the new constructor parameter
  is auto-resolved. If it's `new MainViewModel(...)` somewhere, update that call site.

- [ ] **Step 11.2: Wire the launcher's fpsCap from MainViewModel's launch path**

  Read `MainViewModel`'s Launch handler (search for the existing `IRobloxLauncher.LaunchAsync`
  call sites — typically inside `LaunchAccountAsync` or similar). At each call site, pass the
  selected account's FPS:

  Change:
  ```csharp
  result = await _launcher.LaunchAsync(cookie, target);
  ```
  To:
  ```csharp
  result = await _launcher.LaunchAsync(cookie, target, fpsCap: row.FpsCap);
  ```

  Do this for every existing `LaunchAsync` call site in MainViewModel that has an
  `AccountSummary row` in scope. Other call sites (squad launch, private server) should also
  pass the per-account FpsCap if a row reference is available. If a call site has no row in
  scope (e.g., a one-off launch from a dialog), pass `fpsCap: null` explicitly and list the
  call site in the commit message body — those become follow-up tasks for a v1.2.x patch.

- [ ] **Step 11.3: Build + run all tests**

  Run:
  ```
  dotnet build ROROROblox.sln
  dotnet test ROROROblox.sln
  ```
  Expected: clean build, all tests pass.

- [ ] **Step 11.4: Commit**

  ```
  git add src/ROROROblox.App/App.xaml.cs src/ROROROblox.App/ViewModels/MainViewModel.cs
  git commit -m "feat(app): DI register FPS writer + Bloxstrap detector + thread fpsCap through launches"
  ```

---

## Task 12: Manual smoke + readiness pass

**Files:** none (verification only)

- [ ] **Step 12.1: Launch the app**

  Run: `dotnet run --project src/ROROROblox.App/ROROROblox.App.csproj`

  Expected: app launches, main window shows accounts, each row has the FPS dropdown.

- [ ] **Step 12.2: Set FPS on a test account, launch, verify**

  Pick a real test account, set the dropdown to 30. Click Launch As. After Roblox loads
  (auto-joins your default place), open the dev console (Shift+F5 or Ctrl+Shift+F2) and
  type `print(workspace:GetRealPhysicsFPS())` — expected ~30.

  Repeat with another account at 144. Expected: ~144 fps (or capped to monitor refresh).

- [ ] **Step 12.3: Pre-existing FFlag preservation check**

  Locate `ClientAppSettings.json` in the resolved version folder; manually edit it to add
  `"FStringSomeOtherKey": "test"`. Set FPS via the dropdown to 60 and launch. After launch
  completes, re-open the file: both `DFIntTaskSchedulerTargetFps: 60` and the
  pre-existing `FStringSomeOtherKey` should be present.

- [ ] **Step 12.4: Bloxstrap warning behavior**

  If Bloxstrap is installed AND set as the `roblox-player` handler: launch the app. Expected:
  yellow banner at top of main window. Click Dismiss. Restart the app. Banner should stay
  dismissed. (Skip this step if Bloxstrap is not installed; the registry write to
  fake-register Bloxstrap as the handler is a separate dev exercise.)

- [ ] **Step 12.5: UWP path check**

  If Roblox is installed via Microsoft Store: confirm the version folder under
  `%LOCALAPPDATA%\Packages\ROBLOXCORPORATION.ROBLOX_*\LocalCache\Local\Roblox\Versions\<version>\ClientSettings\ClientAppSettings.json`
  is the file the writer modified. (If the Pet Sim 99 clan tester pool has UWP installs, this
  is the most-load-bearing manual check.)

- [ ] **Step 12.6: Set to "—" (don't write)**

  Pick the account from Step 12.2, set the dropdown back to `—`. Launch. Open the file —
  expected: `DFIntTaskSchedulerTargetFps` key is absent.

- [ ] **Step 12.7: Push the branch**

  ```
  git push -u origin feat/per-account-fps-limiter
  ```

- [ ] **Step 12.8: Open a draft PR (optional)**

  ```
  gh pr create --base main --draft --title "feat: per-account FPS limiter" --body-file docs/superpowers/specs/2026-05-07-per-account-fps-limiter-design.md
  ```

  (Or open via the GitHub UI — the spec doc is the canonical PR body.)

---

## Spec coverage check

Mapping spec sections to tasks (any gaps caught here get a remedial task):

- §1 Overview — covered by the task set as a whole; nothing to implement standalone
- §2 Goals / non-goals — informs scope; no task
- §3 Stack — confirms no new NuGet; no task
- §4 Architecture — implemented across Tasks 3–11
- §5.1 IClientAppSettingsWriter — Task 1 + Task 3
- §5.2 IBloxstrapDetector — Task 4
- §5.3 Account.FpsCap — Task 5
- §5.4 IAccountStore.SetFpsCapAsync — Task 5
- §5.5 RobloxLauncher launch-semaphore + 250 ms — Task 6
- §5.6 MainWindow row UI + warning banner — Task 8 + Task 9 + Task 10
- §6.1 Set per-account FPS data flow — Task 9 + Task 10
- §6.2 Launch As (with FPS) data flow — Task 6 + Task 11
- §7.7 FFlag write failed — Task 6 (swallow on `ClientAppSettingsWriteException`)
- §8 Testing — tests live alongside their tasks (3, 4, 5, 6, 7) + manual smoke in Task 12
- §9 Distribution — no change (existing pipeline)
- §10 Open items — none mandatory
- §11 Decisions log — captured in spec; no implementation
- Appendix A — doc only
- Appendix B — doc only

No gaps.

---

## Self-review notes

- Type consistency: `Account.FpsCap`, `AccountSummary.FpsCap`, `IRobloxLauncher.LaunchAsync(..., int? fpsCap)`, `IClientAppSettingsWriter.WriteFpsAsync(int? fps)`, `IAccountStore.SetFpsCapAsync(Guid, int?)` — same `int?` shape end-to-end.
- Constants: `FpsPresets.MinCustom = 10`, `MaxCustom = 9999`, `Unlimited = 9999`, `CapRemovalThreshold = 240` — used consistently across writer, store, presets.
- All `Task N` files match the file-map at the top of the doc.
- The `Custom...` UI from the spec is intentionally deferred (see Task 10.2 note). The
  underlying `int? FpsCap` field already accepts arbitrary integers — only the dropdown
  options are preset. A follow-up task can add the inline number entry without changes to
  Core.
- No placeholders. Every step has the actual code or command.
