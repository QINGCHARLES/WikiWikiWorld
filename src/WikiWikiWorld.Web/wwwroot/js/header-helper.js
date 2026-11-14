const Root = document.getElementById('HeaderHelper');
const Get = Id => document.getElementById(Id);
const MaxInputBytes = 50 * 1024 * 1024;
const UploadLimitBytes = 10 * 1024 * 1024;
let Source = null, Crop = null, Aspect = null, Drag = null, DownloadUrl = null, DownloadSizeBytes = 0;
let Worker = null, Abort = null, RejectOperation = null, Busy = false, Cancelled = false;
const SourceCanvas = Get('SourceCanvas');
const CropBox = Get('CropBox');
const Coordinates = ['X', 'Y', 'Width', 'Height'];
const Supported = Boolean(globalThis.Worker && globalThis.OffscreenCanvas && globalThis.createImageBitmap && globalThis.WebAssembly);
let PreviewFrame = 0;
const LinkHelp = 'Some websites block direct image imports. Download the picture, then choose Open image or drag it here. The picture stays in your browser until you choose to upload the finished header.';

/** Format a byte count in binary units, switching to KiB below 1 MiB so small headers do not read as 0.00 MiB. */
function FormatBytes(SizeBytes) { return SizeBytes < 1048576 ? `${Math.max(1, Math.round(SizeBytes / 1024))} KiB` : `${(SizeBytes / 1048576).toFixed(2)} MiB`; }

/** Display operation failures without discarding the user's existing crop. */
function ShowError(Message) { Get('HelperError').textContent = Message; Get('HelperError').hidden = !Message; }

/** Keep controls consistent while an import or encode is running. */
function SetBusy(Value) {
    Busy = Value;
    if (Value) Cancelled = false;
    Get('ImportControls').disabled = Value;
    Get('EditControls').disabled = Value || !Source;
    Get('ExportControls').disabled = Value || !Source;
    CropBox.tabIndex = Value || !Source ? -1 : 0;
    Get('CancelOperation').hidden = !Value;
}

/** Run a cancellable codec in its own worker, releasing memory on every exit. */
function RunWorker(Request, Transfers = []) {
    return new Promise((Resolve, Reject) => {
        RejectOperation = Reject;
        Worker = new globalThis.Worker(Root.dataset.workerUrl, { type: 'module' });
        Worker.onmessage = ({ data: Response }) => Response.Error ? Reject(new Error(Response.Error)) : Resolve(Response.Result);
        Worker.onerror = () => Reject(new Error('The image processor could not start. Reload the page and try again.'));
        Worker.postMessage(Request, Transfers);
    }).finally(() => { Worker?.terminate(); Worker = null; RejectOperation = null; });
}

/** Bound network input before constructing a blob; the timeout includes body reads. */
async function ReadUrl() {
    const Url = new URL(Get('SourceUrl').value);
    if (!['https:', 'http:'].includes(Url.protocol)) throw new Error('Enter an HTTP or HTTPS image URL.');
    Abort = new AbortController();
    const Timer = setTimeout(() => Abort?.abort(), 45000);
    try {
        const Response = await fetch(Url, { credentials: 'omit', signal: Abort.signal });
        if (!Response.ok || !Response.headers.get('content-type')?.startsWith('image/')) throw new Error(LinkHelp);
        if (Number(Response.headers.get('content-length')) > MaxInputBytes) throw new Error('Choose an image smaller than 50 MiB.');
        const Reader = Response.body.getReader();
        const Parts = [];
        let SizeBytes = 0;
        try {
            while (true) {
                const { done: Done, value: Part } = await Reader.read();
                if (Done) break;
                SizeBytes += Part.byteLength;
                if (SizeBytes > MaxInputBytes) throw new Error('Choose an image smaller than 50 MiB.');
                Parts.push(Part);
            }
        } finally { await Reader.cancel(); }
        return new File(Parts, decodeURIComponent(Url.pathname.split('/').pop()) || 'picture', { type: Response.headers.get('content-type').split(';')[0] });
    } catch (Failure) {
        if (Failure.name === 'TypeError') throw new Error(LinkHelp);
        throw Failure;
    } finally { clearTimeout(Timer); Abort = null; }
}

