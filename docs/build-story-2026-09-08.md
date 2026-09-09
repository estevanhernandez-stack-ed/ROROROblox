# The words were the easy part

> Build story, 2026-09-08. First person. The app is the setting; the mission is what
> it takes to make software speak. Grounded in the repo's commit log, the 626 Labs
> decision log, and a Partner Center CSV.

I pulled the acquisitions report because I wanted to know who was actually using this
thing.

RoRoRo is my multi-Roblox launcher. It started as a tool for one Pet Sim clan — my
clan — and I have never really stopped thinking of it that way. Then the CSV came
back: 577 installs at the time, and **43% of them from markets that don't speak
English**, against a listing written entirely in English. France. Russia. Germany.
Brazil, which is a top-three Roblox market and was sending numbers so modest they
read as a fence rather than a preference. Poland. A dozen Spanish-speaking countries
adding up.

It's at 1.54k downloads now. The clan is a rounding error in its own tool.

So the plan was obvious and I was wrong about it in the way you're wrong about things
that seem obvious: **translate the strings.**

---

Here's what I actually found.

You cannot translate what you cannot reach, and most of my app's prose was somewhere a
translator could never get to.

The core library — the part that does the work, the part with no UI in it — was
returning **finished English sentences** to the interface. `Failed to obtain auth
ticket: …`. Every one of those was a wall. Worse, I found the UI branching on message
*text*: a `Contains("Roblox does not appear to be installed")` picking which badge to
show. Which means a copy edit could silently break control flow. That had been sitting
there for months, in code I'd have told you I knew well.

That got fixed first, and not by translating anything. The core now returns an enum
and data — a `Kind`, a version number, a count — and exactly one file on the app side
turns those into sentences. Then a test that fails the build if a `string Message`
member ever reappears in the core, because the boundary is only real if something
holds it.

Then the UI. 480 strings in the markup, another ~430 composed at runtime in
view-models, settings summaries, catch blocks, formatters. That second number is the
one I'd have guessed wrong. After the markup sweep was green and shipped I ran a
localized build and the app was still **about half English**, because half the words a
person reads were never in the markup at all.

---

The part that cost me the most was a decision that tested green.

The markup sweep produced `{x:Static loc:Strings.Key}` references — the standard WPF
move, a strongly-typed accessor, resolved at parse time. It built. All 2,040 tests
passed. It shipped. And to make it work in CI at all I'd had to hand-build a code
generator, because the Visual Studio tool that produces that accessor never runs under
`dotnet build`. I was fairly pleased with that. It was the hard part, solved.

Then I added a language picker.

`x:Static` resolves **once**. There's no change notification behind it. Every string
on screen is correct for the language the app *started* in and cannot be told
otherwise. Nothing fails — not a test, not a build, not a startup. The defect only
exists the moment a person changes language without restarting, which is the entire
reason to have a picker.

Today's tree: **zero** `x:Static` references, **586** of the markup-extension form
that replaced them. The code generator I'd been pleased with is deleted. A whole
phase's output, rewritten — because "it passes and ships" and "it is right" are
different claims and I had checked the first one.

---

After that, translating was one afternoon.

Twelve agents, one translator and one adversarial reviewer per language, 532 new keys
× 6. The reviewers did real work rather than nodding: distinct Russian and Polish
genitive plural forms instead of English-shaped stubs, a French phrasing that stays
correct whether the noun in the placeholder is masculine or feminine, a Brazilian
decimal comma to match the numbers the formatter actually renders. Then the
deterministic pipeline — lint the catalogs, refuse to generate if one is incomplete,
regenerate the satellites.

**1,017 keys × 6 languages. 6,102 strings.** Every satellite complete, or the
generator won't run.

And the moment it became real for me wasn't a test passing. It's that the app follows
the operating system. A kid in Warsaw who updates to 1.27 doesn't find a setting or
read an announcement. They open it and it's in Polish. Nobody asked them anything.

---

I almost blew the last mile anyway, and I want it written down.

Store listings are per-language, and I'd translated the descriptions, the features,
the what's-new. Then, building a paste tool to survive ten listings × sixteen fields
in languages I can't proofread, I noticed the **keywords** field was falling through
to English on all six.

Keywords are the search field. English there doesn't read oddly. It makes you
**invisible** — a Polish kid types *wiele instancji* and my perfectly translated
listing is not in the results. Hours before submission, and it was the tool that
caught it, not me. That's the second release running where a three-surface audit
caught something the copy review didn't.

The right keywords weren't translations either. *launcher* stays English in every one
of them, because that's the word Spanish and Brazilian and Polish Roblox communities
actually type. Russian gets Cyrillic *роблокс* because that's how the market spells it
into a box. Translating the words would have been the wrong job.

---

It kept expanding outward the whole time, and that's the part I didn't plan for.

The app, then the listing. Then the screenshot captions — and one of those said *"its
own **Stop** button"* while the Polish app now says *Zatrzymaj*, so the caption had to
stop quoting the interface and start describing it. Then the launch trailer, which
needed its on-screen cards translated with the slot widths measured, because German
runs long and a fixed box clips. Then the narration, which is a different problem
again: a card is constrained by **width**, a voiceover by **duration**, and Spanish
needs about a third more syllables to say the same sentence. Twenty of forty-two lines
came back over budget on the first pass.

Six audio tracks are about to go through the multilingual model in my own voice.

I set out to translate an app. What I actually did was find every place the product
speaks — screens, error messages, store copy, search terms, a picture's caption, a
voice — and discover they each break differently, and that the words were never the
hard part in any of them.

v1.27.0.0 went to Partner Center today.
