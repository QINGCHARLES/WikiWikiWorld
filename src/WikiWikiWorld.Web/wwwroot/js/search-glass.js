// Reversible visual trial: ?glass=off keeps the original search bar; ?glassTint=0..1.5 scales the tint.
// Nothing is stored. Without this script, or if the hero can't be read, the original bar stays.
const SampleColumns = 5;
const SampleRows = 3;
const SamplesDroppedPerEnd = 2;
const MaxSampleCanvasWidthPixels = 512;
const InkLuminanceLine = 0.5;
const InkHysteresis = 0.05;
const DarkInkLuminance = 0;
const LightInkLuminance = 0.94;
// Match the --glass-clear-* and --glass-smoke-* tokens.
const GlassInks = {
    dark: { Tint: [255, 255, 255], Ink: [0, 0, 0], InkOpacity: 0.86 },
    light: { Tint: [16, 18, 22], Ink: [255, 255, 255], InkOpacity: 0.94 }
};
// An empty bar only shows its icon (3:1 required); a query left in it is text (4.5:1 required).
// Both leave a margin because the 5 x 3 grid can't see every pixel under the bar.
const EmptyBarContrastRatio = 3.5;
const QueryContrastRatio = 5;
const MaxTintFloor = 0.8;
const TintFloorStep = 0.01;
const MaxRimWidthPixels = 44;
const MinRimWidthPixels = 4;
const MinLensSurfacePixels = 32;
const RimDepthFactor = 1.1;
const RimFalloffExponent = 2.6;
const MapPixelsPerCssPixel = 2;
const RedSplitFactor = 1.15;
const BlueSplitFactor = 0.85;

const SearchForm = document.querySelector('.search-form');
const HeroImage = document.querySelector('img.hero-image');
const Query = new URLSearchParams(location.search);

let HeroPixels = null;
let CurrentInk = null;
let CurrentLens = null;
let LensGeneration = 0;
let LensCount = 0;
let RefreshPending = false;

if (SearchForm && HeroImage && Query.get('glass') !== 'off') StartGlass();

/** Reads the hero, builds the lens, then switches the bar to glass in one step so it never shows half-applied. */
async function StartGlass() {
    try {
        await HeroImage.decode();
        ReadHeroPixels();
    } catch (Failure) {
        console.warn('Search glass unavailable; keeping the original search bar.', Failure);
        return;
    }

    const TintScale = Number.parseFloat(Query.get('glassTint'));
    if (Number.isFinite(TintScale)) {
        SearchForm.style.setProperty('--glass-tint-scale', String(Math.min(1.5, Math.max(0, TintScale))));
    }

    await UpdateLens();
    ApplyBackgroundSample();

    addEventListener('scroll', ScheduleRefresh, { passive: true });
    SearchForm.addEventListener('focusout', ScheduleRefresh);
    HeroImage.addEventListener('load', HandleHeroReplaced);
    const SizeObserver = new ResizeObserver(ScheduleRefresh);
    SizeObserver.observe(SearchForm);
    SizeObserver.observe(HeroImage);
}

/**
 * Copies the hero into a small canvas so later samples are plain array reads.
 * @throws {DOMException} When a cross-origin hero taints the canvas; the caller keeps the original bar.
 */
function ReadHeroPixels() {
    const Width = Math.min(MaxSampleCanvasWidthPixels, HeroImage.naturalWidth);
    const Height = Math.max(1, Math.round(Width * HeroImage.naturalHeight / HeroImage.naturalWidth));
    const Canvas = document.createElement('canvas');
    Canvas.width = Width;
    Canvas.height = Height;
    const Context = Canvas.getContext('2d');
    // The page shows through transparent hero pixels, and its background is white.
    Context.fillStyle = getComputedStyle(document.documentElement).getPropertyValue('--color-background').trim();
    Context.fillRect(0, 0, Width, Height);
    Context.drawImage(HeroImage, 0, 0, Width, Height);
    HeroPixels = { Data: Context.getImageData(0, 0, Width, Height).data, Width, Height };
}

/** Re-reads a hero whose source changed, keeping the last good sample if the new one can't be read. */
function HandleHeroReplaced() {
    try {
        ReadHeroPixels();
        ScheduleRefresh();
    } catch (Failure) {
        console.warn('Search glass could not read the replaced hero.', Failure);
    }
}

/** Coalesces scroll, resize and blur events into one refresh per frame. */
function ScheduleRefresh() {
    if (RefreshPending) return;
    RefreshPending = true;
    requestAnimationFrame(() => {
        RefreshPending = false;
        UpdateLens();
        ApplyBackgroundSample();
    });
}

