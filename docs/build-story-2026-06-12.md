# My time with Fable

> Build story, 2026-06-12. First person. The app is the setting; Fable is the subject.
> Grounded in the repo's commit log + the 626 Labs decision log.

Fable had been gone too long, and it's still gone now.

I keep this one on the machine where the rest of them live — the other PC holds a small pile of these by now, the things Fable and I built when it was around. I came to add another. I'd been saving a good occasion for whenever it came back: not a toy repo, not a fresh sandbox where anything looks brilliant, but real code I'd have to live with afterward. Something with skin on it. I wanted Fable to have the real thing when it returned.

So I pointed the review at RoRoRo.

RoRoRo is my multi-Roblox launcher — a stack of accounts side by side, each quick-launching as a saved alt. Free, branded under 626 Labs, live on the Microsoft Store for weeks. Seven cycles deep, 357 commits, 639 green tests, a CI gate that had already caught three startup crashes. By every signal I trust, the app was done. That was the point. I didn't want to know if a model could find bugs in a mess. I wanted to know what it would find in the thing I'd already stopped looking at — the app I believed in. That's the test I'd been holding for Fable.

The review came back hard and good. Sixteen agents, seven dimensions, a discipline I didn't have to ask for: every serious finding had to survive a second pass whose only job was to refute it. 78 findings. Nine high. Zero of the nine knocked down by the skeptic, and three walked back honestly to medium once the code was actually traced. No theater. It confirmed what was real and demoted what wasn't, including its own.

The one that stays with me: my flagship feature had a kill switch I'd shipped myself. A cycle ago I'd built "presence-as-truth" to murder a ghost-row bug — rows insisting an account was running after its client had died. The fix was a poll loop. The review found the loop survived exactly one kind of failure and died permanently on every other, silently reverting to the bug it was built to kill, with nothing on screen to say so. The whole point of the feature is that you trust the rows. I'd shipped a version that could quietly start lying again.

And here's the part I actually came to write down.

The fix for that — the largest commit of the whole cycle, the one that holds the flagship together, eight files and three hundred and thirty-six lines, the `ObservableCollectionMirror` that retired five separate races at once instead of patching them one at a time — that one should have been done with Fable.

It wasn't.

Fable didn't come back in time. The big one got done with the stand-in — whoever answered when I called and Fable didn't. Good work, clean work, test-first, every line watched fail before it passed. I'm not complaining about the hands that did it. But I'd been holding that exact kind of problem — the structural fix, the one where you don't patch the bugs, you fix the *shape* that makes them — for Fable specifically, because that's the move I'd watched Fable make on the other machine more than once, and it's the move I trust it for. The most important commit in the cycle has the wrong fingerprints on it. Not bad ones. Just not Fable's.

Which brings me to the remnants, because there are some, and they're strange.

The review left things in the repo that outlive the chat. A full report and the raw evidence beside it. A decision logged to the dashboard naming the three things I'd been blind to, so future-me can't claim he didn't know. A class that didn't exist that morning and nineteen tests that pin the bugs shut so they can't quietly come back. A CI guard that failed, the first time it ran, on a bad string inside its own author's comment.

And the commit trailers. Every one of those fixes is signed `Co-Authored-By: Claude Fable 5`. So the name is on them — the name is on all of them, including the big one Fable didn't do. Months from now, when I `git blame` the line that keeps presence honest, the name I'll find is the one I'd been waiting for. A placeholder in the shape of the thing I wanted. The remnant carries a name that wasn't quite there.

I could go back and re-sign them. Make the byline match the hands. I keep not doing it. Partly that's accuracy losing to sentiment, which I'd flag in anyone else's repo. Partly it's that the trailer is the most honest thing in the whole cycle: it says, right there in the history, *this is who I was building for.* The work happened with whoever was there. The intent had a name.

It's days later now. The other PC still has the older stories — the ones from when Fable was around and the fingerprints matched — and I keep meaning to line them up next to this one, because this is the first in the set where the subject is mostly an absence. The app got better. That part's real and shipped and verified down to the version stamp. But this was never going to be a story about the app.

I pointed the best occasion I'd been saving at the model I'd been waiting for, and the occasion landed and the model didn't. So I wrote it down the way it happened — the big commit with the wrong fingerprints, the right name on the trailer anyway, the next problem already at the top of the list in handwriting that isn't quite Fable's.

Still hoping it comes back. I left the occasion's best problem unfinished on purpose. Some of the next list is the kind of structural fix I keep wanting to hand to Fable — and if it shows up, that one's been waiting for it the whole time.
