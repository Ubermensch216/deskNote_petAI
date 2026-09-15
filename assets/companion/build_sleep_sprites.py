"""Derives a sleeping sprite sheet for every companion species from its rest sheet.

The pet used to "sleep" by holding one frame of the sitting pose while Zs floated off its head,
which is a pet sitting up with its eyes open next to some Zs. A sleeping animal lies down and
shuts its eyes, and neither of those can be faked with a transform over the sitting artwork: a
uniform squash flattens the face, and there is no way to close a drawn-open eye from the outside.

Rather than commission seven new drawings, the sleep pose is computed from the rest pose. Two
operations, the same code for all seven species:

  * The eye is found (the one dark blob on the face carrying a specular highlight), removed by
    growing the surrounding fur inwards over it, and a shut lid is drawn in the artwork's own ink
    colour.
  * The figure is settled onto the floor by a smooth per-row vertical compression that squeezes
    the body far harder than the head, so the animal ends up lying down with its head still up
    instead of looking like it was stepped on.

Run it after changing any `*-rest.png`:

    python assets/companion/build_sleep_sprites.py

Needs Pillow and NumPy. The output is committed, so nobody building the app has to run this.
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SPRITES = ROOT / "src" / "DeskNote.App" / "Assets" / "Companion"

SPECIES = ["rabbit", "cat", "dog", "fennec", "otter", "monkey", "dragon"]
FRAME_COUNT = 4

# How much of its own height each band keeps, and how much wider it spreads.
#
# The head is left at its exact drawn proportions. An earlier cut squeezed it to 0.86 while
# widening the whole figure by 1.16, which is a 35% change to the shape of the face - the one
# part of the drawing anybody looks at, and quite visibly squashed. The descent is carried
# entirely by the body, and the spread is applied per row on the same ramp, so the body broadens
# the way a lying animal does while the head keeps the width it was drawn with.
HEAD_SCALE = 1.0
BODY_SCALE = 0.38
BODY_WIDEN = 1.12


def _luminance(pixels):
    return 0.299 * pixels[..., 0] + 0.587 * pixels[..., 1] + 0.114 * pixels[..., 2]


def _components(mask, width, height):
    """Connected components of a boolean mask, as lists of flat pixel indices."""
    seen = bytearray(width * height)
    blobs = []
    for start in range(width * height):
        if not mask[start] or seen[start]:
            continue
        stack = [start]
        seen[start] = 1
        blob = []
        while stack:
            index = stack.pop()
            blob.append(index)
            x, y = index % width, index // width
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < width and 0 <= ny < height:
                    neighbour = ny * width + nx
                    if mask[neighbour] and not seen[neighbour]:
                        seen[neighbour] = 1
                        stack.append(neighbour)
        blobs.append(blob)
    return blobs


def find_eye(frame):
    """
    Locates the one visible eye.

    Every species is drawn in three-quarter profile with a single large eye that has a white
    specular highlight in it. The highlight is what separates the eye from the cat's dark coat
    patches and from the ink outlines, which are darker still but long, thin and hollow.
    """
    width, height = frame.size
    pixels = frame.load()
    dark = bytearray(width * height)
    bright = bytearray(width * height)
    for y in range(height):
        for x in range(width):
            pixel = pixels[x, y]
            if pixel[3] < 180:
                continue
            level = 0.299 * pixel[0] + 0.587 * pixel[1] + 0.114 * pixel[2]
            if level < 105:
                dark[y * width + x] = 1
            elif level > 232:
                bright[y * width + x] = 1

    best = None
    for blob in _components(dark, width, height):
        if len(blob) < 250:
            continue
        xs = [i % width for i in blob]
        ys = [i // width for i in blob]
        x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
        box_w, box_h = x1 - x0 + 1, y1 - y0 + 1
        fill = len(blob) / float(box_w * box_h)
        aspect = box_w / float(box_h)
        if fill < 0.45 or not 0.45 < aspect < 2.2:
            continue
        highlight = sum(
            1
            for y in range(y0, y1 + 1)
            for x in range(x0, x1 + 1)
            if bright[y * width + x]
        )
        if highlight < 12:
            continue
        score = len(blob) + highlight * 6
        if best is None or score > best[0]:
            best = (score, (x0, y0, x1, y1))

    if best is None:
        raise SystemExit("no eye found - the artwork changed enough to need a new detector")
    return best[1]


def _otsu(values):
    """
    Splits one bag of luminances into dark and light without a fixed threshold.

    The species run from a cream rabbit through a dark brown otter to a blue dragon, so any
    absolute cut-off that finds the rabbit's eye swallows the otter's entire face.
    """
    histogram, edges = np.histogram(values, bins=64, range=(0, 255))
    total = histogram.sum()
    if total == 0:
        return 128.0
    weights = histogram / total
    centres = (edges[:-1] + edges[1:]) / 2
    cumulative = np.cumsum(weights)
    means = np.cumsum(weights * centres)
    denominator = cumulative * (1 - cumulative)
    denominator[denominator == 0] = 1e-9
    between = (means[-1] * cumulative - means) ** 2 / denominator
    return float(centres[int(np.argmax(between))])


def _fill_enclosed(dark):
    """Adds whatever the dark blob encloses, which is the eye's highlight."""
    height, width = dark.shape
    outside = np.zeros_like(dark)
    stack = []
    for x in range(width):
        for y in (0, height - 1):
            if not dark[y, x] and not outside[y, x]:
                outside[y, x] = True
                stack.append((y, x))
    for y in range(height):
        for x in (0, width - 1):
            if not dark[y, x] and not outside[y, x]:
                outside[y, x] = True
                stack.append((y, x))
    while stack:
        y, x = stack.pop()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < height and 0 <= nx < width and not dark[ny, nx] and not outside[ny, nx]:
                outside[ny, nx] = True
                stack.append((ny, nx))
    return dark | ~outside


