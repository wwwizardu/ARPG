#!/usr/bin/env python3
"""Normalize a complete alpha sprite set to a shared 128x128 canvas."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

try:
    from PIL import Image
except ModuleNotFoundError as exception:
    print("Pillow is required. Select a Python runtime with Pillow installed.", file=sys.stderr)
    raise SystemExit(2) from exception


ANIMATION_COUNTS = (("Idle", 2), ("Move", 4), ("Attack", 4), ("Die", 3))
LANCZOS = Image.Resampling.LANCZOS if hasattr(Image, "Resampling") else Image.LANCZOS


def expected_names(base_name: str) -> list[str]:
    return [
        f"{base_name}_{animation}{frame}.png"
        for animation, count in ANIMATION_COUNTS
        for frame in range(1, count + 1)
    ]


def visible_bbox(image: Image.Image, alpha_threshold: int) -> tuple[int, int, int, int] | None:
    alpha = image.getchannel("A")
    mask = alpha.point(lambda value: 255 if value > alpha_threshold else 0)
    return mask.getbbox()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--base-name", required=True)
    parser.add_argument("--canvas-size", type=int, default=128)
    parser.add_argument("--padding", type=int, default=8)
    parser.add_argument("--ground-y", type=int, default=126)
    parser.add_argument("--anchor-height", type=int, default=68)
    parser.add_argument("--alpha-threshold", type=int, default=8)
    parser.add_argument("--overwrite", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    names = expected_names(args.base_name)

    if args.canvas_size <= 0:
        raise ValueError("--canvas-size must be positive")
    if not 0 <= args.padding < args.canvas_size // 2:
        raise ValueError("--padding must leave a positive drawable area")
    if not args.padding < args.ground_y <= args.canvas_size:
        raise ValueError("--ground-y must be inside the canvas and below the top padding")

    images: dict[str, Image.Image] = {}
    boxes: dict[str, tuple[int, int, int, int]] = {}
    for name in names:
        path = args.input_dir / name
        if path.is_file() is False:
            print(f"Missing input: {path}", file=sys.stderr)
            return 1
        with Image.open(path) as source:
            image = source.convert("RGBA")
        bbox = visible_bbox(image, args.alpha_threshold)
        if bbox is None:
            print(f"No visible pixels above alpha threshold: {path}", file=sys.stderr)
            return 1
        images[name] = image
        boxes[name] = bbox

    idle_name = f"{args.base_name}_Idle1.png"
    idle_box = boxes[idle_name]
    idle_height = idle_box[3] - idle_box[1]
    max_width = max(box[2] - box[0] for box in boxes.values())
    max_height = max(box[3] - box[1] for box in boxes.values())
    drawable_width = args.canvas_size - (args.padding * 2)
    drawable_height = args.ground_y - args.padding
    scale = min(
        args.anchor_height / idle_height,
        drawable_width / max_width,
        drawable_height / max_height,
    )
    if scale <= 0:
        raise ValueError("Computed scale is not positive")

    collisions = [args.output_dir / name for name in names if (args.output_dir / name).exists()]
    if collisions and args.overwrite is False:
        print("Refusing to overwrite existing output files:", file=sys.stderr)
        for path in collisions:
            print(f"  {path}", file=sys.stderr)
        return 1

    args.output_dir.mkdir(parents=True, exist_ok=True)
    for name in names:
        image = images[name]
        left, top, right, bottom = boxes[name]
        cropped = image.crop((left, top, right, bottom))
        width = max(1, round(cropped.width * scale))
        height = max(1, round(cropped.height * scale))
        resized = cropped.resize((width, height), LANCZOS)

        x = (args.canvas_size - width) // 2
        y = args.ground_y - height
        if x < 0 or y < 0 or x + width > args.canvas_size or y + height > args.canvas_size:
            print(f"Normalized sprite does not fit the canvas: {name}", file=sys.stderr)
            return 1

        canvas = Image.new("RGBA", (args.canvas_size, args.canvas_size), (0, 0, 0, 0))
        canvas.alpha_composite(resized, (x, y))
        canvas.save(args.output_dir / name, format="PNG", optimize=True)
        print(f"Wrote {args.output_dir / name}")

    print(f"Normalized {len(names)} frames with shared scale {scale:.4f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
