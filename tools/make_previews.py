import os
from PIL import Image, ImageDraw, ImageFont

# Composes the README previews in imgs/ from the raw widget frames that
# tests/HASNetMonitor.Tests/PreviewFrames.cs renders with the app's own renderer.
#
#   GALLERY_OUT=<abs path> dotnet test --filter FullyQualifiedName~PreviewFrames
#   python tools/make_previews.py            (frames from _gallery by default)
#
# Needs Pillow:  py -m pip install pillow

SRC = os.environ.get("PREVIEW_FRAMES", "_gallery")
OUT = "imgs"
os.makedirs(OUT, exist_ok=True)

BG      = (13, 17, 23)
PANEL   = (22, 27, 34)
BORDER  = (48, 54, 61)
TEXT    = (230, 237, 243)
MUTED   = (139, 148, 158)
FRAME   = (36, 42, 51)

FONT_DIR = "C:/Windows/Fonts"

def _font(name, size):
    p = os.path.join(FONT_DIR, name)
    return ImageFont.truetype(p, size) if os.path.exists(p) else ImageFont.load_default()

def bold(sz): return _font("seguisb.ttf", sz)
def reg(sz):  return _font("segoeui.ttf", sz)

def rrect(d, box, r, fill, outline=None, width=1):
    d.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)

def art(name):
    return Image.open(os.path.join(SRC, name + ".png")).convert("RGBA")

def save(img, name):
    p = os.path.join(OUT, name)
    img.convert("RGB").save(p, optimize=True)
    print(f"{name:22s} {img.width}x{img.height}  {os.path.getsize(p)//1024} KB")

# Every render is pasted whole: the strip is sized from the art, so nothing is cropped.
def strip(name, pad=14, bg=(17, 22, 30)):
    a = art(name)
    W, H = a.width + pad * 2, a.height + pad * 2
    s = Image.new("RGBA", (W, H), bg)
    d = ImageDraw.Draw(s)
    rrect(d, (0, 0, W - 1, H - 1), 8, None, (55, 62, 72), 1)
    s.paste(a, (pad, pad), a)
    return s

def tile(name, title, sub, pad=14, cap=48, bg=(17, 22, 30)):
    s = strip(name, pad, bg)
    t = Image.new("RGBA", (s.width + 24, s.height + cap), (0, 0, 0, 0))
    d = ImageDraw.Draw(t)
    rrect(d, (0, 0, t.width - 1, t.height - 1), 12, PANEL, BORDER, 1)
    t.paste(s, (12, 12))
    d.text((16, s.height + 20), title, font=bold(15), fill=TEXT)
    d.text((16, s.height + 38), sub, font=reg(11), fill=MUTED)
    return t

def grid(tiles, cols, gap, name, outer=18, target_width=1200):
    cw = max(t.width for t in tiles)
    ch = max(t.height for t in tiles)
    rows = (len(tiles) + cols - 1) // cols
    W = cols * cw + (cols + 1) * gap
    H = rows * ch + (rows + 1) * gap
    im = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(im)
    rrect(d, (0, 0, W - 1, H - 1), outer, None, FRAME, 1)
    for i, t in enumerate(tiles):
        r, c = divmod(i, cols)
        im.alpha_composite(t, (gap + c * (cw + gap), gap + r * (ch + gap)))

    # Every grid lands on the same width so the README's images line up. Only ever shrink:
    # upscaling a render just blurs it, so a naturally narrower grid keeps its sharpness.
    if target_width and im.width > target_width:
        scale = target_width / im.width
        im = im.resize((target_width, max(1, round(im.height * scale))), Image.Resampling.LANCZOS)
    save(im, name)


def fit_text(d, text, fnt, max_width):
    """Text width, raising instead of drawing past the edge of its panel."""
    w = d.textlength(text, font=fnt)
    if w > max_width:
        raise SystemExit(f"text too wide ({int(w)} > {max_width}): {text!r}")
    return w


def hero():
    a = art("hero")
    W = 1200
    padx, pady, head = 56, 44, 78
    H = a.height + pady * 2 + head
    im = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(im)
    for y in range(H):
        t = y / max(1, H - 1)
        d.line([(0, y), (W, y)], fill=(int(13 + 14 * (1 - t)), int(17 + 18 * (1 - t)), int(23 + 34 * (1 - t))))
    rrect(d, (0, 0, W - 1, H - 1), 18, None, BORDER, 1)

    inner = W - padx * 2
    d.text((padx, pady), "HASNetMonitor", font=bold(34), fill=TEXT)
    fit_text(d, "HASNetMonitor", bold(34), inner)
    tag = "Live upload and download speed on your taskbar, in ten looks"
    fit_text(d, tag, reg(16), inner)
    d.text((padx, pady + 44), tag, font=reg(16), fill=MUTED)

    s = strip("hero", pad=12)
    if s.width > inner:
        raise SystemExit(f"hero art too wide: {s.width} > {inner}")
    # sit the strip against the right edge, matching the banner's padding
    im.paste(s, (W - padx - s.width, pady + head - 6))
    save(im, "hero.png")


DESIGNS = [
    ("card_Bars",     "Bars",     "Compact bars, the default"),
    ("card_Sidebar",  "Sidebar",  "Bars behind the figures"),
    ("card_Segments", "Segments", "Blocky level meter"),
    ("card_Minimal",  "Minimal",  "Figures only"),
    ("card_Neon",     "Neon",     "Glowing outline"),
    ("card_Dots",     "Dots",     "Dot matrix meter"),
]

ARROWS = [
    ("arrow_Classic", "Classic", "Font glyph"),
    ("arrow_Solid",   "Solid",   "Filled glyph"),
    ("arrow_Small",   "Small",   "Compact glyph"),
    ("arrow_Double",  "Double",  "Double glyph"),
    ("arrow_Chevron", "Chevron", "Angled glyph"),
    ("arrow_Bar",     "Bar",     "Drawn geometry"),
]

TEXTS = [
    ("text_white",    "Automatic",       "Taskbar text colour"),
    ("text_bar",      "Same as the bar", "Figure takes its bar colour"),
    ("text_contrast", "Custom colour",   "Any colour you pick"),
]

def designs():
    grid([tile(k, t, s) for k, t, s in DESIGNS], 3, 18, "designs.png")

def arrows():
    # Arrows need room: the drawn Bar arrow is the widest thing in the app.
    grid([tile(k, t, s, pad=18) for k, t, s in ARROWS], 3, 18, "arrows.png")

def text_colours():
    grid([tile(k, t, s) for k, t, s in TEXTS], 3, 18, "text-colours.png")

def themes():
    tiles = []
    for key, title, shade, panel in (
        ("theme_dark",  "Dark taskbar",  (17, 22, 30),  (22, 27, 34)),
        ("theme_light", "Light taskbar", (236, 239, 244), (240, 242, 246)),
    ):
        tiles.append(tile(key, title, "Follows the taskbar either way", bg=shade))
    grid(tiles, 2, 18, "themes.png")


def chips():
    # The four chips (CPU, GPU, RAM, disk) with a bar level, each in its own colour.
    grid([tile("chips_bar", "System-stats chips", "CPU, GPU, RAM and disk, each with a bar level")],
         1, 18, "chips.png")


hero()
designs()
arrows()
text_colours()
themes()
chips()

# Report the widths so a too-narrow image is obvious rather than silently ugly.
print("\nwidths:", {n: Image.open(os.path.join(OUT, n)).width for n in sorted(os.listdir(OUT))})