/** Import atomically so a failed or cancelled replacement leaves the previous image intact. */
async function LoadImage(File, FromUrl = false) {
    if (Busy || !Supported) return;
    SetBusy(true); ShowError('');
    Get('HelperStatus').textContent = 'Opening picture…';
    try {
        if (FromUrl) File = await ReadUrl();
        if (!File || File.size > MaxInputBytes) throw new Error('Choose an image smaller than 50 MiB.');
        const Bitmap = await RunWorker({ Operation: 'decode', File });
        Source?.close(); Source = Bitmap;
        Get('EditorViewport').hidden = false;
        Get('SourceInfo').textContent = `${File.name} · ${Source.width} × ${Source.height} px · ${FormatBytes(File.size)}`;
        // The editor uses a bounded proxy; export always uses Source at its original resolution.
        const Scale = Math.min(1, 1800 / Math.max(Source.width, Source.height));
        SourceCanvas.width = Math.round(Source.width * Scale); SourceCanvas.height = Math.round(Source.height * Scale);
        SourceCanvas.getContext('2d').drawImage(Source, 0, 0, SourceCanvas.width, SourceCanvas.height);
        Reset();
        Get('HelperStatus').textContent = 'Picture ready. Adjust the crop and check the previews.';
    } catch (Error) {
        ShowError(Error.name === 'AbortError' ? 'Opening cancelled or timed out. Try a local file.' : Error.message);
        Get('HelperStatus').textContent = 'Picture was not replaced.';
    } finally { SetBusy(false); Get('SourceFile').value = ''; }
}

/** Match the desktop filename convention while preventing wiki syntax in generated references. */
function Slug(Text) { return Text.trim().toLowerCase().replace(/[^\p{L}\p{Nd}]+/gu, '-').replace(/^-|-$/g, '').slice(0, 100).replace(/-$/, ''); }

/** Require an explicitly entered article reference before encoding, downloading or copying. */
function ValidateArticleSlug() {
    const Valid = Boolean(Slug(Get('ArticleSlug').value));
    Get('ArticleSlugError').textContent = Valid ? '' : 'Enter the article slug before downloading the header or copying markup.';
    Get('ArticleSlugError').hidden = Valid;
    Get('ArticleSlug').setAttribute('aria-invalid', String(!Valid));
    if (!Valid) Get('ArticleSlug').focus();
    return Valid;
}

/** Release stale downloads whenever exported pixels or encoding settings change. */
function Invalidate() {
    if (DownloadUrl) URL.revokeObjectURL(DownloadUrl);
    DownloadUrl = null;
    Get('DownloadImage').hidden = true;
    Get('DownloadImage').removeAttribute('href');
}

/**
 * Generate markup independently of preview-only options. The markup names the file article's URL slug,
 * which has no extension: HeaderImage slugifies its value, so "x.avif" would look up "xavif" and never resolve.
 */
function UpdateMarkup() {
    const ArticleSlug = Slug(Get('ArticleSlug').value);
    if (!ArticleSlug) {
        Get('WikiText').value = '';
        Get('DownloadImage').removeAttribute('download');
        return;
    }
    const FileSlug = `header-for-${ArticleSlug}`;
    const Focus = Get('SiteFocus').value;
    Get('WikiText').value = `{{HeaderImage file:${FileSlug}${Focus === 'top' ? '' : ` |#| Focus=${Focus}`}}}`;
    Get('DownloadImage').download = `${FileSlug}.avif`;
}

