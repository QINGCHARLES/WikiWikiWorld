# Pinned AVIF encoder

Unmodified files from `@jsquash/avif@2.1.1`, https://www.npmjs.com/package/@jsquash/avif/v/2.1.1:

- `codec/enc/avif_enc.js` and `codec/enc/avif_enc.wasm` (copied together into this directory)
- `meta.js` and `LICENSE`

Package SHA-512 integrity: `sha512-LMRxd0fMgfCLtobDh0/sFYJMMiRJTNYSEEWvRDKXlAeZ08t3gI5V+1thIT0XjXJ+SVG7Zug9B0XPyx0Ti5VRNA==`.

Upstream source/build instructions: https://github.com/jamsinclair/jSquash/tree/main/packages/avif . Apache-2.0 license is adjacent. The codec derives from Squoosh and uses libavif/libaom. The application calls the single-thread module directly with upstream default options plus quality/speed/subsampling settings; the package's threading-detection wrapper and decoder are not shipped. No modifications to these distributed files.

Only the helper worker imports this codec, on explicit AVIF preparation. Updating requires a new versioned directory and browser export verification, including the WASM MIME type, dimensions, cancellation and file size. No runtime CDN access.
