# Publish ROROROblox.PluginContract to NuGet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish `ROROROblox.PluginContract` v0.1.0 to nuget.org so plugin authors can `dotnet add package ROROROblox.PluginContract` exactly as `docs/plugins/AUTHOR_GUIDE.md` already tells them to — and switch the first-party RoRoRo Ur Task plugin off its on-disk `ProjectReference` onto the published package.

**Architecture:** The contract project (`src/ROROROblox.PluginContract`, netstandard2.1) already has `PackageId`, `Version`, `Authors`, `Description`, and `PackageReadme.md`. It needs the rest of the publish-quality metadata (license, repo URL, tags), then `dotnet pack` + `dotnet nuget push`. Once the package is live and indexed, the Ur Task repo swaps its `ProjectReference` for a `PackageReference` and drops the sibling-repo checkout from its release CI.

**Tech Stack:** .NET 10 SDK, NuGet, GitHub Actions (Ur Task CI).

**Repos touched:** `ROROROblox` (`C:\Users\estev\Projects\ROROROblox`) and `rororo-ur-task` (`C:\Users\estev\Projects\rororo-ur-task`). Use absolute `git -C` paths — do not rely on `cd` state across steps.

**Branch base:**
- ROROROblox: branch off `main` (`git -C C:\Users\estev\Projects\ROROROblox fetch && git -C ... checkout main && git -C ... pull`), suggested branch `chore/publish-plugincontract-nuget`.
- rororo-ur-task: branch off its default branch (`main`), suggested branch `chore/consume-plugincontract-nuget`.

---

## Prerequisites — resolve BEFORE starting

These are human decisions/inputs the plan cannot fill in. Confirm both, then begin Task 1.

1. **License.** The ROROROblox repo has **no `LICENSE` file**. A NuGet package consumed by third-party plugin authors should declare one. Decide the SPDX license (e.g. `MIT`) and **add a `LICENSE` file to the repo root** as part of Task 1. This plan assumes **`MIT`** — change `<PackageLicenseExpression>` in Task 1 Step 1 if the decision differs. Do not publish a package with no license.
2. **nuget.org API key.** Task 3 (`dotnet nuget push`) requires an nuget.org API key with push scope for `ROROROblox.PluginContract` (or glob). This is Este's to generate at nuget.org → API Keys. The plan references it as `<NUGET_ORG_API_KEY>`.

---

## File Structure

