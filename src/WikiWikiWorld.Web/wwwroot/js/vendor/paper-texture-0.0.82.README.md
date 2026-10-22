# Paper Texture trial dependency

Source: https://github.com/paper-design/shaders and the published
`@paper-design/shaders` npm package, pinned to **0.0.82**.
Apache-2.0; adjacent LICENSE and NOTICE are copied from that package.

Only ShaderMount, paperTextureFragmentShader and getShaderNoiseTexture are bundled.
The committed browser module is self-hosted; there is no runtime CDN, React dependency,
or new application build step. Uncompressed size is about 55 KiB.

To regenerate, extract the pinned npm tarball into `package/`, create `entry.js`:

```js
export { ShaderMount } from './package/dist/shader-mount.js';
export { paperTextureFragmentShader } from './package/dist/shaders/paper-texture.js';
export { getShaderNoiseTexture } from './package/dist/get-shader-noise-texture.js';
```

Run `npx esbuild@0.25.12 entry.js --bundle --minify --format=esm --outfile=paper-texture-0.0.82.js`.
Copy LICENSE and NOTICE from the package alongside it. Shader logic is unmodified.

The header integration specializes `u_folds`, `u_drops` and `u_roughnessRows`
to zero-valued GLSL constants before compilation, without editing this bundle.
When updating the pinned package, verify these declarations still match and
compare output with the approved paper-only preset. See `doc/header-paper-texture.md`.
