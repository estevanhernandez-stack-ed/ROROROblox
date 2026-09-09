# Dogfood feedback — Vibe-Lingual `wpf-resx` adapter

> From localizing RoRoRo end to end: a real .NET 10 / WPF desktop app, 1,017 keys ×
> 6 languages, shipped to the Microsoft Store as v1.27.0.0 on 2026-09-08.
> Issue-ready. Each finding below is a separate issue if you want them filed that way.

The adapter did the job it was built for. This is what a full production run taught
us that a sample app could not, ordered by what it cost us.

---

## 1. `{x:Static}` output is a dead end for any app with a language picker

**Severity: high — it cost us an entire phase's output.**

The adapter swept 480 XAML strings to `{x:Static loc:Strings.Key}`. That is correct
for a statically-localized app and it built, tested and shipped green.

Then we added a language picker, and every one of those references had to be swept
again. Today's tree, counted precisely — these are **markup sites**, not strings and
not translations, since several sites can point at one key:

```bash
grep -ro "x:Static loc:" --include=*.xaml src/ | wc -l   # 0
grep -ro "loc:Loc "      --include=*.xaml src/ | wc -l   # 586
```

| what | count |
|---|---:|
| `x:Static loc:` markup sites | **0** |
| `{loc:Loc}` markup sites | **586** |
| distinct keys those sites reference | 488 |
| keys in the neutral catalog | 1,017 |
| translated strings shipped (1,017 × 6) | 6,102 |

The pair that matters is the first two: every `x:Static` reference is gone and 586
markup sites now resolve through a change-notifying source. The catalog is larger than
the markup count because roughly half the app's prose is composed in C#, which is
finding #3 below.

`x:Static` resolves **once, at parse time**. There is no change notification behind
it, so a culture switch cannot re-render anything already on screen. The only fix is
a markup extension that resolves through an `INotifyPropertyChanged` source — ours is
`{loc:Loc Key}` over a `TranslationSource` singleton that raises `CultureChanged`.

The trap is that nothing fails. The app builds, the tests pass, the satellites load,
and every string is correct **on the culture the app started in**. The defect only
appears the first time someone changes language without restarting — which is exactly
the feature that makes a picker worth having.

**Ask:** emit the markup-extension form by default, or make it the documented
recommendation with `x:Static` as the opt-in for apps that will never switch at
runtime. The second sweep is not a small migration — it is every localized attribute
in the app, twice.

---

## 2. The accessor problem disappears if #1 is fixed

**Severity: medium — we built a tool, then deleted it.**

`x:Static` needs a **public** strongly-typed accessor class. Visual Studio's
`PublicResXFileCodeGenerator` produces one, but it never runs under `dotnet build`,
which means it never runs in CI. We solved this by hand-owning the accessor:
`scripts/gen-strings-accessor.py` emitted `Properties/Strings.cs` from the resx keys,
proven with a one-string probe before committing to the full sweep. A parity fence
test guarded resx-vs-accessor drift.

That script is now deleted and the fence with it. A markup extension takes a **key
string**, so there is no generated class to keep in sync, no public-accessor
requirement, and no CI-only codegen gap.

Worth saying plainly because the accessor problem reads like the hard part of WPF
localization, and it isn't — it is a symptom of choosing `x:Static`.

---

## 3. The sweep reaches XAML; roughly half the prose was not in XAML

**Severity: medium — scoping, not a bug.**

After the XAML sweep the app was still about half English. A localized smoke build
made that obvious in a way the key count did not. The remainder lived in:

- view-model properties composed at runtime (~430 strings)
- code-behind `$"...{ex.Message}"` patterns at catch sites
- formatters and summary builders
- a core layer that returned finished English sentences to the UI

We fixed the last one architecturally first — the core now returns an enum `Kind`
plus data, and one App-side catalog owns every sentence — and then extracted the rest
by hand across 15 PRs.

Not asking the adapter to solve this. Asking for the docs to **say it**: a XAML sweep
is a fraction of a real app's prose, and the honest next question is "what composes
strings outside XAML?" A `--report-only` mode that greps for interpolated string
literals in `.cs` and prints a count would set expectations correctly in about an
hour of work.

---

## 4. A dictionary initialized at static-init freezes the culture

**Severity: medium — silent, and survives every test that starts in one culture.**

Two places in our app mapped values to display strings in a `Dictionary<TKey, string>`
built at static-init:

```csharp
// freezes whatever culture the type was first touched in
static readonly Dictionary<PluginCapability, string> Catalog = new() {
    [PluginCapability.ReadAccounts] = Strings.Plugin_ReadAccounts,
};
```

Any codemod that rewrites those values to resource lookups produces code that is
correct at first render and wrong forever after a switch. The fix is to store **keys**
and resolve at call time:

```csharp
static readonly Dictionary<PluginCapability, string> ResxKeys = new() {
    [PluginCapability.ReadAccounts] = "Plugin_ReadAccounts",
};
public string Display() => Loc.Get(ResxKeys[capability]);   // resolved when asked
```

**Ask:** detect static/`readonly` collection initializers whose values are resource
references and either refuse them or rewrite to the key-plus-lookup shape. Silently
inlining a resolved string there is worse than not touching it.

---

## 5. Plurals need CLDR category awareness, not an `_other` stub

**Severity: low — but it produced dead strings we nearly "fixed" twice.**

We use `Key_one` / `Key_few` / `Key_many` / `Key_other` suffixes with a CLDR selector.
For **Polish and Russian integers, `_other` is never selected** — the fractional arm
that category exists for cannot occur when the count is an `int`. Every `_other` entry
in those two languages is a dead string.

This cost real time twice: once when a reviewer "corrected" Polish `_other` grammar
that no user can ever see, and once when we nearly propagated that correction
catalog-wide before proving the arm was unreachable.

**Ask:** when the target language is one where integer counts cannot select a
category, either omit the key or emit it with a comment saying it is unreachable for
integer counts. Our own generator now carries
`Required["pl"] = ["one", "few", "many"]`.

---

## What worked, so it doesn't get refactored away

- **Idempotence.** We ran the sweep more than once against a dirty tree and it did the
  right thing every time.
- **The whitelist** for strings that must stay English. Product nouns, glyphs, keyboard
  gestures and URLs all needed to survive, and the escape hatch was there.
- **Small, reviewable diffs.** The sweep produced PRs a human could actually read,
  which is why the `x:Static` problem was caught by design review rather than by a
  user in Warsaw.
