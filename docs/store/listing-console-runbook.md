# Store listing updates: the release loop

RoRoRo's Store listing is not edited in Partner Center by hand. Ten listings in seven
languages is seventy fields at the end of a release, most of them in languages nobody here
proofreads by eye, and doing that by hand is how a truncated legal disclaimer sat on the
English listing for months without anyone seeing it.

The tooling lives in a separate private repo, **store-listing-console**. This document covers
only what is specific to RoRoRo. The process itself is documented there and should not be
duplicated here, because two copies of a runbook drift exactly the way two copies of listing
copy do.

## What is different about RoRoRo

Every other app in that console keeps its listing copy inside the console itself. **RoRoRo does
not.** The copy lives in this repo, under `docs/store/`, and the console reads it at a **git
ref** rather than from a working tree.

That was a deliberate fix. The console once held a snapshot of this repo's copy, the two
drifted, and the English description differed by four hundred characters with no way to say
which side was right. A ref has no opinion; a working tree does.

So the release loop starts here:

1. **Edit the sheets in `docs/store/`** — `listing-copy.md` plus the six translations,
   `whats-new-<version>.md`, and `reviewer-letter-<version>.md`.
2. **Commit them.** The console reads a commit, not your working tree, so uncommitted edits are
   invisible to it.
3. **In the console repo**, bump the `ref` and the `version` in the RoRoRo app config to that
   commit and that release.
4. **Then follow the console's own API runbook** from the rebuild step onward.

Point the ref at the commit that actually carries the shipping copy. A release tag is not
automatically the right answer: the v1.27.0.0 tag predated the screenshot captions and
translated keywords that were already live, and reading the tag would have thrown every caption
away.

## Rules that each cost a real submission

**The What's-new block must open with the version on its first line.** The console enforces
this. `v1.28.0.0` or `Version 1.28` both pass; opening with a feature name does not.

**Copyright and trademark is ONE field with a hard 200-character cap.** The `## Copyright`
block alone fits in every language and already carries the full disclaimer. The longer
`## Trademark info` paragraph does **not** belong in that field — gluing the two together is
what silently truncated all ten listings mid-disclaimer. That paragraph belongs in the
description.

**Product names are never written programmatically.** They are set in Partner Center under
Manage app names, by hand. The API's title field is not a reliable view of them in either
direction, and RoRoRo has the most to lose here since every language carries its own reserved
name.

**Trailers and screenshots are not part of this path.** The text tooling never touches media by
design, which is what makes it unable to damage the assets. A created submission copies every
image, package and trailer over untouched. Media changes are a separate job on a submission
created by hand.

**Never open an API-created submission in Partner Center.** Not even to look with intent to
fix. It becomes uncommittable. Choose one path per release, before the first call.

## Where things stand

v1.27.0.0 is published and complete: the trademark disclaimer on all ten listings, seven
keywords in every language, the English listing carrying the localisation release it had
previously missed, and seven trailers — one per language. All ten listings serve their correct
localized name.

## Verifying

The only authority on what a customer actually sees is the live Store page, one URL per
language:

```
https://apps.microsoft.com/detail/9nmjcs390kwb?hl=de-DE&gl=DE
https://apps.microsoft.com/detail/9nmjcs390kwb?hl=ru-RU&gl=RU
```

Neither the submission API nor the reserved-names page reliably predicts it. Both were trusted
over the Store once, agreed with each other, and were wrong together.