/** Clamp all input paths to the same integer source-pixel rectangle. */
function SetCrop(Next) {
    Next.Width = Math.max(1, Math.min(Source.width, Math.round(Next.Width)));
    Next.Height = Math.max(1, Math.min(Source.height, Math.round(Next.Height)));
    Next.X = Math.max(0, Math.min(Source.width - Next.Width, Math.round(Next.X)));
    Next.Y = Math.max(0, Math.min(Source.height - Next.Height, Math.round(Next.Y)));
    Crop = Next; Invalidate();
    for (const Name of Coordinates) Get(`Crop${Name}`).value = Crop[Name];
    Object.assign(CropBox.style, { left: `${Crop.X / Source.width * 100}%`, top: `${Crop.Y / Source.height * 100}%`, width: `${Crop.Width / Source.width * 100}%`, height: `${Crop.Height / Source.height * 100}%` });
    Get('OutputInfo').textContent = `${Crop.Width} × ${Crop.Height} px · Native resolution, no upscaling.${Crop.Width < 1500 ? ' Width below 1,500 px may look soft on wide screens.' : ''}${Crop.Height > Crop.Width ? ' Portrait crop: check desktop framing.' : ''}`;
    UpdateMarkup(); SchedulePreviews();
}

/** Coalesce preview redraws to one per frame; dragging fires far more often, and each redraw samples the full-resolution source. */
function SchedulePreviews() {
    if (!PreviewFrame) PreviewFrame = requestAnimationFrame(() => { PreviewFrame = 0; DrawPreviews(); });
}

/** Resize from an anchored opposite corner, maintaining the locked ratio within image bounds. */
function Resize(Rect, Corner, Dx, Dy) {
    const West = Corner.includes('W'), North = Corner.includes('N');
    const AnchorX = West ? Rect.X + Rect.Width : Rect.X;
    const AnchorY = North ? Rect.Y + Rect.Height : Rect.Y;
    const MaxWidth = West ? AnchorX : Source.width - AnchorX;
    const MaxHeight = North ? AnchorY : Source.height - AnchorY;
    let Width = Math.max(1, Rect.Width + (West ? -Dx : Dx));
    let Height = Math.max(1, Rect.Height + (North ? -Dy : Dy));
    if (Aspect) {
        if (Math.abs(Dx) >= Math.abs(Dy * Aspect)) Height = Width / Aspect;
        else Width = Height * Aspect;
        const Scale = Math.min(1, MaxWidth / Width, MaxHeight / Height);
        Width *= Scale; Height *= Scale;
    } else { Width = Math.min(MaxWidth, Width); Height = Math.min(MaxHeight, Height); }
    Width = Math.max(1, Math.round(Width)); Height = Math.max(1, Math.round(Height));
    return { X: West ? AnchorX - Width : AnchorX, Y: North ? AnchorY - Height : AnchorY, Width, Height };
}

/** Restore full-source framing and fit without retaining a previous aspect lock. */
function Reset() {
    Aspect = null; Get('AspectLock').checked = false;
    Get('Zoom').value = '1'; FitSurface();
    SetCrop({ X: 0, Y: 0, Width: Source.width, Height: Source.height });
}

/** Fit both image dimensions inside the scroll viewport before applying the selected zoom. */
function FitSurface() {
    if (!Source) return;
    const Width = Math.min(Get('EditorViewport').clientWidth - 28, (innerHeight * .65 - 28) * Source.width / Source.height);
    Get('EditorSurface').style.width = `${Math.max(1, Width) * Number(Get('Zoom').value)}px`;
}

