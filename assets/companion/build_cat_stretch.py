"""Draws the cat's stretch: a sheet of the cat bowing forward with its rear in the air.

Every other rest pose is a transform of the sitting artwork, and a stretch is the one pose that
cannot be: a cat stretching puts its front legs out along the floor, drops its chest and head and
raises its rear, and none of that is a scale or a rotation of a cat sitting up. Redrawing it by
hand is not on offer either (nobody here is going to paint a calico cat in this style), so it is
derived the way `build_sleep_sprites.py` derives sleep: from the existing artwork, by an
operation that is the same every time it is run.

The artwork used is the third frame of the walk sheet, the one where the cat is side-on with its
legs spread: it already has a front leg reaching forward, a hind leg trailing behind and the
body horizontal, so it needs bending rather than inventing. The bend is a thin-plate-spline
warp. A handful of landmarks on the cat (head, chest, shoulder, hips, paws, tail) are moved where
a stretching cat would have them, and the spline carries everything between them smoothly, so
there are no cut lines through the fur and the outline stays one unbroken stroke. What moves:

  * the whole body is tipped nose-down about the chest, which lifts the rear;
  * the head is turned further down and carried toward the floor, with its proportions untouched
    (the landmarks on it move rigidly, so the spline has nothing to squash);
  * the front leg is laid flat along the floor, reaching far forward;
  * the hind legs are drawn straight down from the raised hips to the floor.

Four frames, from a mild lean to the full stretch, because the app plays them in order and back
(`PetStretchArt`). The deep ones have the eye shut, in the same way as the sleep sheet: the cat is
blissful, not startled. The last frame is a touch deeper than the one before, and the app flips
between the two during the hold, which is the little tremble a real stretch has.

Run it after changing `cat-walk.png`:

    python assets/companion/build_cat_stretch.py

Needs Pillow and NumPy. The output is committed, so nobody building the app has to run this.
"""

import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from build_sleep_sprites import eye_mask, find_eye, ink_colour, inpaint

ROOT = Path(__file__).resolve().parents[2]
SPRITES = ROOT / "src" / "DeskNote.App" / "Assets" / "Companion"

FRAME_COUNT = 4
WALK_FRAME = 2

# How far each frame is into the stretch. 1.0 is the full pose; the last is a little past it.
STRENGTH = [0.38, 0.72, 1.0, 1.07]

# Which of them have the eye shut.
EYES_SHUT = [False, False, True, True]

# Empty space around the walk frame while it is bent, so that nothing leaves the picture.
PAD_X, PAD_Y = 250, 150

# Landmarks on the walk frame (x, y in its own pixels).
LANDMARKS = {
    # head
    "nose": (497, 333), "forehead": (400, 205), "earL": (355, 172), "earR": (468, 160),
    "headBack": (268, 315), "chin": (440, 395), "cheek": (350, 375), "eye": (430, 315),
    "mouth": (472, 352), "jaw": (452, 384), "cheekR": (478, 300), "crown": (340, 250),
    "headTop": (440, 212),
    # neck and chest
    "chest": (395, 425), "neck": (330, 395),
    # torso
    "back1": (200, 350), "back2": (270, 340), "butt": (125, 405),
    "belly1": (230, 490), "belly2": (330, 490), "mid": (260, 420),
    # tail
    "tailBase": (115, 375), "tailTop": (150, 242), "tailOut": (68, 300), "tailTip": (178, 262),
    # the reaching front leg
    "shoulder": (350, 480), "fKnee": (410, 520), "fPaw": (455, 552),
    # the trailing hind leg, and the one under the belly
    "hHip": (150, 465), "hKnee": (95, 500), "hPaw": (55, 525),
    "nPaw": (235, 545), "nHip": (215, 480),
}

HEAD = ("nose", "forehead", "earL", "earR", "headBack", "chin", "cheek", "eye", "chest", "neck",
        "mouth", "jaw", "cheekR", "crown", "headTop")
TORSO = ("back1", "back2", "butt", "belly1", "belly2", "mid", "tailBase", "tailTop", "tailOut",
         "tailTip", "nHip")
MIDDLE = ("mid", "belly2", "back2")

# The full pose.
PIVOT = (350, 505)       # the chest, low down: the body tips about here
TILT = 22.0              # degrees, clockwise on the screen: nose down, rear up
HEAD_EXTRA = 16.0        # the head goes further down than the body does
DROP = 36.0              # how far the front of the cat is carried toward the floor
FLOOR = 556