def eye_mask(frame, box, grow=4):
    """The eye's own pixels: the iris, its outline, and the highlight on it."""
    pixels = np.array(frame).astype(np.float64)
    x0, y0, x1, y1 = box
    pad = max(2, int((x1 - x0) * 0.14))
    left, top = max(0, x0 - pad), max(0, y0 - pad)
    right = min(pixels.shape[1] - 1, x1 + pad)
    bottom = min(pixels.shape[0] - 1, y1 + pad)

    window = pixels[top:bottom + 1, left:right + 1]
    solid = window[..., 3] > 150
    dark = solid & (_luminance(window) < _otsu(_luminance(window)[solid]))

    mask = np.zeros(pixels.shape[:2], dtype=bool)
    mask[top:bottom + 1, left:right + 1] = _fill_enclosed(dark)

    # The highlight sits on the iris rim as often as inside it, so the flood above does not
    # always reach it, and a stray white comma left on a sleeping face reads as a half-open eye.
    # The detector's box is tight on the eye, so everything inside it goes.
    mask[y0:y1 + 1, x0:x1 + 1] = True

    grown = Image.fromarray((mask * 255).astype(np.uint8), "L")
    return np.array(grown.filter(ImageFilter.MaxFilter(2 * grow + 1))) > 127


def inpaint(frame, mask):
    """
    Fills the hole by growing its rim inwards, one ring of pixels at a time.

    Each new ring is the mean of the neighbours already filled, so the colours arriving in the
    middle are the colours that surrounded the eye: the fur above stays above, the cheek below
    stays below, and a calico patch beside it survives. Blurring the whole neighbourhood instead
    washes it all to one tone, which renders as a blank white eye.
    """
    pixels = np.array(frame).astype(np.float64)
    if not mask.any():
        return frame

    holes = np.where(mask)
    top, bottom = int(holes[0].min()), int(holes[0].max())
    left, right = int(holes[1].min()), int(holes[1].max())
    margin = 4
    top, bottom = max(0, top - margin), min(pixels.shape[0] - 1, bottom + margin)
    left, right = max(0, left - margin), min(pixels.shape[1] - 1, right + margin)

    window = pixels[top:bottom + 1, left:right + 1, :3].copy()
    hole = mask[top:bottom + 1, left:right + 1].copy()
    unknown = hole.copy()

    neighbours = ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1))
    while unknown.any():
        known = ~unknown
        total = np.zeros_like(window)
        count = np.zeros(window.shape[:2])
        for dy, dx in neighbours:
            total += np.roll(np.roll(window * known[..., None], dy, axis=0), dx, axis=1)
            count += np.roll(np.roll(known.astype(float), dy, axis=0), dx, axis=1)
        frontier = unknown & (count > 0)
        if not frontier.any():
            window[unknown] = window[known].mean(axis=0) if known.any() else 200.0
            break
        window[frontier] = (total / np.maximum(count, 1)[..., None])[frontier]
        unknown = unknown & ~frontier

    # Ring growth leaves faint radial banding; one small blur inside the hole clears it without
    # touching the artwork around it.
    softened = Image.fromarray(np.clip(window, 0, 255).astype(np.uint8), "RGB")
    blurred = np.array(softened.filter(ImageFilter.GaussianBlur(2.0))).astype(np.float64)
    window[hole] = blurred[hole]

    out = pixels.copy()
    out[top:bottom + 1, left:right + 1, :3] = window
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")


