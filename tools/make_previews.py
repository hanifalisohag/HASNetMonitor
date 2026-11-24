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
    padx, pady, head = 56, 44, 78

    # The banner is as wide as the README's other images, never narrower, so the taskbar
    # strip below has room for the Start button, the widget and the real system tray.
    W = 1200
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

    # The widget sits on a real Windows 11 taskbar: Start on the left, the live figures
    # where the app puts them, and the system tray - Wi-Fi, volume, battery, clock - on
    # the right, so the preview shows the widget in the place it actually lives.
    bar = taskbar(a, W - padx * 2)
    im.paste(bar, (padx, pady + head - 6), bar)
    save(im, "hero.png")


# Windows 11 taskbar colours, dark theme.
BAR_BG    = (32, 32, 32)
BAR_TOP   = (44, 44, 44)
BAR_ICON  = (243, 243, 243)
BAR_MUTED = (200, 200, 200)

# Segoe MDL2 Assets glyphs, the ones the real tray draws.
G_START   = "\uE700"   # Windows logo
G_SEARCH  = "\uE721"
G_TASKVIEW = "\uE7C4"
G_CHEVRON = "\uE70D"   # the tray's "show hidden icons" arrow
G_WIFI    = "\uE701"
G_VOLUME  = "\uE767"
G_BATTERY = "\uE83F"


def _mdl2(sz):
    return _font("segmdl2.ttf", sz)


def taskbar(widget, width, height=96):
    """A Windows 11 taskbar strip: Start and friends left, the widget, the tray right."""
    bar = Image.new("RGBA", (width, height), BAR_BG)
    d = ImageDraw.Draw(bar)

    # A hairline highlight along the top edge, as the real taskbar has.
    d.line([(0, 0), (width, 0)], fill=BAR_TOP)

    icon = _mdl2(30)
    cy = height // 2

    def glyph(ch, x, fnt=None, fill=BAR_ICON):
        f = fnt or icon
        w = d.textlength(ch, font=f)
        d.text((x, cy), ch, font=f, fill=fill, anchor="lm")
        return w

    # Left cluster: Start, search, task view - the Windows 11 arrangement.
    x = 22
    x += glyph(G_START, x) + 26
    x += glyph(G_SEARCH, x) + 26
    x += glyph(G_TASKVIEW, x) + 26
    x = round(x)

    # The widget, scaled to sit inside the taskbar with a little breathing room.
    art_h = height - 26
    scale = art_h / widget.height
    w = widget.resize((max(1, round(widget.width * scale)), art_h), Image.Resampling.LANCZOS)
    bar.alpha_composite(w, (x, (height - art_h) // 2))

    # Right cluster: the tray. The clock is two stacked lines, so it is drawn by hand.
    clock = reg(15)
    date = reg(13)
    time_s, date_s = "9:41 PM", "6/14/2025"
    tw = max(d.textlength(time_s, font=clock), d.textlength(date_s, font=date))
    tray_w = 30 + 30 + 30 + 22 + tw + 22
    tx = round(width - tray_w)

    tx += glyph(G_CHEVRON, tx, _mdl2(20), BAR_MUTED) + 22
    tx += glyph(G_WIFI, tx) + 22
    tx += glyph(G_VOLUME, tx) + 22
    tx += glyph(G_BATTERY, tx) + 22

    d.text((tx, cy - 9), time_s, font=clock, fill=BAR_ICON, anchor="lm")
    d.text((tx, cy + 9), date_s, font=date, fill=BAR_MUTED, anchor="lm")

    return bar



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
