"""Generate the launcher icon assets from the supplied artwork.

The source image is trimmed to its visible pixels, padded slightly and resized for
the window/sidebar logo and for every size embedded in the Windows icon.

Usage:  python tools/generate_icons.py
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "icon_color.png"
ASSETS = ROOT / "src" / "GodotLauncher.App" / "Assets"

# Sizes embedded in the Windows icon, largest first.
ICON_SIZES = [256, 128, 64, 48, 32, 24, 16]
PADDING_RATIO = 0.04


def load_master() -> Image.Image:
    image = Image.open(SOURCE).convert("RGBA")
    bounds = image.getchannel("A").getbbox()
    if bounds is None:
        raise SystemExit(f"{SOURCE} is fully transparent")
    image = image.crop(bounds)

    side = max(image.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(image, ((side - image.width) // 2, (side - image.height) // 2))

    pad = round(side * PADDING_RATIO)
    padded = Image.new("RGBA", (side + pad * 2, side + pad * 2), (0, 0, 0, 0))
    padded.paste(canvas, (pad, pad))
    return padded


def main() -> int:
    if not SOURCE.exists():
        print(f"Missing source icon: {SOURCE}", file=sys.stderr)
        return 1

    ASSETS.mkdir(parents=True, exist_ok=True)
    master = load_master()

    logo = master.resize((1024, 1024), Image.LANCZOS)
    logo.save(ASSETS / "app.png", optimize=True)

    frames = [master.resize((size, size), Image.LANCZOS) for size in ICON_SIZES]
    frames[0].save(
        ASSETS / "app.ico",
        format="ICO",
        sizes=[(size, size) for size in ICON_SIZES],
        append_images=frames[1:],
    )

    for path in ("app.png", "app.ico"):
        print(f"{ASSETS / path} ({(ASSETS / path).stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