/** Render the desktop cropper's reference viewports using the wiki's current hero geometry. */
function DrawPreviews() {
    if (!Source) return;
    const Styles = getComputedStyle(Root);
    const DarkSurface = Styles.getPropertyValue('--color-surface-strong').trim();
    const LightSurface = Styles.getPropertyValue('--color-white').trim();
    const DarkInk = Styles.getPropertyValue('--color-preview-ink-light').trim();
    const LightInk = Styles.getPropertyValue('--color-preview-ink-dark').trim();
    const BlackRgb = Styles.getPropertyValue('--color-black-rgb').trim();
    for (const [Name, ViewWidth, ViewHeight] of [['Phone', 320, 690], ['Tablet', 800, 1280], ['Desktop', 1920, 1080]]) {
        const Canvas = Get(`Preview${Name}`);
        const Width = Math.min(ViewWidth, 1872), Height = Math.max(416, Math.min(ViewWidth * .52, ViewHeight * .6));
        const Scale = Math.min(1, 720 / Width);
        Canvas.width = Math.round(Width * Scale); Canvas.height = Math.round((Height + 70) * Scale);
        const Context = Canvas.getContext('2d'); Context.scale(Scale, Scale);
        const Dark = Get('DarkPreview').checked, Background = Dark ? DarkSurface : LightSurface;
        Context.fillStyle = Background; Context.fillRect(0, 0, Width, Height + 70);
        const Layer = document.createElement('canvas'); Layer.width = Canvas.width; Layer.height = Math.round(Height * Scale);
        const ImageContext = Layer.getContext('2d'); ImageContext.scale(Scale, Scale);
        const CoverScale = Math.max(Width / Crop.Width, Height / Crop.Height);
        const DrawWidth = Crop.Width * CoverScale, DrawHeight = Crop.Height * CoverScale;
        const Focus = { top: 0, center: .5, bottom: 1 }[Get('SiteFocus').value];
        const Rest = Get('RestScale').checked ? 1.025 : 1;
        ImageContext.translate(Width / 2, Height / 2); ImageContext.scale(Rest, Rest); ImageContext.translate(-Width / 2, -Height / 2);
        ImageContext.drawImage(Source, Crop.X, Crop.Y, Crop.Width, Crop.Height, (Width - DrawWidth) / 2, (Height - DrawHeight) * Focus, DrawWidth, DrawHeight);
        ImageContext.setTransform(Scale, 0, 0, Scale, 0, 0);
        if (Get('ShowFade').checked) {
            ImageContext.globalCompositeOperation = 'destination-in';
            const Fade = ImageContext.createLinearGradient(0, 0, 0, Height);
            for (const [Stop, Opacity] of [[0, 1], [.5, 1], [.58, .96], [.68, .82], [.8, .5], [.91, .15], [1, 0]]) Fade.addColorStop(Stop, `rgb(${BlackRgb} / ${Opacity})`);
            ImageContext.fillStyle = Fade; ImageContext.fillRect(0, 0, Width, Height);
            const Feather = Math.min(96, Math.max(0, (ViewWidth - 1872) / 2));
            if (Feather) {
                const Edge = ImageContext.createLinearGradient(0, 0, Width, 0);
                for (const [Stop, Opacity] of [[0, 0], [.25, .15], [.5, .5], [.75, .85], [.9, .97], [1, 1]]) {
                    Edge.addColorStop(Stop * Feather / Width, `rgb(${BlackRgb} / ${Opacity})`);
                    Edge.addColorStop(1 - Stop * Feather / Width, `rgb(${BlackRgb} / ${Opacity})`);
                }
                ImageContext.fillStyle = Edge; ImageContext.fillRect(0, 0, Width, Height);
            }
        }
        Context.drawImage(Layer, 0, 0, Width, Height);
        if (Get('ShowBar').checked) {
            Context.save(); Context.globalAlpha = 221 / 255; Context.fillStyle = Background; Context.fillRect(0, 0, Width, 75); Context.restore();
            Context.fillStyle = Dark ? DarkInk : LightInk; Context.font = 'bold 24px system-ui'; Context.fillText(Root.dataset.siteName, 20, 45, Width - 40);
        }
        if (Get('ShowTitle').checked) {
            const TitleSize = Math.max(56, Math.min(ViewWidth * .1, ViewHeight * .14, 120));
            Context.fillStyle = Dark ? DarkInk : LightInk; Context.font = `900 ${TitleSize}px Georgia`; Context.textAlign = 'center';
            Context.fillText('Article title', Width / 2, Height - TitleSize * .1, Width - 32);
        }
    }
}