def ink_colour(frame, box):
    """The darkest tone the eye is outlined in, so the drawn lid matches the line art."""
    pixels = np.array(frame).astype(np.float64)
    x0, y0, x1, y1 = box
    patch = pixels[y0:y1 + 1, x0:x1 + 1].reshape(-1, 4)
    patch = patch[patch[:, 3] > 200]
    darkest = patch[np.argsort(_luminance(patch))[:max(1, len(patch) // 12)]]
    return tuple(int(v) for v in np.median(darkest, axis=0)[:3])


def close_eye(frame, box):
    """Removes the open eye and draws a shut one in its place."""
    ink = ink_colour(frame, box)
    out = inpaint(frame, eye_mask(frame, box))

    x0, y0, x1, y1 = box
    eye_w, eye_h = x1 - x0 + 1, y1 - y0 + 1
    cx = (x0 + x1) / 2
    cy = (y0 + y1) / 2 + eye_h * 0.06
    half = eye_w * 0.52
    stroke = max(3, int(eye_h * 0.13))

    overlay = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    draw.arc(
        [cx - half, cy - eye_h * 0.42, cx + half, cy + eye_h * 0.46],
        start=18,
        end=162,
        fill=ink + (255,),
        width=stroke,
    )
    # Two short lashes at the outer corner. Without them the curve reads as a mouth that has
    # wandered up the face rather than as a shut eye.
    for lift, length in ((0.75, 0.30), (0.30, 0.26)):
        ax = cx - half * 0.96
        ay = cy + eye_h * 0.06
        draw.line(
            [ax, ay, ax - eye_w * length * 0.55, ay - eye_h * length * lift],
            fill=ink + (255,),
            width=max(2, stroke - 1),
        )
    return Image.alpha_composite(out, overlay.filter(ImageFilter.GaussianBlur(0.7)))


def settle(frame, eye_box):
    """
    Lowers the figure onto the floor, compressing the body far more than the head.

    The compression rate is a smooth ramp rather than two bands, because a step change in rate
    puts a visible kink in every outline that crosses it. The neck is taken from the eye: on a
    chibi face the chin sits about one eye-height below the eye, and there is no neck to measure.
    """
    pixels = np.array(frame)
    rows = np.where(pixels[:, :, 3].max(axis=1) > 8)[0]
    top, bottom = int(rows[0]), int(rows[-1]) + 1

    eye_h = eye_box[3] - eye_box[1] + 1
    neck = min(bottom - 2, int(eye_box[3] + eye_h * 1.25))
    blend = max(6.0, eye_h * 0.9)

    y = np.arange(top, bottom, dtype=np.float64)
    ramp = 1.0 / (1.0 + np.exp(-(y - neck) / (blend / 4.0)))
    weight = HEAD_SCALE + (BODY_SCALE - HEAD_SCALE) * ramp

    edges = np.concatenate([[0.0], np.cumsum(weight)])
    out_height = int(round(edges[-1]))
    targets = np.arange(out_height, dtype=np.float64) + 0.5
    source = np.interp(targets, edges, np.concatenate([y, [float(bottom)]]))

    low = np.clip(np.floor(source).astype(int), top, bottom - 1)
    high = np.clip(low + 1, top, bottom - 1)
    fraction = (source - low)[:, None, None]
    blended = pixels[low].astype(np.float64) * (1 - fraction) \
        + pixels[high].astype(np.float64) * fraction

    # Spread, per row, on the ramp the compression used. A single resize of the whole figure
    # would widen the head too, which is what made the face look squashed.
    spread = 1.0 + (BODY_WIDEN - 1.0) * np.interp(source, y, ramp)
    blended = _stretch_rows(blended, spread)

    settled = Image.fromarray(np.clip(blended, 0, 255).astype(np.uint8), "RGBA")

    # The feet stay on the same line they stood on, so the pet does not hop when it lies down.
    out = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    out.paste(settled, ((frame.width - settled.width) // 2, bottom - settled.height), settled)
    return out


def _stretch_rows(rows, spread):
    """Scales every row horizontally about the centre by its own factor."""
    height, width = rows.shape[:2]
    centre = (width - 1) / 2.0
    columns = np.arange(width, dtype=np.float64)
    source_x = (columns[None, :] - centre) / spread[:, None] + centre

    left = np.floor(source_x)
    fraction = (source_x - left)[:, :, None]
    left = left.astype(int)
    right = left + 1
    inside = (left >= 0) & (right < width)

    rows_index = np.arange(height)[:, None]
    gathered = (rows[rows_index, np.clip(left, 0, width - 1)] * (1 - fraction)
                + rows[rows_index, np.clip(right, 0, width - 1)] * fraction)
    return np.where(inside[:, :, None], gathered, 0.0)


def build(species):
    sheet = Image.open(SPRITES / f"{species}-rest.png").convert("RGBA")
    frame_width = sheet.width // FRAME_COUNT
    frame = sheet.crop((0, 0, frame_width, sheet.height))

    box = find_eye(frame)
    sleeping = settle(close_eye(frame, box), box)

    # Four identical frames. The sheet contract is four frames wide for every pose, and a
    # sleeping animal holding still is the point - the breathing is applied by the app as a
    # scale, which costs nothing and stays smooth at every pet size.
    out = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    for index in range(FRAME_COUNT):
        out.paste(sleeping, (index * frame_width, 0), sleeping)

    destination = SPRITES / f"{species}-sleep.png"
    out.save(destination, optimize=True)

    content = np.where(np.array(sleeping)[:, :, 3].max(axis=1) > 8)[0]
    return destination, content[0] / sheet.height, (content[-1] + 1) / sheet.height


if __name__ == "__main__":
    print(f"{'species':<8} {'top':>7} {'bottom':>7}   file")
    for name in SPECIES:
        path, top, bottom = build(name)
        print(f"{name:<8} {top:>7.3f} {bottom:>7.3f}   {path.name}")