def _rotate(point, centre, degrees):
    theta = math.radians(degrees)
    x, y = point[0] - centre[0], point[1] - centre[1]
    return (centre[0] + x * math.cos(theta) - y * math.sin(theta),
            centre[1] + x * math.sin(theta) + y * math.cos(theta))


def full_pose():
    """Where every landmark goes in the finished stretch."""
    target = {}
    for name in TORSO:
        target[name] = _rotate(LANDMARKS[name], PIVOT, TILT)

    neck = _rotate(LANDMARKS["neck"], PIVOT, TILT)
    for name in HEAD:
        moved = _rotate(_rotate(LANDMARKS[name], PIVOT, TILT), neck, HEAD_EXTRA)
        target[name] = (moved[0], moved[1] + DROP)
    for name in MIDDLE:
        target[name] = (target[name][0], target[name][1] + DROP * 0.5)

    # The front leg, laid flat along the floor and reaching a long way forward.
    shoulder = _rotate(LANDMARKS["shoulder"], PIVOT, TILT)
    shoulder = (shoulder[0], shoulder[1] + DROP)
    target["shoulder"] = shoulder
    target["fKnee"] = (shoulder[0] + 100, FLOOR - 5)
    target["fPaw"] = (shoulder[0] + 205, FLOOR)

    # The hind legs, straight down from the raised hips to the floor.
    hip = _rotate(LANDMARKS["hHip"], PIVOT, TILT)
    target["hHip"] = (hip[0] + 10, hip[1] + 10)
    target["hKnee"] = (hip[0] + 8, (hip[1] + FLOOR) / 2)
    target["hPaw"] = (hip[0] + 5, FLOOR - 1)
    target["nPaw"] = (LANDMARKS["nPaw"][0], FLOOR - 4)
    return target


def _fit(targets, sources, lam=1e-3):
    """A thin-plate spline that takes the target points to where the pixels come from."""
    n = len(targets)
    d = np.linalg.norm(targets[:, None, :] - targets[None, :, :], axis=2)
    kernel = np.where(d > 0, d * d * np.log(d + 1e-12), 0.0) + lam * np.eye(n)
    affine = np.hstack([np.ones((n, 1)), targets])
    system = np.zeros((n + 3, n + 3))
    system[:n, :n] = kernel
    system[:n, n:] = affine
    system[n:, :n] = affine.T
    rhs = np.zeros((n + 3, 2))
    rhs[:n] = sources - targets
    return np.linalg.solve(system, rhs)


def _source_of(targets, weights, points):
    n = len(targets)
    d = np.linalg.norm(points[:, None, :] - targets[None, :, :], axis=2)
    kernel = np.where(d > 0, d * d * np.log(d + 1e-12), 0.0)
    return weights[n, :] + points @ weights[n + 1:n + 3, :] + kernel @ weights[:n, :] + points


def warp(frame, targets, sources):
    """Resamples `frame` so that the pixel at each source point ends up at its target point."""
    pixels = np.array(frame).astype(np.float64)
    alpha = pixels[..., 3:4] / 255.0

    # Interpolating colour that has not been multiplied by alpha drags the black of transparent
    # pixels into every edge, and the outline goes grey.
    premultiplied = np.concatenate([pixels[..., :3] * alpha, alpha * 255.0], axis=2)
    height, width = pixels.shape[:2]
    weights = _fit(targets, sources)

    ys, xs = np.mgrid[0:height, 0:width]
    grid = np.stack([xs.ravel(), ys.ravel()], axis=1).astype(np.float64)
    out = np.zeros((height * width, 4))
    for start in range(0, len(grid), 40000):
        source = _source_of(targets, weights, grid[start:start + 40000])
        x = np.clip(source[:, 0], 0, width - 1.001)
        y = np.clip(source[:, 1], 0, height - 1.001)
        x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
        fx, fy = (x - x0)[:, None], (y - y0)[:, None]
        out[start:start + 40000] = (
            premultiplied[y0, x0] * (1 - fx) * (1 - fy)
            + premultiplied[y0, x0 + 1] * fx * (1 - fy)
            + premultiplied[y0 + 1, x0] * (1 - fx) * fy
            + premultiplied[y0 + 1, x0 + 1] * fx * fy
        )

    out = out.reshape(height, width, 4)
    covered = out[..., 3:4]
    colour = np.where(covered > 1, out[..., :3] / np.maximum(covered / 255.0, 1e-6), 0)
    result = np.concatenate([np.clip(colour, 0, 255), np.clip(covered, 0, 255)], axis=2)
    return Image.fromarray(result.astype(np.uint8), "RGBA")


