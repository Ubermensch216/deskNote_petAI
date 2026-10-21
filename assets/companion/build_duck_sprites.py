"""Builds the duck's three sprite sheets from its two source drawings.

The other six species come with hand-made walk and rest strips, and their sleep strips are
computed from the rest strips by `build_sleep_sprites.py`. The duck arrived as two single
drawings instead - one standing, one lying down - so its strips are made from those:

  * `duck-walk.png`  the standing drawing, rocked from foot to foot. A waddle is the one gait that
                     is exactly a rotation about the feet, so no part of the drawing is redrawn or
                     squashed (see the proportion rule in docs/companion.md).
  * `duck-rest.png`  the same drawing breathing and, on one frame, blinking.
  * `duck-sleep.png` the lying drawing with both eyes shut. It is not run through
                     `build_sleep_sprites.py`: that script finds the eye by its specular highlight
                     and this duck's eyes are plain dots.

Every sheet keeps the contract the app relies on: four frames side by side, 543 x 724 each, the
animal's feet on the same floor line and its head at the same height as the other species, so the
head-relative ornaments, speech bubble and sleep effects land where they do for the rest.

Run it after changing either source drawing:

    python assets/companion/build_duck_sprites.py

Needs Pillow and NumPy. The output is committed, so nobody building the app has to run this.
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SOURCES = Path(__file__).resolve().parent / "duck"
SPRITES = ROOT / "src" / "DeskNote.App" / "Assets" / "Companion"

FRAME_COUNT = 4
FRAME_W, FRAME_H = 543, 724

# Measured off the other species' sheets: feet at 0.78 of the frame, standing head at 0.21,
# lying head at 0.36 (what the sleep effects and speech bubble are positioned against).
FLOOR = 0.78
STANDING_TOP = 0.21
LYING_TOP = 0.36

# The round eye dots in the source drawings, as (left, top, right, bottom) pixel boxes. The short
# dash and the squiggle above them are brow marks and stay as drawn.
STANDING_EYES = [(640, 351, 693, 401), (1092, 445, 1137, 495)]
LYING_EYES = [(694, 239, 739, 279), (236, 320, 278, 361)]

# The two drawings were inked in different greys (about 42 for the lying one, 82 for the standing
# one). Put the lying drawing on the standing drawing's ink so the duck does not change outline
# when it lies down.
LYING_INK, STANDING_INK = 42.0, 82.0
STANDING_INK_RGB = (82, 82, 76)

# Waddle: (tilt in degrees, lift in frame pixels). The two tilted frames each plant one foot; the
# frames between them are the hop across.
WALK = [(-6.0, 0), (0.0, 9), (6.0, 0), (0.0, 9)]

# Standing rest: (tilt, breathing scale, blink).
REST = [(0.0, 1.0, False), (1.4, 1.014, False), (0.0, 1.0, True), (-1.4, 1.014, False)]


def load(name):
    return Image.open(SOURCES / name).convert("RGBA")


def unify_ink(image):
    """Re-inks the lying drawing in the standing drawing's grey."""
    pixels = np.array(image).astype(np.float64)
    rgb = pixels[..., :3]
    neutral = (rgb.max(axis=-1) - rgb.min(axis=-1)) < 14
    value = rgb.mean(axis=-1)

    # An outline pixel is ink blended over white, so moving the ink moves it along that line. The
    # grey of the beak shadow sits between the two ends and is the same grey in both drawings.
    shifted = 255.0 - (255.0 - value) * ((255.0 - STANDING_INK) / (255.0 - LYING_INK))
    affected = neutral & ((value < 100) | (value > 170))
    delta = np.where(affected, shifted - value, 0.0)
    pixels[..., :3] = np.clip(rgb + delta[..., None], 0, 255)
    return Image.fromarray(pixels.astype(np.uint8), "RGBA")


