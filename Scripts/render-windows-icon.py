#!/usr/bin/env python3
"""Renders Windows/Lumerist.App/Assets/Lumerist.ico from the same SVG sources
as the Mac icon: icon-small.svg for 16-64 px (thicker lines), icon-master.svg
for 128 and 256 px. Sizes up to 64 px are stored as 32-bit bitmaps (read by
every Windows API, the tray included), larger ones as PNG.

Needs rsvg-convert (Homebrew: librsvg) and Pillow. Run from the repository root:
    python3 Scripts/render-windows-icon.py
"""
import pathlib
import struct
import subprocess
import tempfile

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
ICON_DIR = ROOT / "Resources" / "Icon"
OUTPUT = ROOT / "Windows" / "Lumerist.App" / "Assets" / "Lumerist.ico"
SMALL_SIZES = [16, 20, 24, 32, 40, 48, 64]
LARGE_SIZES = [128, 256]


def render(svg: pathlib.Path, size: int, folder: pathlib.Path) -> bytes:
    target = folder / f"{size}.png"
    subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), "-o", str(target), str(svg)], check=True)
    return target.read_bytes() if size > 64 else bitmap(target)


def bitmap(png_path: pathlib.Path) -> bytes:
    """A 32-bit BGRA DIB as ICO frames store it: bottom-up rows, then an empty AND mask."""
    image = Image.open(png_path).convert("RGBA")
    width, height = image.size
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = image.tobytes("raw", "BGRA")
    rows = [pixels[y * width * 4:(y + 1) * width * 4] for y in range(height)]
    mask_row = b"\x00" * (((width + 31) // 32) * 4)
    return header + b"".join(reversed(rows)) + mask_row * height


def main() -> None:
    with tempfile.TemporaryDirectory() as folder:
        images = [(size, render(ICON_DIR / "icon-small.svg", size, pathlib.Path(folder))) for size in SMALL_SIZES]
        images += [(size, render(ICON_DIR / "icon-master.svg", size, pathlib.Path(folder))) for size in LARGE_SIZES]

    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    directory = b""
    data = b""
    for size, frame in images:
        # Width and height 0 mean 256.
        side = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", side, side, 0, 0, 1, 32, len(frame), offset + len(data))
        data += frame
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_bytes(header + directory + data)
    print(f"{OUTPUT.relative_to(ROOT)}: {len(images)} sizes, {OUTPUT.stat().st_size} bytes")


if __name__ == "__main__":
    main()
