// Reversible visual trial: false restores all original headers without changing any images.
const HeaderPaperTextureEnabled = true;
const TextureChoice = new URLSearchParams(location.search).get('headerTexture');

if (TextureChoice === 'on' || (HeaderPaperTextureEnabled && TextureChoice !== 'off')) {
    const HeaderImage = document.querySelector('img.hero-image');
    if (HeaderImage) ApplyPaperTexture(HeaderImage);
}

/** Enhances a loaded header while retaining its original image as the no-JS/error fallback. */
async function ApplyPaperTexture(HeaderImage) {
    let Surface;
    let Mount;
    try {
        await HeaderImage.decode();
        const { ShaderMount, paperTextureFragmentShader, getShaderNoiseTexture } =
            await import('./vendor/paper-texture-0.0.82.js');
        // Specialize only disabled material features, leaving the upstream module intact.
        // GLSL constants let the compiler discard folds, drops and roughness-row calculations.
        // Removing JS uniforms alone would leave them as runtime shader inputs.
        const PaperShader = paperTextureFragmentShader.replace(
            /uniform float (u_folds|u_drops|u_roughnessRows);/g,
            'const float $1 = 0.0;');
        const Noise = getShaderNoiseTexture();
        await Noise.decode();

        Surface = document.createElement('div');
        Surface.className = 'hero-image hero-paper-texture';
        Surface.setAttribute('aria-hidden', 'true');
        HeaderImage.after(Surface);

        // Paper's demo material settings, with the requested folds, wrinkles, crumples and drops.
        // Cover, unit scale and a vertical origin matching the image's data-focus (top unless set)
        // reproduce the CSS object-fit/object-position framing underneath.
        const OriginY = { center: 0.5, bottom: 1 }[HeaderImage.dataset.focus] ?? 0;
        Mount = new ShaderMount(Surface, PaperShader, {
            u_image: HeaderImage, u_isImage: true, u_noiseTexture: Noise,
            u_colorBack: [211 / 255, 210 / 255, 171 / 255, 1],
            u_colorPaper: [1, 1, 1, 1], u_colorShadow: [0.8, 0.8, 0.8, 1],
            u_blending: 1, u_distortion: 0.75, u_clip: false,
            u_angle: 300, u_seed: 4,
            u_roughness: 0.4, u_roughnessSize: 0.5,
            u_fiber: 0.4, u_fiberSize: 0.5,
            u_wrinkles: 0.3, u_wrinkleSize: 0.65,
            u_crumples: 0.3, u_crumpleCount: 6,
            u_fit: 2, u_scale: 1, u_rotation: 0,
            u_offsetX: 0, u_offsetY: 0, u_originX: 0.5, u_originY: OriginY,
            u_worldWidth: 0, u_worldHeight: 0
        }, undefined, 0, 0, 1, 1920 * 1080);

        // Context loss must immediately uncover the original image.
        Mount.canvasElement.addEventListener('webglcontextlost', () => {
            Mount.dispose();
            Surface.remove();
        }, { once: true });
    } catch (Error) {
        Mount?.dispose();
        Surface?.remove();
        console.warn('Header paper texture unavailable; using the original image.', Error);
    }
}