def close_eyes(image, boxes):
    """Paints the round eye dots out and draws a shut lid in their place."""
    out = image.copy()
    draw = ImageDraw.Draw(out)
    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    lids = ImageDraw.Draw(overlay)

    for left, top, right, bottom in boxes:
        pad = 8
        draw.ellipse([left - pad, top - pad, right + pad, bottom + pad], fill=(255, 255, 255, 255))

        width, height = right - left, bottom - top
        centre_x, centre_y = (left + right) / 2, (top + bottom) / 2
        half = width * 0.62
        lids.arc(
            [centre_x - half, centre_y - height * 0.55, centre_x + half, centre_y + height * 0.45],
            start=15,
            end=165,
            fill=STANDING_INK_RGB + (255,),
            width=max(10, int(height * 0.32)),
        )

    return Image.alpha_composite(out, overlay.filter(ImageFilter.GaussianBlur(1.2)))


def trim(image):
    box = image.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox()
    return image.crop(box)


def place(sprite, height, tilt=0.0, lift=0, scale=1.0):
    """
    Puts one pose on a frame: rotated about its feet, scaled to `height`, standing on the floor.

    The sprite is padded first so the rotation cannot clip it, rotated at full resolution, and only
    then reduced, which keeps the outline as clean as the original drawing.
    """
    width, source_height = sprite.size
    pad = max(width, source_height)
    canvas = Image.new("RGBA", (width + 2 * pad, source_height + 2 * pad), (0, 0, 0, 0))
    canvas.paste(sprite, (pad, pad))

    pivot = (pad + width / 2, pad + source_height)
    rotated = canvas.rotate(tilt, resample=Image.BICUBIC, center=pivot)

    factor = height * scale / source_height
    reduced = rotated.resize(
        (round(rotated.width * factor), round(rotated.height * factor)), Image.LANCZOS
    )

    alpha = np.array(reduced)[..., 3]
    rows = np.where(alpha.max(axis=1) > 8)[0]
    columns = np.where(alpha.max(axis=0) > 8)[0]
    lowest = rows[-1] + 1
    centre = (columns[0] + columns[-1] + 1) / 2

    # Whatever the tilt did to the lowest point, the planted foot ends on the floor line, so the
    # pet never sinks through it or hovers above it by an amount that depends on the angle.
    frame = Image.new("RGBA", (FRAME_W, FRAME_H), (0, 0, 0, 0))
    offset = (round(FRAME_W / 2 - centre), round(FLOOR * FRAME_H - lift - lowest))
    frame.paste(reduced, offset, reduced)
    return frame


def sheet(frames):
    out = Image.new("RGBA", (FRAME_W * FRAME_COUNT, FRAME_H), (0, 0, 0, 0))
    for index, frame in enumerate(frames):
        out.paste(frame, (index * FRAME_W, 0), frame)
    return out


def bounds(image):
    rows = np.where(np.array(image)[..., 3].max(axis=1) > 8)[0]
    return rows[0] / image.height, (rows[-1] + 1) / image.height


def build():
    standing = trim(load("standing.png"))
    standing_closed = trim(close_eyes(load("standing.png"), STANDING_EYES))
    standing_height = (FLOOR - STANDING_TOP) * FRAME_H

    walk = sheet([place(standing, standing_height, tilt, lift) for tilt, lift in WALK])

    rest = sheet(
        [
            place(standing_closed if blink else standing, standing_height, tilt, 0, scale)
            for tilt, scale, blink in REST
        ]
    )

    # Closed on the original, mirrored afterwards: the drawing looks left and every other species
    # looks right, because the app flips the sprite when the pet walks the other way.
    lying = close_eyes(unify_ink(load("lying.png")), LYING_EYES).transpose(Image.FLIP_LEFT_RIGHT)
    lying = trim(lying)
    lying_height = (FLOOR - LYING_TOP) * FRAME_H
    sleeping = place(lying, lying_height)
    sleep = sheet([sleeping] * FRAME_COUNT)

    results = []
    for name, image in (("walk", walk), ("rest", rest), ("sleep", sleep)):
        destination = SPRITES / f"duck-{name}.png"
        image.save(destination, optimize=True)
        results.append((destination, image))
    return results


if __name__ == "__main__":
    print(f"{'file':<16} {'top':>7} {'bottom':>7}")
    for path, image in build():
        top, bottom = bounds(image.crop((0, 0, FRAME_W, FRAME_H)))
        print(f"{path.name:<16} {top:>7.3f} {bottom:>7.3f}")