/**
 * Download the header, encoding first only when the pixels or quality changed since the last encode.
 * After a slow encode the click's user activation may have expired, so the browser can block the automatic
 * download; the fallback link stays visible because a direct click on it is always allowed.
 */
async function DownloadHeader() {
    if (!Source || Busy || !ValidateArticleSlug()) return;
    if (!DownloadUrl) await Export();
    if (DownloadUrl && DownloadSizeBytes <= UploadLimitBytes) Get('DownloadImage').click();
}

/** Encode on explicit request, avoiding repeated expensive encodes during crop dragging. */
async function Export() {
    SetBusy(true); ShowError(''); Invalidate();
    Get('HelperStatus').textContent = 'Making your header on your device… Large pictures can take a while.';
    try {
        const Bitmap = await createImageBitmap(Source, Crop.X, Crop.Y, Crop.Width, Crop.Height);
        if (Cancelled) { Bitmap.close(); throw new Error('Processing cancelled.'); }
        const Bytes = await RunWorker({ Operation: 'encode', Bitmap, Quality: Number(Get('Quality').value) }, [Bitmap]);
        const BlobResult = new Blob([Bytes], { type: 'image/avif' });
        DownloadUrl = URL.createObjectURL(BlobResult); DownloadSizeBytes = BlobResult.size;
        Get('DownloadImage').href = DownloadUrl; Get('DownloadImage').hidden = false;
        UpdateMarkup();
        Get('HelperStatus').textContent = `Header ready · ${FormatBytes(BlobResult.size)}.`;
        // Not downloaded automatically: the wiki would reject it, though the link still lets the user keep it.
        if (BlobResult.size > UploadLimitBytes) ShowError('This header exceeds the wiki’s 10 MiB upload limit. Lower the quality or choose a smaller crop, then download it again.');
    } catch (Error) { ShowError(Error.message); Get('HelperStatus').textContent = 'No new header was created.'; }
    finally { SetBusy(false); }
}

