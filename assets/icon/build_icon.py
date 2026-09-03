"""Generates the new DeskNote system logo and icons with transparent background.
Combines Sticky Note + Companion Pet + AI Sparkle into a clean, modern design.
"""

import math
import struct
import io
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
ASSETS_DIR = ROOT / "assets"
APP_ASSETS_DIR = ROOT / "src" / "DeskNote.App" / "Assets"

LOGO_PATH = ASSETS_DIR / "logo.png"
APP_LOGO_PATH = APP_ASSETS_DIR / "logo.png"
ICO_PATH = APP_ASSETS_DIR / "DeskNote.ico"
PREVIEW_PATH = ASSETS_DIR / "icon" / "preview.png"

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# Color Palette: Clean, warm, high-contrast
NOTE_BG = (255, 213, 79, 255)       # Warm Golden Amber
NOTE_SHADOW = (230, 180, 40, 255)   # Subtle outline / fold
EAR_INNER = (255, 171, 145, 255)     # Cute pastel peach inner ear
INK_DARK = (45, 40, 35, 255)        # Soft charcoal for face/lines
SPARKLE_MAIN = (124, 77, 255, 255)  # Vibrant AI Violet / Purple
SPARKLE_CORE = (187, 134, 252, 255) # Light lilac
HALO_WHITE = (255, 255, 255, 255)   # Crisp white contrast halo

SUPERSAMPLE = 4


def sparkle_polygon(cx, cy, radius, grow=0.0, power=2.8, steps=240):
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


def draw_logo(size=512):
    scale = SUPERSAMPLE
    s = size * scale
    image = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    def u(v):
        return v * s / 512.0

    # Small size adaptations for crisp visibility at 16~32px
    detailed = size >= 32
    tiny = size <= 20

    # 1. Outer Ears (behind the note body)
    left_ear = [
        (u(90), u(200)),
        (u(135), u(75)),
        (u(215), u(160)),
    ]
    right_ear = [
        (u(297), u(160)),
        (u(377), u(75)),
        (u(422), u(200)),
    ]

    for ear in [left_ear, right_ear]:
        draw.polygon(ear, fill=NOTE_BG)

    if not tiny:
        # Inner Ears (Pinkish)
        left_inner = [
            (u(110), u(185)),
            (u(140), u(105)),
            (u(195), u(165)),
        ]
        right_inner = [
            (u(317), u(165)),
            (u(372), u(105)),
            (u(402), u(185)),
        ]
        draw.polygon(left_inner, fill=EAR_INNER)
        draw.polygon(right_inner, fill=EAR_INNER)

    # 2. Main Note Body
    note_rect = [u(65), u(135), u(447), u(465)]
    body_radius = u(52) if not tiny else u(36)
    draw.rounded_rectangle(note_rect, radius=body_radius, fill=NOTE_BG)

    outline_w = max(1, round(u(7)))
    draw.rounded_rectangle(note_rect, radius=body_radius, outline=NOTE_SHADOW, width=outline_w)

    # 3. Friendly Pet Face (Eyes, Cheeks, Mouth)
    eye_y = u(255)
    eye_r = u(20) if not tiny else u(28)
    left_eye_x = u(175)
    right_eye_x = u(337)

    draw.ellipse([left_eye_x - eye_r, eye_y - eye_r, left_eye_x + eye_r, eye_y + eye_r], fill=INK_DARK)
    draw.ellipse([right_eye_x - eye_r, eye_y - eye_r, right_eye_x + eye_r, eye_y + eye_r], fill=INK_DARK)

    if detailed:
        # Catchlights
        dot_r = u(6)
        draw.ellipse([left_eye_x - u(5) - dot_r, eye_y - u(6) - dot_r, left_eye_x - u(5) + dot_r, eye_y - u(6) + dot_r], fill=HALO_WHITE)
        draw.ellipse([right_eye_x - u(5) - dot_r, eye_y - u(6) - dot_r, right_eye_x - u(5) + dot_r, eye_y - u(6) + dot_r], fill=HALO_WHITE)

        # Cheerful cheeks
        blush_r_x, blush_r_y = u(24), u(12)
        draw.ellipse([left_eye_x - u(28) - blush_r_x, eye_y + u(22) - blush_r_y, left_eye_x - u(28) + blush_r_x, eye_y + u(22) + blush_r_y], fill=(255, 170, 140, 180))
        draw.ellipse([right_eye_x + u(28) - blush_r_x, eye_y + u(22) - blush_r_y, right_eye_x + u(28) + blush_r_x, eye_y + u(22) + blush_r_y], fill=(255, 170, 140, 180))

    if not tiny:
        # Snout / Mouth
        nose_x, nose_y = u(256), u(280)
        nose_r = u(8)
        draw.ellipse([nose_x - nose_r, nose_y - nose_r, nose_x + nose_r, nose_y + nose_r * 0.8], fill=INK_DARK)

        mouth_w = max(1, round(u(6)))
        draw.arc([nose_x - u(26), nose_y + u(2), nose_x, nose_y + u(24)], start=0, end=180, fill=INK_DARK, width=mouth_w)
        draw.arc([nose_x, nose_y + u(2), nose_x + u(26), nose_y + u(24)], start=0, end=180, fill=INK_DARK, width=mouth_w)

    # 4. Note Lines / Checklist styling on the bottom
    if detailed:
        line_y1, line_y2 = u(360), u(408)
        line_h = max(1, round(u(10)))
        line_radius = line_h / 2

        box_s = u(18)
        draw.rounded_rectangle([u(125), line_y1 - box_s/2, u(125) + box_s, line_y1 + box_s/2], radius=u(4), outline=INK_DARK, width=max(1, round(u(4))))
        chk = [(u(128), line_y1), (u(133), line_y1 + u(5)), (u(140), line_y1 - u(5))]
        draw.line(chk, fill=SPARKLE_MAIN, width=max(1, round(u(4))))

        draw.rounded_rectangle([u(160), line_y1 - line_h/2, u(380), line_y1 + line_h/2], radius=line_radius, fill=INK_DARK)
        draw.rounded_rectangle([u(125), line_y2 - line_h/2, u(320), line_y2 + line_h/2], radius=line_radius, fill=INK_DARK)

    # 5. Glowing AI Sparkles (Top Right)
    sp1_x, sp1_y = u(410), u(105)
    sp1_r = u(72) if not tiny else u(90)
    halo1_grow = u(12) if not tiny else u(16)
    draw.polygon(sparkle_polygon(sp1_x, sp1_y, sp1_r, grow=halo1_grow), fill=HALO_WHITE)
    draw.polygon(sparkle_polygon(sp1_x, sp1_y, sp1_r), fill=SPARKLE_MAIN)

    if detailed:
        draw.polygon(sparkle_polygon(sp1_x, sp1_y, sp1_r * 0.45), fill=SPARKLE_CORE)
        sp2_x, sp2_y = u(325), u(55)
        sp2_r = u(34)
        draw.polygon(sparkle_polygon(sp2_x, sp2_y, sp2_r, grow=u(7)), fill=HALO_WHITE)
        draw.polygon(sparkle_polygon(sp2_x, sp2_y, sp2_r), fill=SPARKLE_MAIN)

    return image.resize((size, size), Image.Resampling.LANCZOS)


