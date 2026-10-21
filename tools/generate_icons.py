#!/usr/bin/env python3
"""Generate the Godot Launcher application and tray icons.

The mark is intentionally original (a play glyph next to a short version list) so
the launcher is not mistaken for an official Godot Engine product.

Usage:  python tools/generate_icons.py
"""

from __future__ import annotations

import os

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "src", "GodotLauncher.App", "Assets")

SIZE = 1024
TOP_LEFT = np.array([109, 92, 255], dtype=np.float64)   # indigo
BOTTOM_RIGHT = np.array([45, 134, 246], dtype=np.float64)  # blue


def diagonal_gradient(size: int) -> Image.Image:
    y, x = np.mgrid[0:size, 0:size]
    t = (x / (size - 1) * 0.55 + y / (size - 1) * 0.45)
    t = t[..., None]
    rgb = TOP_LEFT * (1.0 - t) + BOTTOM_RIGHT * t
    return Image.fromarray(rgb.astype(np.uint8), "RGB")


def rounded_mask(size: int, radius_ratio: float = 0.235) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, size - 1, size - 1], radius=int(size * radius_ratio), fill=255
    )
    return mask


def draw_glyph(canvas: Image.Image, size: int) -> None:
    overlay = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)

    # Play glyph, slightly left of center.
    cx = size * 0.415
    half_h = size * 0.20
    tri = [
        (cx - size * 0.085, size * 0.50 - half_h),
        (cx - size * 0.085, size * 0.50 + half_h),
        (cx + size * 0.145, size * 0.50),
    ]
    draw.polygon(tri, fill=(255, 255, 255, 255))

    # Version list: three bars of decreasing width.
    bars = [0.185, 0.145, 0.105]
    x0 = size * 0.615
    y = size * 0.375
    for width in bars:
        draw.rounded_rectangle(
            [x0, y, x0 + size * width, y + size * 0.058],
            radius=size * 0.029,
            fill=(255, 255, 255, 225),
        )
        y += size * 0.085

    canvas.alpha_composite(overlay)


def make_icon() -> Image.Image:
    gradient = diagonal_gradient(SIZE).convert("RGBA")
    gradient.putalpha(rounded_mask(SIZE))

    # A hairline inner stroke keeps the edges crisp on light backgrounds.
    stroke = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    inset = int(SIZE * 0.008)
    ImageDraw.Draw(stroke).rounded_rectangle(
        [inset, inset, SIZE - 1 - inset, SIZE - 1 - inset],
        radius=int(SIZE * 0.225),
        outline=(255, 255, 255, 40),
        width=max(1, int(SIZE * 0.006)),
    )
    gradient.alpha_composite(Image.composite(stroke, Image.new("RGBA", (SIZE, SIZE)), rounded_mask(SIZE)))

    draw_glyph(gradient, SIZE)
    return gradient


def main() -> None:
    os.makedirs(ASSETS, exist_ok=True)
    icon = make_icon()

    png_path = os.path.join(ASSETS, "app.png")
    icon.save(png_path, "PNG")

    sizes = [256, 128, 64, 48, 32, 24, 16]
    frames = [icon.resize((s, s), Image.LANCZOS) for s in sizes]
    ico_path = os.path.join(ASSETS, "app.ico")
    frames[0].save(ico_path, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[1:])

    print(f"wrote {png_path} and {ico_path} ({os.path.getsize(ico_path)} bytes)")


if __name__ == "__main__":
    main()
