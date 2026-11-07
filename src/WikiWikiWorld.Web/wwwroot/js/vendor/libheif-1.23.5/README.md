# Pinned HEIC decoder

Unmodified `libheif-wasm/libheif-bundle.mjs` and `libheif-wasm/LICENSE` from `libheif-js@1.23.5`, https://www.npmjs.com/package/libheif-js/v/1.23.5 . The ES module bundles its WASM bytes, so it does not fetch another binary.

Package SHA-512 integrity: `sha512-umXZPthnWZtF3iG/5mQG+AZ2V6i0UeYpo3z2CkggTEgL8dKmIIwJT6TDuAMuabXa1yTW8uJ1ZAgR+CxNTklYYw==`.

Exact wrapper source/build scripts: https://github.com/catdad-experiments/libheif-js/tree/d8ec4bc3bc5bf8055975577bccbbc7e1d7313c2d . Its install script consumes the v1.23.5 build at https://github.com/catdad-experiments/libheif-emscripten/releases/tag/v1.23.5 . Compiled library source/builds: https://github.com/catdad-experiments/libheif-emscripten . libheif source: https://github.com/strukturag/libheif . The distributed LGPL/GPL license texts are adjacent; the original package license is in `PACKAGE-LICENSE`. Keep these files and source references with redistributions. The library remains a separate replaceable module, with no application modifications to its source or binary.

Loaded only in the helper worker when native decoding fails for HEIC/HEIF. Updating requires a new versioned directory and testing real HEIC files, dimensions, orientation, failure recovery and cancellation. No runtime CDN access.