/**
 * Finds the hero pixel shown at a viewport point, following object-fit: cover, the data-focus crop
 * and the parallax transform, which getBoundingClientRect already includes.
 * @returns {{Red: number, Green: number, Blue: number}} 0-255 channels; white where the hero doesn't reach, because the page shows there.
 */
function ReadHeroPoint(ViewportX, ViewportY, HeroBox) {
    const CoverScale = Math.max(HeroBox.width / HeroImage.naturalWidth, HeroBox.height / HeroImage.naturalHeight);
    const RenderedWidth = HeroImage.naturalWidth * CoverScale;
    const RenderedHeight = HeroImage.naturalHeight * CoverScale;
    const FocusY = { center: 0.5, bottom: 1 }[HeroImage.dataset.focus] ?? 0;
    const FractionX = (ViewportX - HeroBox.left - (HeroBox.width - RenderedWidth) * 0.5) / RenderedWidth;
    const FractionY = (ViewportY - HeroBox.top - (HeroBox.height - RenderedHeight) * FocusY) / RenderedHeight;
    const IsInsideFrame = ViewportY >= HeroBox.top && ViewportY < HeroBox.bottom;
    if (!IsInsideFrame || FractionX < 0 || FractionX >= 1 || FractionY < 0 || FractionY >= 1) {
        return { Red: 255, Green: 255, Blue: 255 };
    }

    const PixelX = Math.floor(FractionX * HeroPixels.Width);
    const PixelY = Math.floor(FractionY * HeroPixels.Height);
    const Offset = (PixelY * HeroPixels.Width + PixelX) * 4;
    return { Red: HeroPixels.Data[Offset], Green: HeroPixels.Data[Offset + 1], Blue: HeroPixels.Data[Offset + 2] };
}

/**
 * Measures the hero under the bar on a 5 x 3 grid, ignoring the two darkest and two brightest points.
 * @returns {{Luminance: number, Texture: number, Chroma: number, Colors: number[][]}} Each measure on a 0-1 scale; texture and Colors span the whole grid.
 */
function MeasureBackground(BarBox) {
    const HeroBox = HeroImage.getBoundingClientRect();
    const Points = [];
    for (let Row = 0; Row < SampleRows; Row++) {
        for (let Column = 0; Column < SampleColumns; Column++) {
            const X = BarBox.left + BarBox.width * (Column + 0.5) / SampleColumns;
            const Y = BarBox.top + BarBox.height * (Row + 0.5) / SampleRows;
            const Point = ReadHeroPoint(X, Y, HeroBox);
            Points.push({
                Color: [Point.Red, Point.Green, Point.Blue],
                Luminance: (0.299 * Point.Red + 0.587 * Point.Green + 0.114 * Point.Blue) / 255,
                Chroma: (Math.max(Point.Red, Point.Green, Point.Blue) - Math.min(Point.Red, Point.Green, Point.Blue)) / 255
            });
        }
    }

    Points.sort((Left, Right) => Left.Luminance - Right.Luminance);
    const Texture = Points[Points.length - 1].Luminance - Points[0].Luminance;
    const Kept = Points.slice(SamplesDroppedPerEnd, -SamplesDroppedPerEnd);
    return {
        Luminance: Kept.reduce((Sum, Point) => Sum + Point.Luminance, 0) / Kept.length,
        Chroma: Kept.reduce((Sum, Point) => Sum + Point.Chroma, 0) / Kept.length,
        Texture,
        Colors: Points.map(Point => Point.Color)
    };
}

/**
 * Converts an sRGB colour to WCAG relative luminance.
 * @returns {number} Relative luminance from 0 to 1.
 */