| File | Repo | Change |
|---|---|---|
| `LICENSE` | ROROROblox | Create — SPDX license text (prereq #1) |
| `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj` | ROROROblox | Add publish metadata |
| `rororo-ur-task.csproj` | rororo-ur-task | Swap `ProjectReference` → `PackageReference` |
| `.github/workflows/release.yml` | rororo-ur-task | Drop the ROROROblox sibling-repo checkout step |

---

### Task 1: Add publish metadata to the contract project

**Files:**
- Create: `C:\Users\estev\Projects\ROROROblox\LICENSE`
- Modify: `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj`

- [ ] **Step 1: Create the `LICENSE` file**

Create `C:\Users\estev\Projects\ROROROblox\LICENSE` with the full text of the chosen license (prereq #1). For MIT, use the standard MIT template with `Copyright (c) 2026 626 Labs LLC`.

- [ ] **Step 2: Add the publish metadata to the csproj**

In `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj`, replace the existing `<PropertyGroup>` (currently lines 3-14):

```xml
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PackageId>ROROROblox.PluginContract</PackageId>
    <Version>0.1.0</Version>
    <Authors>626 Labs LLC</Authors>
    <Description>gRPC contract for RoRoRo plugins. Reference this NuGet to author a plugin.</Description>
    <PackageReadmeFile>PackageReadme.md</PackageReadmeFile>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
```

with:

```xml
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PackageId>ROROROblox.PluginContract</PackageId>
    <Version>0.1.0</Version>
    <Authors>626 Labs LLC</Authors>
    <Company>626 Labs LLC</Company>
    <Copyright>Copyright (c) 2026 626 Labs LLC</Copyright>
    <Description>gRPC contract for RoRoRo plugins. Reference this NuGet to author a plugin.</Description>
    <PackageReadmeFile>PackageReadme.md</PackageReadmeFile>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/estevanhernandez-stack-ed/ROROROblox</PackageProjectUrl>
    <RepositoryUrl>https://github.com/estevanhernandez-stack-ed/ROROROblox</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageTags>roblox;rororo;plugin;grpc;626labs</PackageTags>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>
```

(Note: `<PackageLicenseExpression>MIT</PackageLicenseExpression>` reads the license from the SPDX identifier, not the `LICENSE` file — but shipping the `LICENSE` file in the repo is still correct hygiene and what consumers expect. If the chosen license is non-SPDX or custom, switch to `<PackageLicenseFile>LICENSE</PackageLicenseFile>` plus a `<None Include="..\..\LICENSE" Pack="true" PackagePath="\" />` item instead.)

- [ ] **Step 3: Verify the project still builds**

Run: `dotnet build src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj -c Release`
Expected: BUILD SUCCEEDED. (Grpc/Protobuf versions are unchanged — see "Optional follow-up" at the bottom of this plan.)

- [ ] **Step 4: Commit**

```bash
git -C C:\Users\estev\Projects\ROROROblox add LICENSE src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj
git -C C:\Users\estev\Projects\ROROROblox commit -m "chore(plugincontract): add license + NuGet publish metadata"
```

---

### Task 2: Pack and inspect the package locally

**Files:** none (produces `artifacts/nuget/*.nupkg`, which is gitignored — `artifacts/` should already be covered, confirm in Step 3).

- [ ] **Step 1: Pack**

Run: `dotnet pack src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj -c Release -o artifacts/nuget`
Expected: produces `artifacts/nuget/ROROROblox.PluginContract.0.1.0.nupkg` and `artifacts/nuget/ROROROblox.PluginContract.0.1.0.snupkg`.

- [ ] **Step 2: Inspect the `.nupkg` contents**

Run (PowerShell):
```powershell
$pkg = "artifacts/nuget/ROROROblox.PluginContract.0.1.0.nupkg"
Copy-Item $pkg "$pkg.zip"
Expand-Archive "$pkg.zip" -DestinationPath artifacts/nuget/_inspect -Force
Get-ChildItem -Recurse artifacts/nuget/_inspect | Select-Object FullName
Remove-Item "$pkg.zip"; Remove-Item -Recurse artifacts/nuget/_inspect
```
Expected: the package contains `lib/netstandard2.1/ROROROblox.PluginContract.dll`, `ROROROblox.PluginContract.nuspec`, and `PackageReadme.md`. The `.nuspec` should show the `MIT` license, the repo URL, the tags, and a dependency group for netstandard2.1 listing `Google.Protobuf` and `Grpc.Net.Client` (Grpc.Tools is `PrivateAssets=all` and correctly absent).

- [ ] **Step 3: Confirm `artifacts/` is gitignored**

Run: `git -C C:\Users\estev\Projects\ROROROblox check-ignore artifacts/nuget/ROROROblox.PluginContract.0.1.0.nupkg`
Expected: prints the path (meaning it's ignored). If it prints nothing, add `artifacts/` to `.gitignore` and commit that change before continuing.

---

### Task 3: Publish to nuget.org

**Files:** none. **Requires the nuget.org API key (prereq #2).** This step is irreversible — a published version number can be unlisted but not re-pushed.

- [ ] **Step 1: Push the package**

Run (substitute the real key):
```bash
dotnet nuget push artifacts/nuget/ROROROblox.PluginContract.0.1.0.nupkg --api-key <NUGET_ORG_API_KEY> --source https://api.nuget.org/v3/index.json
```
Expected: `Your package was pushed.` The `.snupkg` symbol package is picked up automatically alongside it.

- [ ] **Step 2: Wait for indexing + verify it's live**

nuget.org validation + indexing takes a few minutes. Poll:
```bash
dotnet package search ROROROblox.PluginContract --source https://api.nuget.org/v3/index.json --exact-match
```
Expected: returns `ROROROblox.PluginContract` `0.1.0` once indexed. Do not start Task 4 until this returns the package — `dotnet restore` against it will fail otherwise.

---

### Task 4: Switch Ur Task to the published package

**Files:**
- Modify: `C:\Users\estev\Projects\rororo-ur-task\rororo-ur-task.csproj`
- Modify: `C:\Users\estev\Projects\rororo-ur-task\.github\workflows\release.yml`

- [ ] **Step 1: Swap the csproj reference**

In `C:\Users\estev\Projects\rororo-ur-task\rororo-ur-task.csproj`, replace the final `<ItemGroup>` (currently lines 66-71):

```xml
  <ItemGroup>
    <!-- ProjectReference until the PluginContract NuGet publishes to nuget.org.
         Matches the rororo-hello-plugin stub pattern. Requires sibling ROROROblox
         repo on disk for build (CI documents this in the workflow). -->
    <ProjectReference Include="..\ROROROblox\src\ROROROblox.PluginContract\ROROROblox.PluginContract.csproj" />
  </ItemGroup>
```

with:

```xml
  <ItemGroup>
    <PackageReference Include="ROROROblox.PluginContract" Version="0.1.0" />
  </ItemGroup>
```

- [ ] **Step 2: Drop the sibling-repo checkout from release CI**

Open `C:\Users\estev\Projects\rororo-ur-task\.github\workflows\release.yml`. It currently checks out **two** repos — `rororo-ur-task` and the `ROROROblox` sibling (the sibling checkout exists only to satisfy the old `ProjectReference`, and pins `ref: feat/plugin-system`). Remove the step (and any associated `path:` / `ref:` config) that checks out `ROROROblox`. Keep the `rororo-ur-task` checkout, the .NET setup, the `build-plugin.ps1` run, and the release-artifact upload. After the edit, `build-plugin.ps1` resolves `ROROROblox.PluginContract` purely from nuget.org via `dotnet restore`.

If the workflow path differs, run `git -C C:\Users\estev\Projects\rororo-ur-task ls-files .github` to locate it.

- [ ] **Step 3: Restore + build Ur Task against the published package**

Run:
```bash
dotnet restore C:\Users\estev\Projects\rororo-ur-task\rororo-ur-task.csproj --no-cache
dotnet build C:\Users\estev\Projects\rororo-ur-task\rororo-ur-task.csproj -c Release
```
Expected: `dotnet restore` pulls `ROROROblox.PluginContract 0.1.0` from nuget.org; BUILD SUCCEEDED. `--no-cache` forces a fresh pull so this proves the published package — not a stale local ProjectReference artifact — actually works.

- [ ] **Step 4: Run the Ur Task test suite**

Run: `dotnet test C:\Users\estev\Projects\rororo-ur-task\tests\rororo-ur-task.Tests\rororo-ur-task.Tests.csproj`
Expected: PASS — the integration tests still compile and pass against the package-sourced contract types.

- [ ] **Step 5: Commit (rororo-ur-task)**

```bash
git -C C:\Users\estev\Projects\rororo-ur-task add rororo-ur-task.csproj .github/workflows/release.yml
git -C C:\Users\estev\Projects\rororo-ur-task commit -m "chore: consume ROROROblox.PluginContract from nuget.org"
```

---

### Task 5: Final verification + push

- [ ] **Step 1: Confirm the AUTHOR_GUIDE is now accurate**

`docs/plugins/AUTHOR_GUIDE.md` already instructs `<PackageReference Include="ROROROblox.PluginContract" Version="0.1.0" />` — that instruction is now true with no doc change required. Read the "Project setup" section once to confirm the version string matches the published `0.1.0`. No edit expected; if the versions ever diverge, that's the line to fix.

- [ ] **Step 2: Local-path audit (both repos)**

Run:
```bash
git -C C:\Users\estev\Projects\ROROROblox grep -nI "C:\\\\Users" -- src/ROROROblox.PluginContract
git -C C:\Users\estev\Projects\rororo-ur-task grep -nI "C:\\\\Users" -- rororo-ur-task.csproj .github
```
Expected: no output from either — no machine-specific paths in committable code.

- [ ] **Step 3: Push both branches**

```bash
git -C C:\Users\estev\Projects\ROROROblox push -u origin chore/publish-plugincontract-nuget
git -C C:\Users\estev\Projects\rororo-ur-task push -u origin chore/consume-plugincontract-nuget
```

- [ ] **Step 4: Tag a fresh Ur Task release (optional, recommended)**

The previous Ur Task release (v0.2.0) was built against the on-disk `ProjectReference`. Once the CI change in Task 4 Step 2 is on the default branch, cut a `v0.2.1` tag so the next release is produced by the NuGet-only pipeline — proving the sibling-checkout removal end-to-end. This is optional and can be deferred.

---

## Optional follow-up — Grpc/Protobuf version alignment

The contract pins `Google.Protobuf 3.28.3` / `Grpc.Net.Client 2.68.0` / `Grpc.Tools 2.68.0`. Ur Task and the AUTHOR_GUIDE use `Grpc.Net.Client 2.70.0`. NuGet unifies the consumer's direct `2.70.0` over the contract's transitive `2.68.0`, so there is **no conflict** and this is not required for the publish. If you want them aligned anyway, bump all three contract packages to the 2.70.0 line in a separate commit and re-run Task 1 Step 3's build to verify codegen still succeeds — but that would warrant a `0.1.1` package version, so do it as its own cycle, not bundled into this publish.

---

## Self-Review

**Spec coverage:**
- "Publish PluginContract to nuget.org" → Tasks 1-3.
- "So authors can `dotnet add package` as the guide says" → Task 5 Step 1 (guide already correct).
- "Switch Ur Task off the ProjectReference" → Task 4 Steps 1, 3, 4.
- "Retire the sibling-checkout dance" → Task 4 Step 2.

**Placeholder scan:** `<NUGET_ORG_API_KEY>` is a real human-supplied credential (prereq #2), not a TODO. The `release.yml` edit (Task 4 Step 2) is a read-then-delete with precise criteria because the workflow file's exact contents weren't in scope at plan-writing time — it names exactly which step to remove and which to keep. No implementation-code placeholders.

**Type consistency:** `PackageId` `ROROROblox.PluginContract` and `Version` `0.1.0` are consistent across the csproj (Task 1), the pack output filename (Task 2), the push command (Task 3), the `PackageReference` (Task 4 Step 1), and the AUTHOR_GUIDE check (Task 5 Step 1).
