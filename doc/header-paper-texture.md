# Reversible header paper texture

Approved paper-only preset (2026-10-21): **folds 0.0**, **wrinkles 0.3**,
**crumples 0.3**, **drops 0.0**. Other material settings match
https://shaders.paper.design/paper-texture, including roughness rows 0,
roughness/fiber 0.4 and distortion 0.75. Preserve this approved appearance.
Sizing uses cover, scale 1 and top alignment instead of the demo's contain/0.9
 to preserve the site's header framing.

The CMYK experiment was rejected and removed, including its module, second
canvas, PNG capture, resize debounce and preserved drawing buffer.
Old `headerHalftone` query parameters are ignored.

Compare `?headerTexture=on` (paper) and `?headerTexture=off` (original);
use `&` if the URL already has a query string. No choice is persisted.
Set `HeaderPaperTextureEnabled` to `false` in `wwwroot/js/header-paper-texture.js`
to disable the enhancement globally.

## Optimization and verification

Before compilation, the integration replaces the pinned shader's `u_folds`,
`u_drops` and `u_roughnessRows` uniform declarations with zero-valued GLSL
constants. This permits dead-code elimination of those features and their
 dependent calculations. Unused fold size/offset inputs are no longer supplied.
The upstream vendor module is unmodified. To re-enable these features, remove
their constant specialization and restore the relevant uniform inputs.

The original shader already guards the main fold/drop calculations at zero;
this specialization removes their runtime controls and lets the compiler also
eliminate related arithmetic. No GPU timing improvement is claimed.
Chromium verification: the optimized 1600 x 600 render matched the previous
paper-only output byte-for-byte; the three disabled controls and four fold
size/offset uniforms were inactive after linking.

One static canvas renders at speed 0, with a 2,073,600-pixel cap and no retained
drawing buffer or recurring render loop. The vendor runtime handles resizing
and visibility. The canvas shares existing fade/parallax CSS and reduced-motion
handling. The pinned self-hosted shader loads only for enabled headers.

Original images and database records are untouched. The image remains beneath
a decorative, pointer-transparent canvas. Missing JavaScript, unavailable WebGL,
failed loading and context loss leave the original visible.

To remove the enhancement entirely, remove the header-paper-texture script
from Article/View.cshtml, the `.hero-paper-texture` CSS rules, the module,
its four vendor files and this document. No data rollback is needed.
