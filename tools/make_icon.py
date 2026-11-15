"""Generates Assets/HASNetMonitor.ico: the app's green-download / red-upload mark."""
import io
import struct
from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
# From this size up the frame is stored as PNG. The small frames stay DIB for
# compatibility, and compressing the big ones cuts the file most of the way down.
PNG_FROM = 128
DOWN = (46, 204, 113, 255)   # green, same as the tray icon
UP = (231, 76, 60, 255)      # red, same as the tray icon
STROKE = 1.8                 # stroke width on the 16-unit design grid


def on_grid(x, y, edge):
    return (x * edge / 16.0, y * edge / 16.0)


def thick_line(draw, points, fill, width, edge):
    """Polyline with round caps and joins, drawn on the 16-unit design grid."""
    scaled = [on_grid(x, y, edge) for x, y in points]
    draw.line(scaled, fill=fill, width=max(1, int(round(width))), joint="curve")
    radius = width / 2.0
    for x, y in scaled:
        draw.ellipse([x - radius, y - radius, x + radius, y + radius], fill=fill)


def render(edge):
    """Draws the icon at the requested size, supersampled for clean edges."""
    supersample = 8
    canvas = edge * supersample
    image = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    width = STROKE * canvas / 16.0

    # Download: a downward arrow on the left.
    thick_line(draw, [(1.7, 9.5), (4.0, 13.0), (6.3, 9.5)], DOWN, width, canvas)
    thick_line(draw, [(4.0, 1.5), (4.0, 12.2)], DOWN, width, canvas)

    # Upload: an upward arrow on the right.
    thick_line(draw, [(9.7, 6.5), (12.0, 3.0), (14.3, 6.5)], UP, width, canvas)
    thick_line(draw, [(12.0, 3.8), (12.0, 14.5)], UP, width, canvas)

    # Image.LANCZOS is Pillow's long-standing alias for Image.Resampling.LANCZOS and still
    # works, but Pillow's type stubs only declare the newer name.
    return image.resize((edge, edge), Image.LANCZOS)  # pyright: ignore[reportAttributeAccessIssue]


def bmp_entry(image, edge):
    """One ICO image as a 32bpp bottom-up DIB plus its 1bpp AND mask."""
    pixels = image.load()

    xor = bytearray()
    for y in range(edge - 1, -1, -1):
        for x in range(edge):
            r, g, b, a = pixels[x, y]
            # 32bpp DIBs store the alpha channel, not a colour key.
            xor += bytes((b, g, r, a))

    mask_row = ((edge + 31) // 32) * 4
    mask = bytearray(mask_row * edge)
    for y in range(edge - 1, -1, -1):
        row = (edge - 1 - y) * mask_row
        for x in range(edge):
            if pixels[x, y][3] == 0:
                mask[row + (x // 8)] |= 0x80 >> (x % 8)

    header = struct.pack(
        "<IiiHHIIiiII",
        40,        # biSize
        edge,      # biWidth
        edge * 2,  # biHeight: XOR image plus AND mask
        1,         # biPlanes
        32,        # biBitCount
        0,         # biCompression
        len(xor) + len(mask),
        0, 0, 0, 0,
    )
    return header + bytes(xor) + bytes(mask)


def png_entry(image):
    """One ICO image as PNG data.

    PNG is the only compressed image an ICO can hold, and Explorer/Vista and later read
    it for the 256px frame. The small frames stay DIB so every shell version can read
    them, and the large ones - which are the bulk of the file - cost tens of bytes per
    pixel rather than four.
    """
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def main():
    images = []
    for edge in SIZES:
        picture = render(edge)
        images.append((edge, png_entry(picture) if edge >= PNG_FROM else bmp_entry(picture, edge)))
    offset = 6 + 16 * len(images)
    directory = bytearray(struct.pack("<HHH", 0, 1, len(images)))
    for edge, data in images:
        directory += struct.pack(
            "<BBBBHHII",
            0 if edge >= 256 else edge,
            0 if edge >= 256 else edge,
            0, 0, 1, 32, len(data), offset,
        )
        offset += len(data)

    with open("Assets/HASNetMonitor.ico", "wb") as handle:
        handle.write(bytes(directory))
        for _, data in images:
            handle.write(data)


main()
