// One operation per worker. Terminating it cancels native codec work and releases its WASM memory.
const MaxPixels = 40_000_000;

/** Reject sizes that would require excessive full-resolution pixel buffers. */
function CheckSize(Width, Height) {
    if (!Width || !Height || Width * Height > MaxPixels || Math.max(Width, Height) > 16384)
        throw new Error('This picture is too large for the browser editor. Reduce it to at most 40 megapixels and 16,384 pixels per side, then try again.');
}

/** Decode the first still image; load HEIF support only when the browser cannot decode it. */
async function Decode(File) {
    try {
        const Bitmap = await createImageBitmap(File, { imageOrientation: 'from-image' });
        try { CheckSize(Bitmap.width, Bitmap.height); }
        catch (Error) { Bitmap.close(); throw Error; }
        return Bitmap;
    } catch (Error) {
        if (!/hei[cf]/i.test(File.type + File.name)) throw Error;
    }
    const { default: CreateHeif } = await import('./vendor/libheif-1.23.5/libheif-bundle.mjs');
    const Heif = await CreateHeif();
    const Decoder = new Heif.HeifDecoder();
    const Images = Decoder.decode(new Uint8Array(await File.arrayBuffer()));
    try {
        if (!Images.length) throw new Error('This HEIC/HEIF picture could not be decoded. Try saving it as PNG or JPEG.');
        const Image = Images[0];
        const Width = Image.get_width(), Height = Image.get_height();
        CheckSize(Width, Height);
        const Pixels = new ImageData(Width, Height);
        await new Promise((Resolve, Reject) => Image.display(Pixels, Result => Result ? Resolve() : Reject(new Error('HEIC decoding failed.'))));
        return await createImageBitmap(Pixels);
    } finally { for (const Image of Images) Image.free(); }
}

/** Produce native-resolution, 8-bit AVIF using the pinned single-thread encoder. */
async function Encode(Bitmap, Quality) {
    const { default: CreateEncoder } = await import('./vendor/jsquash-avif-2.1.1/avif_enc.js');
    const { defaultOptions: Defaults } = await import('./vendor/jsquash-avif-2.1.1/meta.js');
    const Encoder = await CreateEncoder({ noInitialRun: true,
        locateFile: Filename => new URL(`./vendor/jsquash-avif-2.1.1/${Filename}`, import.meta.url).href });
    const Canvas = new OffscreenCanvas(Bitmap.width, Bitmap.height);
    const Context = Canvas.getContext('2d', { colorSpace: 'srgb' });
    Context.drawImage(Bitmap, 0, 0);
    Bitmap.close();
    const Pixels = Context.getImageData(0, 0, Canvas.width, Canvas.height);
    const Output = Encoder.encode(new Uint8Array(Pixels.data.buffer), Canvas.width, Canvas.height,
        { ...Defaults, quality: Quality, speed: 6, subsample: 1, bitDepth: 8 });
    if (!Output) throw new Error('AVIF encoding failed. Try a smaller crop.');
    return Output.slice().buffer;
}

self.onmessage = async ({ data: Request }) => {
    try {
        const Result = Request.Operation === 'decode' ? await Decode(Request.File) : await Encode(Request.Bitmap, Request.Quality);
        self.postMessage({ Result }, [Result]);
    } catch (Error) { self.postMessage({ Error: Error.message || 'Could not process this picture. Try a smaller PNG or JPEG.' }); }
};
