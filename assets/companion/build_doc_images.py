"""Builds the two pet pictures in docs/images/ that are not photographs of a window.

    rig-signatures.png  one frame of each of the fifteen signature moves
    cat-stretch.png     the cat's stretch sheet on the docs' background

The first reads the contact sheets that `tools/PetSheets` writes, so run that first:

    dotnet run --project tools/PetSheets -- contact <directory>
    python assets/companion/build_doc_images.py <directory>

Needs Pillow.
"""

import os
import sys

from PIL import Image

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
IMAGES = os.path.join(ROOT, 'docs', 'images')
BACKGROUND = (214, 226, 240)

CHARACTERS = ['goku', 'kidgoku', 'roshi', 'vegeta', 'piccolo']
CELL_WIDTH, CELL_HEIGHT = 640, 250


def signatures(contact_directory):
    """One row per character, one column per move, each the third look of the sheet's first row.

    By then the move is under way and the shout is on screen, which is the picture that says what
    the move is."""
    out = Image.new('RGB', (CELL_WIDTH * 3, CELL_HEIGHT * len(CHARACTERS)), BACKGROUND)
    for row, character in enumerate(CHARACTERS):
        for column in range(3):
            sheet = Image.open(os.path.join(contact_directory, f'sig_{character}_{column}.png')).convert('RGB')
            cell_w, cell_h = sheet.width // 4, sheet.height // 3
            left, top = cell_w * 2, cell_h
            cell = sheet.crop((left, top, left + cell_w, top + cell_h)).resize((CELL_WIDTH, CELL_HEIGHT))
            out.paste(cell, (column * CELL_WIDTH, row * CELL_HEIGHT))

    out = out.resize((out.width * 3 // 4, out.height * 3 // 4), Image.LANCZOS)
    out.save(os.path.join(IMAGES, 'rig-signatures.png'), optimize=True)


def cat_stretch():
    sheet = Image.open(os.path.join(ROOT, 'src', 'DeskNote.App', 'Assets', 'Companion', 'cat-stretch.png')).convert('RGBA')
    left, top, right, bottom = sheet.getchannel('A').point(lambda a: 255 if a > 16 else 0).getbbox()
    pad = 24
    box = (max(0, left - pad), max(0, top - pad), min(sheet.width, right + pad), min(sheet.height, bottom + pad))

    background = Image.new('RGBA', sheet.size, BACKGROUND + (255,))
    background.alpha_composite(sheet)
    out = background.crop(box).convert('RGB')
    out = out.resize((out.width * 2 // 3, out.height * 2 // 3), Image.LANCZOS)
    out.save(os.path.join(IMAGES, 'cat-stretch.png'), optimize=True)


if __name__ == '__main__':
    if len(sys.argv) != 2:
        sys.exit(__doc__)

    signatures(sys.argv[1])
    cat_stretch()
