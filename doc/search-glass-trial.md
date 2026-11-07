# Reversible search bar glass trial

The header search bar uses a liquid glass material when it is unfocused and sits
over an article's header image. Pages without a header image, the focused bar
and readers without JavaScript keep the original surface.

Compare with `?glass=off`. Scale the tint with `?glassTint=0` to `1.5`, where 1
is the material as designed. Use `&` if the URL already has a query string. No
choice is persisted.

## How it works

- `wwwroot/css/core/variables.css` holds the registered `--glass-*` tokens and
  the two light-mode states: clear glass with dark ink and smoked glass with
  light ink.
- `wwwroot/css/layout/header.css` applies them once `data-glass-ink` is present.
- `wwwroot/js/search-glass.js` samples the header image on a 5 x 3 grid, chooses
  the ink, density and saturation, and builds the rim lens. The script is loaded
  from `Pages/Article/View.cshtml` only when the article has a header image.

## Differences from the material specification

- **Contrast floor:** the 22% and 30% density limits left high-contrast images
  at about 3:1 for text. The script raises the tint per image, up to 80%, until
  its model of the sampled points reaches 3.5:1 for an empty bar, which only shows
  its icon, or 5:1 with a query left in it. Both leave a margin because the grid
  can't see every pixel. Two of the five test images exceed the specified 30%: 41%
  to 48% empty and 55% to 60% with a query. At a floor, hover may not change the
  tint.
- **Late switch:** glass needs the decoded image, so the bar starts as the original
  surface and then glides to glass. This was 0.3 to 2 seconds under software
  rendering, where compiling the paper shader likely adds to the slower times. On
  a dark image, the bar visibly changes from a light pill to a dark one.
- **Lens on resize:** the map is redrawn when the bar's size changes rather than
  moved. The bar's width and height both vary with the viewport.
- **Blend modes:** `multiply` is off because it changed no pixels when measured.
  `plus-lighter` stays on for light ink.
- **Unreadable images:** the original bar stays, rather than a dense tint chosen
  from the theme, because ink from the theme could be unreadable over a dark
  image.
- **Filter thinning:** below a tint scale of 1, saturation thins with brightness.
- **Not built:** dark-mode glass, because the site's dark theme is disabled;
  saved tint settings; remembering the last ink between pages.

## Verification

Measured in Chrome through Playwright on 2026-11-04, using software rendering.
Contrast comes from screenshot pixels with the ink hidden.

- On five real header images, with the paper texture on and off, and on white,
  black, red, mid-grey, half-black and checkerboard test images, the worst pixel
  was at least 3.7:1 with an empty bar and 5.1:1 with a query left in it.
- The lens bends only the rim, with matching top and bottom bands, at 1x, 1.25x
  and 2x pixel density.
- Ink flips only outside the ±0.05 band around 0.5 luminance.
- The bar's size and position match `?glass=off` at 375, 768 and 1440 px wide, and
  when signed in at 375 and 1440 px wide.
- A no-header page and the focused bar keep their original computed styles.
- Reduced transparency and forced colours give a solid surface. Reduced motion
  removes the transitions. Search submits without JavaScript.

Not verified: Firefox and Safari, which should get the glass without the lens;
scrolling performance on real GPUs and phones.

## Removal

Remove the `search-glass.js` script tag from `Pages/Article/View.cshtml`. The bar
returns to its original surface. To remove the trial entirely, also delete the
script, the glass block in `header.css`, the `--glass-*` tokens in
`variables.css`, this document and its line in `PROJECT.md`. No data is involved.
