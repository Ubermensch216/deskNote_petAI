"""Draws the DeskNote application icon and packs it into an .ico.

The icon is a sticky note (the app's core object, in the default note yellow) with an AI
sparkle breaking out of its top-right corner. Shapes are drawn per target size rather than
scaled down from one master, because the 16px tray rendering needs fewer, fatter strokes
than the 256px shell rendering to stay readable.

    python assets/icon/build_icon.py
"""

import math
import struct
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
ICO_PATH = ROOT / "src" / "DeskNote.App" / "Assets" / "DeskNote.ico"
PREVIEW_PATH = Path(__file__).resolve().parent / "preview.png"

# Sizes Windows asks for: tray/menus (16-24), title bars and Alt-Tab (32-48),
# large shell views (64-256).
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

NOTE_FILL = (255, 202, 64, 255)
NOTE_EDGE = (222, 158, 30, 255)
INK = (58, 48, 32, 255)
SPARKLE = (108, 75, 240, 255)
HALO = (255, 255, 255, 255)

SUPERSAMPLE = 8


def sparkle_points(cx, cy, radius, grow=0.0, power=3.0, steps=480):
    """A four-point sparkle: an astroid, so the sides stay concave and the tips stay sharp.

    `grow` fattens the outline by a constant distance instead of scaling the whole shape,
    so the halo keeps the arms slim rather than swelling them into a cross.
    """
    points = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        c, s = math.cos(t), math.sin(t)
        x = math.copysign(abs(c) ** power, c)
        y = math.copysign(abs(s) ** power, s)
        r = math.hypot(x, y) * radius + grow
        angle = math.atan2(y, x)
        points.append((cx + r * math.cos(angle), cy + r * math.sin(angle)))
    return points


def draw_icon(size):
    """Renders one icon at `size`, supersampled so the curves stay smooth."""
    scale = SUPERSAMPLE
    s = size * scale
    image = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    def u(value):
        """Design-space (256 units) to device pixels."""
        return value * s / 256

    # Small renderings drop the third text line and the second sparkle: at 16px those
    # details collapse into noise and blur the two shapes that carry the meaning.
    detailed = size >= 32

    note = [u(22), u(44), u(208), u(230)]
    draw.rounded_rectangle(note, radius=u(32), fill=NOTE_FILL, outline=NOTE_EDGE, width=max(1, round(u(5))))

    lines = [(u(122), u(122)), (u(160), u(100)), (u(198), u(72))]
    if not detailed:
        lines = [(u(130), u(122)), (u(178), u(90))]

    for y, width in lines:
        thickness = u(18) if detailed else u(22)
        draw.rounded_rectangle(
            [u(52), y - thickness / 2, u(52) + width, y + thickness / 2],
            radius=thickness / 2,
            fill=INK,
        )

    if detailed:
        # The little companion sparkle only reads as "sparkles" when both are visible.
        draw.polygon(sparkle_points(u(132), u(34), u(22), grow=u(7)), fill=HALO)
        draw.polygon(sparkle_points(u(132), u(34), u(22)), fill=SPARKLE)

    # The halo cuts the sparkle free of the note edge it overlaps, in both themes.
    big = u(52) if detailed else u(60)
    draw.polygon(sparkle_points(u(192), u(64), big, grow=u(8)), fill=HALO)
    draw.polygon(sparkle_points(u(192), u(64), big), fill=SPARKLE)

    return image.resize((size, size), Image.LANCZOS)


def bmp_entry(image):
    """An icon directory entry's BMP payload: a 32bpp bottom-up DIB plus an empty AND mask."""
    width, height = image.size
    pixels = image.load()

    body = bytearray()
    for y in range(height - 1, -1, -1):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            body += bytes((b, g, r, a))

    mask_stride = ((width + 31) // 32) * 4
    body += bytes(mask_stride * height)

    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, len(body), 0, 0, 0, 0)
    return bytes(header) + bytes(body)


def write_ico(images, path):
    entries = []
    for image in images:
        width, height = image.size
        if width >= 128:
            # Large frames go in PNG-compressed, which is what keeps the file small.
            import io

            buffer = io.BytesIO()
            image.save(buffer, format="PNG")
            payload = buffer.getvalue()
        else:
            payload = bmp_entry(image)
        entries.append((width, height, payload))

    offset = 6 + 16 * len(entries)
    directory = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    body = bytearray()

    for width, height, payload in entries:
        directory += struct.pack(
            "<BBBBHHII",
            0 if width >= 256 else width,
            0 if height >= 256 else height,
            0, 0, 1, 32, len(payload), offset,
        )
        body += payload
        offset += len(payload)

    path.write_bytes(bytes(directory) + bytes(body))


def write_preview(images, path):
    """A contact sheet of every size on both a light and a dark strip, for eyeballing."""
    pad, gap = 16, 16
    width = pad * 2 + sum(i.width for i in images) + gap * (len(images) - 1)
    row = max(i.height for i in images)
    sheet = Image.new("RGB", (width, pad * 3 + row * 2), (245, 245, 247))
    ImageDraw.Draw(sheet).rectangle([0, pad * 2 + row, width, sheet.height], fill=(32, 32, 36))

    x = pad
    for image in images:
        sheet.paste(image, (x, pad + row - image.height), image)
        sheet.paste(image, (x, pad * 2 + row * 2 - image.height), image)
        x += image.width + gap

    sheet.save(path)


images = [draw_icon(size) for size in SIZES]
ICO_PATH.parent.mkdir(parents=True, exist_ok=True)
write_ico(images, ICO_PATH)
write_preview(images, PREVIEW_PATH)
print(f"wrote {ICO_PATH.relative_to(ROOT)} ({ICO_PATH.stat().st_size:,} bytes)")
print(f"wrote {PREVIEW_PATH.relative_to(ROOT)}")