Get('SourceFile').addEventListener('change', () => { if (Get('SourceFile').files[0]) LoadImage(Get('SourceFile').files[0]); });
Get('FetchImage').addEventListener('click', () => LoadImage(null, true));
Get('SourceUrl').addEventListener('keydown', Event => { if (Event.key === 'Enter') { Event.preventDefault(); LoadImage(null, true); } });
// Accept file drops anywhere on the page: a picture that misses the helper would otherwise make the browser navigate to it and lose the crop.
document.addEventListener('dragover', Event => { if (Event.dataTransfer.types.includes('Files')) { Event.preventDefault(); Root.dataset.dragging = ''; } });
document.addEventListener('dragleave', Event => { if (!Event.relatedTarget) delete Root.dataset.dragging; });
document.addEventListener('drop', Event => {
    if (!Event.dataTransfer.types.includes('Files')) return;
    Event.preventDefault(); delete Root.dataset.dragging;
    if (Event.dataTransfer.files[0]) LoadImage(Event.dataTransfer.files[0]);
});
document.addEventListener('paste', Event => {
    if (Event.target.closest('input, textarea, [contenteditable]')) return;
    const File = [...Event.clipboardData.items].find(Item => Item.type.startsWith('image/'))?.getAsFile();
    if (File) { Event.preventDefault(); LoadImage(File); }
});
Get('CancelOperation').addEventListener('click', () => { Cancelled = true; Abort?.abort(); RejectOperation?.(new Error('Processing cancelled. Your previous picture and crop are unchanged.')); });
Get('ResetCrop').addEventListener('click', Reset);
Get('FitImage').addEventListener('click', () => { Get('Zoom').value = '1'; FitSurface(); });
Get('Zoom').addEventListener('input', FitSurface);
window.addEventListener('resize', FitSurface);
Get('AspectLock').addEventListener('change', () => { Aspect = Get('AspectLock').checked ? Crop.Width / Crop.Height : null; });
for (const Name of Coordinates) Get(`Crop${Name}`).addEventListener('change', Event => {
    const Value = Number(Event.target.value);
    if (!Number.isFinite(Value)) { Event.target.value = Crop[Name]; return; }
    SetCrop(Name === 'Width' ? Resize(Crop, 'SE', Value - Crop.Width, 0) : Name === 'Height' ? Resize(Crop, 'SE', 0, Value - Crop.Height) : { ...Crop, [Name]: Value });
});
CropBox.addEventListener('pointerdown', Event => {
    if (Busy || !Source || Event.button !== 0 || !Event.isPrimary) return;
    Event.preventDefault(); Event.target.focus();
    Drag = { Pointer: Event.pointerId, X: Event.clientX, Y: Event.clientY, Crop: { ...Crop }, Corner: Event.target.dataset.corner, Scale: Source.width / Get('EditorSurface').getBoundingClientRect().width };
    CropBox.setPointerCapture(Event.pointerId);
});
CropBox.addEventListener('pointermove', Event => {
    if (!Drag || Drag.Pointer !== Event.pointerId) return;
    const Dx = (Event.clientX - Drag.X) * Drag.Scale, Dy = (Event.clientY - Drag.Y) * Drag.Scale;
    SetCrop(Drag.Corner ? Resize(Drag.Crop, Drag.Corner, Dx, Dy) : { ...Drag.Crop, X: Drag.Crop.X + Dx, Y: Drag.Crop.Y + Dy });
});
CropBox.addEventListener('pointerup', Event => { if (CropBox.hasPointerCapture(Event.pointerId)) CropBox.releasePointerCapture(Event.pointerId); });
CropBox.addEventListener('lostpointercapture', () => { Drag = null; });
CropBox.addEventListener('dblclick', () => { if (!Busy && Source) Reset(); });
CropBox.addEventListener('keydown', Event => {
    if (Busy || !Source || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(Event.key)) return;
    Event.preventDefault();
    const Step = Event.shiftKey ? 10 : 1;
    const Dx = Event.key === 'ArrowLeft' ? -Step : Event.key === 'ArrowRight' ? Step : 0;
    const Dy = Event.key === 'ArrowUp' ? -Step : Event.key === 'ArrowDown' ? Step : 0;
    SetCrop(Event.target.dataset.corner ? Resize(Crop, Event.target.dataset.corner, Dx, Dy) : { ...Crop, X: Crop.X + Dx, Y: Crop.Y + Dy });
});
for (const Id of ['ShowFade', 'ShowBar', 'ShowTitle', 'RestScale', 'DarkPreview', 'SiteFocus']) Get(Id).addEventListener('change', () => { DrawPreviews(); UpdateMarkup(); });
Get('ArticleSlug').addEventListener('input', () => {
    Get('ArticleSlugError').hidden = true;
    Get('ArticleSlugError').textContent = '';
    Get('ArticleSlug').removeAttribute('aria-invalid');
    UpdateMarkup();
});
Get('DownloadImage').addEventListener('click', Event => { if (!ValidateArticleSlug()) Event.preventDefault(); });
Get('Quality').addEventListener('input', () => { Get('QualityValue').value = Get('Quality').value; Invalidate(); });
Get('ExportImage').addEventListener('click', DownloadHeader);
Get('CopyMarkup').addEventListener('click', async () => {
    if (!ValidateArticleSlug()) return;
    try { await navigator.clipboard.writeText(Get('WikiText').value); Get('HelperStatus').textContent = 'Wiki markup copied.'; }
    catch { Get('WikiText').focus(); Get('WikiText').select(); Get('HelperStatus').textContent = 'Copy the selected markup with Ctrl+C / ⌘C.'; }
});
if (!Supported) {
    Get('HelperStatus').textContent = 'This editor requires a current browser with WebAssembly and OffscreenCanvas. Try an up-to-date Chrome, Edge, Firefox, or Safari.';
} else {
    SetBusy(false); Get('HelperStatus').textContent = 'Open, drop, or paste a picture to begin. Pictures stay on your device.';
}
