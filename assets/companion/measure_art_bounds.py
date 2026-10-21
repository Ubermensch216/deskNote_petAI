"""Prints the table in `PetArtBounds.cs` from the walk sheets.

The pet's window is placed so that what is drawn touches the wall, the ceiling or the floor, and
that needs to know how much of each 543 x 724 frame the drawing really fills. Run it after adding
a species or redrawing a `*-walk.png`, and paste the result over the switch in
`src/DeskNote.Companion.Core/Motion/PetArtBounds.cs`:

    python assets/companion/measure_art_bounds.py

Horizontal extent is the union of the four frames, the head is the highest any frame reaches, and
the feet are the usual floor line (the median over the frames) rather than one stray pixel.

Needs Pillow and NumPy.
"""

from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SPRITES = ROOT / "src" / "DeskNote.App" / "Assets" / "Companion"

# C# enum member, asset key
SPECIES = [
    ("Rabbit", "rabbit"),
    ("Cat", "cat"),
    ("Dog", "dog"),
    ("FennecFox", "fennec"),
    ("Otter", "otter"),
    ("Dragon", "dragon"),
    ("Monkey", "monkey"),
    ("Duck", "duck"),
]
FRAME_COUNT = 4


def measure(key):
    sheet = Image.open(SPRITES / f"{key}-walk.png").convert("RGBA")
    frame_width = sheet.width // FRAME_COUNT
    lefts, tops, rights, bottoms = [], [], [], []
    for index in range(FRAME_COUNT):
        alpha = np.array(sheet.crop((index * frame_width, 0, (index + 1) * frame_width, sheet.height)))[..., 3] > 8
        rows = np.where(alpha.any(axis=1))[0]
        columns = np.where(alpha.any(axis=0))[0]
        lefts.append(columns[0] / frame_width)
        rights.append((columns[-1] + 1) / frame_width)
        tops.append(rows[0] / sheet.height)
        bottoms.append((rows[-1] + 1) / sheet.height)
    return min(lefts), min(tops), max(rights), float(np.median(bottoms))


if __name__ == "__main__":
    for member, key in SPECIES:
        left, top, right, bottom = measure(key)
        print(f"        CompanionPetKind.{member} => new PetInk({left:.3f}, {top:.3f}, {right:.3f}, {bottom:.3f}),")