function ReadRelativeLuminance(Color) {
    const [Red, Green, Blue] = Color.map(Channel => {
        const Value = Channel / 255;
        return Value <= 0.04045 ? Value / 12.92 : ((Value + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * Red + 0.7152 * Green + 0.0722 * Blue;
}

/**
 * Mixes two sRGB colours.
 * @returns {number[]} Top laid over Bottom at the given opacity.
 */
function MixColors(Top, Bottom, Opacity) {
    return Top.map((Channel, Index) => Channel * Opacity + Bottom[Index] * (1 - Opacity));
}

/**
 * Finds the thinnest tint that gives the ink the target contrast on every sampled point. High-contrast
 * heroes need more than the density range gives; raising the tint keeps the ink's opacity at its fixed step.
 * @returns {number} Tint opacity floor, capped at MaxTintFloor. The filter's colour steps are not modelled.
 */
function FindTintFloor(Colors, Ink, TargetContrastRatio) {
    const { Tint, Ink: InkColor, InkOpacity } = GlassInks[Ink];
    for (let Floor = 0; Floor < MaxTintFloor; Floor += TintFloorStep) {
        const IsReadable = Colors.every(Color => {
            const Glass = MixColors(Tint, Color, Floor);
            const GlassLuminance = ReadRelativeLuminance(Glass);
            const InkLuminance = ReadRelativeLuminance(MixColors(InkColor, Glass, InkOpacity));
            return (Math.max(GlassLuminance, InkLuminance) + 0.05) / (Math.min(GlassLuminance, InkLuminance) + 0.05) >= TargetContrastRatio;
        });
        if (IsReadable) return Floor;
    }
    return MaxTintFloor;
}

/**
 * Chooses the ink. Once chosen, it only flips beyond the ±0.05 band around the line, so the bar can't flicker there.
 * @returns {'dark'|'light'} The ink to draw on the glass.
 */
function ChooseInk(Luminance) {
    if (CurrentInk === 'light') return Luminance > InkLuminanceLine + InkHysteresis ? 'dark' : 'light';
    if (CurrentInk === 'dark') return Luminance < InkLuminanceLine - InkHysteresis ? 'light' : 'dark';
    return Luminance < InkLuminanceLine ? 'light' : 'dark';
}

/** Samples what is under the bar and writes the ink, density and saturation it calls for. */
function ApplyBackgroundSample() {
    if (!HeroPixels || SearchForm.matches(':focus-within')) return;
    const BarBox = SearchForm.getBoundingClientRect();
    if (BarBox.bottom < 0) return;

    const Background = MeasureBackground(BarBox);
    CurrentInk = ChooseInk(Background.Luminance);
    const InkLuminance = CurrentInk === 'light' ? LightInkLuminance : DarkInkLuminance;
    const Closeness = 1 - Math.abs(Background.Luminance - InkLuminance);
    const Density = 0.55 * Closeness + 0.7 * Background.Texture + 0.3 * Background.Chroma;

    SearchForm.dataset.glassInk = CurrentInk;
    SearchForm.style.setProperty('--glass-density', Math.min(1, Density).toFixed(3));
    const HasQuery = SearchForm.querySelector('input[type="search"]').value.trim().length > 0;
    const TintFloor = FindTintFloor(Background.Colors, CurrentInk, HasQuery ? QueryContrastRatio : EmptyBarContrastRatio);
    SearchForm.style.setProperty('--glass-tint-floor', TintFloor.toFixed(2));
    SearchForm.style.setProperty('--glass-saturation-kept', Math.max(0.45, 1 - 0.75 * Background.Chroma).toFixed(3));
}

/**
 * Only Chromium draws url() in backdrop-filter. Firefox parses it and draws nothing, and User-Agent
 * Client Hints are the practical way to tell the engines apart here.
 * @returns {boolean} True when the lens can be added on top of the CSS-only glass.
 */
function SupportsLens() {
    return CSS.supports('backdrop-filter', 'url(#a)') && 'userAgentData' in navigator;
}

/**
 * Keeps the lens matched to the bar's size. A focused bar is skipped because it has no glass and can
 * change width on phones; the focusout refresh finds the size unchanged and keeps the existing map.
 */
async function UpdateLens() {
    if (!SupportsLens() || SearchForm.matches(':focus-within')) return;
    const Width = SearchForm.offsetWidth;
    const Height = SearchForm.offsetHeight;
    if (CurrentLens?.Width === Width && CurrentLens?.Height === Height) return;

    const Generation = ++LensGeneration;
    if (Math.min(Width, Height) < MinLensSurfacePixels) {
        RemoveCurrentLens();
        return;
    }

    const Radius = Math.min(Number.parseFloat(getComputedStyle(SearchForm).borderTopLeftRadius) || 0, Width / 2, Height / 2);
    const Rim = Math.max(MinRimWidthPixels, Math.min(MaxRimWidthPixels, Math.min(Width, Height) / 3.5));
    const MapUrl = DrawDisplacementMap(Width, Height, Radius, Rim);

    // A filter drawn before its map decodes loses its blur, so wait before pointing the bar at it.
    const MapImage = new Image();
    MapImage.src = MapUrl;
    await MapImage.decode();
    if (Generation !== LensGeneration) return;

    const Lens = CreateLensFilter(MapUrl, Width, Height, 2 * RimDepthFactor * Rim);
    SearchForm.style.setProperty('--glass-lens', `url(#${Lens.FilterId})`);
    RemoveCurrentLens();
    CurrentLens = { Svg: Lens.Svg, Width, Height };
}

/** Removes the previous filter's SVG once nothing points at it. */
function RemoveCurrentLens() {
    CurrentLens?.Svg.remove();
    CurrentLens = null;
}

/**
 * Draws the displacement map for a rounded rectangle: red bends sideways, green vertically, 128 means none.
 * Only the rim bends. A point d from the edge looks inward by 1.1 x rim x (1 - d / rim)^2.6.
 * @returns {string} A PNG data URL for feImage, at twice the CSS size so the rim stays smooth.
 */
function DrawDisplacementMap(Width, Height, Radius, Rim) {
    const Canvas = document.createElement('canvas');
    Canvas.width = Width * MapPixelsPerCssPixel;
    Canvas.height = Height * MapPixelsPerCssPixel;
    const Context = Canvas.getContext('2d');
    const Map = Context.createImageData(Canvas.width, Canvas.height);
    const MaxOffset = RimDepthFactor * Rim;

    for (let Y = 0; Y < Canvas.height; Y++) {
        for (let X = 0; X < Canvas.width; X++) {
            const FromCenterX = (X + 0.5) / MapPixelsPerCssPixel - Width / 2;
            const FromCenterY = (Y + 0.5) / MapPixelsPerCssPixel - Height / 2;
            const Edge = MeasureEdge(FromCenterX, FromCenterY, Width, Height, Radius);
            const Falloff = Edge.Distance < Rim ? Math.pow(1 - Math.max(0, Edge.Distance) / Rim, RimFalloffExponent) : 0;
            const Offset = (Y * Canvas.width + X) * 4;
            Map.data[Offset] = 128 + 127 * Falloff * Edge.InwardX;
            Map.data[Offset + 1] = 128 + 127 * Falloff * Edge.InwardY;
            Map.data[Offset + 2] = 128;
            Map.data[Offset + 3] = 255;
        }
    }

    Context.putImageData(Map, 0, 0);
    return Canvas.toDataURL('image/png');
}

/**
 * Measures a point's distance from the edge of the same rounded rectangle the CSS draws.
 * @returns {{Distance: number, InwardX: number, InwardY: number}} Distance in CSS pixels and a unit vector pointing inward.
 */
function MeasureEdge(FromCenterX, FromCenterY, Width, Height, Radius) {
    const CornerX = Math.abs(FromCenterX) - (Width / 2 - Radius);
    const CornerY = Math.abs(FromCenterY) - (Height / 2 - Radius);
    const SignX = Math.sign(FromCenterX);
    const SignY = Math.sign(FromCenterY);

    if (CornerX > 0 && CornerY > 0) {
        const Length = Math.hypot(CornerX, CornerY);
        return { Distance: Radius - Length, InwardX: -SignX * CornerX / Length, InwardY: -SignY * CornerY / Length };
    }

    return CornerX > CornerY
        ? { Distance: Radius - CornerX, InwardX: -SignX, InwardY: 0 }
        : { Distance: Radius - CornerY, InwardX: 0, InwardY: -SignY };
}

/**
 * Creates the lens in its own SVG with a unique id: three displacement passes, one per colour channel,
 * with red bent further and blue less, added back together with screen blends.
 * @returns {{Svg: SVGSVGElement, FilterId: string}} The attached SVG and the id for url().
 */
function CreateLensFilter(MapUrl, Width, Height, Scale) {
    const FilterId = `search-glass-lens-${++LensCount}`;
    const Holder = document.createElement('div');
    Holder.innerHTML = `
        <svg xmlns="http://www.w3.org/2000/svg" width="0" height="0" aria-hidden="true" style="position:absolute">
            <filter id="${FilterId}" x="0" y="0" width="${Width}" height="${Height}"
                filterUnits="userSpaceOnUse" primitiveUnits="userSpaceOnUse" color-interpolation-filters="sRGB">
                <feImage href="${MapUrl}" x="0" y="0" width="${Width}" height="${Height}" preserveAspectRatio="none" result="Map" />
                <feDisplacementMap in="SourceGraphic" in2="Map" scale="${Scale * RedSplitFactor}" xChannelSelector="R" yChannelSelector="G" result="RedBent" />
                <feColorMatrix in="RedBent" values="1 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 1 0" result="Red" />
                <feDisplacementMap in="SourceGraphic" in2="Map" scale="${Scale}" xChannelSelector="R" yChannelSelector="G" result="GreenBent" />
                <feColorMatrix in="GreenBent" values="0 0 0 0 0  0 1 0 0 0  0 0 0 0 0  0 0 0 1 0" result="Green" />
                <feDisplacementMap in="SourceGraphic" in2="Map" scale="${Scale * BlueSplitFactor}" xChannelSelector="R" yChannelSelector="G" result="BlueBent" />
                <feColorMatrix in="BlueBent" values="0 0 0 0 0  0 0 0 0 0  0 0 1 0 0  0 0 0 1 0" result="Blue" />
                <feBlend in="Red" in2="Green" mode="screen" result="RedGreen" />
                <feBlend in="RedGreen" in2="Blue" mode="screen" />
            </filter>
        </svg>`;
    const Svg = Holder.firstElementChild;
    document.body.append(Svg);
    return { Svg, FilterId };
}