def bmp_entry(image):
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
    pad, gap = 16, 16
    width = pad * 2 + sum(i.width for i in images) + gap * (len(images) - 1)
    row = max(i.height for i in images)
    sheet = Image.new("RGBA", (width, pad * 3 + row * 2), (0, 0, 0, 0))

    draw = ImageDraw.Draw(sheet)
    draw.rounded_rectangle([0, 0, width, pad * 1.5 + row], radius=12, fill=(245, 245, 248, 255))
    draw.rounded_rectangle([0, pad * 1.5 + row, width, sheet.height], radius=12, fill=(30, 30, 36, 255))

    x = pad
    for image in images:
        sheet.alpha_composite(image, (x, pad + row - image.height))
        sheet.alpha_composite(image, (x, pad * 2 + row * 2 - image.height))
        x += image.width + gap

    sheet.save(path)


def main():
    ASSETS_DIR.mkdir(parents=True, exist_ok=True)
    APP_ASSETS_DIR.mkdir(parents=True, exist_ok=True)

    # 1. 512x512 High-Res Transparent PNG Logo
    logo_512 = draw_logo(512)
    logo_512.save(LOGO_PATH, format="PNG", optimize=True)
    logo_512.save(APP_LOGO_PATH, format="PNG", optimize=True)

    size_kb = LOGO_PATH.stat().st_size / 1024
    print(f"Generated {LOGO_PATH} ({size_kb:.1f} KB)")
    print(f"Generated {APP_LOGO_PATH} ({APP_LOGO_PATH.stat().st_size / 1024:.1f} KB)")

    # 2. Multi-size ICO
    icons = [draw_logo(sz) for sz in SIZES]
    write_ico(icons, ICO_PATH)
    ico_size_kb = ICO_PATH.stat().st_size / 1024
    print(f"Generated {ICO_PATH} ({ico_size_kb:.1f} KB)")

    # 3. Preview Sheet
    write_preview(icons, PREVIEW_PATH)
    print(f"Generated {PREVIEW_PATH}")


if __name__ == "__main__":
    main()

