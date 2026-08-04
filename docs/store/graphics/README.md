# Store graphics

Partner Center listing artwork. Regenerated 2026-08-04 to correct the wordmark casing —
the previous set read **RORORO**; the product is **RoRoRo**.

## Regenerating

`store-mark.source.html` is the single source for all six wordmark graphics. It picks its layout
from the viewport's aspect ratio, so one file renders every size and the exports cannot drift
apart:

| Aspect | Layout | Used for |
|---|---|---|
| wider than 1.4 | mark left, text right | hero |
| 0.85 – 1.4 | stacked, centred | boxart |
| narrower than 0.85 | stacked, centred, larger mark | poster |

Serve the file over HTTP (a `file://` URL is blocked by the capture tooling), then screenshot at
each viewport. The layout re-evaluates on resize, so set the viewport **after** load:

```
python3 -m http.server 8731        # from this directory
```

| Output | Viewport |
|---|---|
| `store-hero-1920x1080.png` | 1920 × 1080 |
| `store-hero-3840x2160.png` | 3840 × 2160 |
| `store-boxart-1080x1080.png` | 1080 × 1080 |
| `store-boxart-2160x2160.png` | 2160 × 2160 |
| `store-poster-720x1080.png` | 720 × 1080 |
| `store-poster-1440x2160.png` | 1440 × 2160 |

Fonts load from Google Fonts, so the render needs network access. Space Grotesk 700 is the
wordmark; a fallback substitution will change the letterforms without failing, so check one
export by eye before shipping a set.

## Not generated from this file

`store-display-71x71.png`, `-150x150.png`, and `-300x300.png` are the mark alone with no
wordmark, so the casing fix did not touch them. They are unchanged from the original set.

The in-package tile logos under `src/ROROROblox.App/Package/Logos/` are also separate — those are
the MSIX assets, and `ShowNameOnTiles` draws the manifest `DisplayName` over them as live text
rather than baking it into the image.