def bent(walk, strength):
    """The walk frame, `strength` of the way into the stretch, on a padded canvas."""
    canvas = Image.new("RGBA", (walk.width + 2 * PAD_X, walk.height + PAD_Y), (0, 0, 0, 0))
    canvas.paste(walk, (PAD_X, PAD_Y))

    goal = full_pose()
    names = list(LANDMARKS)
    offset = np.array([PAD_X, PAD_Y], dtype=float)
    sources = np.array([LANDMARKS[n] for n in names], dtype=float)
    targets = np.array([goal[n] for n in names], dtype=float)
    targets = sources + (targets - sources) * strength

    # Anchors keep the empty corners and the bottom edge where they are, so the spline has
    # nothing to do out there.
    width, height = canvas.size
    anchors = np.array([
        (0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1),
        (width / 2, height - 1), (width / 4, height - 1), (3 * width / 4, height - 1),
        (width / 2, 5), (5, height / 2), (width - 6, height / 2),
    ], dtype=float)
    return warp(
        canvas,
        np.vstack([targets + offset, anchors]),
        np.vstack([sources + offset, anchors]),
    )


def shut_eye(frame, box, tilt):
    """
    Takes the open eye out and draws a shut one, turned with the head.

    The shut eye is the sleep sheet's: a curve in the artwork's own ink, with two lashes. Here the
    head is tipped nose-down, and a curve left level on a tipped face reads as a smile that has
    slipped, so the curve is turned by as much as the head is.
    """
    ink = ink_colour(frame, box)
    out = inpaint(frame, eye_mask(frame, box))

    x0, y0, x1, y1 = box
    eye_w, eye_h = x1 - x0 + 1, y1 - y0 + 1
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2 + eye_h * 0.06
    half = eye_w * 0.52
    stroke = max(3, int(eye_h * 0.13))

    overlay = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    draw.arc([cx - half, cy - eye_h * 0.42, cx + half, cy + eye_h * 0.46],
             start=18, end=162, fill=ink + (255,), width=stroke)
    for lift, length in ((0.75, 0.30), (0.30, 0.26)):
        # Where the curve ends, so that the lashes grow out of it instead of floating beside it.
        ax, ay = cx - half * 0.95, cy + eye_h * 0.156
        draw.line([ax, ay, ax - eye_w * length * 0.55, ay - eye_h * length * lift],
                  fill=ink + (255,), width=max(2, stroke - 1))

    # PIL turns anticlockwise for a positive angle; the head went clockwise.
    overlay = overlay.rotate(-tilt, center=(cx, cy), resample=Image.BICUBIC)
    return Image.alpha_composite(out, overlay.filter(ImageFilter.GaussianBlur(0.7)))


def content_box(image, threshold=8):
    pixels = np.array(image)[..., 3] > threshold
    rows = np.where(pixels.any(axis=1))[0]
    columns = np.where(pixels.any(axis=0))[0]
    return columns[0], rows[0], columns[-1] + 1, rows[-1] + 1


def build():
    sheet = Image.open(SPRITES / "cat-walk.png").convert("RGBA")
    frame_width = sheet.width // FRAME_COUNT
    walk = sheet.crop((WALK_FRAME * frame_width, 0, (WALK_FRAME + 1) * frame_width, sheet.height))

    # Where the walking cat's feet are, so the stretching one stands on the same line and the pet
    # does not hop when it stretches.
    _, _, _, floor = content_box(walk)

    frames = [bent(walk, strength) for strength in STRENGTH]

    # The rear end stays where it is and the front reaches forward, which is what a stretching cat
    # does: every frame is placed by its left edge, and the deepest frame, the widest, is the one
    # centred on the picture. Standing on the floor line, so the pet does not hop.
    left, _, right, bottom = content_box(frames[-1])
    anchor = int(round((frame_width - (right - left)) / 2))
    shift_y = floor - bottom

    out = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    for index, frame in enumerate(frames):
        if EYES_SHUT[index]:
            frame = shut_eye(frame, find_eye(frame), (TILT + HEAD_EXTRA) * STRENGTH[index])

        frame_left = content_box(frame)[0]
        placed = Image.new("RGBA", (frame_width, sheet.height), (0, 0, 0, 0))
        placed.paste(frame, (anchor - frame_left, shift_y), frame)
        out.paste(placed, (index * frame_width, 0))

    destination = SPRITES / "cat-stretch.png"
    out.save(destination, optimize=True)
    return destination, content_box(out.crop((2 * frame_width, 0, 3 * frame_width, sheet.height)))


if __name__ == "__main__":
    path, box = build()
    print(f"wrote {path.name}; the full stretch fills x {box[0]}-{box[2]}, y {box[1]}-{box[3]} of its frame")
