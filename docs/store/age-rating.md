# Age-rating questionnaire — RORORO

> Microsoft Store age rating uses the IARC questionnaire. Per Sanduhr playbook 10.1.4.4: answers must be CONSISTENT with the privacy policy and listing description — inconsistency is its own rejection vector.

## RORORO is a launcher, not a game

The most important framing point: RORORO is a **utility/launcher** that opens the official Roblox client. It does not contain any game content of its own. Its rating reflects what's *in this app*, not what Roblox itself contains. The Roblox application has its own IARC rating (10+ in most regions); we route traffic to it but don't control its content.

Phrase any "does this app contain..." answer as: **does this app, on its own, in its own UI, contain X**.

## Answers

| Question (paraphrased) | Answer | Notes |
|---|---|---|
| Contains violent content | **No** | The app is a launcher window with text + buttons. No game content. |
| Contains sexual content / nudity | **No** | |
| Contains profanity / crude humour | **No** | |
| Contains depictions of drug, alcohol, or tobacco use | **No** | |
| Contains gambling / simulated gambling | **No** | |
| Contains user-generated content viewable in-app | **No** | All UGC lives in the Roblox client we launch — not in our UI. |
| Contains chat / messaging features | **No** | We don't have any chat. |
| Allows users to interact with strangers | **No** | We don't have multiplayer or networking features ourselves. |
| Collects personal information | **No** | DPAPI-encrypted local storage of Roblox session cookies; nothing transmitted off-device except to roblox.com. |
| Shares personal information with third parties | **No** | No telemetry, no analytics, no third-party SDKs. |
| Uses the camera, microphone, or location | **No** | None of those. |
| Has in-app purchases | **No** | Free; no IAP infrastructure. |
| Includes ads | **No** | No ads. |
| Network connectivity required | **Yes — for Roblox-side calls only** | We hit `auth.roblox.com` for the auth-ticket flow + GitHub Releases for Velopack updates. Document this in the questionnaire's free-text field. |

## Free-text disclosures (where the questionnaire allows)

- **Network usage:** "RORORO makes HTTPS calls to `auth.roblox.com` (documented authentication-ticket endpoint, called only during a Launch As action), `users.roblox.com` (avatar metadata), `thumbnails.roblox.com` (avatar imagery), and the GitHub Releases API for auto-update checks. No analytics, no telemetry, no third-party tracking."
- **Local storage:** "Roblox session cookies are stored in `accounts.dat` in the app's local data folder, encrypted with the Windows Data Protection API. No plaintext secrets are ever written to disk."
- **Trademark notice:** Drop the standard nominative-use disclaimer in any "anything else we should know" field.

## Expected outcome

Likely **rated for 10+** (mirroring Roblox itself) **or higher**, even though our app on its own contains nothing rateable, because reviewers may infer that an app-launching-Roblox effectively co-distributes with whatever Roblox contains. We accept the rating; don't argue it.

If we receive a rating LOWER than 10+, treat that as a flag — reviewers may have missed the launcher relationship, which could surface later as a 10.1.4.4 question. Re-read the listing copy to ensure the Roblox connection is explicit but properly disclaimed.
